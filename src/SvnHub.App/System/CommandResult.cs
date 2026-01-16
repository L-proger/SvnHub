namespace SvnHub.App.System;

public sealed record CommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError
)
{
    public bool IsSuccess => ExitCode == 0;
}

public sealed record CommandBinaryResult(
    int ExitCode,
    byte[] StandardOutput,
    string StandardError
)
{
    public bool IsSuccess => ExitCode == 0;
}
