using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModuleValidationServiceTests
{
    [Fact]
    public void Run_DocumentationValidation_CanRequireParameterAndTypeDescriptions()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var manifestPath = Path.Combine(root.FullName, "TestModule.psd1");
            File.WriteAllText(manifestPath, "@{}");

            var payload = new DocumentationExtractionPayload
            {
                Commands =
                {
                    new DocumentationCommandHelp
                    {
                        Name = "Get-Thing",
                        Synopsis = "Gets a thing.",
                        Description = "Long description.",
                        Examples = { new DocumentationExampleHelp { Code = "Get-Thing" } },
                        Parameters =
                        {
                            new DocumentationParameterHelp { Name = "Name", Description = "Thing name." },
                            new DocumentationParameterHelp { Name = "Mode", Description = string.Empty }
                        },
                        Inputs =
                        {
                            new DocumentationTypeHelp { Name = "ThingInput", ClrTypeName = "Demo.ThingInput", Description = string.Empty }
                        },
                        Outputs =
                        {
                            new DocumentationTypeHelp { Name = "ThingOutput", ClrTypeName = "Demo.ThingOutput", Description = "Output docs." }
                        }
                    }
                }
            };

            var settings = new ModuleValidationSettings
            {
                Enable = true,
                Structure = new ModuleStructureValidationSettings { Severity = ValidationSeverity.Off },
                Documentation = new DocumentationValidationSettings
                {
                    Severity = ValidationSeverity.Warning,
                    MinSynopsisPercent = 100,
                    MinDescriptionPercent = 100,
                    MinExampleCountPerCommand = 1,
                    MinParameterDescriptionPercent = 100,
                    MinTypeDescriptionPercent = 100
                },
                ScriptAnalyzer = new ScriptAnalyzerValidationSettings { Severity = ValidationSeverity.Off },
                FileIntegrity = new FileIntegrityValidationSettings { Severity = ValidationSeverity.Off },
                Tests = new TestSuiteValidationSettings { Severity = ValidationSeverity.Off },
                Binary = new BinaryModuleValidationSettings { Severity = ValidationSeverity.Off },
                Csproj = new CsprojValidationSettings { Severity = ValidationSeverity.Off }
            };

            var service = new ModuleValidationService(
                new NullLogger(),
                new DocumentationPayloadRunner(payload));

            var report = service.Run(new ModuleValidationSpec
            {
                ProjectRoot = root.FullName,
                StagingPath = root.FullName,
                ModuleName = "TestModule",
                ManifestPath = manifestPath,
                Settings = settings
            });

            var check = Assert.Single(report.Checks);
            Assert.Equal("Documentation", check.Name);
            Assert.Equal(CheckStatus.Warning, check.Status);
            Assert.Contains("parameter docs 1/2", check.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("type docs 1/2", check.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(check.Issues, issue => issue.Contains("Parameter description coverage", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(check.Issues, issue => issue.Contains("Get-Thing: parameter 'Mode' is missing description", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(check.Issues, issue => issue.Contains("Type description coverage", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(check.Issues, issue => issue.Contains("Get-Thing: type 'ThingInput' is missing description", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Run_DocumentationValidation_AllowsOptionalParameterDescriptions_WhenThresholdDisabled()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var manifestPath = Path.Combine(root.FullName, "TestModule.psd1");
            File.WriteAllText(manifestPath, "@{}");

            var payload = new DocumentationExtractionPayload
            {
                Commands =
                {
                    new DocumentationCommandHelp
                    {
                        Name = "Get-Thing",
                        Synopsis = "Gets a thing.",
                        Examples = { new DocumentationExampleHelp { Code = "Get-Thing" } },
                        Parameters =
                        {
                            new DocumentationParameterHelp { Name = "Mode", Description = string.Empty }
                        },
                        Inputs =
                        {
                            new DocumentationTypeHelp { Name = "ThingInput", ClrTypeName = "Demo.ThingInput", Description = "Input docs." }
                        }
                    }
                }
            };

            var settings = new ModuleValidationSettings
            {
                Enable = true,
                Structure = new ModuleStructureValidationSettings { Severity = ValidationSeverity.Off },
                Documentation = new DocumentationValidationSettings
                {
                    Severity = ValidationSeverity.Warning,
                    MinSynopsisPercent = 100,
                    MinDescriptionPercent = 0,
                    MinExampleCountPerCommand = 1,
                    MinParameterDescriptionPercent = 0,
                    MinTypeDescriptionPercent = 100
                },
                ScriptAnalyzer = new ScriptAnalyzerValidationSettings { Severity = ValidationSeverity.Off },
                FileIntegrity = new FileIntegrityValidationSettings { Severity = ValidationSeverity.Off },
                Tests = new TestSuiteValidationSettings { Severity = ValidationSeverity.Off },
                Binary = new BinaryModuleValidationSettings { Severity = ValidationSeverity.Off },
                Csproj = new CsprojValidationSettings { Severity = ValidationSeverity.Off }
            };

            var service = new ModuleValidationService(
                new NullLogger(),
                new DocumentationPayloadRunner(payload));

            var report = service.Run(new ModuleValidationSpec
            {
                ProjectRoot = root.FullName,
                StagingPath = root.FullName,
                ModuleName = "TestModule",
                ManifestPath = manifestPath,
                Settings = settings
            });

            var check = Assert.Single(report.Checks);
            Assert.Equal("Documentation", check.Name);
            Assert.Equal(CheckStatus.Pass, check.Status);
            Assert.DoesNotContain(check.Issues, issue => issue.Contains("parameter 'Mode'", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

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

    [Fact]
    public void Run_ScriptAnalyzerAssemblyConflict_FailsWhenSkipIsDisabled()
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
            Assert.Equal(CheckStatus.Warning, check.Status);
            Assert.Equal("assembly conflict", check.Summary);
            var issue = Assert.Single(check.Issues);
            Assert.Contains("already loaded", issue, StringComparison.OrdinalIgnoreCase);
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

    private sealed class DocumentationPayloadRunner : IPowerShellRunner
    {
        private readonly DocumentationExtractionPayload _payload;

        public DocumentationPayloadRunner(DocumentationExtractionPayload payload)
        {
            _payload = payload;
        }

        public PowerShellRunResult Run(PowerShellRunRequest request)
        {
            var outputPath = request.Arguments.First(arg => arg.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            using var stream = File.Create(outputPath);
            var serializer = new DataContractJsonSerializer(typeof(DocumentationExtractionPayload));
            serializer.WriteObject(stream, _payload);

            return new PowerShellRunResult(
                0,
                string.Empty,
                string.Empty,
                @"C:\Program Files\PowerShell\7\pwsh.exe");
        }
    }
}
