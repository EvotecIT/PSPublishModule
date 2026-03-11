using System;
using System.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed class PowerShellCompatibilityDisplayServiceTests
{
    [Fact]
    public void CreateSummary_FormatsStatusAndRecommendations()
    {
        var service = new PowerShellCompatibilityDisplayService();
        var summary = new PowerShellCompatibilitySummary(
            CheckStatus.Warning,
            DateTime.Now,
            totalFiles: 3,
            powerShell51Compatible: 2,
            powerShell7Compatible: 2,
            crossCompatible: 1,
            filesWithIssues: 2,
            crossCompatibilityPercentage: 33.3,
            message: "Compatibility issues found.",
            recommendations: new[] { "Use cross-platform APIs." });

        var lines = service.CreateSummary(summary);

        Assert.Contains(lines, line => line.Text.Contains("Status: Warning", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Text == "Recommendations:");
        Assert.Contains(lines, line => line.Text == "- Use cross-platform APIs.");
    }

    [Fact]
    public void CreateDetails_FormatsPerFileIssues()
    {
        var service = new PowerShellCompatibilityDisplayService();
        var results = new[]
        {
            new PowerShellCompatibilityFileResult(
                fullPath: @"C:\Repo\a.ps1",
                relativePath: "a.ps1",
                powerShell51Compatible: false,
                powerShell7Compatible: true,
                encoding: null,
                issues: new[]
                {
                    new PowerShellCompatibilityIssue(
                        PowerShellCompatibilityIssueType.PowerShell7Feature,
                        "using namespace requires PS 7+.",
                        "Avoid using namespace.",
                        PowerShellCompatibilitySeverity.Medium)
                })
        };

        var lines = service.CreateDetails(results);

        Assert.Contains(lines, line => line.Text == "Detailed Analysis:");
        Assert.Contains(lines, line => line.Text == "a.ps1");
        Assert.Contains(lines, line => line.Text.Contains("PowerShell7Feature", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Text.Contains("Avoid using namespace.", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateInternalSummaryMessages_IncludesExportOutcomeAndIssueTypes()
    {
        var service = new PowerShellCompatibilityDisplayService();
        var report = new PowerShellCompatibilityReport(
            new PowerShellCompatibilitySummary(CheckStatus.Pass, DateTime.Now, 1, 1, 1, 1, 1, 100, "ok", Array.Empty<string>()),
            new[]
            {
                new PowerShellCompatibilityFileResult(
                    fullPath: @"C:\Repo\a.ps1",
                    relativePath: "a.ps1",
                    powerShell51Compatible: false,
                    powerShell7Compatible: true,
                    encoding: null,
                    issues: new[]
                    {
                        new PowerShellCompatibilityIssue(
                            PowerShellCompatibilityIssueType.PowerShell7Feature,
                            "using namespace requires PS 7+.",
                            "Avoid using namespace.",
                            PowerShellCompatibilitySeverity.Medium)
                    })
            },
            exportPath: @"C:\Repo\compat.csv");

        var messages = service.CreateInternalSummaryMessages(report, showDetails: true, exportPath: @"C:\Repo\compat.csv");

        Assert.Contains(messages, message => message.Contains("Found 1 PowerShell files", StringComparison.Ordinal));
        Assert.Contains(messages, message => message.Contains("Issues in a.ps1", StringComparison.Ordinal));
        Assert.Contains(messages, message => message.Contains("Detailed report exported to", StringComparison.Ordinal));
    }
}
