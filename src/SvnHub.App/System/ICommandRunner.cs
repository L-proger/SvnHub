namespace SvnHub.App.System;

public interface ICommandRunner
{
    Task<CommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default
    );

    Task<CommandBinaryResult> RunBinaryAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default
    );

    Task<CommandBinaryResult> RunBinaryPrefixAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        int maxBytes,
        CancellationToken cancellationToken = default
    );
}
