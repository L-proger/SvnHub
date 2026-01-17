namespace SvnHub.Domain;

[Flags]
public enum PortalUserRoles
{
    None = 0,

    AdminRepo = 1 << 0,
    AdminSystem = 1 << 1,
    AdminHooks = 1 << 2,

    AllAdmin = AdminRepo | AdminSystem | AdminHooks,
}

