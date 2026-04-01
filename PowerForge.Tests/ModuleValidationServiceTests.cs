using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModuleValidationServiceTests
{
    [Fact]
    public void Run_ScriptAnalyzerNoOutput_ReportsRunnerDetails()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "TestScript.ps1"), "Write-Output 'hello'");

            var settings = new ModuleValidationSettings
            {
                Enable = true,
                Structure = new ModuleStructureValidationSettings { Severity = ValidationSeverity.Off },
                Documentation = new DocumentationValidationSettings { Severity = ValidationSeverity.Off },
                ScriptAnalyzer = new ScriptAnalyzerValidationSettings
                {
                    Enable = true,
                    Severity = ValidationSeverity.Warning,
                    ExcludeDirectories = Array.Empty<string>(),
                    ExcludeRules = Array.Empty<string>(),
                    SkipIfUnavailable = false,
                    TimeoutSeconds = 5
                },
                FileIntegrity = new FileIntegrityValidationSettings { Severity = ValidationSeverity.Off },
                Tests = new TestSuiteValidationSettings { Severity = ValidationSeverity.Off },
                Binary = new BinaryModuleValidationSettings { Severity = ValidationSeverity.Off },
                Csproj = new CsprojValidationSettings { Severity = ValidationSeverity.Off }
            };

            var service = new ModuleValidationService(
                new NullLogger(),
                new StubPowerShellRunner(new PowerShellRunResult(
                    0,
                    "stdout-text",
                    "stderr-text",
                    @"C:\Program Files\PowerShell\7\pwsh.exe")));

            var report = service.Run(new ModuleValidationSpec
            {
                ProjectRoot = root.FullName,
                StagingPath = root.FullName,
                ModuleName = "TestModule",
                ManifestPath = string.Empty,
                Settings = settings
            });

            var check = Assert.Single(report.Checks);
            Assert.Equal("PSScriptAnalyzer", check.Name);
            Assert.Equal(CheckStatus.Warning, check.Status);
            Assert.Equal("no output", check.Summary);
            var issue = Assert.Single(check.Issues);
            Assert.Contains("without writing the results file", issue, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("runner=pwsh.exe", issue, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("stdout=stdout-text", issue, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("stderr=stderr-text", issue, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Run_ScriptAnalyzerInstallIfUnavailable_UsesRuntimeDependencyInstaller()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "TestScript.ps1"), "Write-Output 'hello'");

            var settings = new ModuleValidationSettings
            {
                Enable = true,
                Structure = new ModuleStructureValidationSettings { Severity = ValidationSeverity.Off },
                Documentation = new DocumentationValidationSettings { Severity = ValidationSeverity.Off },
                ScriptAnalyzer = new ScriptAnalyzerValidationSettings
                {
                    Enable = true,
                    Severity = ValidationSeverity.Warning,
                    ExcludeDirectories = Array.Empty<string>(),
                    ExcludeRules = Array.Empty<string>(),
                    SkipIfUnavailable = true,
                    InstallIfUnavailable = true,
                    TimeoutSeconds = 5
                },
                FileIntegrity = new FileIntegrityValidationSettings { Severity = ValidationSeverity.Off },
                Tests = new TestSuiteValidationSettings { Severity = ValidationSeverity.Off },
                Binary = new BinaryModuleValidationSettings { Severity = ValidationSeverity.Off },
                Csproj = new CsprojValidationSettings { Severity = ValidationSeverity.Off }
            };

            var installCalled = false;
            var service = new ModuleValidationService(
                new NullLogger(),
                new StubPowerShellRunner(new PowerShellRunResult(
                    0,
                    "PFVALID::SKIP::PSSA",
                    string.Empty,
                    @"C:\Program Files\PowerShell\7\pwsh.exe")),
                (dependencies, options) =>
                {
                    installCalled = true;
                    var dependency = Assert.Single(dependencies);
                    Assert.Equal("PSScriptAnalyzer", dependency.Name);
                    Assert.NotNull(options);
                    Assert.Equal(TimeSpan.FromMinutes(5), options.TimeoutPerModule);
                    return new[]
                    {
                        new ModuleDependencyInstallResult(
                            name: "PSScriptAnalyzer",
                            installedVersion: null,
                            resolvedVersion: "1.24.0",
                            requestedVersion: null,
                            status: ModuleDependencyInstallStatus.Satisfied,
                            installer: "PSResourceGet",
                            message: "Already installed")
                    };
                });

            var report = service.Run(new ModuleValidationSpec
            {
                ProjectRoot = root.FullName,
                StagingPath = root.FullName,
                ModuleName = "TestModule",
                ManifestPath = string.Empty,
                Settings = settings
            });

            Assert.True(installCalled);
            var check = Assert.Single(report.Checks);
            Assert.Equal("PSScriptAnalyzer", check.Name);
            Assert.Equal(CheckStatus.Pass, check.Status);
            Assert.Equal("skipped (not installed)", check.Summary);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Run_ScriptAnalyzerInstallFailure_StillSkipsWhenConfiguredToSkip()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "TestScript.ps1"), "Write-Output 'hello'");

            var settings = new ModuleValidationSettings
            {
                Enable = true,
                Structure = new ModuleStructureValidationSettings { Severity = ValidationSeverity.Off },
                Documentation = new DocumentationValidationSettings { Severity = ValidationSeverity.Off },
                ScriptAnalyzer = new ScriptAnalyzerValidationSettings
                {
                    Enable = true,
                    Severity = ValidationSeverity.Error,
                    ExcludeDirectories = Array.Empty<string>(),
                    ExcludeRules = Array.Empty<string>(),
                    SkipIfUnavailable = true,
                    InstallIfUnavailable = true,
                    TimeoutSeconds = 5
                },
                FileIntegrity = new FileIntegrityValidationSettings { Severity = ValidationSeverity.Off },
                Tests = new TestSuiteValidationSettings { Severity = ValidationSeverity.Off },
                Binary = new BinaryModuleValidationSettings { Severity = ValidationSeverity.Off },
                Csproj = new CsprojValidationSettings { Severity = ValidationSeverity.Off }
            };

            var service = new ModuleValidationService(
                new NullLogger(),
                new StubPowerShellRunner(new PowerShellRunResult(
                    0,
                    "PFVALID::SKIP::PSSA",
                    string.Empty,
                    @"C:\Program Files\PowerShell\7\pwsh.exe")),
                (_, _) => new[]
                {
                    new ModuleDependencyInstallResult(
                        name: "PSScriptAnalyzer",
                        installedVersion: null,
                        resolvedVersion: null,
                        requestedVersion: null,
                        status: ModuleDependencyInstallStatus.Failed,
                        installer: null,
                        message: "Repository unavailable")
                });

            var report = service.Run(new ModuleValidationSpec
            {
                ProjectRoot = root.FullName,
                StagingPath = root.FullName,
                ModuleName = "TestModule",
                ManifestPath = string.Empty,
                Settings = settings
            });

            var check = Assert.Single(report.Checks);
            Assert.Equal("PSScriptAnalyzer", check.Name);
            Assert.Equal(CheckStatus.Pass, check.Status);
            Assert.Equal("skipped (not installed)", check.Summary);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Run_ScriptAnalyzerAssemblyConflict_SkipsWhenConfiguredToSkip()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "TestScript.ps1"), "Write-Output 'hello'");

            var settings = new ModuleValidationSettings
            {
                Enable = true,
                Structure = new ModuleStructureValidationSettings { Severity = ValidationSeverity.Off },
                Documentation = new DocumentationValidationSettings { Severity = ValidationSeverity.Off },
                ScriptAnalyzer = new ScriptAnalyzerValidationSettings
                {
                    Enable = true,
                    Severity = ValidationSeverity.Warning,
                    ExcludeDirectories = Array.Empty<string>(),
                    ExcludeRules = Array.Empty<string>(),
                    SkipIfUnavailable = true,
                    TimeoutSeconds = 5
                },
                FileIntegrity = new FileIntegrityValidationSettings { Severity = ValidationSeverity.Off },
                Tests = new TestSuiteValidationSettings { Severity = ValidationSeverity.Off },
                Binary = new BinaryModuleValidationSettings { Severity = ValidationSeverity.Off },
                Csproj = new CsprojValidationSettings { Severity = ValidationSeverity.Off }
            };

            var service = new ModuleValidationService(
                new NullLogger(),
                new StubPowerShellRunner(new PowerShellRunResult(
                    0,
                    "PFVALID::SKIP::PSSA-CONFLICT",
                    string.Empty,
                    @"C:\Program Files\PowerShell\7\pwsh.exe")));

            var report = service.Run(new ModuleValidationSpec
            {
                ProjectRoot = root.FullName,
                StagingPath = root.FullName,
                ModuleName = "TestModule",
                ManifestPath = string.Empty,
                Settings = settings
            });

            var check = Assert.Single(report.Checks);
            Assert.Equal("PSScriptAnalyzer", check.Name);
            Assert.Equal(CheckStatus.Pass, check.Status);
            Assert.Equal("skipped (assembly conflict)", check.Summary);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private sealed class StubPowerShellRunner : IPowerShellRunner
    {
        private readonly PowerShellRunResult _result;

        public StubPowerShellRunner(PowerShellRunResult result)
        {
            _result = result;
        }

        public PowerShellRunResult Run(PowerShellRunRequest request)
        {
            return _result;
        }
    }
}
