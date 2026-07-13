using SvnHub.App.Storage;
using SvnHub.App.Support;
using SvnHub.App.System;
using SvnHub.Domain;
using SvnHub.App.Configuration;
using SvnHub.App.Indexing;

namespace SvnHub.App.Services;

public sealed class RepositoryService
{
    private readonly IPortalStore _store;
    private readonly ISvnRepositoryProvisioner _provisioner;
    private readonly SvnHubOptions _options;
    private readonly IAuthFilesWriter _authFilesWriter;
    private readonly SettingsService _settings;
    private readonly ISvnLookClient _svnlook;
    private readonly IRepositoryIndexStore _indexStore;

    public RepositoryService(
        IPortalStore store,
        ISvnRepositoryProvisioner provisioner,
        SvnHubOptions options,
        IAuthFilesWriter authFilesWriter,
        SettingsService settings,
        ISvnLookClient svnlook,
        IRepositoryIndexStore indexStore)
    {
        _store = store;
        _provisioner = provisioner;
        _options = options;
        _authFilesWriter = authFilesWriter;
        _settings = settings;
        _svnlook = svnlook;
        _indexStore = indexStore;
    }

    public IReadOnlyList<Repository> List()
    {
        var state = _store.Read();
        return state.Repositories
            .Where(r => r.IsAvailable)
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<Repository> ListRegistrations()
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
        return state.Repositories.FirstOrDefault(r => r.IsAvailable &&
            string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public Repository? FindById(Guid id)
    {
        var state = _store.Read();
        return state.Repositories.FirstOrDefault(r => r.Id == id && r.IsAvailable);
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
        if (!CanCreateRepositories(state, actorUserId))
        {
            return OperationResult<Repository>.Fail("You don't have permission to create repositories.");
        }

        var actor = state.Users.FirstOrDefault(u => u.Id == actorUserId && u.IsActive);
        if (actor is null)
        {
            return OperationResult<Repository>.Fail("Actor user not found.");
        }

        if (state.Repositories.Any(r =>
                !r.IsArchived &&
                string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return OperationResult<Repository>.Fail("Repository already exists.");
        }

        var root = _settings.GetEffectiveRepositoriesRootPath(state);
        var localPath = Path.Combine(root, name);

        try
        {
            await _provisioner.CreateAsync(localPath, initializeStandardLayout, actor.UserName, cancellationToken);
        }
        catch (Exception ex)
        {
            return OperationResult<Repository>.Fail($"Failed to create repository on disk: {ex.Message}");
        }

        var now = DateTimeOffset.UtcNow;
        string? svnUuid = null;
        try
        {
            svnUuid = await _svnlook.GetRepositoryUuidAsync(localPath, cancellationToken);
        }
        catch
        {
            // Synchronize can backfill identity if metadata inspection is temporarily unavailable.
        }

        var repo = new Repository(
            Id: Guid.NewGuid(),
            Name: name,
            LocalPath: localPath,
            CreatedAt: now,
            IsArchived: false
        )
        {
            SvnUuid = svnUuid,
            LastSeenAt = now,
        };

        var ownerGrant = new RepositoryManagementGrant(
            Id: Guid.NewGuid(),
            RepositoryId: repo.Id,
            SubjectType: SubjectType.User,
            SubjectId: actorUserId,
            Role: RepositoryManagementRole.Admin,
            CreatedAt: DateTimeOffset.UtcNow
        );

        var managementGrants = state.RepositoryManagementGrants.ToList();
        managementGrants.Add(ownerGrant);

        var newState = state with
        {
            Repositories = [..state.Repositories, repo],
            RepositoryManagementGrants = managementGrants,
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

    public async Task<OperationResult<Repository>> SetInheritedRepositoryGrantsAsync(
        Guid actorUserId,
        Guid repositoryId,
        bool includeInheritedContentGrants,
        bool includeInheritedManagementGrants,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var state = _store.Read();
        var repo = state.Repositories.FirstOrDefault(r => r.Id == repositoryId);
        if (repo is null || !repo.IsAvailable)
        {
            return OperationResult<Repository>.Fail("Repository not found.");
        }

        if (!CanAdminRepository(state, actorUserId, repositoryId))
        {
            return OperationResult<Repository>.Fail("You don't have permission to manage repository administrators.");
        }

        if (repo.IncludeInheritedContentGrants == includeInheritedContentGrants &&
            repo.IncludeInheritedManagementGrants == includeInheritedManagementGrants)
        {
            return OperationResult<Repository>.Ok(repo);
        }

        var updated = repo with
        {
            IncludeInheritedContentGrants = includeInheritedContentGrants,
            IncludeInheritedManagementGrants = includeInheritedManagementGrants,
        };
        var newState = state with
        {
            Repositories = state.Repositories.Select(r => r.Id == repositoryId ? updated : r).ToList(),
            AuditEvents =
            [
                ..state.AuditEvents,
                new AuditEvent(
                    Id: Guid.NewGuid(),
                    CreatedAt: DateTimeOffset.UtcNow,
                    ActorUserId: actorUserId,
                    Action: "repo.grants.inheritance",
                    Target: repo.Name,
                    Success: true,
                    Details: $"content={includeInheritedContentGrants}; management={includeInheritedManagementGrants}"
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

    public Task<OperationResult<Repository>> SetLabelsAsync(
        Guid actorUserId,
        Guid repositoryId,
        string? labelsText,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!RepositoryLabels.TryParse(labelsText, out var labels, out var labelError))
        {
            return Task.FromResult(OperationResult<Repository>.Fail(labelError ?? "Invalid labels."));
        }

        var state = _store.Read();
        var repo = state.Repositories.FirstOrDefault(r => r.Id == repositoryId);
        if (repo is null || !repo.IsAvailable)
        {
            return Task.FromResult(OperationResult<Repository>.Fail("Repository not found."));
        }

        if (!CanAdminRepository(state, actorUserId, repositoryId))
        {
            return Task.FromResult(OperationResult<Repository>.Fail("You don't have permission to manage this repository."));
        }

        var currentLabels = RepositoryLabels.Normalize(repo.Labels);
        if (currentLabels.SequenceEqual(labels, StringComparer.Ordinal))
        {
            return Task.FromResult(OperationResult<Repository>.Ok(repo));
        }

        var updated = repo with { Labels = labels };
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
                    Action: "repo.labels",
                    Target: repo.Name,
                    Success: true,
                    Details: labels.Count == 0 ? "cleared" : string.Join(", ", labels)
                ),
            ],
        };

        _store.Write(newState);
        return Task.FromResult(OperationResult<Repository>.Ok(updated));
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
        if (repo is null || !repo.IsAvailable)
        {
            return OperationResult<Repository>.Fail("Repository not found.");
        }

        if (!CanAdminRepository(state, actorUserId, repositoryId))
        {
            return OperationResult<Repository>.Fail("You don't have permission to manage this repository.");
        }

        if (state.Repositories.Any(r =>
                r.Id != repositoryId &&
                !r.IsArchived &&
                string.Equals(r.Name, newName, StringComparison.OrdinalIgnoreCase)))
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

        var updated = repo with
        {
            Name = newName,
            LocalPath = newLocalPath,
            LastSeenAt = DateTimeOffset.UtcNow,
        };
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
        if (repo is null || !repo.IsAvailable)
        {
            return OperationResult.Fail("Repository not found.");
        }

        if (!CanAdminRepository(state, actorUserId, repositoryId))
        {
            return OperationResult.Fail("You don't have permission to delete this repository.");
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

        var updated = repo with
        {
            IsArchived = true,
            LocalPath = deletedPath,
            AvailabilityDetails = null,
            DetectedPath = null,
            DetectedSvnUuid = null,
        };
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

    public async Task<OperationResult<RepositorySynchronizationResult>> SynchronizeAsync(
        Guid actorUserId,
        CancellationToken cancellationToken = default
    )
    {
        var state = _store.Read();
        if (!CanSynchronizeRepositories(state, actorUserId))
        {
            return OperationResult<RepositorySynchronizationResult>.Fail(
                "You don't have permission to synchronize repositories.");
        }

        var root = _settings.GetEffectiveRepositoriesRootPath(state);

        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return OperationResult<RepositorySynchronizationResult>.Fail(
                "Repositories root path does not exist.");
        }

        RepositoryRootScan scan;
        try
        {
            scan = await ScanRepositoryRootAsync(root, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return OperationResult<RepositorySynchronizationResult>.Fail(
                $"Failed to scan repositories root: {ex.Message}");
        }

        // The filesystem scan can take a while on large installations. Re-read
        // state so a concurrent labels/grants edit is not overwritten by the
        // snapshot captured before the scan started.
        state = _store.Read();
        if (!CanSynchronizeRepositories(state, actorUserId))
        {
            return OperationResult<RepositorySynchronizationResult>.Fail(
                "You no longer have permission to synchronize repositories.");
        }

        var currentRoot = _settings.GetEffectiveRepositoriesRootPath(state);
        if (!PathComparer.Equals(Path.GetFullPath(root), Path.GetFullPath(currentRoot)))
        {
            return OperationResult<RepositorySynchronizationResult>.Fail(
                "Repositories root changed during synchronization. Run synchronization again.");
        }

        var now = DateTimeOffset.UtcNow;
        var repos = state.Repositories.ToList();
        var claimedPaths = new HashSet<string>(PathComparer);
        var added = 0;
        var reconnected = 0;

        // Older registrations did not persist the SVN UUID. Backfill it from the
        // old location when possible before attempting to match a relocated repo.
        for (var i = 0; i < repos.Count; i++)
        {
            var repository = repos[i];
            if (repository.IsArchived || !string.IsNullOrWhiteSpace(repository.SvnUuid))
            {
                continue;
            }

            var atKnownPath = scan.Repositories.FirstOrDefault(candidate =>
                PathComparer.Equals(candidate.LocalPath, repository.LocalPath));
            var uuid = atKnownPath?.SvnUuid;
            if (uuid is null && Directory.Exists(repository.LocalPath))
            {
                uuid = await TryGetRepositoryUuidAsync(repository.LocalPath, cancellationToken);
            }

            if (uuid is not null)
            {
                repos[i] = repository with { SvnUuid = uuid };
            }
        }

        var candidatesByName = scan.Repositories
            .GroupBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var candidatesByUuid = scan.Repositories
            .GroupBy(candidate => candidate.SvnUuid, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < repos.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var repository = repos[i];
            if (repository.IsArchived)
            {
                continue;
            }

            ScannedRepository? candidate = null;
            string? conflictDetails = null;

            if (!string.IsNullOrWhiteSpace(repository.SvnUuid) &&
                candidatesByUuid.TryGetValue(repository.SvnUuid, out var uuidMatches))
            {
                var availableMatches = uuidMatches
                    .Where(match => !claimedPaths.Contains(match.LocalPath))
                    .ToArray();
                if (availableMatches.Length == 1)
                {
                    candidate = availableMatches[0];
                }
                else if (availableMatches.Length > 1)
                {
                    candidate = availableMatches[0];
                    conflictDetails = "Multiple repositories with the same SVN UUID were found.";
                }
            }

            if (candidate is null && conflictDetails is null &&
                candidatesByName.TryGetValue(repository.Name, out var nameMatches))
            {
                var availableMatches = nameMatches
                    .Where(match => !claimedPaths.Contains(match.LocalPath))
                    .ToArray();
                if (availableMatches.Length == 1)
                {
                    var byName = availableMatches[0];
                    if (string.IsNullOrWhiteSpace(repository.SvnUuid) &&
                        PathComparer.Equals(repository.LocalPath, byName.LocalPath))
                    {
                        candidate = byName;
                    }
                    else if (string.IsNullOrWhiteSpace(repository.SvnUuid))
                    {
                        candidate = byName;
                        conflictDetails =
                            "This legacy registration has no stored SVN UUID, so an identically named repository at a new location cannot be verified automatically.";
                    }
                    else if (string.Equals(repository.SvnUuid, byName.SvnUuid, StringComparison.OrdinalIgnoreCase))
                    {
                        candidate = byName;
                    }
                    else
                    {
                        candidate = byName;
                        conflictDetails =
                            $"A different SVN repository now uses this name (expected UUID {repository.SvnUuid}, found {byName.SvnUuid}).";
                    }
                }
                else if (availableMatches.Length > 1)
                {
                    candidate = availableMatches[0];
                    conflictDetails = "Multiple repositories with this name were found.";
                }
            }

            if (candidate is not null && conflictDetails is null)
            {
                var nameOwnedByAnotherRegistration = repos.Any(other =>
                    other.Id != repository.Id &&
                    !other.IsArchived &&
                    string.Equals(other.Name, candidate.Name, StringComparison.OrdinalIgnoreCase));
                if (nameOwnedByAnotherRegistration)
                {
                    conflictDetails = "The detected repository name belongs to another registration.";
                }
            }

            if (candidate is not null && conflictDetails is not null)
            {
                claimedPaths.Add(candidate.LocalPath);
                repos[i] = repository with
                {
                    Availability = RepositoryAvailability.Conflict,
                    MissingSince = null,
                    AvailabilityDetails = conflictDetails,
                    DetectedPath = candidate.LocalPath,
                    DetectedSvnUuid = candidate.SvnUuid,
                };
                continue;
            }

            if (candidate is null)
            {
                if (scan.UnverifiedRepositoryPaths.Contains(Path.GetFullPath(repository.LocalPath)))
                {
                    // A transient svnlook failure is not evidence that the
                    // repository disappeared. Keep its previous availability.
                    continue;
                }

                repos[i] = repository with
                {
                    Availability = RepositoryAvailability.Missing,
                    MissingSince = repository.MissingSince ?? now,
                    AvailabilityDetails = "Repository was not found in the configured repositories root.",
                    DetectedPath = null,
                    DetectedSvnUuid = null,
                };
                continue;
            }

            claimedPaths.Add(candidate.LocalPath);
            if (!repository.IsAvailable ||
                !PathComparer.Equals(repository.LocalPath, candidate.LocalPath) ||
                !string.Equals(repository.Name, candidate.Name, StringComparison.Ordinal))
            {
                reconnected++;
            }

            repos[i] = repository with
            {
                Name = candidate.Name,
                LocalPath = candidate.LocalPath,
                SvnUuid = candidate.SvnUuid,
                Availability = RepositoryAvailability.Available,
                LastSeenAt = now,
                MissingSince = null,
                AvailabilityDetails = null,
                DetectedPath = null,
                DetectedSvnUuid = null,
            };
        }

        foreach (var candidate in scan.Repositories.Where(candidate => !claimedPaths.Contains(candidate.LocalPath)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (repos.Any(repository =>
                    !repository.IsArchived &&
                    string.Equals(repository.Name, candidate.Name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            repos.Add(new Repository(
                Id: Guid.NewGuid(),
                Name: candidate.Name,
                LocalPath: candidate.LocalPath,
                CreatedAt: now,
                IsArchived: false)
            {
                SvnUuid = candidate.SvnUuid,
                LastSeenAt = now,
            });
            claimedPaths.Add(candidate.LocalPath);
            added++;
        }

        var missing = repos.Count(repository =>
            !repository.IsArchived && repository.Availability == RepositoryAvailability.Missing);
        var conflicts = repos.Count(repository =>
            !repository.IsArchived && repository.Availability == RepositoryAvailability.Conflict);
        var result = new RepositorySynchronizationResult(
            ScannedRepositories: scan.Repositories.Count,
            AddedRepositories: added,
            ReconnectedRepositories: reconnected,
            MissingRepositories: missing,
            ConflictRepositories: conflicts,
            IgnoredDirectories: scan.IgnoredDirectories,
            InspectionFailures: scan.UnverifiedRepositoryPaths.Count);

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
                    Action: "repo.synchronize",
                    Target: root,
                    Success: true,
                    Details: result.ToSummary()
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

        return OperationResult<RepositorySynchronizationResult>.Ok(result);
    }

    public async Task<OperationResult> AdoptDetectedRepositoryAsync(
        Guid actorUserId,
        Guid repositoryId,
        CancellationToken cancellationToken = default)
    {
        var state = _store.Read();
        if (!CanSynchronizeRepositories(state, actorUserId))
        {
            return OperationResult.Fail("You don't have permission to resolve repository conflicts.");
        }

        var repository = state.Repositories.FirstOrDefault(item => item.Id == repositoryId && !item.IsArchived);
        if (repository is null || repository.Availability != RepositoryAvailability.Conflict ||
            string.IsNullOrWhiteSpace(repository.DetectedPath) ||
            string.IsNullOrWhiteSpace(repository.DetectedSvnUuid))
        {
            return OperationResult.Fail("Repository conflict was not found.");
        }

        var root = _settings.GetEffectiveRepositoriesRootPath(state);
        if (!IsPathInsideRoot(repository.DetectedPath, root))
        {
            return OperationResult.Fail("Detected repository path is outside the configured root.");
        }

        var actualUuid = await TryGetRepositoryUuidAsync(repository.DetectedPath, cancellationToken);
        if (!string.Equals(actualUuid, repository.DetectedSvnUuid, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Fail("Detected repository changed; synchronize again before adopting it.");
        }

        var detectedName = Path.GetFileName(repository.DetectedPath.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));
        if (!Validation.IsValidRepositoryName(detectedName))
        {
            return OperationResult.Fail("Detected repository name is invalid.");
        }

        if (state.Repositories.Any(other =>
                other.Id != repository.Id &&
                !other.IsArchived &&
                string.Equals(other.Name, detectedName, StringComparison.OrdinalIgnoreCase)))
        {
            return OperationResult.Fail("Another registration already uses the detected repository name.");
        }

        var now = DateTimeOffset.UtcNow;
        var updated = repository with
        {
            Name = detectedName,
            LocalPath = repository.DetectedPath,
            SvnUuid = actualUuid,
            Availability = RepositoryAvailability.Available,
            LastSeenAt = now,
            MissingSince = null,
            AvailabilityDetails = null,
            DetectedPath = null,
            DetectedSvnUuid = null,
        };
        var newState = state with
        {
            Repositories = state.Repositories
                .Select(item => item.Id == repositoryId ? updated : item)
                .ToList(),
            AuditEvents =
            [
                ..state.AuditEvents,
                new AuditEvent(
                    Guid.NewGuid(),
                    now,
                    actorUserId,
                    "repo.registration.adopt",
                    repository.Name,
                    true,
                    $"uuid={actualUuid}; path={updated.LocalPath}")
            ],
        };

        _store.Write(newState);
        await WriteAuthzBestEffortAsync(newState, cancellationToken);
        return OperationResult.Ok();
    }

    public async Task<OperationResult<int>> ForgetRegistrationsAsync(
        Guid actorUserId,
        IReadOnlyCollection<Guid> repositoryIds,
        CancellationToken cancellationToken = default)
    {
        var state = _store.Read();
        if (!CanSynchronizeRepositories(state, actorUserId))
        {
            return OperationResult<int>.Fail("You don't have permission to forget repository registrations.");
        }

        var requestedIds = repositoryIds
            .Where(id => id != Guid.Empty)
            .ToHashSet();
        if (requestedIds.Count == 0)
        {
            return OperationResult<int>.Fail("Select at least one repository registration.");
        }

        var repositories = state.Repositories
            .Where(repository => requestedIds.Contains(repository.Id) && !repository.IsArchived)
            .ToArray();
        if (repositories.Length != requestedIds.Count || repositories.Any(repository => repository.IsAvailable))
        {
            return OperationResult<int>.Fail(
                "Only existing missing or conflicting registrations can be forgotten.");
        }

        // Purging the index first is failure-safe: if the state write later fails,
        // a still-registered repository can simply be indexed again.
        try
        {
            foreach (var repositoryId in requestedIds)
            {
                await _indexStore.DeleteRepositoryAsync(repositoryId, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return OperationResult<int>.Fail(
                $"Failed to remove indexed metadata. No registrations were forgotten: {ex.Message}");
        }

        var now = DateTimeOffset.UtcNow;
        var newState = state with
        {
            Repositories = state.Repositories.Where(item => !requestedIds.Contains(item.Id)).ToList(),
            PermissionRules = state.PermissionRules
                .Where(rule => !requestedIds.Contains(rule.RepositoryId))
                .ToList(),
            RepositoryManagementGrants = state.RepositoryManagementGrants
                .Where(grant => !requestedIds.Contains(grant.RepositoryId))
                .ToList(),
            AuditEvents =
            [
                ..state.AuditEvents,
                ..repositories.Select(repository => new AuditEvent(
                        Guid.NewGuid(),
                        now,
                        actorUserId,
                        "repo.registration.forget",
                        repository.Name,
                        true,
                        $"uuid={repository.SvnUuid ?? "unknown"}; path={repository.LocalPath}")),
            ],
        };

        _store.Write(newState);
        await WriteAuthzBestEffortAsync(newState, cancellationToken);
        return OperationResult<int>.Ok(repositories.Length);
    }

    private async Task<RepositoryRootScan> ScanRepositoryRootAsync(
        string root,
        CancellationToken cancellationToken)
    {
        var directories = Directory.GetDirectories(root);
        var tasks = directories.Select(async directory =>
        {
            await RepositoryScanGate.WaitAsync(cancellationToken);
            try
            {
                var name = Path.GetFileName(directory.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar));
                if (string.Equals(name, ".deleted", StringComparison.OrdinalIgnoreCase) ||
                    !Validation.IsValidRepositoryName(name) ||
                    !File.Exists(Path.Combine(directory, "format")) ||
                    !Directory.Exists(Path.Combine(directory, "db")))
                {
                    return new RepositoryDirectoryInspection(null, null);
                }

                var uuid = await TryGetRepositoryUuidAsync(directory, cancellationToken);
                var fullPath = Path.GetFullPath(directory);
                return uuid is null
                    ? new RepositoryDirectoryInspection(null, fullPath)
                    : new RepositoryDirectoryInspection(new ScannedRepository(name, fullPath, uuid), null);
            }
            finally
            {
                RepositoryScanGate.Release();
            }
        });

        var inspected = await Task.WhenAll(tasks);
        var repositories = inspected
            .Where(result => result.Repository is not null)
            .Select(result => result.Repository!)
            .ToArray();
        var unverifiedPaths = inspected
            .Where(result => result.UnverifiedPath is not null)
            .Select(result => result.UnverifiedPath!)
            .ToHashSet(PathComparer);
        return new RepositoryRootScan(
            repositories,
            unverifiedPaths,
            directories.Length - repositories.Length - unverifiedPaths.Count);
    }

    private async Task<string?> TryGetRepositoryUuidAsync(
        string localPath,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _svnlook.GetRepositoryUuidAsync(localPath, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private async Task WriteAuthzBestEffortAsync(PortalState state, CancellationToken cancellationToken)
    {
        try
        {
            await _authFilesWriter.WriteAuthzAsync(state, cancellationToken);
            await _authFilesWriter.ReloadApacheAsync(cancellationToken);
        }
        catch
        {
        }
    }

    private static bool IsPathInsideRoot(string path, string root)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return relative == "." ||
            (!Path.IsPathRooted(relative) &&
             !relative.Equals("..", StringComparison.Ordinal) &&
             !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
             !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal));
    }

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static readonly SemaphoreSlim RepositoryScanGate = new(8, 8);

    private sealed record ScannedRepository(string Name, string LocalPath, string SvnUuid);

    private sealed record RepositoryDirectoryInspection(
        ScannedRepository? Repository,
        string? UnverifiedPath);

    private sealed record RepositoryRootScan(
        IReadOnlyList<ScannedRepository> Repositories,
        IReadOnlySet<string> UnverifiedRepositoryPaths,
        int IgnoredDirectories);

    private static bool CanCreateRepositories(PortalState state, Guid actorUserId) =>
        state.Users.Any(u =>
            u.Id == actorUserId &&
            u.IsActive &&
            u.Roles.CanCreateRepositories());

    private static bool CanSynchronizeRepositories(PortalState state, Guid actorUserId) =>
        RepositoryManagementEvaluator.CanSynchronizeRepositories(state, actorUserId);

    private static bool CanAdminRepository(PortalState state, Guid actorUserId, Guid repositoryId) =>
        RepositoryManagementEvaluator.CanAdminRepository(state, actorUserId, repositoryId);
}

public sealed record RepositorySynchronizationResult(
    int ScannedRepositories,
    int AddedRepositories,
    int ReconnectedRepositories,
    int MissingRepositories,
    int ConflictRepositories,
    int IgnoredDirectories,
    int InspectionFailures)
{
    public string ToSummary() =>
        $"scanned={ScannedRepositories}; added={AddedRepositories}; reconnected={ReconnectedRepositories}; " +
        $"missing={MissingRepositories}; conflicts={ConflictRepositories}; ignored={IgnoredDirectories}; " +
        $"inspectionFailures={InspectionFailures}";
}
