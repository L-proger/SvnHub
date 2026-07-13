using SvnHub.Domain;

namespace SvnHub.App.Services;

public static class RepositoryAccessEvaluator
{
    public static AccessLevel GetAccess(PortalState state, Guid userId, Guid repositoryId, string path)
    {
        var user = state.Users.FirstOrDefault(u => u.Id == userId);
        if (user is null || !user.IsActive)
        {
            return AccessLevel.None;
        }

        var repo = state.Repositories.FirstOrDefault(r => r.Id == repositoryId);
        if (repo is null || !repo.IsAvailable)
        {
            return AccessLevel.None;
        }

        if (user.Roles.HasFlag(PortalUserRoles.Owner))
        {
            return AccessLevel.Write;
        }

        var normalized = NormalizePath(path);
        var groupIds = ExpandGroupIdsForUser(state, userId).ToHashSet();
        var access = GetInheritedAccess(repo, user.Roles);

        foreach (var rule in state.PermissionRules)
        {
            if (rule.RepositoryId != repositoryId)
            {
                continue;
            }

            if (rule.Access is not (AccessLevel.None or AccessLevel.Read or AccessLevel.Write))
            {
                continue;
            }

            if (!IsPathUnder(normalized, rule.Path))
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
