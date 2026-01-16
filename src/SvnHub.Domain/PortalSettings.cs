namespace SvnHub.Domain;

public sealed record PortalSettings
{
    public string RepositoriesRootPath { get; init; } = "";
}

