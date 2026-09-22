namespace PowerForge.Tests;

public sealed class ProjectBuildCommandHostServiceTests
{
    [Fact]
    public async Task ExecuteBuildAsync_ForwardsCancellationToRunningPowerShell()
    {
        var runner = new CancellableRunner();
        var service = new ProjectBuildCommandHostService(runner);
        using var cancellation = new CancellationTokenSource();
        var pending = service.ExecuteBuildAsync(new ProjectBuildCommandBuildRequest {
            RepositoryRoot = Path.GetTempPath(), ModulePath = "PSPublishModule"
        }, cancellation.Token);
        await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.True(runner.Token.IsCancellationRequested);
    }

    private sealed class CancellableRunner : IPowerShellRunner, ICancellablePowerShellRunner
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }
        public PowerShellRunResult Run(PowerShellRunRequest request) => throw new InvalidOperationException("The synchronous path cannot cancel a child process.");
        public async Task<PowerShellRunResult> RunAsync(PowerShellRunRequest request, CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            Started.SetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("Expected cancellation.");
        }
    }

    [Fact]
    public async Task GeneratePlanAsync_UsesSharedInvokeProjectBuildPlanCommand()
    {
        PowerShellRunRequest? captured = null;
        var service = new ProjectBuildCommandHostService(new StubPowerShellRunner(request => {
            captured = request;
            return new PowerShellRunResult(0, "planned", string.Empty, "pwsh");
        }));

        var result = await service.GeneratePlanAsync(new ProjectBuildCommandPlanRequest {
            RepositoryRoot = @"C:\Repo",
            PlanOutputPath = @"C:\Repo\plan.json",
            ConfigPath = @"C:\Repo\Build\project.build.json",
            ModulePath = @"C:\Repo\Module\PSPublishModule.psd1"
        });

        Assert.True(result.Succeeded);
        Assert.NotNull(captured);
        Assert.Equal(PowerShellInvocationMode.Command, captured!.InvocationMode);
        Assert.Contains("Invoke-ProjectBuild -Plan:$true", captured.CommandText, StringComparison.Ordinal);
        Assert.Contains("-PlanPath 'C:\\Repo\\plan.json'", captured.CommandText, StringComparison.Ordinal);
        Assert.Contains("-ConfigPath 'C:\\Repo\\Build\\project.build.json'", captured.CommandText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeneratePlanAsync_InvokesDiscoveredScriptWithBuildAndPublishingDisabled()
    {
        PowerShellRunRequest? captured = null;
        var service = new ProjectBuildCommandHostService(new StubPowerShellRunner(request => {
            captured = request;
            return new PowerShellRunResult(0, "planned", string.Empty, "pwsh");
        }));

        var result = await service.GeneratePlanAsync(new ProjectBuildCommandPlanRequest {
            RepositoryRoot = @"C:\Repo",
            ScriptPath = @"C:\Repo\Build\Build-Project.ps1",
            PlanOutputPath = @"C:\Repo\plan.json",
            ModulePath = "PSPublishModule"
        });

        Assert.True(result.Succeeded);
        Assert.NotNull(captured);
        Assert.Contains("& 'C:\\Repo\\Build\\Build-Project.ps1' -Plan:$true -Build:$false -PublishNuget:$false -PublishGitHub:$false -UpdateVersions:$false -PlanPath 'C:\\Repo\\plan.json'",
            captured!.CommandText, StringComparison.Ordinal);
        Assert.DoesNotContain("Invoke-ProjectBuild -Plan", captured.CommandText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteBuildAsync_UsesSharedInvokeProjectBuildBuildCommand()
    {
        PowerShellRunRequest? captured = null;
        var service = new ProjectBuildCommandHostService(new StubPowerShellRunner(request => {
            captured = request;
            return new PowerShellRunResult(0, "built", string.Empty, "pwsh");
        }));

        var result = await service.ExecuteBuildAsync(new ProjectBuildCommandBuildRequest {
            RepositoryRoot = @"C:\Repo",
            ConfigPath = @"C:\Repo\Build\project.build.json",
            ModulePath = @"C:\Repo\Module\PSPublishModule.psd1"
        });

        Assert.True(result.Succeeded);
        Assert.NotNull(captured);
        Assert.Contains("Invoke-ProjectBuild -Build:$true -PublishNuget:$false -PublishGitHub:$false -UpdateVersions:$false", captured!.CommandText, StringComparison.Ordinal);
        Assert.Contains("-ConfigPath 'C:\\Repo\\Build\\project.build.json'", captured.CommandText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteBuildAsync_InvokesDiscoveredScriptWithPublishingDisabled()
    {
        PowerShellRunRequest? captured = null;
        var service = new ProjectBuildCommandHostService(new StubPowerShellRunner(request => {
            captured = request;
            return new PowerShellRunResult(0, "built", string.Empty, "pwsh");
        }));

        var result = await service.ExecuteBuildAsync(new ProjectBuildCommandBuildRequest {
            RepositoryRoot = @"C:\Repo",
            ScriptPath = @"C:\Repo\Build\Build-Project.ps1",
            ModulePath = "PSPublishModule"
        });

        Assert.True(result.Succeeded);
        Assert.NotNull(captured);
        Assert.Contains("& 'C:\\Repo\\Build\\Build-Project.ps1' -Build:$true -PublishNuget:$false -PublishGitHub:$false -UpdateVersions:$false",
            captured!.CommandText, StringComparison.Ordinal);
        Assert.DoesNotContain("Invoke-ProjectBuild -Build", captured.CommandText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteBuildAsync_ForwardsLiveOutputCallbacksToPowerShellRunner()
    {
        var received = new List<string>();
        var service = new ProjectBuildCommandHostService(new StubPowerShellRunner(request => {
            request.OutputLineReceived?.Invoke("restoring packages");
            request.ErrorLineReceived?.Invoke("warning from build");
            return new PowerShellRunResult(0, "restoring packages", "warning from build", "pwsh");
        }));

        var result = await service.ExecuteBuildAsync(new ProjectBuildCommandBuildRequest {
            RepositoryRoot = Path.GetTempPath(),
            ModulePath = "PSPublishModule",
            OutputLineReceived = line => received.Add("out: " + line),
            ErrorLineReceived = line => received.Add("err: " + line)
        });

        Assert.True(result.Succeeded);
        Assert.Equal(["out: restoring packages", "err: warning from build"], received);
        Assert.Equal("restoring packages", result.StandardOutput);
        Assert.Equal("warning from build", result.StandardError);
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
