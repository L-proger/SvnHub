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

    public async Task MoveAsync(
        string repoLocalPath,
        string oldPath,
        string newPath,
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

        var repoRoot = new Uri(Path.GetFullPath(repoLocalPath) + Path.DirectorySeparatorChar);

        var oldRel = NormalizeRepoRelativePath(oldPath);
        var newRel = NormalizeRepoRelativePath(newPath);
        if (string.IsNullOrWhiteSpace(oldRel) || string.IsNullOrWhiteSpace(newRel))
        {
            throw new ArgumentException("Invalid path.");
        }

        if (string.Equals(oldRel, newRel, StringComparison.Ordinal))
        {
            return;
        }

        var oldUrl = new Uri(repoRoot, oldRel).AbsoluteUri;
        var newUrl = new Uri(repoRoot, newRel).AbsoluteUri;

        var tmpMsgFile = Path.Combine(Path.GetTempPath(), $"svnhub-msg-{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(tmpMsgFile, message ?? "", cancellationToken);

            var args = new List<string>
            {
                "--non-interactive",
                "-F",
                tmpMsgFile,
                "mv",
                oldUrl,
                newUrl,
            };

            var mucc = await _runner.RunAsync(_options.SvnmuccCommand, args, cancellationToken);
            if (!mucc.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"svnmucc mv failed (exit {mucc.ExitCode}): {mucc.StandardError}".Trim());
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

    public async Task PutFileAsync(
        string repoLocalPath,
        string oldPath,
        string newPath,
        byte[] contents,
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

        ArgumentNullException.ThrowIfNull(contents);

        var repoRoot = new Uri(Path.GetFullPath(repoLocalPath) + Path.DirectorySeparatorChar);

        var oldRel = NormalizeRepoRelativePath(oldPath);
        var newRel = NormalizeRepoRelativePath(newPath);
        if (string.IsNullOrWhiteSpace(oldRel) || string.IsNullOrWhiteSpace(newRel))
        {
            throw new ArgumentException("Invalid path.");
        }

        var oldUrl = new Uri(repoRoot, oldRel).AbsoluteUri;
        var newUrl = new Uri(repoRoot, newRel).AbsoluteUri;

        // Use svnmucc (no working copy needed).
        var tmpFile = Path.Combine(Path.GetTempPath(), $"svnhub-put-{Guid.NewGuid():N}.tmp");
        var tmpMsgFile = Path.Combine(Path.GetTempPath(), $"svnhub-msg-{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllBytesAsync(tmpFile, contents, cancellationToken);
            await File.WriteAllTextAsync(tmpMsgFile, message ?? "", cancellationToken);

            var args = new List<string>
            {
                "--non-interactive",
                "-F",
                tmpMsgFile,
            };

            if (!string.Equals(oldRel, newRel, StringComparison.Ordinal))
            {
                args.AddRange(["mv", oldUrl, newUrl]);
            }

            args.AddRange(["put", tmpFile, newUrl]);

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
                if (File.Exists(tmpFile))
                {
                    File.Delete(tmpFile);
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
        }
    }
}
