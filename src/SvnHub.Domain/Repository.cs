namespace SvnHub.Domain;

public sealed record Repository(
    Guid Id,
    string Name,
    string LocalPath,
    DateTimeOffset CreatedAt,
    bool IsArchived,
    AccessLevel? AuthenticatedDefaultAccess = null
);
