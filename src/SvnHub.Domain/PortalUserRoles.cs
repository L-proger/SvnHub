namespace SvnHub.Domain;

[Flags]
public enum PortalUserRoles
{
    None = 0,

    RepoAdmin = 1 << 0,
    AdminSystem = 1 << 1,
    RepoHooks = 1 << 2,
    AdminUsers = 1 << 3,
    Owner = 1 << 4,
    RepoCreate = 1 << 5,
    RepoRead = 1 << 6,
    RepoWrite = 1 << 7,

    AllAdmin = RepoAdmin | AdminSystem | RepoHooks | AdminUsers | Owner | RepoCreate,
}
