using PowerForge;
using PowerForgeStudio.Orchestrator.Portfolio;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioPowerShellCommandRunnerTests
{
    [Fact]
    public async Task RunCommandAsync_BuildsSharedPowerShellCommandRequest()
    {
        PowerShellRunRequest? captured = null;
        var runner = new PowerShellCommandRunner(new StubPowerShellRunner(request => {
            captured = request;
            return new PowerShellRunResult(0, "done", string.Empty, "pwsh");
        }));
        var environmentVariables = new Dictionary<string, string?> {
            ["PF_STUDIO"] = "1"
        };

        var result = await runner.RunCommandAsync(
            workingDirectory: @"C:\repo",
            script: "Get-ChildItem",
            environmentVariables: environmentVariables,
            cancellationToken: CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(PowerShellInvocationMode.Command, captured!.InvocationMode);
        Assert.Equal(@"C:\repo", captured.WorkingDirectory);
        Assert.Equal("Get-ChildItem", captured.CommandText);
        Assert.Equal(environmentVariables, captured.EnvironmentVariables);
        Assert.Equal(!OperatingSystem.IsWindows(), captured.PreferPwsh);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("done", result.StandardOutput);
    }

    private sealed class StubPowerShellRunner : IPowerShellRunner
    {
        private readonly Func<PowerShellRunRequest, PowerShellRunResult> _execute;

        public StubPowerShellRunner(Func<PowerShellRunRequest, PowerShellRunResult> execute)
        {
            _execute = execute;
        }

        public PowerShellRunResult Run(PowerShellRunRequest request)
            => _execute(request);
    }
}
