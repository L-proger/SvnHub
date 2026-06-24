using SvnHub.App.Configuration;
using SvnHub.App.Storage;
using SvnHub.App.Support;
using SvnHub.Domain;

namespace SvnHub.App.Services;

public sealed class BrandingService
{
    public const long MaxFaviconBytes = 512L * 1024;

    private const string BrandingDirectoryName = "branding";
    private const string PngFileName = "favicon.png";
    private const string IcoFileName = "favicon.ico";

    private readonly IPortalStore _store;
    private readonly SvnHubOptions _options;

    public BrandingService(IPortalStore store, SvnHubOptions options)
    {
        _store = store;
        _options = options;
    }

    public BrandingFavicon? GetCustomFavicon()
    {
        var state = _store.Read();
        var fileName = NormalizeStoredFileName(state.Settings.CustomFaviconFileName);
        if (fileName is null)
        {
            return null;
        }

        var path = Path.Combine(GetBrandingDirectory(), fileName);
        if (!File.Exists(path))
        {
            return null;
        }

        return new BrandingFavicon(
            path,
            GetContentType(fileName),
            GetFaviconVersion(state.Settings));
    }

    public string GetFaviconVersion()
    {
        var state = _store.Read();
        return GetFaviconVersion(state.Settings);
    }

    public BrandingFaviconLink GetFaviconLink()
    {
        var state = _store.Read();
        var fileName = NormalizeStoredFileName(state.Settings.CustomFaviconFileName);
        if (fileName is null)
        {
            return new BrandingFaviconLink("~/favicon.svg", "image/svg+xml");
        }

        return new BrandingFaviconLink(
            $"~/branding/favicon/{GetFaviconVersion(state.Settings)}/{fileName}",
            GetContentType(fileName));
    }

    public async Task<OperationResult> SetFaviconAsync(
        Guid actorUserId,
        string fileName,
        Stream content,
        long? length,
        CancellationToken cancellationToken = default)
    {
        var state = _store.Read();
        if (!CanManageBranding(state, actorUserId))
        {
            return OperationResult.Fail("You don't have permission to manage system settings.");
        }

        var extension = Path.GetExtension(fileName);
        var targetFileName = extension.ToLowerInvariant() switch
        {
            ".png" => PngFileName,
            ".ico" => IcoFileName,
            _ => null,
        };

        if (targetFileName is null)
        {
            return OperationResult.Fail("Only PNG and ICO icons are supported.");
        }

        if (length is <= 0)
        {
            return OperationResult.Fail("Icon file is empty.");
        }

        if (length > MaxFaviconBytes)
        {
            return OperationResult.Fail($"Icon file is too large. Max size is {MaxFaviconBytes / 1024} KB.");
        }

        byte[] bytes;
        try
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            if (buffer.Length <= 0)
            {
                return OperationResult.Fail("Icon file is empty.");
            }

            if (buffer.Length > MaxFaviconBytes)
            {
                return OperationResult.Fail($"Icon file is too large. Max size is {MaxFaviconBytes / 1024} KB.");
            }

            bytes = buffer.ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail($"Failed to read icon file: {ex.Message}");
        }

        if (!HasExpectedSignature(targetFileName, bytes))
        {
            return OperationResult.Fail("Icon file content does not match the selected PNG or ICO format.");
        }

        try
        {
            var directory = GetBrandingDirectory();
            Directory.CreateDirectory(directory);

            var targetPath = Path.Combine(directory, targetFileName);
            var tempPath = Path.Combine(directory, $"{targetFileName}.{Guid.NewGuid():N}.tmp");
            await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken);
            File.Move(tempPath, targetPath, overwrite: true);

            var oldFileName = targetFileName == PngFileName ? IcoFileName : PngFileName;
            var oldPath = Path.Combine(directory, oldFileName);
            if (File.Exists(oldPath))
            {
                File.Delete(oldPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail($"Failed to save icon file: {ex.Message}");
        }

        var now = DateTimeOffset.UtcNow;
        _store.Write(state with
        {
            Settings = state.Settings with
            {
                CustomFaviconFileName = targetFileName,
                CustomFaviconVersion = now.ToUnixTimeMilliseconds(),
            },
            AuditEvents =
            [
                ..state.AuditEvents,
                new AuditEvent(
                    Id: Guid.NewGuid(),
                    CreatedAt: now,
                    ActorUserId: actorUserId,
                    Action: "settings.set_favicon",
                    Target: "favicon",
                    Success: true,
                    Details: targetFileName
                ),
            ],
        });

        return OperationResult.Ok();
    }

    public OperationResult ResetFavicon(Guid actorUserId)
    {
        var state = _store.Read();
        if (!CanManageBranding(state, actorUserId))
        {
            return OperationResult.Fail("You don't have permission to manage system settings.");
        }

        try
        {
            var directory = GetBrandingDirectory();
            DeleteIfExists(Path.Combine(directory, PngFileName));
            DeleteIfExists(Path.Combine(directory, IcoFileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail($"Failed to delete icon file: {ex.Message}");
        }

        var now = DateTimeOffset.UtcNow;
        _store.Write(state with
        {
            Settings = state.Settings with
            {
                CustomFaviconFileName = "",
                CustomFaviconVersion = now.ToUnixTimeMilliseconds(),
            },
            AuditEvents =
            [
                ..state.AuditEvents,
                new AuditEvent(
                    Id: Guid.NewGuid(),
                    CreatedAt: now,
                    ActorUserId: actorUserId,
                    Action: "settings.reset_favicon",
                    Target: "favicon",
                    Success: true,
                    Details: null
                ),
            ],
        });

        return OperationResult.Ok();
    }

    private static bool HasExpectedSignature(string fileName, byte[] bytes) =>
        fileName switch
        {
            PngFileName => HasPngSignature(bytes),
            IcoFileName => HasIcoSignature(bytes),
            _ => false,
        };

    private static bool HasPngSignature(byte[] bytes) =>
        bytes.Length >= 8 &&
        bytes[0] == 0x89 &&
        bytes[1] == 0x50 &&
        bytes[2] == 0x4E &&
        bytes[3] == 0x47 &&
        bytes[4] == 0x0D &&
        bytes[5] == 0x0A &&
        bytes[6] == 0x1A &&
        bytes[7] == 0x0A;

    private static bool HasIcoSignature(byte[] bytes)
    {
        if (bytes.Length < 6)
        {
            return false;
        }

        var imageCount = bytes[4] | (bytes[5] << 8);
        return bytes[0] == 0x00 &&
            bytes[1] == 0x00 &&
            bytes[2] == 0x01 &&
            bytes[3] == 0x00 &&
            imageCount > 0;
    }

    private static string? NormalizeStoredFileName(string? fileName) =>
        fileName switch
        {
            PngFileName => PngFileName,
            IcoFileName => IcoFileName,
            _ => null,
        };

    private static string GetContentType(string fileName) =>
        fileName == PngFileName ? "image/png" : "image/x-icon";

    private static string GetFaviconVersion(PortalSettings settings) =>
        settings.CustomFaviconVersion > 0
            ? settings.CustomFaviconVersion.ToString(global::System.Globalization.CultureInfo.InvariantCulture)
            : "default";

    private string GetBrandingDirectory() =>
        Path.Combine(_options.DataDirectory, BrandingDirectoryName);

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static bool CanManageBranding(PortalState state, Guid actorUserId) =>
        state.Users.Any(u =>
            u.Id == actorUserId &&
            u.IsActive &&
            u.Roles.HasEffectiveRole(PortalUserRoles.AdminSystem));
}

public sealed record BrandingFavicon(string FilePath, string ContentType, string Version);

public sealed record BrandingFaviconLink(string Href, string ContentType);
