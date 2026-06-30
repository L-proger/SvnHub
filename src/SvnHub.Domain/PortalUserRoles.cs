namespace SvnHub.Domain;

[Flags]
public enum PortalUserRoles
{
    None = 0,

    AdminRepo = 1 << 0,
    AdminSystem = 1 << 1,
    AdminHooks = 1 << 2,
    AdminUsers = 1 << 3,
    Owner = 1 << 4,
    RepoCreator = 1 << 5,

    AllAdmin = AdminRepo | AdminSystem | AdminHooks | AdminUsers | Owner | RepoCreator,
}
