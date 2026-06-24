namespace SvnHub.Domain;

public sealed record ApiToken(
    Guid Id,
    Guid UserId,
    string Name,
    string TokenHash,
    string TokenPrefix,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? LastUsedAt
)
{
    public bool IsActive(DateTimeOffset now) =>
        RevokedAt is null && (ExpiresAt is null || ExpiresAt > now);
}
