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
            claims.Add(new Claim(ClaimTypes.Role, nameof(PortalUserRoles.Owner)));
        }

        if (user.Roles.HasEffectiveRole(PortalUserRoles.AdminRepo))
        {
            claims.Add(new Claim(ClaimTypes.Role, nameof(PortalUserRoles.AdminRepo)));
        }

        if (user.Roles.HasEffectiveRole(PortalUserRoles.AdminSystem))
        {
            claims.Add(new Claim(ClaimTypes.Role, nameof(PortalUserRoles.AdminSystem)));
        }

        if (user.Roles.HasEffectiveRole(PortalUserRoles.AdminHooks))
        {
            claims.Add(new Claim(ClaimTypes.Role, nameof(PortalUserRoles.AdminHooks)));
        }

        if (user.Roles.HasEffectiveRole(PortalUserRoles.AdminUsers))
        {
            claims.Add(new Claim(ClaimTypes.Role, nameof(PortalUserRoles.AdminUsers)));
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
