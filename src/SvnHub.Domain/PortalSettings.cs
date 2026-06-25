namespace SvnHub.Domain;

public sealed record PortalSettings
{
    public int RoleSchemaVersion { get; init; } = 0;

    public string OrganizationName { get; init; } = "";

    public string CustomFaviconFileName { get; init; } = "";

    public long CustomFaviconVersion { get; init; } = 0;

    public string RepositoriesRootPath { get; init; } = "";

    /// <summary>
    /// Default access granted to authenticated users for repositories, unless overridden per-repository.
    /// Used to generate authz rules for $authenticated.
    /// </summary>
    public AccessLevel DefaultAuthenticatedAccess { get; init; } = AccessLevel.Write;

    /// <summary>
    /// Public base URL for SVN repositories (e.g. https://svn.example.com/svn).
    /// Used to build "checkout" URLs for files/folders in the UI.
    /// </summary>
    public string SvnBaseUrl { get; init; } = "";

    /// <summary>
    /// Maximum allowed upload size for the web UI (bytes).
    /// This is enforced by the server and by request body limits.
    /// </summary>
    public long MaxUploadBytes { get; init; } = 0;

    public bool IndexingEnabled { get; init; } = false;

    public int IndexingScanIntervalSeconds { get; init; } = 300;

    public int IndexingMaxRevisionsPerRepositoryPerScan { get; init; } = 200;
}
