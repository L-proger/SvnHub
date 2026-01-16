using System.Text;

namespace SvnHub.Web.Support;

public static class SvnCheckoutUrl
{
    public static string? Build(string? svnBaseUrl, string repoName, string svnPath)
    {
        if (string.IsNullOrWhiteSpace(svnBaseUrl))
        {
            return null;
        }

        var baseUrl = svnBaseUrl.Trim().TrimEnd('/');

        var normalizedPath = NormalizeSvnPath(svnPath);

        var sb = new StringBuilder(baseUrl.Length + repoName.Length + normalizedPath.Length + 8);
        sb.Append(baseUrl);
        sb.Append('/');
        sb.Append(Uri.EscapeDataString(repoName));

        if (normalizedPath != "/")
        {
            var parts = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                sb.Append('/');
                sb.Append(Uri.EscapeDataString(part));
            }
        }

        return sb.ToString();
    }

    private static string NormalizeSvnPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/")
        {
            return "/";
        }

        var p = path.Trim();
        if (!p.StartsWith('/'))
        {
            p = "/" + p;
        }

        while (p.Contains("//", StringComparison.Ordinal))
        {
            p = p.Replace("//", "/", StringComparison.Ordinal);
        }

        if (p.Length > 1 && p.EndsWith('/'))
        {
            p = p.TrimEnd('/');
        }

        return p;
    }
}
