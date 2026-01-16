using SvnHub.App.Storage;
using SvnHub.App.Support;
using SvnHub.App.System;
using SvnHub.Domain;
using SvnHub.App.Configuration;

namespace SvnHub.App.Services;

public sealed class RepositoryService
{
    private readonly IPortalStore _store;
    private readonly ISvnRepositoryProvisioner _provisioner;
    private readonly SvnHubOptions _options;
    private readonly IAuthFilesWriter _authFilesWriter;
    private readonly SettingsService _settings;
    private readonly ISvnLookClient _svnlook;

    public RepositoryService(
        IPortalStore store,
        ISvnRepositoryProvisioner provisioner,
        SvnHubOptions options,
        IAuthFilesWriter authFilesWriter,
        SettingsService settings,
        ISvnLookClient svnlook)
    {
        _store = store;
        _provisioner = provisioner;
        _options = options;
        _authFilesWriter = authFilesWriter;
        _settings = settings;
        _svnlook = svnlook;
    }

    public IReadOnlyList<Repository> List()
    {
        var state = _store.Read();
        return state.Repositories
            .Where(r => !r.IsArchived)
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public Repository? FindByName(string name)
    {
        var state = _store.Read();
        return state.Repositories.FirstOrDefault(r =>
            string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public Repository? FindById(Guid id)
    {
        var state = _store.Read();
        return state.Repositories.FirstOrDefault(r => r.Id == id);
    }

    public async Task<OperationResult<Repository>> CreateAsync(
        Guid actorUserId,
        string name,
        bool initializeStandardLayout,
        CancellationToken cancellationToken = default
    )
    {
        if (!Validation.IsValidRepositoryName(name))
        {
            return OperationResult<Repository>.Fail("Invalid repository name.");
        }

        var state = _store.Read();
        if (state.Repositories.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return OperationResult<Repository>.Fail("Repository already exists.");
        }

        var root = _settings.GetEffectiveRepositoriesRootPath(state);
        var localPath = Path.Combine(root, name);

        try
        {
            await _provisioner.CreateAsync(localPath, initializeStandardLayout, cancellationToken);
        }
        catch (Exception ex)
        {
            return OperationResult<Repository>.Fail($"Failed to create repository on disk: {ex.Message}");
        }

        var repo = new Repository(
            Id: Guid.NewGuid(),
            Name: name,
            LocalPath: localPath,
            CreatedAt: DateTimeOffset.UtcNow,
            IsArchived: false
        );

        var newState = state with
        {
            Repositories = [..state.Repositories, repo],
            AuditEvents =
            [
                ..state.AuditEvents,
                new AuditEvent(
                    Id: Guid.NewGuid(),
                    CreatedAt: DateTimeOffset.UtcNow,
                    ActorUserId: actorUserId,
                    Action: "repo.create",
                    Target: repo.Name,
                    Success: true,
                    Details: repo.LocalPath
                ),
            ],
        };

        _store.Write(newState);

        try
        {
            await _authFilesWriter.WriteAuthzAsync(newState, cancellationToken);
            await _authFilesWriter.ReloadApacheAsync(cancellationToken);
        }
        catch
        {
            // MVP: repo is created, authz sync can be retried later.
        }

        return OperationResult<Repository>.Ok(repo);
    }

    public async Task<OperationResult<Repository>> RenameAsync(
        Guid actorUserId,
        Guid repositoryId,
        string newName,
        CancellationToken cancellationToken = default
    )
    {
        if (!Validation.IsValidRepositoryName(newName))
        {
            return OperationResult<Repository>.Fail("Invalid repository name.");
        }

        var state = _store.Read();
        var repo = state.Repositories.FirstOrDefault(r => r.Id == repositoryId);
        if (repo is null || repo.IsArchived)
        {
            return OperationResult<Repository>.Fail("Repository not found.");
        }

        if (state.Repositories.Any(r => r.Id != repositoryId && string.Equals(r.Name, newName, StringComparison.OrdinalIgnoreCase)))
        {
            return OperationResult<Repository>.Fail("Repository with this name already exists.");
        }

        if (string.Equals(repo.Name, newName, StringComparison.Ordinal))
        {
            return OperationResult<Repository>.Ok(repo);
        }

        var parent = Path.GetDirectoryName(repo.LocalPath);
        if (string.IsNullOrWhiteSpace(parent))
        {
            parent = _settings.GetEffectiveRepositoriesRootPath(state);
        }

        var newLocalPath = Path.Combine(parent, newName);

        try
        {
            if (Directory.Exists(newLocalPath))
            {
                return OperationResult<Repository>.Fail("Target path already exists on disk.");
            }

            Directory.Move(repo.LocalPath, newLocalPath);
        }
        catch (Exception ex)
        {
            return OperationResult<Repository>.Fail($"Failed to rename repository on disk: {ex.Message}");
        }

        var updated = repo with { Name = newName, LocalPath = newLocalPath };
        var newRepos = state.Repositories.Select(r => r.Id == repositoryId ? updated : r).ToList();

        var newState = state with
        {
            Repositories = newRepos,
            AuditEvents =
            [
                ..state.AuditEvents,
                new AuditEvent(
                    Id: Guid.NewGuid(),
                    CreatedAt: DateTimeOffset.UtcNow,
                    ActorUserId: actorUserId,
                    Action: "repo.rename",
                    Target: repo.Name,
                    Success: true,
                    Details: updated.Name
                ),
            ],
        };

        _store.Write(newState);

        try
        {
            await _authFilesWriter.WriteAuthzAsync(newState, cancellationToken);
            await _authFilesWriter.ReloadApacheAsync(cancellationToken);
        }
        catch
        {
        }

        return OperationResult<Repository>.Ok(updated);
    }

    public async Task<OperationResult> DeleteAsync(
        Guid actorUserId,
        Guid repositoryId,
        CancellationToken cancellationToken = default
    )
    {
        var state = _store.Read();
        var repo = state.Repositories.FirstOrDefault(r => r.Id == repositoryId);
        if (repo is null || repo.IsArchived)
        {
            return OperationResult.Fail("Repository not found.");
        }

        var parent = Path.GetDirectoryName(repo.LocalPath);
        if (string.IsNullOrWhiteSpace(parent))
        {
            parent = _settings.GetEffectiveRepositoriesRootPath(state);
        }

        var deletedRoot = Path.Combine(parent, ".deleted");
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        var deletedPath = Path.Combine(deletedRoot, $"{repo.Name}-{stamp}");

        try
        {
            Directory.CreateDirectory(deletedRoot);
            if (Directory.Exists(deletedPath))
            {
                deletedPath = Path.Combine(deletedRoot, $"{repo.Name}-{stamp}-{Guid.NewGuid():N}");
            }

            Directory.Move(repo.LocalPath, deletedPath);
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Failed to delete repository on disk: {ex.Message}");
        }

        var updated = repo with { IsArchived = true, LocalPath = deletedPath };
        var newRepos = state.Repositories.Select(r => r.Id == repositoryId ? updated : r).ToList();

        var newState = state with
        {
            Repositories = newRepos,
            AuditEvents =
            [
                ..state.AuditEvents,
                new AuditEvent(
                    Id: Guid.NewGuid(),
                    CreatedAt: DateTimeOffset.UtcNow,
                    ActorUserId: actorUserId,
                    Action: "repo.delete",
                    Target: repo.Name,
                    Success: true,
                    Details: deletedPath
                ),
            ],
        };

        _store.Write(newState);

        try
        {
            await _authFilesWriter.WriteAuthzAsync(newState, cancellationToken);
            await _authFilesWriter.ReloadApacheAsync(cancellationToken);
        }
        catch
        {
        }

        return OperationResult.Ok();
    }

    public async Task<OperationResult<int>> DiscoverAsync(
        Guid actorUserId,
        CancellationToken cancellationToken = default
    )
    {
        var state = _store.Read();
        var root = _settings.GetEffectiveRepositoriesRootPath(state);

        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return OperationResult<int>.Fail("Repositories root path does not exist.");
        }

        var dirs = Directory.GetDirectories(root);
        var discovered = 0;
        var repos = state.Repositories.ToList();

        foreach (var dir in dirs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var name = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (string.Equals(name, ".deleted", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Only register names we can safely route to in the UI.
            if (!Validation.IsValidRepositoryName(name))
            {
                continue;
            }

            if (!await IsValidSvnRepositoryAsync(dir, cancellationToken))
            {
                continue;
            }

            var existing = repos.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                repos.Add(new Repository(
                    Id: Guid.NewGuid(),
                    Name: name,
                    LocalPath: dir,
                    CreatedAt: DateTimeOffset.UtcNow,
                    IsArchived: false
                ));
                discovered++;
                continue;
            }

            if (existing.IsArchived)
            {
                var unarchived = existing with { LocalPath = dir, IsArchived = false };
                for (var i = 0; i < repos.Count; i++)
                {
                    if (repos[i].Id == existing.Id)
                    {
                        repos[i] = unarchived;
                        discovered++;
                        break;
                    }
                }
            }
        }

        if (discovered == 0)
        {
            return OperationResult<int>.Ok(0);
        }

        var newState = state with
        {
            Repositories = repos,
            AuditEvents =
            [
                ..state.AuditEvents,
                new AuditEvent(
                    Id: Guid.NewGuid(),
                    CreatedAt: DateTimeOffset.UtcNow,
                    ActorUserId: actorUserId,
                    Action: "repo.discover",
                    Target: root,
                    Success: true,
                    Details: $"discovered={discovered}"
                ),
            ],
        };

        _store.Write(newState);

        try
        {
            await _authFilesWriter.WriteAuthzAsync(newState, cancellationToken);
            await _authFilesWriter.ReloadApacheAsync(cancellationToken);
        }
        catch
        {
        }

        return OperationResult<int>.Ok(discovered);
    }

    private async Task<bool> IsValidSvnRepositoryAsync(string localPath, CancellationToken cancellationToken)
    {
        // Fast checks first.
        if (!File.Exists(Path.Combine(localPath, "format")))
        {
            return false;
        }

        if (!Directory.Exists(Path.Combine(localPath, "db")))
        {
            return false;
        }

        try
        {
            _ = await _svnlook.GetYoungestRevisionAsync(localPath, cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
