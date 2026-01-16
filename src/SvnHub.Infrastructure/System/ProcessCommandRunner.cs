using System.ComponentModel;
using System.Diagnostics;
using SvnHub.App.System;

namespace SvnHub.Infrastructure.System;

public sealed class ProcessCommandRunner : ICommandRunner
{
    public async Task<CommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);

        var attemptedResolutions = new List<string>();
        var resolvedFileName = TryResolveExecutable(fileName, attemptedResolutions) ?? fileName;

        var psi = new ProcessStartInfo
        {
            FileName = resolvedFileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            throw new InvalidOperationException(
                $"Failed to start process '{fileName}' (resolved as '{resolvedFileName}'). " +
                $"WorkingDirectory='{psi.WorkingDirectory}'. " +
                $"PATH='{path}'. " +
                $"Tried=[{string.Join("; ", attemptedResolutions.Distinct(StringComparer.OrdinalIgnoreCase))}]. " +
                $"Win32Error={ex.NativeErrorCode}: {ex.Message}",
                ex);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return new CommandResult(process.ExitCode, stdout, stderr);
    }

    public async Task<CommandBinaryResult> RunBinaryAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);

        var attemptedResolutions = new List<string>();
        var resolvedFileName = TryResolveExecutable(fileName, attemptedResolutions) ?? fileName;

        var psi = new ProcessStartInfo
        {
            FileName = resolvedFileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            throw new InvalidOperationException(
                $"Failed to start process '{fileName}' (resolved as '{resolvedFileName}'). " +
                $"WorkingDirectory='{psi.WorkingDirectory}'. " +
                $"PATH='{path}'. " +
                $"Tried=[{string.Join("; ", attemptedResolutions.Distinct(StringComparer.OrdinalIgnoreCase))}]. " +
                $"Win32Error={ex.NativeErrorCode}: {ex.Message}",
                ex);
        }

        await using var ms = new MemoryStream();
        var stdoutTask = process.StandardOutput.BaseStream.CopyToAsync(ms, cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        await stdoutTask;

        var stderr = await stderrTask;
        return new CommandBinaryResult(process.ExitCode, ms.ToArray(), stderr);
    }

    private static string? TryResolveExecutable(string fileName, List<string> attemptedResolutions)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var candidates = new List<string> { fileName };

        if (OperatingSystem.IsWindows())
        {
            if (string.IsNullOrWhiteSpace(Path.GetExtension(fileName)))
            {
                candidates.Add(fileName + ".exe");
                candidates.Add(fileName + ".cmd");
                candidates.Add(fileName + ".bat");
            }
        }

        // If the caller provided a path, only try that path (+ extension variants on Windows).
        if (fileName.Contains(Path.DirectorySeparatorChar) || fileName.Contains(Path.AltDirectorySeparatorChar) || Path.IsPathRooted(fileName))
        {
            foreach (var candidate in candidates)
            {
                attemptedResolutions.Add(candidate);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        var parts = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var dirRaw in parts)
        {
            var dir = dirRaw.Trim().Trim('"');
            if (dir.Length == 0)
            {
                continue;
            }

            foreach (var candidate in candidates)
            {
                var full = Path.Combine(dir, candidate);
                attemptedResolutions.Add(full);
                if (File.Exists(full))
                {
                    return full;
                }
            }
        }

        return null;
    }
}
