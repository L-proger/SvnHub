using SvnHub.App.Configuration;
using SvnHub.App.Storage;
using SvnHub.App.Support;
using SvnHub.Domain;

namespace SvnHub.App.Services;

public sealed class SettingsService
{
    public const long DefaultMaxUploadBytes = 100L * 1024 * 1024;
    public const long MaxAllowedUploadBytes = 2L * 1024 * 1024 * 1024;

    private readonly IPortalStore _store;
    private readonly SvnHubOptions _options;

    public SettingsService(IPortalStore store, SvnHubOptions options)
    {
        _store = store;
        _options = options;
    }

    public string GetEffectiveRepositoriesRootPath()
    {
        var state = _store.Read();
        return GetEffectiveRepositoriesRootPath(state);
    }

    public string GetEffectiveRepositoriesRootPath(PortalState state)
    {
        if (!string.IsNullOrWhiteSpace(state.Settings.RepositoriesRootPath))
        {
            return state.Settings.RepositoriesRootPath;
        }

        return _options.RepositoriesRootPath;
    }

    public string GetEffectiveSvnBaseUrl()
    {
        var state = _store.Read();
        return GetEffectiveSvnBaseUrl(state);
    }

    public string GetEffectiveSvnBaseUrl(PortalState state)
    {
        if (!string.IsNullOrWhiteSpace(state.Settings.SvnBaseUrl))
        {
            return state.Settings.SvnBaseUrl;
        }

        return "";
    }

    public AccessLevel GetEffectiveDefaultAuthenticatedAccess()
    {
        var state = _store.Read();
        return GetEffectiveDefaultAuthenticatedAccess(state);
    }

    public static AccessLevel GetEffectiveDefaultAuthenticatedAccess(PortalState state)
    {
        return state.Settings.DefaultAuthenticatedAccess switch
        {
            AccessLevel.None => AccessLevel.None,
            AccessLevel.Read => AccessLevel.Read,
            AccessLevel.Write => AccessLevel.Write,
            _ => AccessLevel.Write,
        };
    }

    public long GetEffectiveMaxUploadBytes()
    {
        var state = _store.Read();
        return GetEffectiveMaxUploadBytes(state);
    }

    public long GetEffectiveMaxUploadBytes(PortalState state)
    {
        var v = state.Settings.MaxUploadBytes;
        if (v <= 0)
        {
            return DefaultMaxUploadBytes;
        }

        if (v > MaxAllowedUploadBytes)
        {
            return MaxAllowedUploadBytes;
        }

        return v;
    }

    public async Task<OperationResult> SetRepositoriesRootPathAsync(
        Guid actorUserId,
        string repositoriesRootPath,
        bool createIfMissing,
        string? svnBaseUrl,
        AccessLevel defaultAuthenticatedAccess,
        long maxUploadBytes,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(repositoriesRootPath))
        {
            return OperationResult.Fail("Repositories root path is required.");
        }

        var normalized = repositoriesRootPath.Trim();
        if (!Path.IsPathRooted(normalized))
        {
            return OperationResult.Fail("Repositories root path must be an absolute path.");
        }

        if (createIfMissing)
        {
            try
            {
                Directory.CreateDirectory(normalized);
            }
            catch (Exception ex)
            {
                return OperationResult.Fail($"Failed to create folder: {ex.Message}");
            }
        }
        else if (!Directory.Exists(normalized))
        {
            return OperationResult.Fail("Folder does not exist (enable 'Create if missing' or create it manually).");
        }

        var state = _store.Read();
        var normalizedSvnBaseUrl = NormalizeSvnBaseUrl(svnBaseUrl);
        if (normalizedSvnBaseUrl is null)
        {
            return OperationResult.Fail("SVN base URL must be an absolute http(s) URL, or empty.");
        }

        if (maxUploadBytes <= 0)
        {
            maxUploadBytes = DefaultMaxUploadBytes;
        }

        if (maxUploadBytes > MaxAllowedUploadBytes)
        {
            return OperationResult.Fail($"Max upload size is too large (>{MaxAllowedUploadBytes} bytes).");
        }

        var newSettings = state.Settings with
        {
            RepositoriesRootPath = normalized,
            SvnBaseUrl = normalizedSvnBaseUrl,
            DefaultAuthenticatedAccess = defaultAuthenticatedAccess,
            MaxUploadBytes = maxUploadBytes,
        };

        var newState = state with
        {
            Settings = newSettings,
            AuditEvents =
            [
                ..state.AuditEvents,
                new AuditEvent(
                    Id: Guid.NewGuid(),
                    CreatedAt: DateTimeOffset.UtcNow,
                    ActorUserId: actorUserId,
                    Action: "settings.set_repos_root",
                    Target: "repositoriesRootPath",
                    Success: true,
                    Details: normalized
                ),
                new AuditEvent(
                    Id: Guid.NewGuid(),
                    CreatedAt: DateTimeOffset.UtcNow,
                    ActorUserId: actorUserId,
                    Action: "settings.set_default_access",
                    Target: "defaultAuthenticatedAccess",
                    Success: true,
                    Details: defaultAuthenticatedAccess.ToString()
                ),
                new AuditEvent(
                    Id: Guid.NewGuid(),
                    CreatedAt: DateTimeOffset.UtcNow,
                    ActorUserId: actorUserId,
                    Action: "settings.set_max_upload",
                    Target: "maxUploadBytes",
                    Success: true,
                    Details: maxUploadBytes.ToString()
                ),
            ],
        };

        _store.Write(newState);
        await Task.CompletedTask;
        return OperationResult.Ok();
    }

    private static string? NormalizeSvnBaseUrl(string? svnBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(svnBaseUrl))
        {
            return "";
        }

        var trimmed = svnBaseUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return trimmed;
    }
}
