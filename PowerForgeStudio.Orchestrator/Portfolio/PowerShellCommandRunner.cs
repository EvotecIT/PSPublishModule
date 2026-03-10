using System.Diagnostics;

namespace PowerForgeStudio.Orchestrator.Portfolio;

public sealed class PowerShellCommandRunner
{
    public async Task<PowerShellExecutionResult> RunCommandAsync(
        string workingDirectory,
        string script,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(script);

        var executable = ResolvePowerShellExecutable();
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-NonInteractive");
        if (OperatingSystem.IsWindows())
        {
            process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
            process.StartInfo.ArgumentList.Add("Bypass");
        }

        process.StartInfo.ArgumentList.Add("-Command");
        process.StartInfo.ArgumentList.Add(script);

        var startedAt = Stopwatch.StartNew();
        process.Start();

        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        startedAt.Stop();
        return new PowerShellExecutionResult(
            process.ExitCode,
            startedAt.Elapsed,
            await stdOutTask,
            await stdErrTask);
    }

    private static string ResolvePowerShellExecutable()
    {
        var configuredExecutable = Environment.GetEnvironmentVariable("RELEASE_OPS_STUDIO_POWERSHELL_EXE");
        if (!string.IsNullOrWhiteSpace(configuredExecutable) && CanExecute(configuredExecutable))
        {
            return configuredExecutable;
        }

        foreach (var candidate in GetCandidateExecutables())
        {
            if (CanExecute(candidate))
            {
                return candidate;
            }
        }

        return OperatingSystem.IsWindows() ? "powershell" : "pwsh";
    }

    private static IReadOnlyList<string> GetCandidateExecutables()
    {
        return OperatingSystem.IsWindows()
            ? ["powershell", "pwsh"]
            : ["pwsh", "powershell"];
    }

    private static bool CanExecute(string executable)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo {
                FileName = executable,
                Arguments = "-NoProfile -NonInteractive -Command \"$PSVersionTable.PSVersion.ToString()\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();
            process.WaitForExit(3000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
