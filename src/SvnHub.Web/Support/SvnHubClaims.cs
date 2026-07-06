using System.Security.Claims;
using SvnHub.Domain;

namespace SvnHub.Web.Support;

public static class SvnHubClaims
{
    public static ClaimsPrincipal CreatePrincipal(PortalUser user, string authenticationType)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString("D")),
            new(ClaimTypes.Name, user.UserName),
        };

        if (user.Roles.HasFlag(PortalUserRoles.Owner))
        {
            claims.Add(new Claim(ClaimTypes.Role, PortalUserRoleExtensions.OwnerClaim));
        }

        if (user.Roles.HasEffectiveRole(PortalUserRoles.RepoAdmin))
        {
            claims.Add(new Claim(ClaimTypes.Role, PortalUserRoleExtensions.RepoAdminClaim));
        }

        if (user.Roles.HasEffectiveRole(PortalUserRoles.AdminSystem))
        {
            claims.Add(new Claim(ClaimTypes.Role, PortalUserRoleExtensions.AdminSystemClaim));
        }

        if (user.Roles.HasEffectiveRole(PortalUserRoles.RepoHooks))
        {
            claims.Add(new Claim(ClaimTypes.Role, PortalUserRoleExtensions.RepoHooksClaim));
        }

        if (user.Roles.HasEffectiveRole(PortalUserRoles.AdminUsers))
        {
            claims.Add(new Claim(ClaimTypes.Role, PortalUserRoleExtensions.AdminUsersClaim));
        }

        if (user.Roles.CanCreateRepositories())
        {
            claims.Add(new Claim(ClaimTypes.Role, PortalUserRoleExtensions.RepoCreateClaim));
        }

        if (user.Roles.HasGlobalRepositoryRead())
        {
            claims.Add(new Claim(ClaimTypes.Role, PortalUserRoleExtensions.RepoReadClaim));
        }

        if (user.Roles.HasGlobalRepositoryWrite())
        {
            claims.Add(new Claim(ClaimTypes.Role, PortalUserRoleExtensions.RepoWriteClaim));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType));
    }

    public static bool SameIdentityAndRoles(ClaimsPrincipal? left, ClaimsPrincipal right)
    {
        if (left is null)
        {
            return false;
        }

        var leftName = left.FindFirstValue(ClaimTypes.Name);
        var rightName = right.FindFirstValue(ClaimTypes.Name);
        if (!string.Equals(leftName, rightName, StringComparison.Ordinal))
        {
            return false;
        }

        var leftId = left.FindFirstValue(ClaimTypes.NameIdentifier);
        var rightId = right.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.Equals(leftId, rightId, StringComparison.Ordinal))
        {
            return false;
        }

        var leftRoles = left.FindAll(ClaimTypes.Role).Select(c => c.Value).Order(StringComparer.Ordinal).ToArray();
        var rightRoles = right.FindAll(ClaimTypes.Role).Select(c => c.Value).Order(StringComparer.Ordinal).ToArray();
        return leftRoles.SequenceEqual(rightRoles, StringComparer.Ordinal);
    }
}
