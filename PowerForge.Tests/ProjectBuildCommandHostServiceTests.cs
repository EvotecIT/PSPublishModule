namespace PowerForge.Tests;

public sealed class ProjectBuildCommandHostServiceTests
{
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
