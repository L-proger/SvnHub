using SvnHub.App.Configuration;
using SvnHub.App.System;

namespace SvnHub.Infrastructure.System;

public sealed class SvnCliRepositoryWriter : ISvnRepositoryWriter
{
    private readonly ICommandRunner _runner;
    private readonly SvnHubOptions _options;

    public SvnCliRepositoryWriter(ICommandRunner runner, SvnHubOptions options)
    {
        _runner = runner;
        _options = options;
    }

    public async Task DeleteAsync(
        string repoLocalPath,
        string path,
        string message,
        string? userName = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repoLocalPath))
        {
            throw new ArgumentException("Repository local path is required.", nameof(repoLocalPath));
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path is required.", nameof(path));
        }

        var repoRoot = new Uri(Path.GetFullPath(repoLocalPath) + Path.DirectorySeparatorChar);
        var rel = NormalizeRepoRelativePath(path);
        if (string.IsNullOrWhiteSpace(rel))
        {
            throw new ArgumentException("Invalid path.", nameof(path));
        }

        var targetUrl = new Uri(repoRoot, rel).AbsoluteUri;

        // Use -F file and a temporary svn config dir forcing UTF-8 log encoding, for consistent Unicode commit messages.
        var tmpConfigDir = CreateTempConfigDir();
        var tmpMsgFile = Path.Combine(Path.GetTempPath(), $"svnhub-msg-{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(tmpMsgFile, message ?? "", cancellationToken);
            await WriteUtf8LogEncodingConfigAsync(tmpConfigDir, cancellationToken);

            var args = new List<string>
            {
                "--non-interactive",
                "--config-dir",
                tmpConfigDir,
            };

            if (!string.IsNullOrWhiteSpace(userName))
            {
                args.AddRange(["--username", userName]);
            }

            args.AddRange(["delete", "--force-log", "-F", tmpMsgFile, targetUrl]);

            var result = await _runner.RunAsync(_options.SvnCommand, args, cancellationToken);

            if (!result.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"svn delete failed (exit {result.ExitCode}): {result.StandardError}".Trim());
            }
        }
        finally
        {
            try
            {
                if (File.Exists(tmpMsgFile))
                {
                    File.Delete(tmpMsgFile);
                }
            }
            catch
            {
                // best-effort cleanup
            }

            TryDeleteDirectory(tmpConfigDir);
        }
    }

    private static string NormalizeRepoRelativePath(string path)
    {
        var p = path.Trim().Replace('\\', '/').TrimStart('/');

        while (p.Contains("//", StringComparison.Ordinal))
        {
            p = p.Replace("//", "/", StringComparison.Ordinal);
        }

        // Reject attempts to traverse.
        if (p.Contains("../", StringComparison.Ordinal) || p.Contains("/..", StringComparison.Ordinal))
        {
            return "";
        }

        return p;
    }

    public async Task CreateDirectoryAsync(
        string repoLocalPath,
        string path,
        IReadOnlyList<SvnPropertyEdit> propertyEdits,
        string message,
        string? userName = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repoLocalPath))
        {
            throw new ArgumentException("Repository local path is required.", nameof(repoLocalPath));
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path is required.", nameof(path));
        }

        ArgumentNullException.ThrowIfNull(propertyEdits);

        var repoRoot = new Uri(Path.GetFullPath(repoLocalPath) + Path.DirectorySeparatorChar);
        var rel = NormalizeRepoRelativePath(path);
        if (string.IsNullOrWhiteSpace(rel))
        {
            throw new ArgumentException("Invalid path.", nameof(path));
        }

        var url = new Uri(repoRoot, rel).AbsoluteUri;

        var tmpConfigDir = CreateTempConfigDir();
        var tmpMsgFile = Path.Combine(Path.GetTempPath(), $"svnhub-msg-{Guid.NewGuid():N}.txt");
        var propFiles = new List<string>();

        try
        {
            await File.WriteAllTextAsync(tmpMsgFile, message ?? "", cancellationToken);
            await WriteUtf8LogEncodingConfigAsync(tmpConfigDir, cancellationToken);

            var args = new List<string>
            {
                "--non-interactive",
                !string.IsNullOrWhiteSpace(userName) ? "-u" : "",
                !string.IsNullOrWhiteSpace(userName) ? userName! : "",
                "--config-dir",
                tmpConfigDir,
                "-F",
                tmpMsgFile,
                "mkdir",
                url,
            };

            args.RemoveAll(string.IsNullOrWhiteSpace);

            foreach (var edit in propertyEdits)
            {
                if (string.IsNullOrWhiteSpace(edit.Name))
                {
                    continue;
                }

                if (edit.IsDelete)
                {
                    throw new InvalidOperationException($"Cannot delete a property while creating a directory: {edit.Name}");
                }

                var file = Path.Combine(Path.GetTempPath(), $"svnhub-prop-{Guid.NewGuid():N}.txt");
                propFiles.Add(file);
                await File.WriteAllTextAsync(file, edit.Value ?? "", cancellationToken);
                args.AddRange(["propsetf", edit.Name, file, url]);
            }

            var mucc = await _runner.RunAsync(_options.SvnmuccCommand, args, cancellationToken);
            if (!mucc.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"svnmucc mkdir failed (exit {mucc.ExitCode}): {mucc.StandardError}".Trim());
            }
        }
        finally
        {
            try
            {
                if (File.Exists(tmpMsgFile))
                {
                    File.Delete(tmpMsgFile);
                }
            }
            catch
            {
                // best-effort cleanup
            }

            foreach (var file in propFiles)
            {
                try
                {
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                }
            }

            TryDeleteDirectory(tmpConfigDir);
        }
    }

    public async Task UploadAsync(
        string repoLocalPath,
        IReadOnlyList<string> createDirectories,
        IReadOnlyList<SvnPutFile> putFiles,
        string message,
        string? userName = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repoLocalPath))
        {
            throw new ArgumentException("Repository local path is required.", nameof(repoLocalPath));
        }

        ArgumentNullException.ThrowIfNull(createDirectories);
        ArgumentNullException.ThrowIfNull(putFiles);

        var repoRoot = new Uri(Path.GetFullPath(repoLocalPath) + Path.DirectorySeparatorChar);

        var mkdirUrls = new List<string>();
        foreach (var p in createDirectories)
        {
            if (string.IsNullOrWhiteSpace(p))
            {
                continue;
            }

            var rel = NormalizeRepoRelativePath(p);
            if (string.IsNullOrWhiteSpace(rel))
            {
                throw new ArgumentException($"Invalid path: {p}", nameof(createDirectories));
            }

            mkdirUrls.Add(new Uri(repoRoot, rel).AbsoluteUri);
        }

        var putItems = new List<(string TempFile, string Url, byte[] Contents)>();
        foreach (var f in putFiles)
        {
            if (f is null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(f.Path))
            {
                throw new ArgumentException("File path is required.", nameof(putFiles));
            }

            var rel = NormalizeRepoRelativePath(f.Path);
            if (string.IsNullOrWhiteSpace(rel))
            {
                throw new ArgumentException($"Invalid path: {f.Path}", nameof(putFiles));
            }

            var url = new Uri(repoRoot, rel).AbsoluteUri;
            var tmp = Path.Combine(Path.GetTempPath(), $"svnhub-put-{Guid.NewGuid():N}.tmp");
            putItems.Add((tmp, url, f.Contents));
        }

        if (mkdirUrls.Count == 0 && putItems.Count == 0)
        {
            return;
        }

        var tmpConfigDir = CreateTempConfigDir();
        var tmpMsgFile = Path.Combine(Path.GetTempPath(), $"svnhub-msg-{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(tmpMsgFile, message ?? "", cancellationToken);
            await WriteUtf8LogEncodingConfigAsync(tmpConfigDir, cancellationToken);

            foreach (var (tmp, _, contents) in putItems)
            {
                await File.WriteAllBytesAsync(tmp, contents, cancellationToken);
            }

            var args = new List<string>
            {
                "--non-interactive",
                !string.IsNullOrWhiteSpace(userName) ? "-u" : "",
                !string.IsNullOrWhiteSpace(userName) ? userName! : "",
                "--config-dir",
                tmpConfigDir,
                "-F",
                tmpMsgFile,
            };

            args.RemoveAll(string.IsNullOrWhiteSpace);

            var seenMkdir = new HashSet<string>(StringComparer.Ordinal);
            foreach (var url in mkdirUrls)
            {
                if (!seenMkdir.Add(url))
                {
                    continue;
                }

                args.AddRange(["mkdir", url]);
            }

            foreach (var (tmp, url, _) in putItems)
            {
                args.AddRange(["put", tmp, url]);
            }

            var mucc = await _runner.RunAsync(_options.SvnmuccCommand, args, cancellationToken);
            if (!mucc.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"svnmucc upload failed (exit {mucc.ExitCode}): {mucc.StandardError}".Trim());
            }
        }
        finally
        {
            try
            {
                if (File.Exists(tmpMsgFile))
                {
                    File.Delete(tmpMsgFile);
                }
            }
            catch
            {
            }

            foreach (var (tmp, _, _) in putItems)
            {
                try
                {
                    if (File.Exists(tmp))
                    {
                        File.Delete(tmp);
                    }
                }
                catch
                {
                }
            }

            TryDeleteDirectory(tmpConfigDir);
        }
    }

    public async Task EditAsync(
        string repoLocalPath,
        string oldPath,
        string newPath,
        byte[]? newContents,
        IReadOnlyList<SvnPropertyEdit> propertyEdits,
        string message,
        string? userName = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repoLocalPath))
        {
            throw new ArgumentException("Repository local path is required.", nameof(repoLocalPath));
        }

        if (string.IsNullOrWhiteSpace(oldPath))
        {
            throw new ArgumentException("Old path is required.", nameof(oldPath));
        }

        if (string.IsNullOrWhiteSpace(newPath))
        {
            throw new ArgumentException("New path is required.", nameof(newPath));
        }

        ArgumentNullException.ThrowIfNull(propertyEdits);

        var repoRoot = new Uri(Path.GetFullPath(repoLocalPath) + Path.DirectorySeparatorChar);

        var oldRel = NormalizeRepoRelativePath(oldPath);
        var newRel = NormalizeRepoRelativePath(newPath);
        if (string.IsNullOrWhiteSpace(oldRel) || string.IsNullOrWhiteSpace(newRel))
        {
            throw new ArgumentException("Invalid path.");
        }

        var oldUrl = new Uri(repoRoot, oldRel).AbsoluteUri;
        var newUrl = new Uri(repoRoot, newRel).AbsoluteUri;

        var needsMove = !string.Equals(oldRel, newRel, StringComparison.Ordinal);
        var needsPut = newContents is not null;
        var needsProps = propertyEdits.Count != 0;

        if (!needsMove && !needsPut && !needsProps)
        {
            return;
        }

        // Use svnmucc (no working copy needed).
        var tmpConfigDir = CreateTempConfigDir();
        var tmpPutFile = Path.Combine(Path.GetTempPath(), $"svnhub-put-{Guid.NewGuid():N}.tmp");
        var tmpMsgFile = Path.Combine(Path.GetTempPath(), $"svnhub-msg-{Guid.NewGuid():N}.txt");
        var propFiles = new List<string>();
        try
        {
            if (needsPut)
            {
                await File.WriteAllBytesAsync(tmpPutFile, newContents!, cancellationToken);
            }

            await File.WriteAllTextAsync(tmpMsgFile, message ?? "", cancellationToken);
            await WriteUtf8LogEncodingConfigAsync(tmpConfigDir, cancellationToken);

            var args = new List<string>
            {
                "--non-interactive",
                !string.IsNullOrWhiteSpace(userName) ? "-u" : "",
                !string.IsNullOrWhiteSpace(userName) ? userName! : "",
                "--config-dir",
                tmpConfigDir,
                "-F",
                tmpMsgFile,
            };

            args.RemoveAll(string.IsNullOrWhiteSpace);

            if (needsMove)
            {
                args.AddRange(["mv", oldUrl, newUrl]);
            }

            if (needsPut)
            {
                args.AddRange(["put", tmpPutFile, newUrl]);
            }

            foreach (var edit in propertyEdits)
            {
                if (string.IsNullOrWhiteSpace(edit.Name))
                {
                    continue;
                }

                if (edit.IsDelete)
                {
                    args.AddRange(["propdel", edit.Name, newUrl]);
                    continue;
                }

                var file = Path.Combine(Path.GetTempPath(), $"svnhub-prop-{Guid.NewGuid():N}.txt");
                propFiles.Add(file);
                await File.WriteAllTextAsync(file, edit.Value ?? "", cancellationToken);
                args.AddRange(["propsetf", edit.Name, file, newUrl]);
            }

            var mucc = await _runner.RunAsync(_options.SvnmuccCommand, args, cancellationToken);
            if (!mucc.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"svnmucc put failed (exit {mucc.ExitCode}): {mucc.StandardError}".Trim());
            }

            return;
        }
        finally
        {
            try
            {
                if (File.Exists(tmpPutFile))
                {
                    File.Delete(tmpPutFile);
                }
            }
            catch
            {
                // best-effort cleanup
            }

            try
            {
                if (File.Exists(tmpMsgFile))
                {
                    File.Delete(tmpMsgFile);
                }
            }
            catch
            {
                // best-effort cleanup
            }

            foreach (var file in propFiles)
            {
                try
                {
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                }
            }

            TryDeleteDirectory(tmpConfigDir);
        }
    }

    private static string CreateTempConfigDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"svnhub-svnconfig-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static Task WriteUtf8LogEncodingConfigAsync(string configDir, CancellationToken cancellationToken)
    {
        // Subversion reads "config" file from config-dir.
        // Force UTF-8 log encoding so commit messages with Cyrillic are accepted consistently.
        var configPath = Path.Combine(configDir, "config");
        var content = "[miscellany]\nlog-encoding = UTF-8\n";
        return File.WriteAllTextAsync(configPath, content, cancellationToken);
    }

    private static void TryDeleteDirectory(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir))
        {
            return;
        }

        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
