namespace SvnHub.Domain;

public sealed record PortalSettings
{
    public string RepositoriesRootPath { get; init; } = "";

    /// <summary>
    /// Public base URL for SVN repositories (e.g. https://svn.example.com/svn).
    /// Used to build "checkout" URLs for files/folders in the UI.
    /// </summary>
    public string SvnBaseUrl { get; init; } = "";
}
