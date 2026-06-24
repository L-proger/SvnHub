namespace SvnHub.Domain;

public static class PortalUserRoleExtensions
{
    public const PortalUserRoles LegacyAllAdmin =
        PortalUserRoles.AdminRepo |
        PortalUserRoles.AdminSystem |
        PortalUserRoles.AdminHooks;

    public static bool HasEffectiveRole(this PortalUserRoles roles, PortalUserRoles role)
    {
        if (role == PortalUserRoles.Owner)
        {
            return roles.HasFlag(PortalUserRoles.Owner);
        }

        return roles.HasFlag(PortalUserRoles.Owner) || roles.HasFlag(role);
    }

    public static bool HasAnyAdminRole(this PortalUserRoles roles) =>
        roles.HasFlag(PortalUserRoles.AdminRepo) ||
        roles.HasFlag(PortalUserRoles.AdminSystem) ||
        roles.HasFlag(PortalUserRoles.AdminHooks) ||
        roles.HasFlag(PortalUserRoles.AdminUsers) ||
        roles.HasFlag(PortalUserRoles.Owner);

    public static PortalUserRoles NormalizeLegacyRoles(this PortalUserRoles roles)
    {
        if ((roles & LegacyAllAdmin) == LegacyAllAdmin)
        {
            roles |= PortalUserRoles.Owner;
        }

        if (roles.HasFlag(PortalUserRoles.AdminSystem))
        {
            roles |= PortalUserRoles.AdminUsers;
        }

        return roles;
    }
}
