using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModuleImportValidationServiceTests
{
    [Fact]
    public void Validate_AddsSuccessfulPowerShell7ImportEvidence()
    {
        using var moduleRoot = new TemporaryDirectory();
        var modulePath = Path.Combine(moduleRoot.Path, "Company.Tools", "1.2.0");
        Directory.CreateDirectory(modulePath);
        File.WriteAllText(Path.Combine(modulePath, "Company.Tools.psd1"), "@{ ModuleVersion = '1.2.0' }");
        PowerShellRunRequest? captured = null;
        var service = CreateService(request =>
            {
                captured = request;
                return new PowerShellRunResult(0, "PFMMIMPORT::VERSION::1.2.0", string.Empty, @"C:\Tools\pwsh.exe");
            },
            @"C:\Tools\pwsh.exe");

        var result = service.Validate(
            CreateResult(modulePath, "1.2.0"),
            new[] { ManagedModuleImportValidationHost.PowerShell7 });

        var validation = Assert.Single(Assert.Single(result.Runs).ImportValidations);
        Assert.True(validation.Succeeded);
        Assert.Equal(ManagedModuleImportValidationHost.PowerShell7, validation.Host);
        Assert.Equal(@"C:\Tools\pwsh.exe", validation.HostExecutable);
        Assert.Equal("1.2.0", validation.ImportedVersion);
        Assert.NotNull(captured);
        Assert.Equal(PowerShellInvocationMode.Command, captured!.InvocationMode);
        Assert.Equal(@"C:\Tools\pwsh.exe", captured.ExecutableOverride);
        Assert.Contains("Company.Tools.psd1", captured.CommandText, StringComparison.Ordinal);
        Assert.Contains("PrivateData", captured.CommandText, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_TreatsSemanticallyEquivalentStableVersionsAsMatch()
    {
        using var moduleRoot = new TemporaryDirectory();
        var modulePath = Path.Combine(moduleRoot.Path, "Company.Tools", "1.2.0");
        Directory.CreateDirectory(modulePath);
        var service = CreateService(_ =>
            new PowerShellRunResult(0, "PFMMIMPORT::VERSION::1.2.0", string.Empty, "pwsh"));

        var result = service.Validate(
            CreateResult(modulePath, "1.2"),
            new[] { ManagedModuleImportValidationHost.PowerShell7 });

        Assert.True(Assert.Single(Assert.Single(result.Runs).ImportValidations).Succeeded);
    }

    [Fact]
    public void Validate_RecordsHostFailureEvidence()
    {
        using var moduleRoot = new TemporaryDirectory();
        var modulePath = Path.Combine(moduleRoot.Path, "Company.Tools", "1.2.0");
        Directory.CreateDirectory(modulePath);
        var service = CreateService(_ =>
            new PowerShellRunResult(1, string.Empty, "Import failed.", "pwsh"));

        var result = service.Validate(
            CreateResult(modulePath, "1.2.0"),
            new[] { ManagedModuleImportValidationHost.PowerShell7 });

        var validation = Assert.Single(Assert.Single(result.Runs).ImportValidations);
        Assert.False(validation.Succeeded);
        Assert.Equal("Import failed.", validation.Message);
    }

    [Fact]
    public void Validate_SkipsRunsWithoutInstallEvidence()
    {
        var service = CreateService(_ => throw new InvalidOperationException("Runner should not be called."));
        var result = new ManagedModuleBenchmarkResult
        {
            Runs = new[]
            {
                new ManagedModuleBenchmarkRunResult
                {
                    ModuleName = "Company.Tools",
                    Succeeded = false,
                    ModulePath = null
                }
            }
        };

        service.Validate(result, new[] { ManagedModuleImportValidationHost.PowerShell7 });

        Assert.Empty(Assert.Single(result.Runs).ImportValidations);
    }

    private static ManagedModuleBenchmarkResult CreateResult(string modulePath, string version)
        => new()
        {
            Runs = new[]
            {
                new ManagedModuleBenchmarkRunResult
                {
                    ScenarioId = "Install:Company.Tools",
                    Operation = ManagedModuleBenchmarkOperation.Install,
                    Engine = "Managed",
                    ModuleName = "Company.Tools",
                    Succeeded = true,
                    Status = "Installed",
                    Version = version,
                    ModulePath = modulePath
                }
            }
        };

    private static ManagedModuleImportValidationService CreateService(
        Func<PowerShellRunRequest, PowerShellRunResult> run,
        string executablePath = "pwsh")
        => new(
            new StubPowerShellRunner(run),
            _ => executablePath);

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
