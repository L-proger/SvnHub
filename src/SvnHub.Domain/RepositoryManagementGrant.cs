namespace SvnHub.Domain;

public sealed record RepositoryManagementGrant(
    Guid Id,
    Guid RepositoryId,
    SubjectType SubjectType,
    Guid SubjectId,
    RepositoryManagementRole Role,
    DateTimeOffset CreatedAt
);
