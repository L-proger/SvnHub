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
}
