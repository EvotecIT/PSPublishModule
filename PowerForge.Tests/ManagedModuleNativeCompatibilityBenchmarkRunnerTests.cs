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
        var scripts = new List<string>();
        var runner = new ManagedModuleNativeCompatibilityBenchmarkRunner(
            new StubPowerShellRunner(request =>
            {
                requests.Add(request);
                scripts.Add(File.ReadAllText(request.ScriptPath!));
                if (scripts[^1].Contains("Register-PSRepository", StringComparison.Ordinal))
                {
                    return new PowerShellRunResult(0, "PFPWSGET::REPO::CREATED::1", string.Empty, "powershell.exe");
                }

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

        Assert.Equal(2, requests.Count);
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools")));
        Assert.All(requests, request => Assert.Equal(moduleRoot.Path, request.WorkingDirectory));
        Assert.All(requests, request => Assert.Contains("PSGallery", request.Arguments));
        Assert.All(requests, request => Assert.Equal(Path.Combine(moduleRoot.Path, "temp"), request.EnvironmentVariables!["TEMP"]));
        Assert.All(requests, request => Assert.Equal(Path.Combine(moduleRoot.Path, "temp"), request.EnvironmentVariables!["TMP"]));
        Assert.Contains(scripts, script => script.Contains("Register-PSRepository", StringComparison.Ordinal));
        Assert.Contains(scripts, script => script.Contains("Install-Module", StringComparison.Ordinal));
        Assert.Contains(scripts, script => script.Contains("SecurityProtocol", StringComparison.Ordinal) && script.Contains("Tls12", StringComparison.Ordinal));
        Assert.Contains(scripts, script => script.Contains("Parameters.ContainsKey('AcceptLicense')", StringComparison.Ordinal));
        Assert.Contains(scripts, script => script.Contains("Install-PackageProvider", StringComparison.Ordinal) && script.Contains("Scope CurrentUser", StringComparison.Ordinal));
        var repositoryFile = Path.Combine(
            moduleRoot.Path,
            "home",
            "AppData",
            "Local",
            "Microsoft",
            "Windows",
            "PowerShell",
            "PowerShellGet",
            "PSRepositories.xml");
        Assert.Contains("https://www.powershellgallery.com/api/v2", File.ReadAllText(repositoryFile), StringComparison.Ordinal);
        if (Path.DirectorySeparatorChar == '\\')
        {
            Assert.All(requests, request => Assert.Contains("powershell.exe", request.ExecutableOverride!, StringComparison.OrdinalIgnoreCase));
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
