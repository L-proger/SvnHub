namespace SvnHub.App.Configuration;

public sealed class SvnHubOptions
{
    public const long DefaultMaxPreviewBytes = 50L * 1024 * 1024;
    public const long MaxAllowedPreviewBytes = 2L * 1024 * 1024 * 1024;

    public string DataDirectory { get; set; } = "data";

    public string IndexDatabasePath { get; set; } = "";

    public string RepositoriesRootPath { get; set; } = "/srv/svn/repos";

    public string SvnBaseUrl { get; set; } = "http://localhost:8080/svn";

    public List<string> SvnBaseUrlAliases { get; set; } = [];

    public string HtpasswdCommand { get; set; } = "htpasswd";

    public string SvnadminCommand { get; set; } = "svnadmin";

    public string SvnCommand { get; set; } = "svn";

    public string SvnmuccCommand { get; set; } = "svnmucc";

    public string SvnlookCommand { get; set; } = "svnlook";

    public string ApacheReloadProgram { get; set; } = "systemctl";

    public string ApacheReloadArguments { get; set; } = "reload apache2";

    public long MaxPreviewBytes { get; set; } = DefaultMaxPreviewBytes;

    public long GetEffectiveMaxPreviewBytes()
    {
        if (MaxPreviewBytes <= 0)
        {
            return DefaultMaxPreviewBytes;
        }

        if (MaxPreviewBytes > MaxAllowedPreviewBytes)
        {
            return MaxAllowedPreviewBytes;
        }

        return MaxPreviewBytes;
    }
}
