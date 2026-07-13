using System.Text.Json.Serialization;

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
    public string? SvnUuid { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<RepositoryAvailability>))]
    public RepositoryAvailability Availability { get; init; } = RepositoryAvailability.Available;

    public DateTimeOffset? LastSeenAt { get; init; }

    public DateTimeOffset? MissingSince { get; init; }

    public string? AvailabilityDetails { get; init; }

    public string? DetectedPath { get; init; }

    public string? DetectedSvnUuid { get; init; }

    [JsonIgnore]
    public bool IsAvailable => !IsArchived && Availability == RepositoryAvailability.Available;

    public bool IncludeInheritedContentGrants { get; init; } = true;

    public bool IncludeInheritedManagementGrants { get; init; } = true;
}
