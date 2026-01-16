using System.Text.RegularExpressions;

namespace SvnHub.Domain;

public static partial class Validation
{
    private static readonly Regex RepoNameRegex = RepoNameRegexFactory();
    private static readonly Regex UserNameRegex = UserNameRegexFactory();
    private static readonly Regex GroupNameRegex = GroupNameRegexFactory();

    public static bool IsValidRepositoryName(string name) => RepoNameRegex.IsMatch(name);

    public static bool IsValidUserName(string userName) => UserNameRegex.IsMatch(userName);

    public static bool IsValidGroupName(string name) => GroupNameRegex.IsMatch(name);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{1,80}$", RegexOptions.CultureInvariant)]
    private static partial Regex RepoNameRegexFactory();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{1,80}$", RegexOptions.CultureInvariant)]
    private static partial Regex UserNameRegexFactory();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{1,80}$", RegexOptions.CultureInvariant)]
    private static partial Regex GroupNameRegexFactory();
}
