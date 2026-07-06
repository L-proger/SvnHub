using SvnHub.App.Configuration;
using SvnHub.App.System;

namespace SvnHub.Infrastructure.System;

public sealed class SvnadminRepositoryProvisioner : ISvnRepositoryProvisioner
{
    private readonly ICommandRunner _runner;
    private readonly SvnHubOptions _options;

    public SvnadminRepositoryProvisioner(ICommandRunner runner, SvnHubOptions options)
    {
        _runner = runner;
        _options = options;
    }

    public async Task CreateAsync(
        string localPath,
        bool initializeStandardLayout,
        string? authorUserName = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(localPath))
        {
            throw new ArgumentException("Local path is required.", nameof(localPath));
        }

        var result = await _runner.RunAsync(_options.SvnadminCommand, ["create", localPath], cancellationToken);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"svnadmin failed (exit {result.ExitCode}): {result.StandardError}".Trim());
        }

        if (!initializeStandardLayout)
        {
            return;
        }

        var repoUri = new Uri(Path.GetFullPath(localPath));
        var repoUrl = repoUri.AbsoluteUri.TrimEnd('/');

        var args = new List<string>
        {
            "--non-interactive",
        };

        if (!string.IsNullOrWhiteSpace(authorUserName))
        {
            args.AddRange(["--username", authorUserName]);
        }

        args.AddRange(
        [
            "mkdir",
            "-m",
            "Initialize standard layout (trunk/branches/tags)",
            $"{repoUrl}/trunk",
            $"{repoUrl}/branches",
            $"{repoUrl}/tags",
        ]);

        var mkdir = await _runner.RunAsync(
            _options.SvnCommand,
            args,
            cancellationToken);

        if (!mkdir.IsSuccess)
        {
            throw new InvalidOperationException(
                $"svn mkdir failed (exit {mkdir.ExitCode}): {mkdir.StandardError}".Trim());
        }
    }
}
