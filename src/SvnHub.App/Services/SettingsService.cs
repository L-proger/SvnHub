using SvnHub.App.Configuration;
using SvnHub.App.Storage;
using SvnHub.App.Support;
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
        AccessLevel defaultAuthenticatedAccess,
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

        if (defaultAuthenticatedAccess is not (AccessLevel.None or AccessLevel.Read or AccessLevel.Write))
        {
            return OperationResult.Fail("Invalid default authenticated access.");
        }

        if (state.Settings.DefaultAuthenticatedAccess != defaultAuthenticatedAccess &&
            !CanManageRepositoryAccessPolicy(state, actorUserId))
        {
            return OperationResult.Fail("You don't have permission to change repository access defaults.");
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

        var newSettings = state.Settings with
        {
            OrganizationName = normalizedOrganizationName,
            RepositoriesRootPath = normalized,
            SvnBaseUrl = normalizedSvnBaseUrl,
            DefaultAuthenticatedAccess = defaultAuthenticatedAccess,
            MaxUploadBytes = maxUploadBytes,
            IndexingEnabled = indexingEnabled,
            IndexingScanIntervalSeconds = indexingScanIntervalSeconds,
            IndexingMaxRevisionsPerRepositoryPerScan = indexingMaxRevisionsPerRepositoryPerScan,
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
                    Action: "settings.set_organization",
                    Target: "organizationName",
                    Success: true,
                    Details: normalizedOrganizationName
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

    private static string? NormalizeOrganizationName(string? organizationName)
    {
        if (string.IsNullOrWhiteSpace(organizationName))
        {
            return "";
        }

        var trimmed = organizationName.Trim();
        return trimmed.Length <= 80 ? trimmed : null;
    }

    private static bool CanManageSystemSettings(PortalState state, Guid actorUserId) =>
        state.Users.Any(u =>
            u.Id == actorUserId &&
            u.IsActive &&
            u.Roles.HasEffectiveRole(PortalUserRoles.AdminSystem));

    private static bool CanManageRepositoryAccessPolicy(PortalState state, Guid actorUserId) =>
        state.Users.Any(u =>
            u.Id == actorUserId &&
            u.IsActive &&
            u.Roles.HasEffectiveRole(PortalUserRoles.AdminRepo));
}
