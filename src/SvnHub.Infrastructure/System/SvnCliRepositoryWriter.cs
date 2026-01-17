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

        var result = await _runner.RunAsync(
            _options.SvnCommand,
            ["delete", "--force-log", "-m", message, targetUrl],
            cancellationToken);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"svn delete failed (exit {result.ExitCode}): {result.StandardError}".Trim());
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

        var tmpMsgFile = Path.Combine(Path.GetTempPath(), $"svnhub-msg-{Guid.NewGuid():N}.txt");
        var propFiles = new List<string>();

        try
        {
            await File.WriteAllTextAsync(tmpMsgFile, message ?? "", cancellationToken);

            var args = new List<string>
            {
                "--non-interactive",
                "-F",
                tmpMsgFile,
                "mkdir",
                url,
            };

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
        }
    }

    public async Task UploadAsync(
        string repoLocalPath,
        IReadOnlyList<string> createDirectories,
        IReadOnlyList<SvnPutFile> putFiles,
        string message,
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

        var tmpMsgFile = Path.Combine(Path.GetTempPath(), $"svnhub-msg-{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(tmpMsgFile, message ?? "", cancellationToken);

            foreach (var (tmp, _, contents) in putItems)
            {
                await File.WriteAllBytesAsync(tmp, contents, cancellationToken);
            }

            var args = new List<string>
            {
                "--non-interactive",
                "-F",
                tmpMsgFile,
            };

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
        }
    }

    public async Task EditAsync(
        string repoLocalPath,
        string oldPath,
        string newPath,
        byte[]? newContents,
        IReadOnlyList<SvnPropertyEdit> propertyEdits,
        string message,
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

            var args = new List<string>
            {
                "--non-interactive",
                "-F",
                tmpMsgFile,
            };

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
        }
    }
}
