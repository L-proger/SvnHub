namespace SvnHub.Domain;

public static class PortalUserRoleExtensions
{
    public const PortalUserRoles LegacyAllAdmin =
        PortalUserRoles.RepoAdmin |
        PortalUserRoles.AdminSystem |
        PortalUserRoles.RepoHooks;

    public const string RepoAdminClaim = "repo.admin";
    public const string RepoCreateClaim = "repo.create";
    public const string RepoReadClaim = "repo.read";
    public const string RepoWriteClaim = "repo.write";
    public const string RepoHooksClaim = "repo.hooks";
    public const string AdminSystemClaim = "admin.system";
    public const string AdminUsersClaim = "admin.users";
    public const string OwnerClaim = "owner";

    public static bool HasEffectiveRole(this PortalUserRoles roles, PortalUserRoles role)
    {
        if (role == PortalUserRoles.Owner)
        {
            return roles.HasFlag(PortalUserRoles.Owner);
        }

        return roles.HasFlag(PortalUserRoles.Owner) || roles.HasFlag(role);
    }

    public static bool HasAnyAdminRole(this PortalUserRoles roles) =>
        roles.HasFlag(PortalUserRoles.RepoAdmin) ||
        roles.HasFlag(PortalUserRoles.AdminSystem) ||
        roles.HasFlag(PortalUserRoles.RepoHooks) ||
        roles.HasFlag(PortalUserRoles.AdminUsers) ||
        roles.HasFlag(PortalUserRoles.Owner) ||
        roles.HasFlag(PortalUserRoles.RepoCreate);

    public static bool CanCreateRepositories(this PortalUserRoles roles) =>
        roles.HasEffectiveRole(PortalUserRoles.RepoCreate);

    public static bool HasGlobalRepositoryRead(this PortalUserRoles roles) =>
        roles.HasEffectiveRole(PortalUserRoles.RepoRead) ||
        roles.HasEffectiveRole(PortalUserRoles.RepoWrite);

    public static bool HasGlobalRepositoryWrite(this PortalUserRoles roles) =>
        roles.HasEffectiveRole(PortalUserRoles.RepoWrite);

    public static bool HasGlobalRepositoryAdmin(this PortalUserRoles roles) =>
        roles.HasEffectiveRole(PortalUserRoles.RepoAdmin);

    public static bool HasGlobalRepositoryHooks(this PortalUserRoles roles) =>
        roles.HasEffectiveRole(PortalUserRoles.RepoHooks);

    public static PortalUserRoles NormalizeLegacyRoles(this PortalUserRoles roles)
    {
        if ((roles & LegacyAllAdmin) == LegacyAllAdmin)
        {
            roles |= PortalUserRoles.Owner;
        }

        return roles;
    }
}
