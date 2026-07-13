using SvnHub.Domain;

namespace SvnHub.App.Services;

public static class RepositoryAccessEvaluator
{
    public static AccessLevel GetAccess(PortalState state, Guid userId, Guid repositoryId, string path)
    {
        var user = state.Users.FirstOrDefault(candidate => candidate.Id == userId);
        if (user is null || !user.IsActive)
        {
            return AccessLevel.None;
        }

        var repository = state.Repositories.FirstOrDefault(candidate => candidate.Id == repositoryId);
        if (repository is null || !repository.IsAvailable)
        {
            return AccessLevel.None;
        }

        return EvaluateAccess(
            repository,
            user.Roles,
            userId,
            ExpandGroupIdsForUser(state, userId).ToHashSet(),
            path,
            state.PermissionRules);
    }

    public static EvaluationContext CreateContext(PortalState state, Guid userId) =>
        new(state, userId);

    public sealed class EvaluationContext
    {
        private readonly Guid _userId;
        private readonly PortalUserRoles _roles;
        private readonly bool _isActive;
        private readonly IReadOnlyDictionary<Guid, Repository> _repositories;
        private readonly IReadOnlyDictionary<Guid, PermissionRule[]> _rulesByRepository;
        private readonly HashSet<Guid> _groupIds;

        internal EvaluationContext(PortalState state, Guid userId)
        {
            _userId = userId;
            var user = state.Users.FirstOrDefault(candidate => candidate.Id == userId);
            _roles = user?.Roles ?? PortalUserRoles.None;
            _isActive = user?.IsActive == true;
            _repositories = state.Repositories.ToDictionary(repository => repository.Id);
            _rulesByRepository = state.PermissionRules
                .GroupBy(rule => rule.RepositoryId)
                .ToDictionary(group => group.Key, group => group.ToArray());
            _groupIds = ExpandGroupIdsForUser(state, userId).ToHashSet();
        }

        public AccessLevel GetAccess(Guid repositoryId, string path)
        {
            if (!_isActive ||
                !_repositories.TryGetValue(repositoryId, out var repository) ||
                !repository.IsAvailable)
            {
                return AccessLevel.None;
            }

            return EvaluateAccess(
                repository,
                _roles,
                _userId,
                _groupIds,
                path,
                _rulesByRepository.GetValueOrDefault(repositoryId) ?? []);
        }
    }

    private static AccessLevel EvaluateAccess(
        Repository repository,
        PortalUserRoles roles,
        Guid userId,
        HashSet<Guid> groupIds,
        string path,
        IEnumerable<PermissionRule> rules)
    {
        if (roles.HasFlag(PortalUserRoles.Owner))
        {
            return AccessLevel.Write;
        }

        var normalized = NormalizePath(path);
        var access = GetInheritedAccess(repository, roles);

        foreach (var rule in rules)
        {
            if (rule.RepositoryId != repository.Id ||
                rule.Access is not (AccessLevel.None or AccessLevel.Read or AccessLevel.Write) ||
                !IsPathUnder(normalized, rule.Path))
            {
                continue;
            }

            var subjectMatches = rule.SubjectType switch
            {
                SubjectType.User => rule.SubjectId == userId,
                SubjectType.Group => groupIds.Contains(rule.SubjectId),
                _ => false,
            };

            if (subjectMatches)
            {
                access = rule.Access;
            }
        }

        return access;
    }

    private static AccessLevel GetInheritedAccess(Repository repo, PortalUserRoles roles)
    {
        if (!repo.IncludeInheritedContentGrants)
        {
            return AccessLevel.None;
        }

        if (roles.HasGlobalRepositoryWrite())
        {
            return AccessLevel.Write;
        }

        return roles.HasGlobalRepositoryRead() ? AccessLevel.Read : AccessLevel.None;
    }

    private static bool IsPathUnder(string requested, string rulePath)
    {
        if (rulePath == "/")
        {
            return true;
        }

        if (!requested.StartsWith(rulePath, StringComparison.Ordinal))
        {
            return false;
        }

        return requested.Length == rulePath.Length || requested[rulePath.Length] == '/';
    }

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

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/")
        {
            return "/";
        }

        var p = path.Trim();
        if (!p.StartsWith('/'))
        {
            p = "/" + p;
        }

        while (p.Contains("//", StringComparison.Ordinal))
        {
            p = p.Replace("//", "/", StringComparison.Ordinal);
        }

        if (p.Length > 1 && p.EndsWith('/'))
        {
            p = p.TrimEnd('/');
        }

        return p;
    }
}
