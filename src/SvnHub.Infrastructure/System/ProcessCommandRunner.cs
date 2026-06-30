using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
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
        EnsureUtf8Locale(psi);

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

        try
        {
            await process.WaitForExitAsync(cancellationToken);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            return new CommandResult(process.ExitCode, stdout, stderr);
        }
        catch
        {
            KillIfRunning(process);
            throw;
        }
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
        EnsureUtf8Locale(psi);

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

        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await stdoutTask;

            var stderr = await stderrTask;
            return new CommandBinaryResult(process.ExitCode, ms.ToArray(), stderr);
        }
        catch
        {
            KillIfRunning(process);
            throw;
        }
    }

    public async Task<CommandBinaryResult> RunBinaryPrefixAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        int maxBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);

        if (maxBytes <= 0)
        {
            return new CommandBinaryResult(0, [], "");
        }

        var attemptedResolutions = new List<string>();
        var resolvedFileName = TryResolveExecutable(fileName, attemptedResolutions) ?? fileName;

        var psi = new ProcessStartInfo
        {
            FileName = resolvedFileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        EnsureUtf8Locale(psi);

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

        await using var ms = new MemoryStream(Math.Min(maxBytes, 8192));
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var buffer = new byte[Math.Min(maxBytes, 8192)];
        var limitReached = false;

        try
        {
            while (ms.Length < maxBytes)
            {
                var readSize = (int)Math.Min(buffer.Length, maxBytes - ms.Length);
                var read = await process.StandardOutput.BaseStream.ReadAsync(
                    buffer.AsMemory(0, readSize),
                    cancellationToken);

                if (read == 0)
                {
                    break;
                }

                await ms.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            if (ms.Length >= maxBytes && !process.HasExited)
            {
                limitReached = true;
                KillIfRunning(process);
            }

            await process.WaitForExitAsync(cancellationToken);
            var stderr = await stderrTask;
            var exitCode = limitReached ? 0 : process.ExitCode;
            return new CommandBinaryResult(exitCode, ms.ToArray(), stderr);
        }
        catch
        {
            if (!process.HasExited)
            {
                KillIfRunning(process);
            }

            throw;
        }
    }

    private static void KillIfRunning(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort cleanup on cancellation or partial process startup.
        }
    }

    private static void EnsureUtf8Locale(ProcessStartInfo psi)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var lcAll = psi.Environment.TryGetValue("LC_ALL", out var existingLcAll) ? existingLcAll : null;
        var lang = psi.Environment.TryGetValue("LANG", out var existingLang) ? existingLang : null;

        if (IsExplicitNonUtf8Locale(lcAll) || (string.IsNullOrWhiteSpace(lcAll) && !IsUtf8Locale(lang)))
        {
            psi.Environment["LC_ALL"] = "C.UTF-8";
        }

        if (!IsUtf8Locale(lang))
        {
            psi.Environment["LANG"] = "C.UTF-8";
        }
    }

    private static bool IsExplicitNonUtf8Locale(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return string.Equals(value, "C", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "POSIX", StringComparison.OrdinalIgnoreCase) ||
            !IsUtf8Locale(value);
    }

    private static bool IsUtf8Locale(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("UTF-8", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("UTF8", StringComparison.OrdinalIgnoreCase);
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
