namespace SvnHub.Domain;

public sealed record PortalUser(
    Guid Id,
    string UserName,
    string UiPasswordHash,
    string? SvnBcryptHash,
    bool IsActive,
    PortalUserRoles Roles,
    DateTimeOffset CreatedAt
);
