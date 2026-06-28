using System.Reflection;

namespace SvnHub.Web.Support;

public static class SvnHubVersion
{
    private const string Unknown = "unknown";

    public static string InformationalVersion { get; } = GetInformationalVersion();

    public static string GitDescribe { get; } = GetMetadata("SvnHubGitDescribe") ?? InformationalVersion;

    public static string Commit { get; } = GetMetadata("SvnHubGitCommit") ?? Unknown;

    public static string ProductVersion { get; } = NormalizeProductVersion(GitDescribe);

    public static string DisplayVersion { get; } = string.IsNullOrWhiteSpace(GitDescribe) ? Unknown : GitDescribe;

    public static string BuildDetails { get; } = string.IsNullOrWhiteSpace(Commit) || Commit.Equals(Unknown, StringComparison.OrdinalIgnoreCase)
        ? DisplayVersion
        : $"{DisplayVersion} ({Commit})";

    private static string GetInformationalVersion()
    {
        var assembly = typeof(SvnHubVersion).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? Unknown;
    }

    private static string? GetMetadata(string key)
    {
        return typeof(SvnHubVersion).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            ?.Value;
    }

    private static string NormalizeProductVersion(string gitDescribe)
    {
        var value = gitDescribe.StartsWith('v') || gitDescribe.StartsWith('V')
            ? gitDescribe[1..]
            : gitDescribe;

        var separatorIndex = value.IndexOfAny(['-', '+']);
        return separatorIndex > 0 ? value[..separatorIndex] : value;
    }
}
