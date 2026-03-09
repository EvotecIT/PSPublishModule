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
