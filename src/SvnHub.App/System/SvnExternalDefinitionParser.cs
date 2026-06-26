using System.Text;

namespace SvnHub.App.System;

public static class SvnExternalDefinitionParser
{
    public static IReadOnlyList<SvnExternalDefinition> Parse(string parentPath, string value)
    {
        var normalizedParent = NormalizeRepositoryPath(parentPath);
        var rows = new List<SvnExternalDefinition>();
        foreach (var rawLine in value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var rawDefinition = rawLine.Trim();
            if (rawDefinition.Length == 0 || rawDefinition.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            rows.Add(ParseDefinition(normalizedParent, rawDefinition));
        }

        return rows;
    }

    private static SvnExternalDefinition ParseDefinition(string parentPath, string rawDefinition)
    {
        var tokens = Tokenize(rawDefinition);
        var revision = ExtractRevision(tokens);
        var urlIndex = tokens.FindIndex(IsUrlToken);

        string? targetPath = null;
        string? url = null;
        string? pegRevision = null;

        if (urlIndex >= 0)
        {
            (url, pegRevision) = SplitPegRevision(tokens[urlIndex]);
            targetPath = urlIndex == tokens.Count - 1
                ? tokens.FirstOrDefault()
                : tokens.ElementAtOrDefault(urlIndex + 1);
        }
        else
        {
            targetPath = tokens.FirstOrDefault();
        }

        targetPath = NullIfWhiteSpace(targetPath);
        url = NullIfWhiteSpace(url);

        return new SvnExternalDefinition(
            parentPath,
            targetPath,
            ResolveTargetPath(parentPath, targetPath),
            url,
            NullIfWhiteSpace(revision),
            NullIfWhiteSpace(pegRevision),
            !string.IsNullOrWhiteSpace(revision) || !string.IsNullOrWhiteSpace(pegRevision),
            rawDefinition);
    }

    private static List<string> Tokenize(string value)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        char? quote = null;
        var escaping = false;

        foreach (var ch in value)
        {
            if (escaping)
            {
                current.Append(ch);
                escaping = false;
                continue;
            }

            if (quote is not null)
            {
                if (ch == '\\')
                {
                    escaping = true;
                    continue;
                }

                if (ch == quote)
                {
                    quote = null;
                    continue;
                }

                current.Append(ch);
                continue;
            }

            if (ch is '"' or '\'')
            {
                quote = ch;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                FlushToken(tokens, current);
                continue;
            }

            current.Append(ch);
        }

        FlushToken(tokens, current);
        return tokens;
    }

    private static void FlushToken(List<string> tokens, StringBuilder current)
    {
        if (current.Length == 0)
        {
            return;
        }

        tokens.Add(current.ToString());
        current.Clear();
    }

    private static string? ExtractRevision(List<string> tokens)
    {
        string? revision = null;
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (string.Equals(token, "-r", StringComparison.Ordinal) ||
                string.Equals(token, "--revision", StringComparison.Ordinal))
            {
                if (i + 1 < tokens.Count)
                {
                    revision = tokens[i + 1];
                    tokens.RemoveAt(i + 1);
                }

                tokens.RemoveAt(i);
                i--;
                continue;
            }

            if (token.StartsWith("-r", StringComparison.Ordinal) && token.Length > 2)
            {
                revision = token[2..];
                tokens.RemoveAt(i);
                i--;
                continue;
            }

            const string revisionPrefix = "--revision=";
            if (token.StartsWith(revisionPrefix, StringComparison.Ordinal))
            {
                revision = token[revisionPrefix.Length..];
                tokens.RemoveAt(i);
                i--;
            }
        }

        return revision;
    }

    private static bool IsUrlToken(string token) =>
        token.Contains("://", StringComparison.Ordinal) ||
        token.StartsWith("^/", StringComparison.Ordinal) ||
        token.StartsWith("//", StringComparison.Ordinal) ||
        token.StartsWith("../", StringComparison.Ordinal) ||
        token.StartsWith("./", StringComparison.Ordinal) ||
        token.StartsWith("/", StringComparison.Ordinal);

    private static (string Url, string? PegRevision) SplitPegRevision(string value)
    {
        var at = value.LastIndexOf('@');
        if (at <= 0 || at == value.Length - 1)
        {
            return (value, null);
        }

        var suffix = value[(at + 1)..];
        if (suffix.Contains('/', StringComparison.Ordinal) ||
            suffix.Contains('\\', StringComparison.Ordinal))
        {
            return (value, null);
        }

        return (value[..at], suffix);
    }

    private static string? ResolveTargetPath(string parentPath, string? targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return null;
        }

        var target = targetPath.Trim().Replace('\\', '/');
        if (target.StartsWith("/", StringComparison.Ordinal))
        {
            return NormalizeRepositoryPath(target);
        }

        var parent = NormalizeRepositoryPath(parentPath);
        return parent == "/"
            ? NormalizeRepositoryPath("/" + target)
            : NormalizeRepositoryPath(parent + "/" + target);
    }

    public static string NormalizeRepositoryPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/")
        {
            return "/";
        }

        var p = path.Trim().Replace('\\', '/');
        if (!p.StartsWith("/", StringComparison.Ordinal))
        {
            p = "/" + p;
        }

        while (p.Contains("//", StringComparison.Ordinal))
        {
            p = p.Replace("//", "/", StringComparison.Ordinal);
        }

        if (p.Length > 1 && p.EndsWith("/", StringComparison.Ordinal))
        {
            p = p.TrimEnd('/');
        }

        return p;
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record SvnExternalDefinition(
    string ParentPath,
    string? TargetPath,
    string? ResolvedPath,
    string? Url,
    string? Revision,
    string? PegRevision,
    bool IsPinned,
    string RawDefinition);
