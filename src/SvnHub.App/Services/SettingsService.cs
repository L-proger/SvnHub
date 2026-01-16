using SvnHub.App.Configuration;
using SvnHub.App.Storage;
using SvnHub.App.Support;
using SvnHub.Domain;

namespace SvnHub.App.Services;

public sealed class SettingsService
{
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

    public async Task<OperationResult> SetRepositoriesRootPathAsync(
        Guid actorUserId,
        string repositoriesRootPath,
        bool createIfMissing,
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
                return OperationResult.Fail($"Failed to create directory: {ex.Message}");
            }
        }
        else if (!Directory.Exists(normalized))
        {
            return OperationResult.Fail("Directory does not exist (enable 'Create if missing' or create it manually).");
        }

        var state = _store.Read();
        var newSettings = state.Settings with { RepositoriesRootPath = normalized };

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
            ],
        };

        _store.Write(newState);
        await Task.CompletedTask;
        return OperationResult.Ok();
    }
}

