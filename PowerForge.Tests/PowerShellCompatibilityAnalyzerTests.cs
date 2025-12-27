using System;
using System.IO;
using System.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed class PowerShellCompatibilityAnalyzerTests
{
    [Fact]
    public void Analyze_FindsIssuesAndBuildsSummary()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var file1 = Path.Combine(root.FullName, "a.ps1");
            var file2 = Path.Combine(root.FullName, "b.ps1");

            File.WriteAllText(file1, "using namespace System.Text\n");
            File.WriteAllText(file2, "workflow Test-Thing { }\n");

            var analyzer = new PowerShellCompatibilityAnalyzer(new NullLogger());
            var report = analyzer.Analyze(
                spec: new PowerShellCompatibilitySpec(root.FullName, recurse: false, excludeDirectories: Array.Empty<string>()),
                progress: null,
                exportPath: null);

            Assert.Equal(2, report.Files.Length);
            Assert.Equal(2, report.Summary.TotalFiles);
            Assert.Equal(2, report.Summary.FilesWithIssues);

            var byName = report.Files.ToDictionary(f => Path.GetFileName(f.FullPath), StringComparer.OrdinalIgnoreCase);

            Assert.False(byName["a.ps1"].PowerShell51Compatible);
            Assert.True(byName["a.ps1"].PowerShell7Compatible);
            Assert.Contains(byName["a.ps1"].Issues, i => i.Type == PowerShellCompatibilityIssueType.PowerShell7Feature);

            Assert.True(byName["b.ps1"].PowerShell51Compatible);
            Assert.False(byName["b.ps1"].PowerShell7Compatible);
            Assert.Contains(byName["b.ps1"].Issues, i => i.Type == PowerShellCompatibilityIssueType.Workflow);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Analyze_WritesCsv_WhenExportPathProvided()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var file = Path.Combine(root.FullName, "a.ps1");
            File.WriteAllText(file, "using namespace System.Text\n");

            var exportPath = Path.Combine(root.FullName, "compat.csv");
            var analyzer = new PowerShellCompatibilityAnalyzer(new NullLogger());

            var report = analyzer.Analyze(
                spec: new PowerShellCompatibilitySpec(root.FullName, recurse: false, excludeDirectories: Array.Empty<string>()),
                progress: null,
                exportPath: exportPath);

            Assert.Equal(exportPath, report.ExportPath);
            Assert.True(File.Exists(exportPath));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }
}

