using SvnHub.App.Configuration;
using SvnHub.App.System;
using System.Globalization;
using System.Text;

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

    public async Task<long> GetLastChangedRevisionAsync(
        string repoLocalPath,
        string path,
        long revision,
        CancellationToken cancellationToken = default)
    {
        var repoRelPath = ToRepoRelativePath(path);
        if (string.IsNullOrWhiteSpace(repoRelPath))
        {
            throw new ArgumentException("Path is required.", nameof(path));
        }

        var result = await _runner.RunBinaryAsync(
            _options.SvnlookCommand,
            ["history", "-r", revision.ToString(), "-l", "1", repoLocalPath, repoRelPath],
            cancellationToken);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"svnlook history failed (exit {result.ExitCode}): {result.StandardError}".Trim());
        }

        var text = DecodeSvnText(result.StandardOutput);
        var lines = text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length != 0)
            .ToArray();

        if (lines.Length == 0)
        {
            throw new InvalidOperationException("Unexpected svnlook history output: <empty>");
        }

        // Typical output:
        // REVISION   PATH
        // --------   ----
        //        5   /trunk/file.txt
        var dataLine = lines.FirstOrDefault(l =>
        {
            if (l.StartsWith("REVISION", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (l.StartsWith("----", StringComparison.Ordinal))
            {
                return false;
            }

            var first = l.Split(' ', '\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(first))
            {
                return false;
            }

            first = first.Trim();
            if (first.StartsWith('r'))
            {
                first = first.TrimStart('r');
            }

            return long.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
        });

        if (string.IsNullOrWhiteSpace(dataLine))
        {
            throw new InvalidOperationException($"Unexpected svnlook history output: {lines[0]}");
        }

        var token = dataLine.Split(' ', '\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException($"Unexpected svnlook history output: {dataLine}");
        }

        if (token.StartsWith('r'))
        {
            token = token.TrimStart('r');
        }

        if (!long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lastRev))
        {
            throw new InvalidOperationException($"Unexpected svnlook history output: {dataLine}");
        }

        return lastRev;
    }

    public async Task<DateTimeOffset> GetRevisionDateAsync(
        string repoLocalPath,
        long revision,
        CancellationToken cancellationToken = default)
    {
        var result = await _runner.RunAsync(
            _options.SvnlookCommand,
            ["date", "-r", revision.ToString(CultureInfo.InvariantCulture), repoLocalPath],
            cancellationToken);

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

    public async Task<string> GetRevisionLogAsync(
        string repoLocalPath,
        long revision,
        CancellationToken cancellationToken = default)
    {
        var result = await _runner.RunBinaryAsync(
            _options.SvnlookCommand,
            ["log", "-r", revision.ToString(CultureInfo.InvariantCulture), repoLocalPath],
            cancellationToken);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"svnlook log failed (exit {result.ExitCode}): {result.StandardError}".Trim());
        }

        return DecodeSvnText(result.StandardOutput).TrimEnd();
    }

    public async Task<string> GetRevisionAuthorAsync(
        string repoLocalPath,
        long revision,
        CancellationToken cancellationToken = default)
    {
        var result = await _runner.RunBinaryAsync(
            _options.SvnlookCommand,
            ["author", "-r", revision.ToString(CultureInfo.InvariantCulture), repoLocalPath],
            cancellationToken);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"svnlook author failed (exit {result.ExitCode}): {result.StandardError}".Trim());
        }

        return DecodeSvnText(result.StandardOutput).Trim();
    }

    public async Task<long> GetFileSizeAsync(
        string repoLocalPath,
        string filePath,
        long revision,
        CancellationToken cancellationToken = default)
    {
        var repoRelPath = ToRepoRelativePath(filePath);
        if (string.IsNullOrWhiteSpace(repoRelPath))
        {
            throw new ArgumentException("File path is required.", nameof(filePath));
        }

        var result = await _runner.RunAsync(
            _options.SvnlookCommand,
            ["filesize", "-r", revision.ToString(), repoLocalPath, repoRelPath],
            cancellationToken);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"svnlook filesize failed (exit {result.ExitCode}): {result.StandardError}".Trim());
        }

        var text = result.StandardOutput.Trim();
        if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) || size < 0)
        {
            throw new InvalidOperationException($"Unexpected svnlook filesize output: {text}");
        }

        return size;
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

        var result = await _runner.RunBinaryAsync(_options.SvnlookCommand, args, cancellationToken);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"svnlook tree failed (exit {result.ExitCode}): {result.StandardError}".Trim());
        }

        var normalizedBase = NormalizePath(path);

        var stdout = DecodeSvnText(result.StandardOutput);

        return stdout
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
                if (string.IsNullOrWhiteSpace(name))
                {
                    return null;
                }
                var fullPath = Combine(normalizedBase, name);
                return new SvnTreeEntry(name, fullPath, isDir);
            })
            .Where(e => e is not null)
            .Select(e => e!)
            .OrderBy(e => e.IsDirectory ? 0 : 1)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string DecodeSvnText(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return "";
        }

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Fallback for tools that output in a legacy single-byte code page on Windows.
            try
            {
                var ansiCp = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
                return Encoding.GetEncoding(ansiCp).GetString(bytes);
            }
            catch
            {
                try
                {
                    var oemCp = CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
                    return Encoding.GetEncoding(oemCp).GetString(bytes);
                }
                catch
                {
                    // Last resort: lossless byte-to-char mapping.
                    return Encoding.Latin1.GetString(bytes);
                }
            }
        }
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

    public async Task<IReadOnlyList<SvnProperty>> GetPropertiesAsync(
        string repoLocalPath,
        string path,
        long revision,
        CancellationToken cancellationToken = default)
    {
        var repoRelPath = ToRepoRelativePath(path);
        if (string.IsNullOrWhiteSpace(repoRelPath))
        {
            throw new ArgumentException("Path is required.", nameof(path));
        }

        var list = await _runner.RunAsync(
            _options.SvnlookCommand,
            ["proplist", "-r", revision.ToString(), repoLocalPath, repoRelPath],
            cancellationToken);

        if (!list.IsSuccess)
        {
            throw new InvalidOperationException(
                $"svnlook proplist failed (exit {list.ExitCode}): {list.StandardError}".Trim());
        }

        var names = list.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length != 0)
            .Where(l => !l.StartsWith("Properties on", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (names.Length == 0)
        {
            return Array.Empty<SvnProperty>();
        }

        async Task<SvnProperty> LoadOneAsync(string propName)
        {
            var get = await _runner.RunAsync(
                _options.SvnlookCommand,
                ["propget", "-r", revision.ToString(), repoLocalPath, propName, repoRelPath],
                cancellationToken);

            if (!get.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"svnlook propget failed (exit {get.ExitCode}): {get.StandardError}".Trim());
            }

            // svnlook prints raw property value; keep it as-is for editing (including newlines).
            return new SvnProperty(propName, get.StandardOutput);
        }

        var props = await Task.WhenAll(names.Select(LoadOneAsync));
        return props.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray();
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
