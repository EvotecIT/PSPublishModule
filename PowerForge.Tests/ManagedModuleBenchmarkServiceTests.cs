using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModuleBenchmarkServiceTests
{
    [Fact]
    public async Task RunAsync_MeasuresManagedInstallScenario()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));
        var service = new ManagedModuleBenchmarkService(new NullLogger());

        var result = await service.RunAsync(new ManagedModuleBenchmarkRequest
        {
            Scenarios = new[]
            {
                new ManagedModuleBenchmarkScenario
                {
                    Id = "install-small",
                    Operation = ManagedModuleBenchmarkOperation.Install,
                    Repository = new ManagedModuleRepository("Local", feed.Path),
                    Name = "Company.Tools",
                    Version = "1.0.0",
                    Scope = ManagedModuleInstallScope.Custom,
                    ModuleRoot = moduleRoot.Path
                }
            }
        });

        var run = Assert.Single(result.Runs);
        Assert.True(result.CompletedAtUtc >= result.StartedAtUtc);
        Assert.Equal("install-small", run.ScenarioId);
        Assert.Equal(ManagedModuleBenchmarkOperation.Install, run.Operation);
        Assert.Equal("Managed", run.Engine);
        Assert.Equal("Company.Tools", run.ModuleName);
        Assert.True(run.Succeeded);
        Assert.Equal("Installed", run.Status);
        Assert.Equal("1.0.0", run.Version);
        Assert.True(run.Elapsed > TimeSpan.Zero);
        Assert.True(run.ServiceElapsed > TimeSpan.Zero);
        Assert.True(run.PackageBytes > 0);
        Assert.True(run.ExtractedBytes > 0);
        Assert.True(run.ExtractionElapsed.GetValueOrDefault() > TimeSpan.Zero);
        Assert.Equal(1, run.FileCount);
        Assert.Equal(1, run.PackageCount);
        Assert.Equal(0, run.RepositoryRequestCount);
        Assert.True(run.TotalPackageBytes >= run.PackageBytes);
        Assert.True(run.TotalExtractedBytes >= run.ExtractedBytes);
        Assert.True(run.TotalFileCount >= run.FileCount);
        Assert.True(run.TotalExtractionElapsed.GetValueOrDefault() > TimeSpan.Zero);
        Assert.True(run.FinalDiskBytes > 0);
        Assert.Equal("1.0.0", run.ValidatedVersion);
        Assert.True(run.VersionValidationSucceeded);
        Assert.Contains("Validated manifest version", run.VersionValidationMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0", "Company.Tools.psd1")));
    }

    [Fact]
    public async Task RunAsync_MeasuresManagedSaveScenario()
    {
        using var feed = new TemporaryDirectory();
        using var saveRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));
        var service = new ManagedModuleBenchmarkService(new NullLogger());

        var result = await service.RunAsync(new ManagedModuleBenchmarkRequest
        {
            Scenarios = new[]
            {
                new ManagedModuleBenchmarkScenario
                {
                    Id = "save-small",
                    Operation = ManagedModuleBenchmarkOperation.Save,
                    Repository = new ManagedModuleRepository("Local", feed.Path),
                    Name = "Company.Tools",
                    Version = "1.0.0",
                    ModuleRoot = saveRoot.Path
                }
            }
        });

        var run = Assert.Single(result.Runs);
        Assert.True(run.Succeeded);
        Assert.Equal(ManagedModuleBenchmarkOperation.Save, run.Operation);
        Assert.Equal("Installed", run.Status);
        Assert.Equal(saveRoot.Path, run.ModuleRoot);
        Assert.Equal("1.0.0", run.ValidatedVersion);
        Assert.True(run.VersionValidationSucceeded);
        Assert.True(run.FinalDiskBytes > 0);
        Assert.True(File.Exists(Path.Combine(saveRoot.Path, "Company.Tools", "1.0.0", "Company.Tools.psd1")));
    }

    [Fact]
    public async Task RunAsync_MeasuresManagedUpdateScenario()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.1.0.nupkg"),
            "Company.Tools",
            "1.1.0",
            files: CreateModuleFiles("1.1.0"));
        var installedPath = Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0");
        Directory.CreateDirectory(installedPath);
        File.WriteAllText(Path.Combine(installedPath, "Company.Tools.psd1"), "@{ ModuleVersion = '1.0.0' }");
        var service = new ManagedModuleBenchmarkService(new NullLogger());

        var result = await service.RunAsync(new ManagedModuleBenchmarkRequest
        {
            Scenarios = new[]
            {
                new ManagedModuleBenchmarkScenario
                {
                    Id = "update-small",
                    Operation = ManagedModuleBenchmarkOperation.Update,
                    Repository = new ManagedModuleRepository("Local", feed.Path),
                    Name = "Company.Tools",
                    Scope = ManagedModuleInstallScope.Custom,
                    ModuleRoot = moduleRoot.Path
                }
            }
        });

        var run = Assert.Single(result.Runs);
        Assert.True(run.Succeeded);
        Assert.Equal("Updated", run.Status);
        Assert.Equal("1.0.0", run.PreviousVersion);
        Assert.Equal("1.1.0", run.Version);
        Assert.True(run.PackageBytes > 0);
        Assert.Equal("1.1.0", run.ValidatedVersion);
        Assert.True(run.VersionValidationSucceeded);
        Assert.True(run.FinalDiskBytes > 0);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.1.0", "Company.Tools.psd1")));
    }

    [Fact]
    public async Task RunAsync_RecordsDependencyTotalsForManagedInstall()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Dependency.2.0.0.nupkg"),
            "Company.Dependency",
            "2.0.0",
            files: CreateDependencyModuleFiles("2.0.0"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.Dependency", "[2.0.0]", null) },
            files: CreateModuleFiles("1.0.0"));
        var service = new ManagedModuleBenchmarkService(new NullLogger());

        var result = await service.RunAsync(new ManagedModuleBenchmarkRequest
        {
            Scenarios = new[]
            {
                new ManagedModuleBenchmarkScenario
                {
                    Id = "install-with-dependency",
                    Operation = ManagedModuleBenchmarkOperation.Install,
                    Repository = new ManagedModuleRepository("Local", feed.Path),
                    Name = "Company.Tools",
                    Version = "1.0.0",
                    Scope = ManagedModuleInstallScope.Custom,
                    ModuleRoot = moduleRoot.Path
                }
            }
        });

        var run = Assert.Single(result.Runs);
        Assert.True(run.Succeeded);
        Assert.Equal(1, run.DependencyCount);
        Assert.Equal(2, run.PackageCount);
        Assert.True(run.TotalPackageBytes > run.PackageBytes);
        Assert.True(run.TotalExtractedBytes > run.ExtractedBytes);
        Assert.True(run.TotalExtractionElapsed.GetValueOrDefault() > run.ExtractionElapsed.GetValueOrDefault());
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Dependency", "2.0.0", "Company.Dependency.psd1")));
    }

    [Fact]
    public async Task RunAsync_RecordsFailuresWhenContinueOnErrorIsEnabled()
    {
        using var feed = new TemporaryDirectory();
        var service = new ManagedModuleBenchmarkService(new NullLogger());

        var result = await service.RunAsync(new ManagedModuleBenchmarkRequest
        {
            ContinueOnError = true,
            Scenarios = new[]
            {
                new ManagedModuleBenchmarkScenario
                {
                    Id = "missing-save-root",
                    Operation = ManagedModuleBenchmarkOperation.Save,
                    Repository = new ManagedModuleRepository("Local", feed.Path),
                    Name = "Company.Tools",
                    ModuleRoot = Path.Combine(feed.Path, "saved")
                }
            }
        });

        var run = Assert.Single(result.Runs);
        Assert.False(run.Succeeded);
        Assert.Equal("Failed", run.Status);
        Assert.Contains("No versions of 'Company.Tools'", run.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_MeasuresCompatibilityEnginesWithInjectedRunner()
    {
        var calls = new List<ManagedModuleBenchmarkEngine>();
        var service = new ManagedModuleBenchmarkService(
            new NullLogger(),
            compatibilityRunner: (scenario, engine) =>
            {
                calls.Add(engine);
                return new ModuleDependencyInstallResult(
                    scenario.Name,
                    null,
                    "1.0.0",
                    scenario.Version,
                    ModuleDependencyInstallStatus.Installed,
                    engine.ToString(),
                    null);
            });

        var result = await service.RunAsync(new ManagedModuleBenchmarkRequest
        {
            Engines = new[] { ManagedModuleBenchmarkEngine.PSResourceGet, ManagedModuleBenchmarkEngine.PowerShellGet },
            Scenarios = new[]
            {
                new ManagedModuleBenchmarkScenario
                {
                    Id = "compat-install",
                    Operation = ManagedModuleBenchmarkOperation.Install,
                    Repository = new ManagedModuleRepository("Local", "https://example.test/index.json"),
                    Name = "Company.Tools",
                    Version = "1.0.0"
                }
            }
        });

        Assert.Equal(
            new[] { ManagedModuleBenchmarkEngine.PSResourceGet, ManagedModuleBenchmarkEngine.PowerShellGet },
            calls);
        Assert.Collection(
            result.Runs,
            run =>
            {
                Assert.Equal("PSResourceGet", run.Engine);
                Assert.True(run.Succeeded);
                Assert.Equal("Installed", run.Status);
            },
            run =>
            {
                Assert.Equal("PowerShellGet", run.Engine);
                Assert.True(run.Succeeded);
                Assert.Equal("Installed", run.Status);
            });
    }

    [Fact]
    public async Task RunAsync_RecordsUnsupportedCompatibilitySaveAsFailure()
    {
        using var root = new TemporaryDirectory();
        var service = new ManagedModuleBenchmarkService(new NullLogger());

        var result = await service.RunAsync(new ManagedModuleBenchmarkRequest
        {
            ContinueOnError = true,
            Engines = new[] { ManagedModuleBenchmarkEngine.PSResourceGet },
            Scenarios = new[]
            {
                new ManagedModuleBenchmarkScenario
                {
                    Id = "compat-save",
                    Operation = ManagedModuleBenchmarkOperation.Save,
                    Repository = new ManagedModuleRepository("Local", "https://example.test/index.json"),
                    Name = "Company.Tools",
                    ModuleRoot = root.Path
                }
            }
        });

        var run = Assert.Single(result.Runs);
        Assert.False(run.Succeeded);
        Assert.Equal("PSResourceGet", run.Engine);
        Assert.Contains("support Install and Update", run.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, string> CreateModuleFiles(string version)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.Tools.psd1"] = "@{ ModuleVersion = '" + version + "' }"
        };

    private static IReadOnlyDictionary<string, string> CreateDependencyModuleFiles(string version)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.Dependency.psd1"] = "@{ ModuleVersion = '" + version + "' }"
        };
}
