namespace SvnHub.Web.Support;

public static class TortoiseSvnCommandUrl
{
    public static string? BuildCheckoutUrl(string? svnUrl)
    {
        if (string.IsNullOrWhiteSpace(svnUrl))
        {
            return null;
        }

        return $"tsvncmd:command:checkout?url:{Uri.EscapeDataString(svnUrl.Trim())}";
    }
}
