using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModuleNativeCompatibilityBenchmarkRunnerTests
{
    [Fact]
    public void Prepare_RemovesCopiedManagedInstallBeforeNativeUpdateSeed()
    {
        using var moduleRoot = new TemporaryDirectory();
        var copiedModulePath = Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0");
        Directory.CreateDirectory(copiedModulePath);
        File.WriteAllText(Path.Combine(copiedModulePath, "Company.Tools.psd1"), "@{ ModuleVersion = '1.0.0' }");
        File.WriteAllText(Path.Combine(copiedModulePath, "Company.Tools.psm1"), string.Empty);
        var requests = new List<PowerShellRunRequest>();
        var runner = new ManagedModuleNativeCompatibilityBenchmarkRunner(
            new StubPowerShellRunner(request =>
            {
                requests.Add(request);
                return new PowerShellRunResult(0, "PFMOD::INSTALL::OK", string.Empty, "powershell.exe");
            }),
            new NullLogger());

        runner.Prepare(
            new ManagedModuleBenchmarkScenario
            {
                Operation = ManagedModuleBenchmarkOperation.Update,
                Repository = new ManagedModuleRepository("PSGallery", "https://www.powershellgallery.com/api/v2"),
                Name = "Company.Tools",
                ModuleRoot = moduleRoot.Path,
                Scope = ManagedModuleInstallScope.Custom
            },
            ManagedModuleBenchmarkEngine.PowerShellGet);

        Assert.Single(requests);
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools")));
        Assert.Equal(moduleRoot.Path, requests[0].WorkingDirectory);
        if (Path.DirectorySeparatorChar == '\\')
        {
            Assert.Contains("powershell.exe", requests[0].ExecutableOverride!, StringComparison.OrdinalIgnoreCase);
        }
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
