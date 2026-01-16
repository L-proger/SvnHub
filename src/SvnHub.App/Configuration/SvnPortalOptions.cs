namespace SvnHub.App.Configuration;

public sealed class SvnHubOptions
{
    public string DataFilePath { get; set; } = "data/SvnHub.json";

    public string RepositoriesRootPath { get; set; } = "/srv/svn/repos";

    public string AuthzPath { get; set; } = "/etc/svn/authz";

    public string HtpasswdPath { get; set; } = "/etc/svn/htpasswd";

    public string HtpasswdCommand { get; set; } = "htpasswd";

    public string SvnadminCommand { get; set; } = "svnadmin";

    public string SvnCommand { get; set; } = "svn";

    public string SvnlookCommand { get; set; } = "svnlook";

    public string ApacheReloadProgram { get; set; } = "systemctl";

    public string ApacheReloadArguments { get; set; } = "reload apache2";
}
