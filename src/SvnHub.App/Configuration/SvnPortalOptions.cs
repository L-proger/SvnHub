namespace SvnHub.App.Configuration;

public sealed class SvnHubOptions
{
    public string DataDirectory { get; set; } = "data";

    public string RepositoriesRootPath { get; set; } = "/srv/svn/repos";

    public string HtpasswdCommand { get; set; } = "htpasswd";

    public string SvnadminCommand { get; set; } = "svnadmin";

    public string SvnCommand { get; set; } = "svn";

    public string SvnmuccCommand { get; set; } = "svnmucc";

    public string SvnlookCommand { get; set; } = "svnlook";

    public string ApacheReloadProgram { get; set; } = "systemctl";

    public string ApacheReloadArguments { get; set; } = "reload apache2";
}
