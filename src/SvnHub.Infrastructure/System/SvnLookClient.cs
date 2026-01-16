using SvnHub.App.Configuration;
using SvnHub.App.System;
using System.Globalization;

namespace SvnHub.Infrastructure.System;

public sealed class SvnLookClient : ISvnLookClient
{
    private readonly ICommandRunner _runner;
    private readonly SvnHubOptions _options;

    public SvnLookClient(ICommandRunner runner, SvnHubOptions options)
    {
        _runner = runner;
        _options = options;
    }

    public async Task<long> GetYoungestRevisionAsync(string repoLocalPath, CancellationToken cancellationToken = default)
    {
        var result = await _runner.RunAsync(_options.SvnlookCommand, ["youngest", repoLocalPath], cancellationToken);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"svnlook youngest failed (exit {result.ExitCode}): {result.StandardError}".Trim());
        }

        var text = result.StandardOutput.Trim();
        if (!long.TryParse(text, out var rev))
        {
            throw new InvalidOperationException($"Unexpected svnlook youngest output: {text}");
        }

        return rev;
    }

    public async Task<DateTimeOffset> GetHeadChangedAtAsync(string repoLocalPath, CancellationToken cancellationToken = default)
    {
        var result = await _runner.RunAsync(_options.SvnlookCommand, ["date", repoLocalPath], cancellationToken);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"svnlook date failed (exit {result.ExitCode}): {result.StandardError}".Trim());
        }

        var text = result.StandardOutput.Trim();
        if (text.Length == 0)
        {
            throw new InvalidOperationException("Unexpected svnlook date output: <empty>");
        }

        if (TryParseSvnDate(text, out var dto))
        {
            return dto;
        }

        throw new InvalidOperationException($"Unexpected svnlook date output: {text}");
    }

    public async Task<IReadOnlyList<SvnTreeEntry>> ListTreeAsync(
        string repoLocalPath,
        string path,
        long revision,
        CancellationToken cancellationToken = default
    )
    {
        var repoRelPath = ToRepoRelativePath(path);
        var repoRelPrefix = string.IsNullOrWhiteSpace(repoRelPath) ? null : repoRelPath.TrimEnd('/') + "/";
        var baseNamePrefix = string.IsNullOrWhiteSpace(repoRelPath) ? null : repoRelPath.Split('/').Last().TrimEnd('/') + "/";
        var baseName = string.IsNullOrWhiteSpace(repoRelPath) ? null : repoRelPath.Split('/').Last().TrimEnd('/');

        var args = new List<string>
        {
            "tree",
            "-r",
            revision.ToString(),
            "-N",
            repoLocalPath,
        };

        if (!string.IsNullOrWhiteSpace(repoRelPath))
        {
            args.Add(repoRelPath);
        }

        var result = await _runner.RunAsync(_options.SvnlookCommand, args, cancellationToken);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"svnlook tree failed (exit {result.ExitCode}): {result.StandardError}".Trim());
        }

        var normalizedBase = NormalizePath(path);

        return result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length != 0)
            .Select(line =>
            {
                if (string.IsNullOrWhiteSpace(repoRelPrefix) && string.IsNullOrWhiteSpace(baseNamePrefix))
                {
                    return line;
                }

                // svnlook tree -N may include the provided directory name itself as the first line.
                if (!string.IsNullOrWhiteSpace(baseName) &&
                    (string.Equals(line, baseName, StringComparison.Ordinal) || string.Equals(line, baseName + "/", StringComparison.Ordinal)))
                {
                    return "";
                }

                if (!string.IsNullOrWhiteSpace(repoRelPrefix) && line.StartsWith(repoRelPrefix, StringComparison.Ordinal))
                {
                    return line[repoRelPrefix.Length..];
                }

                // Some svnlook versions output paths relative to the argument (e.g. "Services/..." instead of "trunk/Services/...").
                if (!string.IsNullOrWhiteSpace(baseNamePrefix) && line.StartsWith(baseNamePrefix, StringComparison.Ordinal))
                {
                    return line[baseNamePrefix.Length..];
                }

                return line;
            })
            .Where(line => line.Length != 0)
            .Select(line =>
            {
                var isDir = line.EndsWith("/", StringComparison.Ordinal);
                var name = isDir ? line.TrimEnd('/') : line;
                var fullPath = Combine(normalizedBase, name);
                return new SvnTreeEntry(name, fullPath, isDir);
            })
            .OrderBy(e => e.IsDirectory ? 0 : 1)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<string> CatAsync(
        string repoLocalPath,
        string filePath,
        long revision,
        CancellationToken cancellationToken = default
    )
    {
        var repoRelPath = ToRepoRelativePath(filePath);
        if (string.IsNullOrWhiteSpace(repoRelPath))
        {
            throw new ArgumentException("File path is required.", nameof(filePath));
        }

        var result = await _runner.RunAsync(
            _options.SvnlookCommand,
            ["cat", "-r", revision.ToString(), repoLocalPath, repoRelPath],
            cancellationToken);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"svnlook cat failed (exit {result.ExitCode}): {result.StandardError}".Trim());
        }

        return result.StandardOutput;
    }

    public async Task<byte[]> CatBytesAsync(
        string repoLocalPath,
        string filePath,
        long revision,
        CancellationToken cancellationToken = default
    )
    {
        var repoRelPath = ToRepoRelativePath(filePath);
        if (string.IsNullOrWhiteSpace(repoRelPath))
        {
            throw new ArgumentException("File path is required.", nameof(filePath));
        }

        var result = await _runner.RunBinaryAsync(
            _options.SvnlookCommand,
            ["cat", "-r", revision.ToString(), repoLocalPath, repoRelPath],
            cancellationToken);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"svnlook cat failed (exit {result.ExitCode}): {result.StandardError}".Trim());
        }

        return result.StandardOutput;
    }

    private static bool TryParseSvnDate(string text, out DateTimeOffset dto)
    {
        // Typical svnlook output:
        // 2026-01-15 13:37:00 +0000 (Thu, 15 Jan 2026)
        var trimmed = text.Trim();
        var withoutComment = trimmed;
        var parenIndex = trimmed.IndexOf('(');
        if (parenIndex >= 0)
        {
            withoutComment = trimmed[..parenIndex].Trim();
        }

        var parts = withoutComment.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 3)
        {
            var datePart = parts[0];
            var timePart = parts[1];
            var offsetPart = parts[2];

            if (offsetPart.Length == 5 && (offsetPart[0] == '+' || offsetPart[0] == '-'))
            {
                offsetPart = offsetPart.Insert(3, ":");
            }

            var normalized = $"{datePart}T{timePart}{offsetPart}";
            if (DateTimeOffset.TryParseExact(
                    normalized,
                    "yyyy-MM-dd'T'HH:mm:sszzz",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowLeadingWhite | DateTimeStyles.AllowTrailingWhite,
                    out dto))
            {
                return true;
            }
        }

        return DateTimeOffset.TryParse(withoutComment, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out dto)
               || DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out dto);
    }

    private static string? ToRepoRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/")
        {
            return null;
        }

        return NormalizePath(path).TrimStart('/');
    }

    private static string NormalizePath(string? path)
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

    private static string Combine(string basePath, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return basePath;
        }

        if (basePath == "/")
        {
            return "/" + name;
        }

        return basePath + "/" + name;
    }
}
