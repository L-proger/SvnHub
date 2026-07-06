namespace SvnHub.Domain;

public sealed record Repository(
    Guid Id,
    string Name,
    string LocalPath,
    DateTimeOffset CreatedAt,
    bool IsArchived,
    IReadOnlyList<string>? Labels = null
)
{
    public bool IncludeInheritedContentGrants { get; init; } = true;

    public bool IncludeInheritedManagementGrants { get; init; } = true;
}
