namespace SvnHub.Domain;

public sealed record PermissionRule(
    Guid Id,
    Guid RepositoryId,
    string Path,
    SubjectType SubjectType,
    Guid SubjectId,
    AccessLevel Access,
    DateTimeOffset CreatedAt
);
