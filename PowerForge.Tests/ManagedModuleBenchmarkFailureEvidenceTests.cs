using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModuleBenchmarkFailureEvidenceTests
{
    [Fact]
    public async Task RunAsync_MeasuresNativeCompatibilityDiskEvidenceFromValidatedModuleDirectory()
    {
        using var moduleRoot = new TemporaryDirectory();
        var modulePath = Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0");
        var sourcePath = Path.Combine(moduleRoot.Path, "source");
        Directory.CreateDirectory(modulePath);
        Directory.CreateDirectory(sourcePath);
        File.WriteAllText(Path.Combine(modulePath, "Company.Tools.psd1"), "@{ ModuleVersion = '1.0.0' }");
        File.WriteAllText(Path.Combine(modulePath, "Company.Tools.psm1"), "module-content");
        File.WriteAllText(Path.Combine(sourcePath, "large-source.bin"), new string('x', 4096));
        var expectedBytes = Directory.EnumerateFiles(modulePath, "*", SearchOption.AllDirectories)
            .Sum(static path => new FileInfo(path).Length);
        var service = new ManagedModuleBenchmarkService(
            new NullLogger(),
            compatibilityRunner: (scenario, engine) => new ModuleDependencyInstallResult(
                scenario.Name,
                installedVersion: null,
                resolvedVersion: "1.0.0",
                requestedVersion: scenario.Version,
                ModuleDependencyInstallStatus.Installed,
                engine.ToString(),
                message: null));

        var result = await service.RunAsync(new ManagedModuleBenchmarkRequest
        {
            ContinueOnError = true,
            Engines = new[] { ManagedModuleBenchmarkEngine.PSResourceGet },
            Scenarios = new[]
            {
                new ManagedModuleBenchmarkScenario
                {
                    Id = "native-module-evidence",
                    Operation = ManagedModuleBenchmarkOperation.Install,
                    Repository = new ManagedModuleRepository("PSGallery", "https://www.powershellgallery.com/api/v2"),
                    Name = "Company.Tools",
                    Version = "1.0.0",
                    Scope = ManagedModuleInstallScope.Custom,
                    ModuleRoot = moduleRoot.Path
                }
            }
        });

        var run = Assert.Single(result.Runs);
        Assert.True(run.Succeeded);
        Assert.Equal(modulePath, run.ModulePath);
        Assert.Equal(2, run.FileCount);
        Assert.Equal(expectedBytes, run.FinalDiskBytes);
    }

    [Fact]
    public async Task RunAsync_RecordsNativeCompatibilityDiskEvidenceWhenRunnerFailsAfterWritingModule()
    {
        using var moduleRoot = new TemporaryDirectory();
        var service = new ManagedModuleBenchmarkService(
            new NullLogger(),
            compatibilityRunner: (scenario, _) =>
            {
                var modulePath = Path.Combine(scenario.ModuleRoot!, scenario.Name, "1.0.0");
                Directory.CreateDirectory(modulePath);
                File.WriteAllText(Path.Combine(modulePath, scenario.Name + ".psd1"), "@{ ModuleVersion = '1.0.0' }");
                File.WriteAllText(Path.Combine(modulePath, scenario.Name + ".psm1"), string.Empty);
                throw new InvalidOperationException("native failed after writing module");
            });

        var result = await service.RunAsync(new ManagedModuleBenchmarkRequest
        {
            ContinueOnError = true,
            Engines = new[] { ManagedModuleBenchmarkEngine.PowerShellGet },
            Scenarios = new[]
            {
                new ManagedModuleBenchmarkScenario
                {
                    Id = "native-partial-install",
                    Operation = ManagedModuleBenchmarkOperation.Install,
                    Repository = new ManagedModuleRepository("PSGallery", "https://www.powershellgallery.com/api/v2"),
                    Name = "Company.Tools",
                    Version = "1.0.0",
                    Scope = ManagedModuleInstallScope.Custom,
                    ModuleRoot = moduleRoot.Path
                }
            }
        });

        var run = Assert.Single(result.Runs);
        Assert.False(run.Succeeded);
        Assert.Equal("Failed", run.Status);
        Assert.Equal("1.0.0", run.Version);
        Assert.Equal("1.0.0", run.ValidatedVersion);
        Assert.True(run.VersionValidationSucceeded);
        Assert.Contains("Validated native-installed manifest version 1.0.0.", run.VersionValidationMessage);
        Assert.Equal(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0"), run.ModulePath);
        Assert.True(run.FileCount >= 2);
        Assert.True(run.FinalDiskBytes > 0);
        Assert.Contains("native failed after writing module", run.ErrorMessage);
    }
}
