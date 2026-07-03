using System.Security.Cryptography;
using System.Text;

namespace SvnHub.Web.Support;

internal static class HttpEntityTags
{
    public static string WeakRepositoryFileTag(string repoName, long revision, string path)
    {
        var normalizedPath = RepositoryPath.Normalize(path);
        var input = string.Join('\n', repoName, revision.ToString(System.Globalization.CultureInfo.InvariantCulture), normalizedPath);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));

        return $"W/\"{Convert.ToHexString(hash).ToLowerInvariant()}\"";
    }
}
