namespace SvnHub.Domain;

public sealed record GroupGroupMember(
    Guid GroupId,
    Guid ChildGroupId
);

