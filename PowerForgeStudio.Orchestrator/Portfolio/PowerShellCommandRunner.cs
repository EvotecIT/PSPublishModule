using System.Diagnostics;
using PowerForge;

namespace PowerForgeStudio.Orchestrator.Portfolio;

public sealed class PowerShellCommandRunner
{
    private readonly IPowerShellRunner _powerShellRunner;

    public PowerShellCommandRunner()
        : this(new PowerShellRunner())
    {
    }

    internal PowerShellCommandRunner(IPowerShellRunner powerShellRunner)
    {
        _powerShellRunner = powerShellRunner;
    }

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

        var startedAt = Stopwatch.StartNew();
        var result = await Task.Run(() => _powerShellRunner.Run(PowerShellRunRequest.ForCommand(
            commandText: script,
            timeout: TimeSpan.FromMinutes(15),
            preferPwsh: !OperatingSystem.IsWindows(),
            workingDirectory: workingDirectory,
            environmentVariables: environmentVariables,
            executableOverride: Environment.GetEnvironmentVariable("RELEASE_OPS_STUDIO_POWERSHELL_EXE"))), cancellationToken).ConfigureAwait(false);
        startedAt.Stop();

        return new PowerShellExecutionResult(
            result.ExitCode,
            startedAt.Elapsed,
            result.StdOut,
            result.StdErr);
    }
}
