using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModuleBenchmarkPrepareFailureTests
{
    [Fact]
    public async Task RunAsync_RecordsFailedRunWhenNativeUpdatePrepareFails()
    {
        using var moduleRoot = new TemporaryDirectory();
        var installedPath = Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0");
        Directory.CreateDirectory(installedPath);
        File.WriteAllText(Path.Combine(installedPath, "Company.Tools.psd1"), "@{ ModuleVersion = '1.0.0' }");
        var service = new ManagedModuleBenchmarkService(
            new NullLogger(),
            new ThrowingPrepareRunner());

        var result = await service.RunAsync(new ManagedModuleBenchmarkRequest
        {
            ContinueOnError = true,
            EnableNativeInstallUpdateBenchmarks = true,
            Engines = new[] { ManagedModuleBenchmarkEngine.PowerShellGet },
            Scenarios = new[]
            {
                new ManagedModuleBenchmarkScenario
                {
                    Id = "prepare-failure",
                    Operation = ManagedModuleBenchmarkOperation.Update,
                    Repository = new ManagedModuleRepository("Local", moduleRoot.Path),
                    Name = "Company.Tools",
                    Version = "1.1.0",
                    Scope = ManagedModuleInstallScope.Custom,
                    ModuleRoot = moduleRoot.Path
                }
            }
        });

        var run = Assert.Single(result.Runs);
        Assert.False(run.Succeeded);
        Assert.Equal("Failed", run.Status);
        Assert.Equal("1.1.0", run.Version);
        Assert.Equal(moduleRoot.Path, run.ModuleRoot);
        Assert.Contains("native prepare failed", run.ErrorMessage);
    }

    private sealed class ThrowingPrepareRunner : IManagedModuleNativeCompatibilityBenchmarkRunner
    {
        public void Prepare(ManagedModuleBenchmarkScenario scenario, ManagedModuleBenchmarkEngine engine)
            => throw new InvalidOperationException("native prepare failed");

        public ModuleDependencyInstallResult Run(ManagedModuleBenchmarkScenario scenario, ManagedModuleBenchmarkEngine engine)
            => throw new NotSupportedException();
    }
}
