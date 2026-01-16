namespace SvnHub.Domain;

public sealed record Group(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt
);

