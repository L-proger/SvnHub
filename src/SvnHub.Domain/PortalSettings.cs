namespace SvnHub.Domain;

public sealed record PortalSettings
{
    public int RoleSchemaVersion { get; init; } = 0;

    public string OrganizationName { get; init; } = "";

    public string CustomFaviconFileName { get; init; } = "";

    public long CustomFaviconVersion { get; init; } = 0;

    public string RepositoriesRootPath { get; init; } = "";

    /// <summary>
    /// Public base URL for SVN repositories (e.g. https://svn.example.com/svn).
    /// Used to build "checkout" URLs for files/folders in the UI.
    /// </summary>
    public string SvnBaseUrl { get; init; } = "";

    /// <summary>
    /// Distinguishes an explicitly disabled public URL from an older configuration
    /// that has not overridden the deployment default yet.
    /// </summary>
    public bool SvnBaseUrlConfigured { get; init; } = false;

    /// <summary>
    /// Legacy or alternate SVN base URLs that should still be treated as internal SvnHub repository URLs.
    /// Used when resolving svn:externals.
    /// </summary>
    public IReadOnlyList<string> SvnBaseUrlAliases { get; init; } = [];

    /// <summary>
    /// Maximum allowed upload size for the web UI (bytes).
    /// This is enforced by the server and by request body limits.
    /// </summary>
    public long MaxUploadBytes { get; init; } = 0;

    public bool IndexingEnabled { get; init; } = false;

    public int IndexingScanIntervalSeconds { get; init; } = 300;

    public int IndexingMaxRevisionsPerRepositoryPerScan { get; init; } = 200;
}
