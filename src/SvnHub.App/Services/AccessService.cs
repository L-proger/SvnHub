using System.Security.Claims;
using SvnHub.App.Storage;
using SvnHub.Domain;

namespace SvnHub.App.Services;

public sealed class AccessService
{
    private readonly IPortalStore _store;

    public AccessService(IPortalStore store)
    {
        _store = store;
    }

    public AccessLevel GetAccess(Guid userId, Guid repositoryId, string path)
    {
        var state = _store.Read();
        var user = state.Users.FirstOrDefault(u => u.Id == userId);
        if (user is null || !user.IsActive)
        {
            return AccessLevel.None;
        }

        if (user.Roles.HasFlag(PortalUserRoles.AdminRepo))
        {
            return AccessLevel.Write;
        }

        var normalized = NormalizePath(path);

        var groupIds = state.GroupMembers
            .Where(m => m.UserId == userId)
            .Select(m => m.GroupId)
            .Distinct()
            .ToArray();

        var userRule = GetBestMatchRule(
            state.PermissionRules,
            repositoryId,
            normalized,
            SubjectType.User,
            userId);

        var groupRules = groupIds
            .Select(gid => GetBestMatchRule(state.PermissionRules, repositoryId, normalized, SubjectType.Group, gid))
            .Where(r => r is not null)
            .Cast<PermissionRule>()
            .ToArray();

        // Default: every authenticated user can read everything unless explicitly denied/overridden.
        var baseline = AccessLevel.Read;

        if (userRule is null && groupRules.Length == 0)
        {
            return baseline;
        }

        var candidates = new List<PermissionRule>();
        if (userRule is not null)
        {
            candidates.Add(userRule);
        }
        candidates.AddRange(groupRules);

        var bestPathLen = candidates.Max(r => r.Path.Length);
        var bestByPath = candidates.Where(r => r.Path.Length == bestPathLen).ToArray();

        // If there's an explicit user rule for the most-specific path, it wins over group rules.
        var bestUserRule = bestByPath.FirstOrDefault(r => r.SubjectType == SubjectType.User);
        if (bestUserRule is not null)
        {
            return bestUserRule.Access;
        }

        // Group rules at the same specificity: any explicit deny wins, otherwise max access.
        if (bestByPath.Any(r => r.Access == AccessLevel.None))
        {
            return AccessLevel.None;
        }

        return bestByPath.Max(r => r.Access);
    }

    private static PermissionRule? GetBestMatchRule(
        IReadOnlyList<PermissionRule> rules,
        Guid repositoryId,
        string normalizedPath,
        SubjectType subjectType,
        Guid subjectId
    )
    {
        PermissionRule? best = null;

        foreach (var rule in rules)
        {
            if (rule.RepositoryId != repositoryId)
            {
                continue;
            }

            if (rule.SubjectType != subjectType || rule.SubjectId != subjectId)
            {
                continue;
            }

            if (!IsPathUnder(normalizedPath, rule.Path))
            {
                continue;
            }

            if (best is null)
            {
                best = rule;
                continue;
            }

            if (rule.Path.Length > best.Path.Length
                || (rule.Path.Length == best.Path.Length && rule.Access > best.Access)
                || (rule.Path.Length == best.Path.Length && rule.Access == best.Access && rule.CreatedAt > best.CreatedAt))
            {
                best = rule;
            }
        }

        return best;
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

    public static Guid? GetUserIdFromClaimsPrincipal(ClaimsPrincipal principal)
    {
        var idStr = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(idStr, out var id) ? id : null;
    }
}
