using SvnHub.App.Storage;
using SvnHub.App.Support;
using SvnHub.Domain;

namespace SvnHub.App.Services;

public sealed class RepositoryManagementService
{
    private readonly IPortalStore _store;

    public RepositoryManagementService(IPortalStore store)
    {
        _store = store;
    }

    public IReadOnlyList<RepositoryManagementGrant> ListGrants()
    {
        var state = _store.Read();
        return state.RepositoryManagementGrants
            .OrderBy(g => g.RepositoryId)
            .ThenBy(g => g.SubjectType)
            .ThenBy(g => g.SubjectId)
            .ToArray();
    }

    public bool CanAdminRepository(Guid actorUserId, Guid repositoryId)
    {
        var state = _store.Read();
        return RepositoryManagementEvaluator.CanAdminRepository(state, actorUserId, repositoryId);
    }

    public bool CanManageRepositoryHooks(Guid actorUserId, Guid repositoryId)
    {
        var state = _store.Read();
        return RepositoryManagementEvaluator.CanManageRepositoryHooks(state, actorUserId, repositoryId);
    }

    public bool WouldDisablingInheritedManagementGrantsRemoveAdmin(Guid actorUserId, Guid repositoryId)
    {
        var state = _store.Read();
        var repo = state.Repositories.FirstOrDefault(r => r.Id == repositoryId);
        if (repo is null || repo.IsArchived || !repo.IncludeInheritedManagementGrants)
        {
            return false;
        }

        if (!RepositoryManagementEvaluator.CanAdminRepository(state, actorUserId, repositoryId))
        {
            return false;
        }

        var simulatedState = state with
        {
            Repositories = state.Repositories
                .Select(r => r.Id == repositoryId ? r with { IncludeInheritedManagementGrants = false } : r)
                .ToList(),
        };

        return !RepositoryManagementEvaluator.CanAdminRepository(simulatedState, actorUserId, repositoryId);
    }

    public bool WouldDeletingGrantRemoveAdmin(Guid actorUserId, Guid grantId)
    {
        var state = _store.Read();
        var grant = state.RepositoryManagementGrants.FirstOrDefault(g => g.Id == grantId);
        if (grant is null)
        {
            return false;
        }

        if (!RepositoryManagementEvaluator.CanAdminRepository(state, actorUserId, grant.RepositoryId))
        {
            return false;
        }

        var simulatedState = state with
        {
            RepositoryManagementGrants = state.RepositoryManagementGrants
                .Where(g => g.Id != grantId)
                .ToList(),
        };

        return !RepositoryManagementEvaluator.CanAdminRepository(
            simulatedState,
            actorUserId,
            grant.RepositoryId);
    }

    public async Task<OperationResult<RepositoryManagementGrant>> AddGrantAsync(
        Guid actorUserId,
        Guid repositoryId,
        SubjectType subjectType,
        Guid subjectId,
        RepositoryManagementRole role,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var state = _store.Read();
        if (state.Repositories.All(r => r.Id != repositoryId || r.IsArchived))
        {
            return OperationResult<RepositoryManagementGrant>.Fail("Repository not found.");
        }

        if (!RepositoryManagementEvaluator.CanAdminRepository(state, actorUserId, repositoryId))
        {
            return OperationResult<RepositoryManagementGrant>.Fail("You don't have permission to manage repository administrators.");
        }

        var subjectExists = subjectType switch
        {
            SubjectType.User => state.Users.Any(u => u.Id == subjectId),
            SubjectType.Group => state.Groups.Any(g => g.Id == subjectId),
            _ => false,
        };

        if (!subjectExists)
        {
            return OperationResult<RepositoryManagementGrant>.Fail("Subject not found.");
        }

        if (role is not RepositoryManagementRole.Admin)
        {
            return OperationResult<RepositoryManagementGrant>.Fail("Invalid repository management role.");
        }

        var existing = state.RepositoryManagementGrants.FirstOrDefault(g =>
            g.RepositoryId == repositoryId &&
            g.SubjectType == subjectType &&
            g.SubjectId == subjectId);

        var now = DateTimeOffset.UtcNow;
        RepositoryManagementGrant grant;
        List<RepositoryManagementGrant> grants;
        if (existing is null)
        {
            grant = new RepositoryManagementGrant(
                Id: Guid.NewGuid(),
                RepositoryId: repositoryId,
                SubjectType: subjectType,
                SubjectId: subjectId,
                Role: role,
                CreatedAt: now);
            grants = [..state.RepositoryManagementGrants, grant];
        }
        else
        {
            grant = existing with { Role = role };
            grants = state.RepositoryManagementGrants
                .Select(g => g.Id == existing.Id ? grant : g)
                .ToList();
        }

        var newState = state with
        {
            RepositoryManagementGrants = grants,
            AuditEvents =
            [
                ..state.AuditEvents,
                new AuditEvent(
                    Id: Guid.NewGuid(),
                    CreatedAt: now,
                    ActorUserId: actorUserId,
                    Action: existing is null ? "repo.management.add" : "repo.management.update",
                    Target: repositoryId.ToString("D"),
                    Success: true,
                    Details: $"{subjectType} {subjectId} {role}"
                ),
            ],
        };

        _store.Write(newState);
        await Task.CompletedTask;
        return OperationResult<RepositoryManagementGrant>.Ok(grant);
    }

    public async Task<OperationResult> DeleteGrantAsync(
        Guid actorUserId,
        Guid grantId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var state = _store.Read();
        var existing = state.RepositoryManagementGrants.FirstOrDefault(g => g.Id == grantId);
        if (existing is null)
        {
            return OperationResult.Fail("Repository management grant not found.");
        }

        if (!RepositoryManagementEvaluator.CanAdminRepository(state, actorUserId, existing.RepositoryId))
        {
            return OperationResult.Fail("You don't have permission to manage repository administrators.");
        }

        var newState = state with
        {
            RepositoryManagementGrants = state.RepositoryManagementGrants.Where(g => g.Id != grantId).ToList(),
            AuditEvents =
            [
                ..state.AuditEvents,
                new AuditEvent(
                    Id: Guid.NewGuid(),
                    CreatedAt: DateTimeOffset.UtcNow,
                    ActorUserId: actorUserId,
                    Action: "repo.management.delete",
                    Target: existing.RepositoryId.ToString("D"),
                    Success: true,
                    Details: $"{existing.SubjectType} {existing.SubjectId}"
                ),
            ],
        };

        _store.Write(newState);
        await Task.CompletedTask;
        return OperationResult.Ok();
    }
}
