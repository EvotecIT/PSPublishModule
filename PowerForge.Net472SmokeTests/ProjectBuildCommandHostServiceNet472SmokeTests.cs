using PowerForge;

namespace PowerForge.Net472SmokeTests;

public sealed class ProjectBuildCommandHostServiceNet472SmokeTests
{
    [Fact]
    public async Task GeneratePlanAsync_BuildsExpectedPowerShellCommandUnderNet472()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "PowerForge.Net472SmokeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var modulePath = Path.Combine(tempRoot, "PSPublishModule.psd1");
        File.WriteAllText(modulePath, "@{}");

        PowerShellRunRequest? captured = null;
        var service = new ProjectBuildCommandHostService(new StubPowerShellRunner(request => {
            captured = request;
            return new PowerShellRunResult(0, "planned", string.Empty, "powershell.exe");
        }));

        var result = await service.GeneratePlanAsync(new ProjectBuildCommandPlanRequest {
            RepositoryRoot = @"C:\Repo",
            PlanOutputPath = @"C:\Repo\plan.json",
            ConfigPath = @"C:\Repo\Build\project.build.json",
            ModulePath = modulePath
        });

        Assert.True(result.Succeeded);
        Assert.NotNull(captured);
        Assert.Equal(PowerShellInvocationMode.Command, captured!.InvocationMode);
        Assert.Equal(@"C:\Repo", captured.WorkingDirectory);
        Assert.Contains("Import-Module", captured.CommandText, StringComparison.Ordinal);
        Assert.Contains($"'{modulePath}'", captured.CommandText, StringComparison.Ordinal);
        Assert.Contains("Set-Location -LiteralPath 'C:\\Repo'", captured.CommandText, StringComparison.Ordinal);
        Assert.Contains("Invoke-ProjectBuild -Plan:$true -PlanPath 'C:\\Repo\\plan.json'", captured.CommandText, StringComparison.Ordinal);
        Assert.Contains("-ConfigPath 'C:\\Repo\\Build\\project.build.json'", captured.CommandText, StringComparison.Ordinal);

        Directory.Delete(tempRoot, recursive: true);
    }

    private sealed class StubPowerShellRunner : IPowerShellRunner
    {
        private readonly Func<PowerShellRunRequest, PowerShellRunResult> _execute;

        public StubPowerShellRunner(Func<PowerShellRunRequest, PowerShellRunResult> execute)
        {
            _execute = execute;
        }

        public PowerShellRunResult Run(PowerShellRunRequest request)
        {
            return _execute(request);
        }
    }
}
