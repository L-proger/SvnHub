using SvnHub.Domain;

namespace SvnHub.App.Services;

public static class RepositoryManagementEvaluator
{
    public static RepositoryManagementRole? GetRole(PortalState state, Guid userId, Guid repositoryId)
    {
        var user = state.Users.FirstOrDefault(u => u.Id == userId);
        if (user is null || !user.IsActive)
        {
            return null;
        }

        var repo = state.Repositories.FirstOrDefault(r => r.Id == repositoryId);
        if (repo is null || repo.IsArchived)
        {
            return null;
        }

        if (IsOwner(user))
        {
            return RepositoryManagementRole.Admin;
        }

        var groupIds = ExpandGroupIdsForUser(state, userId).ToHashSet();
        RepositoryManagementRole? best = null;

        if (repo.IncludeInheritedManagementGrants && user.Roles.HasGlobalRepositoryAdmin())
        {
            best = Better(best, RepositoryManagementRole.Admin);
        }

        foreach (var grant in state.RepositoryManagementGrants.Where(g => g.RepositoryId == repositoryId))
        {
            if (!AppliesTo(grant.SubjectType, grant.SubjectId, userId, groupIds))
            {
                continue;
            }

            best = Better(best, grant.Role);
        }

        return best;
    }

    public static bool CanDiscoverRepositories(PortalState state, Guid actorUserId) =>
        state.Users.Any(u =>
            u.Id == actorUserId &&
            u.IsActive &&
            u.Roles.HasEffectiveRole(PortalUserRoles.AdminSystem));

    public static bool CanAdminRepository(PortalState state, Guid actorUserId, Guid repositoryId) =>
        GetRole(state, actorUserId, repositoryId) is { } role && role >= RepositoryManagementRole.Admin;

    public static bool CanManageRepositoryHooks(PortalState state, Guid actorUserId, Guid repositoryId)
    {
        var user = state.Users.FirstOrDefault(u => u.Id == actorUserId);
        if (user is null || !user.IsActive)
        {
            return false;
        }

        var repo = state.Repositories.FirstOrDefault(r => r.Id == repositoryId);
        if (repo is null || repo.IsArchived)
        {
            return false;
        }

        if (IsOwner(user))
        {
            return true;
        }

        return user.Roles.HasGlobalRepositoryHooks() && CanAdminRepository(state, actorUserId, repositoryId);
    }

    private static bool IsOwner(PortalUser user) =>
        user.Roles.HasFlag(PortalUserRoles.Owner);

    private static bool AppliesTo(
        SubjectType subjectType,
        Guid subjectId,
        Guid userId,
        IReadOnlySet<Guid> groupIds) =>
        subjectType switch
        {
            SubjectType.User => subjectId == userId,
            SubjectType.Group => groupIds.Contains(subjectId),
            _ => false,
        };

    private static RepositoryManagementRole Better(
        RepositoryManagementRole? current,
        RepositoryManagementRole candidate) =>
        current is null || candidate > current ? candidate : current.Value;

    private static Guid[] ExpandGroupIdsForUser(PortalState state, Guid userId)
    {
        var direct = state.GroupMembers
            .Where(m => m.UserId == userId)
            .Select(m => m.GroupId)
            .Distinct()
            .ToArray();

        if (direct.Length == 0 || state.GroupGroupMembers.Count == 0)
        {
            return direct;
        }

        var result = new HashSet<Guid>(direct);
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var edge in state.GroupGroupMembers)
            {
                if (!result.Contains(edge.ChildGroupId))
                {
                    continue;
                }

                if (result.Add(edge.GroupId))
                {
                    changed = true;
                }
            }
        }

        return result.ToArray();
    }
}
