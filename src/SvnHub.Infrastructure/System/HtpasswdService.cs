using SvnHub.App.Configuration;
using SvnHub.App.System;

namespace SvnHub.Infrastructure.System;

public sealed class HtpasswdService : IHtpasswdService
{
    private readonly ICommandRunner _runner;
    private readonly SvnHubOptions _options;

    public HtpasswdService(ICommandRunner runner, SvnHubOptions options)
    {
        _runner = runner;
        _options = options;
    }

    public async Task<string> CreateBcryptHashAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default
    )
    {
        // htpasswd output is "user:$2y$...."
        var result = await _runner.RunAsync(
            _options.HtpasswdCommand,
            ["-nbB", userName, password],
            cancellationToken
        );

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"htpasswd failed (exit {result.ExitCode}): {result.StandardError}".Trim());
        }

        var line = result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(line))
        {
            throw new InvalidOperationException("htpasswd did not return output.");
        }

        var index = line.IndexOf(':', StringComparison.Ordinal);
        if (index <= 0 || index == line.Length - 1)
        {
            throw new InvalidOperationException("Unexpected htpasswd output format.");
        }

        var outUser = line[..index];
        var hash = line[(index + 1)..];

        if (!string.Equals(outUser, userName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Unexpected htpasswd output user name.");
        }

        return hash;
    }
}

