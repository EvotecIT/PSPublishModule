using System.Diagnostics;

namespace PowerForgeStudio.Orchestrator.Portfolio;

public sealed class PowerShellCommandRunner
{
    private static readonly Lazy<string> DefaultExecutable = new(ResolveDefaultPowerShellExecutable, LazyThreadSafetyMode.ExecutionAndPublication);

    public async Task<PowerShellExecutionResult> RunCommandAsync(
        string workingDirectory,
        string script,
        CancellationToken cancellationToken = default)
    {
        return await RunCommandAsync(workingDirectory, script, environmentVariables: null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PowerShellExecutionResult> RunCommandAsync(
        string workingDirectory,
        string script,
        IReadOnlyDictionary<string, string?>? environmentVariables = null,
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
        if (environmentVariables is not null)
        {
            foreach (var variable in environmentVariables)
            {
                if (variable.Value is null)
                {
                    process.StartInfo.Environment.Remove(variable.Key);
                    continue;
                }

                process.StartInfo.Environment[variable.Key] = variable.Value;
            }
        }

        var startedAt = Stopwatch.StartNew();
        process.Start();

        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        startedAt.Stop();
        return new PowerShellExecutionResult(
            process.ExitCode,
            startedAt.Elapsed,
            await stdOutTask.ConfigureAwait(false),
            await stdErrTask.ConfigureAwait(false));
    }

    private static string ResolvePowerShellExecutable()
    {
        var configuredExecutable = Environment.GetEnvironmentVariable("RELEASE_OPS_STUDIO_POWERSHELL_EXE");
        if (!string.IsNullOrWhiteSpace(configuredExecutable) && CanExecute(configuredExecutable))
        {
            return configuredExecutable;
        }

        return DefaultExecutable.Value;
    }

    private static string ResolveDefaultPowerShellExecutable()
    {
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
            if (!process.WaitForExit(3000))
            {
                TryKill(process);
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(1000);
            }
        }
        catch
        {
            // Swallow probe cleanup failures; the caller is only checking executable availability.
        }
    }
}
