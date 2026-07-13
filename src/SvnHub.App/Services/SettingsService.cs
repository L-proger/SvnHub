using SvnHub.App.Configuration;
using SvnHub.App.Storage;
using SvnHub.App.Support;
using SvnHub.App.System;
using SvnHub.Domain;

namespace SvnHub.App.Services;

public sealed class SettingsService
{
    public const long DefaultMaxUploadBytes = 100L * 1024 * 1024;
    public const long MaxAllowedUploadBytes = 2L * 1024 * 1024 * 1024;
    public const int DefaultIndexingScanIntervalSeconds = 300;
    public const int MinIndexingScanIntervalSeconds = 30;
    public const int MaxIndexingScanIntervalSeconds = 86_400;
    public const int DefaultIndexingMaxRevisionsPerRepositoryPerScan = 200;
    public const int MinIndexingMaxRevisionsPerRepositoryPerScan = 0;
    public const int MaxIndexingMaxRevisionsPerRepositoryPerScan = 10_000;

    private readonly IPortalStore _store;
    private readonly SvnHubOptions _options;
    private readonly IAuthFilesWriter _authFilesWriter;

    public SettingsService(IPortalStore store, SvnHubOptions options, IAuthFilesWriter authFilesWriter)
    {
        _store = store;
        _options = options;
        _authFilesWriter = authFilesWriter;
    }

    public string GetEffectiveRepositoriesRootPath()
    {
        var state = _store.Read();
        return GetEffectiveRepositoriesRootPath(state);
    }

    public string GetOrganizationName()
    {
        var state = _store.Read();
        return state.Settings.OrganizationName;
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

        return _options.SvnBaseUrl;
    }

    public IReadOnlyList<string> GetEffectiveSvnBaseUrls()
    {
        var state = _store.Read();
        var urls = new List<string>
        {
            GetEffectiveSvnBaseUrl(state),
        };

        urls.AddRange(state.Settings.SvnBaseUrlAliases ?? []);
        urls.AddRange(_options.SvnBaseUrlAliases);
        return NormalizeSvnBaseUrlList(urls) ?? [];
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

    public RepositoryIndexingSettings GetEffectiveIndexingSettings()
    {
        var state = _store.Read();
        return GetEffectiveIndexingSettings(state);
    }

    public static RepositoryIndexingSettings GetEffectiveIndexingSettings(PortalState state)
    {
        var scanIntervalSeconds = state.Settings.IndexingScanIntervalSeconds;
        if (scanIntervalSeconds <= 0)
        {
            scanIntervalSeconds = DefaultIndexingScanIntervalSeconds;
        }

        scanIntervalSeconds = Math.Clamp(
            scanIntervalSeconds,
            MinIndexingScanIntervalSeconds,
            MaxIndexingScanIntervalSeconds);

        var maxRevisionsPerRepositoryPerScan = state.Settings.IndexingMaxRevisionsPerRepositoryPerScan;
        if (maxRevisionsPerRepositoryPerScan < 0)
        {
            maxRevisionsPerRepositoryPerScan = DefaultIndexingMaxRevisionsPerRepositoryPerScan;
        }

        maxRevisionsPerRepositoryPerScan = Math.Clamp(
            maxRevisionsPerRepositoryPerScan,
            MinIndexingMaxRevisionsPerRepositoryPerScan,
            MaxIndexingMaxRevisionsPerRepositoryPerScan);

        return new RepositoryIndexingSettings(
            state.Settings.IndexingEnabled,
            scanIntervalSeconds,
            maxRevisionsPerRepositoryPerScan);
    }

    public async Task<OperationResult> SetRepositoriesRootPathAsync(
        Guid actorUserId,
        string repositoriesRootPath,
        bool createIfMissing,
        string? organizationName,
        string? svnBaseUrl,
        string? svnBaseUrlAliases,
        long maxUploadBytes,
        bool indexingEnabled,
        int indexingScanIntervalSeconds,
        int indexingMaxRevisionsPerRepositoryPerScan,
        CancellationToken cancellationToken = default
    )
    {
        var state = _store.Read();
        if (!CanManageSystemSettings(state, actorUserId))
        {
            return OperationResult.Fail("You don't have permission to manage system settings.");
        }

        if (string.IsNullOrWhiteSpace(repositoriesRootPath))
        {
            return OperationResult.Fail("Repositories root path is required.");
        }

        var normalized = repositoriesRootPath.Trim();
        var normalizedOrganizationName = NormalizeOrganizationName(organizationName);
        if (normalizedOrganizationName is null)
        {
            return OperationResult.Fail("Organization name is too long.");
        }

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

        var normalizedSvnBaseUrl = NormalizeSvnBaseUrl(svnBaseUrl);
        if (normalizedSvnBaseUrl is null)
        {
            return OperationResult.Fail("SVN base URL must be an absolute http(s) URL, or empty.");
        }

        var normalizedSvnBaseUrlAliases = NormalizeSvnBaseUrlLines(svnBaseUrlAliases);
        if (normalizedSvnBaseUrlAliases is null)
        {
            return OperationResult.Fail("SVN base URL aliases must contain absolute http(s) URLs, one per line.");
        }

        if (maxUploadBytes <= 0)
        {
            maxUploadBytes = DefaultMaxUploadBytes;
        }

        if (maxUploadBytes > MaxAllowedUploadBytes)
        {
            return OperationResult.Fail($"Max upload size is too large (>{MaxAllowedUploadBytes} bytes).");
        }

        if (indexingScanIntervalSeconds < MinIndexingScanIntervalSeconds ||
            indexingScanIntervalSeconds > MaxIndexingScanIntervalSeconds)
        {
            return OperationResult.Fail(
                $"Index scan interval must be between {MinIndexingScanIntervalSeconds} and {MaxIndexingScanIntervalSeconds} seconds.");
        }

        if (indexingMaxRevisionsPerRepositoryPerScan < MinIndexingMaxRevisionsPerRepositoryPerScan ||
            indexingMaxRevisionsPerRepositoryPerScan > MaxIndexingMaxRevisionsPerRepositoryPerScan)
        {
            return OperationResult.Fail(
                $"Index batch size must be between {MinIndexingMaxRevisionsPerRepositoryPerScan} and {MaxIndexingMaxRevisionsPerRepositoryPerScan} revisions (0 means unlimited).");
        }

        var previousRoot = GetEffectiveRepositoriesRootPath(state);
        var rootChanged = !PathEquals(previousRoot, normalized);
        var now = DateTimeOffset.UtcNow;
        var repositories = rootChanged
            ? state.Repositories.Select(repository =>
            {
                if (repository.IsArchived || IsPathInsideRoot(repository.LocalPath, normalized))
                {
                    return repository;
                }

                return repository with
                {
                    Availability = RepositoryAvailability.Missing,
                    MissingSince = repository.MissingSince ?? now,
                    AvailabilityDetails =
                        "Repository registration has not been synchronized with the configured root.",
                    DetectedPath = null,
                    DetectedSvnUuid = null,
                };
            }).ToList()
            : state.Repositories;

        var newSettings = state.Settings with
        {
            OrganizationName = normalizedOrganizationName,
            RepositoriesRootPath = normalized,
            SvnBaseUrl = normalizedSvnBaseUrl,
            SvnBaseUrlAliases = normalizedSvnBaseUrlAliases,
            MaxUploadBytes = maxUploadBytes,
            IndexingEnabled = indexingEnabled,
            IndexingScanIntervalSeconds = indexingScanIntervalSeconds,
            IndexingMaxRevisionsPerRepositoryPerScan = indexingMaxRevisionsPerRepositoryPerScan,
        };

        var newState = state with
        {
            Settings = newSettings,
            Repositories = repositories,
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
                    Action: "settings.set_organization",
                    Target: "organizationName",
                    Success: true,
                    Details: normalizedOrganizationName
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
                new AuditEvent(
                    Id: Guid.NewGuid(),
                    CreatedAt: DateTimeOffset.UtcNow,
                    ActorUserId: actorUserId,
                    Action: "settings.set_indexing",
                    Target: "repositoryIndex",
                    Success: true,
                    Details: $"enabled={indexingEnabled}; interval={indexingScanIntervalSeconds}; batch={indexingMaxRevisionsPerRepositoryPerScan}"
                ),
            ],
        };

        _store.Write(newState);
        if (rootChanged)
        {
            try
            {
                await _authFilesWriter.WriteAuthzAsync(newState, cancellationToken);
                await _authFilesWriter.ReloadApacheAsync(cancellationToken);
            }
            catch
            {
                // Settings are durable; startup sync can retry authz generation.
            }
        }

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

    private static IReadOnlyList<string>? NormalizeSvnBaseUrlLines(string? svnBaseUrls)
    {
        if (string.IsNullOrWhiteSpace(svnBaseUrls))
        {
            return [];
        }

        var values = svnBaseUrls
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return NormalizeSvnBaseUrlList(values);
    }

    private static IReadOnlyList<string>? NormalizeSvnBaseUrlList(IEnumerable<string> svnBaseUrls)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in svnBaseUrls)
        {
            var normalized = NormalizeSvnBaseUrl(value);
            if (normalized is null)
            {
                return null;
            }

            if (normalized.Length == 0 || !seen.Add(normalized))
            {
                continue;
            }

            result.Add(normalized);
        }

        return result;
    }

    private static string? NormalizeOrganizationName(string? organizationName)
    {
        if (string.IsNullOrWhiteSpace(organizationName))
        {
            return "";
        }

        var trimmed = organizationName.Trim();
        return trimmed.Length <= 80 ? trimmed : null;
    }

    private static bool PathEquals(string left, string right) =>
        (OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .Equals(Path.GetFullPath(left), Path.GetFullPath(right));

    private static bool IsPathInsideRoot(string path, string root)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return relative == "." ||
            (!Path.IsPathRooted(relative) &&
             !relative.Equals("..", StringComparison.Ordinal) &&
             !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
             !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal));
    }

    private static bool CanManageSystemSettings(PortalState state, Guid actorUserId) =>
        state.Users.Any(u =>
            u.Id == actorUserId &&
            u.IsActive &&
            u.Roles.HasEffectiveRole(PortalUserRoles.AdminSystem));

}
