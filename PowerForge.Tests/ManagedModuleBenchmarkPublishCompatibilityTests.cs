using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModuleBenchmarkPublishCompatibilityTests
{
    [Theory]
    [InlineData(ManagedModuleBenchmarkEngine.PSResourceGet, "Register-PSResourceRepository", "Publish-PSResource", "Unregister-PSResourceRepository", "PFPSRG::REPO::CREATED::1", 2)]
    [InlineData(ManagedModuleBenchmarkEngine.PowerShellGet, "Register-PSRepository", "Publish-Module", "Unregister-PSRepository", "PFPWSGET::REPO::CREATED::1", 1)]
    public async Task RunAsync_registers_disposable_local_repository_for_compatibility_publish(
        ManagedModuleBenchmarkEngine engine,
        string expectedRegisterCommand,
        string expectedPublishCommand,
        string expectedUnregisterCommand,
        string createdOutput,
        int publishRepositoryArgumentIndex)
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        WritePublishModule(moduleRoot.Path, "Company.Tools", "1.0.0");
        var requests = new List<PowerShellRunRequest>();
        var runner = new StubPowerShellRunner(request =>
        {
            requests.Add(request);
            var script = File.ReadAllText(request.ScriptPath!);
            if (script.Contains(expectedRegisterCommand, StringComparison.Ordinal))
                return new PowerShellRunResult(0, createdOutput, string.Empty, "pwsh");
            if (script.Contains(expectedPublishCommand, StringComparison.Ordinal))
                return new PowerShellRunResult(0, string.Empty, string.Empty, "pwsh");
            if (script.Contains(expectedUnregisterCommand, StringComparison.Ordinal))
                return new PowerShellRunResult(0, string.Empty, string.Empty, "pwsh");

            throw new InvalidOperationException("Unexpected compatibility script: " + script);
        });
        var service = new ManagedModuleBenchmarkService(new NullLogger(), compatibilityPowerShellRunner: runner);

        var result = await service.RunAsync(new ManagedModuleBenchmarkRequest
        {
            ContinueOnError = false,
            Engines = new[] { engine },
            Scenarios = new[]
            {
                new ManagedModuleBenchmarkScenario
                {
                    Id = "compat-local-publish",
                    Operation = ManagedModuleBenchmarkOperation.Publish,
                    Repository = new ManagedModuleRepository("Local", feed.Path),
                    Name = "Company.Tools",
                    ModulePath = moduleRoot.Path
                }
            }
        });

        var run = Assert.Single(result.Runs);
        Assert.True(run.Succeeded);
        Assert.Equal("Published", run.Status);
        Assert.True(run.Published);
        Assert.Equal("1.0.0", run.Version);
        Assert.StartsWith("PFManagedPublish", run.PublishSource, StringComparison.Ordinal);
        Assert.Equal(3, requests.Count);
        var repositoryName = requests[0].Arguments[0];
        Assert.Equal(run.PublishSource, repositoryName);
        Assert.Equal(feed.Path, requests[0].Arguments[1]);
        Assert.Equal(repositoryName, requests[1].Arguments[publishRepositoryArgumentIndex]);
        Assert.Equal(repositoryName, requests[2].Arguments[0]);
    }

    [Fact]
    public async Task RunAsync_isolates_local_publish_feeds_for_multi_engine_comparison()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        WritePublishModule(moduleRoot.Path, "Company.Tools", "1.0.0");
        var requests = new List<PowerShellRunRequest>();
        var runner = new StubPowerShellRunner(request =>
        {
            requests.Add(request);
            var script = File.ReadAllText(request.ScriptPath!);
            if (script.Contains("Register-PSRepository", StringComparison.Ordinal))
                return new PowerShellRunResult(0, "PFPWSGET::REPO::CREATED::1", string.Empty, "pwsh");
            if (script.Contains("Publish-Module", StringComparison.Ordinal))
                return new PowerShellRunResult(0, string.Empty, string.Empty, "pwsh");
            if (script.Contains("Unregister-PSRepository", StringComparison.Ordinal))
                return new PowerShellRunResult(0, string.Empty, string.Empty, "pwsh");

            throw new InvalidOperationException("Unexpected compatibility script: " + script);
        });
        var service = new ManagedModuleBenchmarkService(new NullLogger(), compatibilityPowerShellRunner: runner);

        var result = await service.RunAsync(new ManagedModuleBenchmarkRequest
        {
            ContinueOnError = false,
            Engines = new[] { ManagedModuleBenchmarkEngine.Managed, ManagedModuleBenchmarkEngine.PowerShellGet },
            Scenarios = new[]
            {
                new ManagedModuleBenchmarkScenario
                {
                    Id = "publish-compare",
                    Operation = ManagedModuleBenchmarkOperation.Publish,
                    Repository = new ManagedModuleRepository("Local", feed.Path),
                    Name = "Company.Tools",
                    Version = "1.0.0",
                    ModulePath = moduleRoot.Path
                }
            }
        });

        Assert.Equal(2, result.Runs.Count);
        var managed = Assert.Single(result.Runs, run => run.Engine == ManagedModuleBenchmarkEngine.Managed.ToString());
        var compatibility = Assert.Single(result.Runs, run => run.Engine == ManagedModuleBenchmarkEngine.PowerShellGet.ToString());
        Assert.True(managed.Published);
        Assert.True(compatibility.Published);
        Assert.Contains(Path.Combine(feed.Path, "Managed", "1"), managed.PublishSource, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Path.Combine(feed.Path, "PowerShellGet", "1"), requests[0].Arguments[1]);
    }

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
}
