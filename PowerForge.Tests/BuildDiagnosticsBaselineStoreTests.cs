using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class BuildDiagnosticsBaselineStoreTests
{
    [Fact]
    public void Evaluate_GenerateBaseline_WritesCurrentDiagnostics()
    {
        var root = CreateTempDirectory();
        try
        {
            var diagnostics = new[]
            {
                new BuildDiagnostic(
                    ruleId: "FC-ENCODING",
                    area: BuildDiagnosticArea.FileConsistency,
                    severity: BuildDiagnosticSeverity.Warning,
                    scope: BuildDiagnosticScope.Project,
                    owner: BuildDiagnosticOwner.ModuleAuthor,
                    remediationKind: BuildDiagnosticRemediationKind.ManualFix,
                    canAutoFix: false,
                    summary: "Encoding normalization required",
                    details: "2 file(s) do not use UTF8BOM.",
                    recommendedAction: "Resave the affected files as UTF8BOM.",
                    sourcePath: "Private/Get-Test.ps1")
            };

            var comparison = BuildDiagnosticsBaselineStore.Evaluate(
                root,
                new ModulePipelineDiagnosticsOptions
                {
                    BaselinePath = ".powerforge/module-diagnostics-baseline.json",
                    GenerateBaseline = true
                },
                diagnostics);

            Assert.NotNull(comparison);
            Assert.True(comparison!.BaselineGenerated);
            Assert.Equal(1, comparison.CurrentDiagnosticCount);
            Assert.True(File.Exists(comparison.BaselinePath));
            var bytes = File.ReadAllBytes(comparison.BaselinePath);
            Assert.True(bytes.Length >= 3);
            Assert.Equal((byte)0xEF, bytes[0]);
            Assert.Equal((byte)0xBB, bytes[1]);
            Assert.Equal((byte)0xBF, bytes[2]);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Evaluate_LoadBaseline_MarksExistingAndNewDiagnostics()
    {
        var root = CreateTempDirectory();
        try
        {
            var existing = new BuildDiagnostic(
                ruleId: "COMPAT-DOTNET-FRAMEWORK",
                area: BuildDiagnosticArea.Compatibility,
                severity: BuildDiagnosticSeverity.Warning,
                scope: BuildDiagnosticScope.Project,
                owner: BuildDiagnosticOwner.ModuleAuthor,
                remediationKind: BuildDiagnosticRemediationKind.ManualFix,
                canAutoFix: false,
                summary: ".NET Framework APIs",
                details: "System.Web assembly may not be available in PowerShell 7.",
                recommendedAction: "Guard this code path.",
                sourcePath: "Module.ps1");

            var baselinePath = BuildDiagnosticsBaselineStore.ResolveBaselinePath(root, ".powerforge/module-diagnostics-baseline.json");
            _ = BuildDiagnosticsBaselineStore.Evaluate(
                root,
                new ModulePipelineDiagnosticsOptions
                {
                    BaselinePath = ".powerforge/module-diagnostics-baseline.json",
                    GenerateBaseline = true
                },
                new[] { existing });

            var current = new[]
            {
                existing,
                new BuildDiagnostic(
                    ruleId: "VALIDATION-DOCS",
                    area: BuildDiagnosticArea.Validation,
                    severity: BuildDiagnosticSeverity.Warning,
                    scope: BuildDiagnosticScope.Project,
                    owner: BuildDiagnosticOwner.ModuleAuthor,
                    remediationKind: BuildDiagnosticRemediationKind.ManualFix,
                    canAutoFix: false,
                    summary: "Complete command documentation",
                    details: "2 command(s) missing required examples",
                    recommendedAction: "Add examples.",
                    sourcePath: "Docs/New-Test.md")
            };

            var comparison = BuildDiagnosticsBaselineStore.Evaluate(
                root,
                new ModulePipelineDiagnosticsOptions
                {
                    BaselinePath = baselinePath
                },
                current);

            Assert.NotNull(comparison);
            Assert.True(comparison!.BaselineLoaded);
            Assert.Equal(1, comparison.BaselineDiagnosticCount);
            Assert.Equal(2, comparison.CurrentDiagnosticCount);
            Assert.Equal(1, comparison.ExistingDiagnosticCount);
            Assert.Equal(1, comparison.NewDiagnosticCount);
            Assert.Contains(current, d => d.BaselineState == BuildDiagnosticBaselineState.Existing);
            Assert.Contains(current, d => d.BaselineState == BuildDiagnosticBaselineState.New);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Evaluate_IgnoresInfoDiagnosticsForBaseline()
    {
        var root = CreateTempDirectory();
        try
        {
            var diagnostics = new[]
            {
                new BuildDiagnostic(
                    ruleId: "FC-REPORT",
                    area: BuildDiagnosticArea.FileConsistency,
                    severity: BuildDiagnosticSeverity.Info,
                    scope: BuildDiagnosticScope.Project,
                    owner: BuildDiagnosticOwner.ModuleAuthor,
                    remediationKind: BuildDiagnosticRemediationKind.ManualFix,
                    canAutoFix: false,
                    summary: "Review the full report",
                    details: "The console table may not show every affected file.",
                    recommendedAction: "Open the CSV report.")
            };

            var comparison = BuildDiagnosticsBaselineStore.Evaluate(
                root,
                new ModulePipelineDiagnosticsOptions
                {
                    BaselinePath = ".powerforge/module-diagnostics-baseline.json",
                    GenerateBaseline = true
                },
                diagnostics);

            Assert.NotNull(comparison);
            Assert.Equal(0, comparison!.CurrentDiagnosticCount);
            Assert.Equal(BuildDiagnosticBaselineState.Unspecified, diagnostics[0].BaselineState);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
