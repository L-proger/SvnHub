namespace SvnHub.Domain;

public sealed record GroupMember(
    Guid GroupId,
    Guid UserId
);

