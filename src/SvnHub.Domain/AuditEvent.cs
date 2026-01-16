namespace SvnHub.Domain;

public sealed record AuditEvent(
    Guid Id,
    DateTimeOffset CreatedAt,
    Guid? ActorUserId,
    string Action,
    string Target,
    bool Success,
    string? Details
);

