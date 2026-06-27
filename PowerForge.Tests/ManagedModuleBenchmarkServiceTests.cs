using System.Net;
using System.Net.Http.Headers;
using System.Text;
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
    public async Task RunAsync_MeasuresManagedPublishScenario()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        using var packageOutput = new TemporaryDirectory();
        WritePublishModule(moduleRoot.Path, "Company.Tools", "1.0.0");
        var service = new ManagedModuleBenchmarkService(new NullLogger());

        var result = await service.RunAsync(new ManagedModuleBenchmarkRequest
        {
            Scenarios = new[]
            {
                new ManagedModuleBenchmarkScenario
                {
                    Id = "publish-small",
                    Operation = ManagedModuleBenchmarkOperation.Publish,
                    Repository = new ManagedModuleRepository("Local", feed.Path),
                    Name = "Company.Tools",
                    ModulePath = moduleRoot.Path,
                    PackageOutputDirectory = packageOutput.Path
                }
            }
        });

        var run = Assert.Single(result.Runs);
        Assert.True(run.Succeeded);
        Assert.Equal(ManagedModuleBenchmarkOperation.Publish, run.Operation);
        Assert.Equal("Published", run.Status);
        Assert.Equal("Company.Tools", run.ModuleName);
        Assert.Equal("1.0.0", run.Version);
        Assert.True(run.Published);
        Assert.False(run.Duplicate);
        Assert.True(run.PackageBytes > 0);
        Assert.True(run.FileCount > 0);
        Assert.True(run.PackageCount > 0);
        Assert.True(File.Exists(run.PackagePath));
        Assert.True(File.Exists(Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg")));
    }

    [Fact]
    public async Task RunAsync_MeasuresManagedPrivateMetadataLookupScenario()
    {
        var requests = new List<RecordedRequest>();
        using var client = new HttpClient(new PrivateMetadataHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var service = new ManagedModuleBenchmarkService(new NullLogger(), repositoryClient: repositoryClient);

        var result = await service.RunAsync(new ManagedModuleBenchmarkRequest
        {
            Scenarios = new[]
            {
                new ManagedModuleBenchmarkScenario
                {
                    Id = "private-metadata",
                    Operation = ManagedModuleBenchmarkOperation.Find,
                    Repository = new ManagedModuleRepository("Private", "https://private.example.test/v3/index.json"),
                    Name = "Company.Tools",
                    Credential = new RepositoryCredential
                    {
                        UserName = "build",
                        Secret = "token"
                    }
                }
            }
        });

        var run = Assert.Single(result.Runs);
        Assert.True(run.Succeeded);
        Assert.Equal(ManagedModuleBenchmarkOperation.Find, run.Operation);
        Assert.Equal("Found", run.Status);
        Assert.Equal("Company.Tools", run.ModuleName);
        Assert.Equal("1.1.0", run.Version);
        Assert.Equal(2, run.PackageCount);
        Assert.Equal(2, run.RepositoryRequestCount);
        Assert.Equal(0, run.PackageBytes);
        Assert.True(run.ServiceElapsed.GetValueOrDefault() > TimeSpan.Zero);
        Assert.All(requests, request =>
        {
            Assert.NotNull(request.Authorization);
            Assert.Equal("Basic", request.Authorization!.Scheme);
            var decoded = Encoding.ASCII.GetString(Convert.FromBase64String(request.Authorization.Parameter!));
            Assert.Equal("build:token", decoded);
        });
    }

    [Theory]
    [InlineData(ManagedModuleBenchmarkOperation.Install)]
    [InlineData(ManagedModuleBenchmarkOperation.Save)]
    public async Task RunAsync_MeasuresPrivatePackageDeliveryWithCredentials(ManagedModuleBenchmarkOperation operation)
    {
        var requests = new List<RecordedRequest>();
        using var packageRoot = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        var packagePath = Path.Combine(packageRoot.Path, "Company.Tools.1.0.0.nupkg");
        TestPackageFactory.Create(
            packagePath,
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));
        using var client = new HttpClient(new PrivateMetadataHandler(requests, File.ReadAllBytes(packagePath)));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var installService = new ManagedModuleInstallService(new NullLogger(), repositoryClient);
        var service = new ManagedModuleBenchmarkService(new NullLogger(), installService: installService);

        var result = await service.RunAsync(new ManagedModuleBenchmarkRequest
        {
            Scenarios = new[]
            {
                new ManagedModuleBenchmarkScenario
                {
                    Id = "private-" + operation.ToString().ToLowerInvariant(),
                    Operation = operation,
                    Repository = new ManagedModuleRepository("Private", "https://private.example.test/v3/index.json"),
                    Name = "Company.Tools",
                    Version = "1.0.0",
                    Scope = ManagedModuleInstallScope.Custom,
                    ModuleRoot = moduleRoot.Path,
                    Credential = new RepositoryCredential
                    {
                        UserName = "build",
                        Secret = "token"
                    }
                }
            }
        });

        var run = Assert.Single(result.Runs);
        Assert.True(run.Succeeded);
        Assert.Equal(operation, run.Operation);
        Assert.Equal("Installed", run.Status);
        Assert.Equal("1.0.0", run.Version);
        Assert.Equal("1.0.0", run.ValidatedVersion);
        Assert.True(run.PackageBytes > 0);
        Assert.True(run.ExtractedBytes > 0);
        Assert.True(run.RepositoryRequestCount > 0);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0", "Company.Tools.psd1")));
        Assert.Contains(requests, request => request.Url == "https://private.example.test/v3/index.json");
        Assert.Contains(requests, request => request.Url == "https://private.example.test/packages/company.tools/1.0.0/company.tools.1.0.0.nupkg");
        Assert.All(requests, request =>
        {
            Assert.NotNull(request.Authorization);
            Assert.Equal("Basic", request.Authorization!.Scheme);
            var decoded = Encoding.ASCII.GetString(Convert.FromBase64String(request.Authorization.Parameter!));
            Assert.Equal("build:token", decoded);
        });
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
    public async Task RunAsync_IsolatesModuleRootPerEngineForMultiEngineMeasurements()
    {
        using var moduleRoot = new TemporaryDirectory();
        var roots = new List<string?>();
        var service = new ManagedModuleBenchmarkService(
            new NullLogger(),
            compatibilityRunner: (scenario, engine) =>
            {
                roots.Add(scenario.ModuleRoot);
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
                    Id = "compat-install-isolated-roots",
                    Operation = ManagedModuleBenchmarkOperation.Install,
                    Repository = new ManagedModuleRepository("Local", "https://example.test/index.json"),
                    Name = "Company.Tools",
                    Version = "1.0.0",
                    ModuleRoot = moduleRoot.Path
                }
            }
        });

        Assert.Equal(2, result.Runs.Count);
        Assert.Equal(2, roots.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(Path.Combine(moduleRoot.Path, "PSResourceGet", "1"), roots);
        Assert.Contains(Path.Combine(moduleRoot.Path, "PowerShellGet", "1"), roots);
        Assert.All(result.Runs, run => Assert.StartsWith(moduleRoot.Path, run.ModuleRoot, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_RecordsUnsupportedCompatibilityVersionPolicyAsFailure()
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
                    Id = "compat-version-policy",
                    Operation = ManagedModuleBenchmarkOperation.Save,
                    Repository = new ManagedModuleRepository("Local", "https://example.test/index.json"),
                    Name = "Company.Tools",
                    ModuleRoot = root.Path,
                    VersionPolicy = "[1.0.0,2.0.0)"
                }
            }
        });

        var run = Assert.Single(result.Runs);
        Assert.False(run.Succeeded);
        Assert.Equal("PSResourceGet", run.Engine);
        Assert.Contains("VersionPolicy", run.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_BlocksDefaultCompatibilityInstallWithModuleRoot()
    {
        using var moduleRoot = new TemporaryDirectory();
        var runner = new StubPowerShellRunner(_ => throw new InvalidOperationException("Runner should not be called."));
        var service = new ManagedModuleBenchmarkService(new NullLogger(), compatibilityPowerShellRunner: runner);

        var result = await service.RunAsync(new ManagedModuleBenchmarkRequest
        {
            ContinueOnError = true,
            Engines = new[] { ManagedModuleBenchmarkEngine.PowerShellGet },
            Scenarios = new[]
            {
                new ManagedModuleBenchmarkScenario
                {
                    Id = "compat-install-isolated",
                    Operation = ManagedModuleBenchmarkOperation.Install,
                    Repository = new ManagedModuleRepository("CompanyRepository", "https://example.test/index.json"),
                    Name = "Company.Tools",
                    Version = "1.0.0",
                    ModuleRoot = moduleRoot.Path,
                    AllowClobber = true
                }
            }
        });

        var run = Assert.Single(result.Runs);
        Assert.False(run.Succeeded);
        Assert.Equal("Failed", run.Status);
        Assert.Equal("PowerShellGet", run.Engine);
        Assert.Contains("disabled", run.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("custom module-root isolation", run.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_BlocksDefaultCompatibilityInstallWithoutModuleRoot()
    {
        var runner = new StubPowerShellRunner(_ => throw new InvalidOperationException("Runner should not be called."));
        var service = new ManagedModuleBenchmarkService(new NullLogger(), compatibilityPowerShellRunner: runner);

        var result = await service.RunAsync(new ManagedModuleBenchmarkRequest
        {
            ContinueOnError = true,
            Engines = new[] { ManagedModuleBenchmarkEngine.PowerShellGet },
            Scenarios = new[]
            {
                new ManagedModuleBenchmarkScenario
                {
                    Id = "compat-install-no-root",
                    Operation = ManagedModuleBenchmarkOperation.Install,
                    Repository = new ManagedModuleRepository("CompanyRepository", "https://example.test/index.json"),
                    Name = "Company.Tools",
                    Version = "1.0.0"
                }
            }
        });

        var run = Assert.Single(result.Runs);
        Assert.False(run.Succeeded);
        Assert.Equal("Failed", run.Status);
        Assert.Contains("disabled", run.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        var gate = Assert.Single(result.TransitionGates);
        Assert.Equal(ManagedModuleBenchmarkOperation.Install, gate.Operation);
        Assert.Equal(ManagedModuleBenchmarkTransitionGateStatus.Blocked, gate.Status);
        Assert.False(gate.ReadyForDefaultManagedTransport);
        Assert.True(gate.CompatibilityFallbackRequired);
        Assert.True(gate.NativeIsolationRequired);
        Assert.Contains("isolated disposable host", gate.CompatibilityFallbackReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(gate.Reasons, reason => reason.Contains("explicit disposable-host runner", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_MarksInstallTransitionReadyWhenManagedAndCompatibilityBaselinesPass()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));
        var service = new ManagedModuleBenchmarkService(
            new NullLogger(),
            compatibilityRunner: (scenario, engine) => new ModuleDependencyInstallResult(
                scenario.Name,
                null,
                "1.0.0",
                scenario.Version,
                ModuleDependencyInstallStatus.Installed,
                engine.ToString(),
                null));

        var result = await service.RunAsync(new ManagedModuleBenchmarkRequest
        {
            Engines = new[]
            {
                ManagedModuleBenchmarkEngine.Managed,
                ManagedModuleBenchmarkEngine.PSResourceGet,
                ManagedModuleBenchmarkEngine.PowerShellGet
            },
            Scenarios = new[]
            {
                new ManagedModuleBenchmarkScenario
                {
                    Id = "install-transition",
                    Operation = ManagedModuleBenchmarkOperation.Install,
                    Repository = new ManagedModuleRepository("Local", feed.Path),
                    Name = "Company.Tools",
                    Version = "1.0.0",
                    Scope = ManagedModuleInstallScope.Custom,
                    ModuleRoot = moduleRoot.Path,
                    AllowClobber = true
                }
            }
        });

        Assert.Equal(3, result.Runs.Count);
        var gate = Assert.Single(result.TransitionGates);
        Assert.Equal(ManagedModuleBenchmarkOperation.Install, gate.Operation);
        Assert.Equal(ManagedModuleBenchmarkTransitionGateStatus.Ready, gate.Status);
        Assert.True(gate.ReadyForDefaultManagedTransport);
        Assert.False(gate.CompatibilityFallbackRequired);
        Assert.False(gate.NativeIsolationRequired);
        Assert.Null(gate.CompatibilityFallbackReason);
        Assert.Equal(1, gate.SuccessfulManagedRunCount);
        Assert.Equal(2, gate.SuccessfulCompatibilityRunCount);
        Assert.Contains("PSResourceGet", gate.CoveredCompatibilityEngines);
        Assert.Contains("PowerShellGet", gate.CoveredCompatibilityEngines);
    }

    [Theory]
    [InlineData(ManagedModuleBenchmarkEngine.PSResourceGet, "Save-PSResource", "PFPSRG::SAVE::ITEM::")]
    [InlineData(ManagedModuleBenchmarkEngine.PowerShellGet, "Save-Module", "PFPWSGET::SAVE::ITEM::")]
    public async Task RunAsync_MeasuresCompatibilitySaveScenario(
        ManagedModuleBenchmarkEngine engine,
        string expectedCommand,
        string outputPrefix)
    {
        using var saveRoot = new TemporaryDirectory();
        var requests = new List<PowerShellRunRequest>();
        var runner = new StubPowerShellRunner(request =>
        {
            requests.Add(request);
            var script = File.ReadAllText(request.ScriptPath!);
            Assert.Contains(expectedCommand, script, StringComparison.Ordinal);
            var output = outputPrefix + Encode("Company.Tools") + "::" + Encode("1.0.0");
            return new PowerShellRunResult(0, output, string.Empty, "pwsh");
        });
        var service = new ManagedModuleBenchmarkService(new NullLogger(), compatibilityPowerShellRunner: runner);

        var result = await service.RunAsync(new ManagedModuleBenchmarkRequest
        {
            ContinueOnError = true,
            Engines = new[] { engine },
            Scenarios = new[]
            {
                new ManagedModuleBenchmarkScenario
                {
                    Id = "compat-save",
                    Operation = ManagedModuleBenchmarkOperation.Save,
                    Repository = new ManagedModuleRepository("CompanyRepository", "https://example.test/index.json"),
                    Name = "Company.Tools",
                    Version = "1.0.0",
                    ModuleRoot = saveRoot.Path
                }
            }
        });

        var run = Assert.Single(result.Runs);
        Assert.True(run.Succeeded);
        Assert.Equal(engine.ToString(), run.Engine);
        Assert.Equal("Saved", run.Status);
        Assert.Equal("1.0.0", run.Version);
        Assert.Equal(saveRoot.Path, run.ModuleRoot);
        Assert.Single(requests);
        Assert.Contains("CompanyRepository", requests[0].Arguments);
    }

    [Theory]
    [InlineData(ManagedModuleBenchmarkEngine.PSResourceGet, "Publish-PSResource")]
    [InlineData(ManagedModuleBenchmarkEngine.PowerShellGet, "Publish-Module")]
    public async Task RunAsync_MeasuresCompatibilityPublishScenario(
        ManagedModuleBenchmarkEngine engine,
        string expectedCommand)
    {
        using var moduleRoot = new TemporaryDirectory();
        var requests = new List<PowerShellRunRequest>();
        var runner = new StubPowerShellRunner(request =>
        {
            requests.Add(request);
            var script = File.ReadAllText(request.ScriptPath!);
            Assert.Contains(expectedCommand, script, StringComparison.Ordinal);
            return new PowerShellRunResult(0, string.Empty, string.Empty, "pwsh");
        });
        var service = new ManagedModuleBenchmarkService(new NullLogger(), compatibilityPowerShellRunner: runner);

        var result = await service.RunAsync(new ManagedModuleBenchmarkRequest
        {
            ContinueOnError = true,
            Engines = new[] { engine },
            Scenarios = new[]
            {
                new ManagedModuleBenchmarkScenario
                {
                    Id = "compat-publish",
                    Operation = ManagedModuleBenchmarkOperation.Publish,
                    Repository = new ManagedModuleRepository("CompanyRepository", "https://example.test/index.json"),
                    Name = "Company.Tools",
                    Version = "1.0.0",
                    ModulePath = moduleRoot.Path
                }
            }
        });

        var run = Assert.Single(result.Runs);
        Assert.True(run.Succeeded);
        Assert.Equal(engine.ToString(), run.Engine);
        Assert.Equal("Published", run.Status);
        Assert.True(run.Published);
        Assert.Equal("CompanyRepository", run.PublishSource);
        Assert.Single(requests);
        Assert.Contains(moduleRoot.Path, requests[0].Arguments);
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

    private static void WritePublishModule(string moduleRoot, string moduleName, string version)
    {
        Directory.CreateDirectory(moduleRoot);
        File.WriteAllText(Path.Combine(moduleRoot, moduleName + ".psm1"), string.Empty);
        File.WriteAllText(
            Path.Combine(moduleRoot, moduleName + ".psd1"),
            string.Join(Environment.NewLine, new[]
            {
                "@{",
                $"    RootModule = '{moduleName}.psm1'",
                $"    ModuleVersion = '{version}'",
                "    GUID = '11111111-1111-1111-1111-111111111111'",
                "    Author = 'Evotec'",
                "    Description = 'Benchmark publish module.'",
                "    FunctionsToExport = @()",
                "    CmdletsToExport = @()",
                "    AliasesToExport = @()",
                "}"
            }));
    }

    private static string Encode(string value)
        => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value));

    private sealed class StubPowerShellRunner : IPowerShellRunner
    {
        private readonly Func<PowerShellRunRequest, PowerShellRunResult> _run;

        public StubPowerShellRunner(Func<PowerShellRunRequest, PowerShellRunResult> run)
        {
            _run = run;
        }

        public PowerShellRunResult Run(PowerShellRunRequest request)
            => _run(request);
    }

    private sealed class PrivateMetadataHandler : HttpMessageHandler
    {
        private readonly List<RecordedRequest> _requests;
        private readonly byte[]? _packageBytes;

        public PrivateMetadataHandler(List<RecordedRequest> requests, byte[]? packageBytes = null)
        {
            _requests = requests;
            _packageBytes = packageBytes;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("Request URI is required.");
            _requests.Add(new RecordedRequest(uri.AbsoluteUri, request.Headers.Authorization));

            if (uri.AbsoluteUri == "https://private.example.test/v3/index.json")
            {
                return Json("{\"resources\":[" +
                            "{\"@id\":\"https://private.example.test/packages/\",\"@type\":\"PackageBaseAddress/3.0.0\"}" +
                            "]}");
            }

            if (uri.AbsoluteUri == "https://private.example.test/packages/company.tools/index.json")
                return Json("{\"versions\":[\"1.0.0\",\"1.1.0-preview1\",\"1.1.0\"]}");

            if (uri.AbsoluteUri == "https://private.example.test/packages/company.tools/1.0.0/company.tools.1.0.0.nupkg" &&
                _packageBytes is not null)
                return Binary(_packageBytes);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static Task<HttpResponseMessage> Json(string json)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });

        private static Task<HttpResponseMessage> Binary(byte[] content)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            });
    }

    private sealed class RecordedRequest
    {
        public RecordedRequest(string url, AuthenticationHeaderValue? authorization)
        {
            Url = url;
            Authorization = authorization;
        }

        public string Url { get; }

        public AuthenticationHeaderValue? Authorization { get; }
    }
}
