using System;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModuleTestSuiteDisplayServiceTests
{
    [Fact]
    public void CreateDependencySummary_IncludesRequiredAndNonSkippedAdditionalModules()
    {
        var service = new ModuleTestSuiteDisplayService();
        var lines = service.CreateDependencySummary(
            requiredModules:
            [
                new RequiredModuleReference("Pester", moduleVersion: "5.7.1")
            ],
            additionalModules: ["PSWriteColor", "Pester"],
            skipModules: ["Pester"]);

        Assert.Contains(lines, line => line.Text == "Step 3: Dependency summary...");
        Assert.Contains(lines, line => line.Text == "Required modules:");
        Assert.Contains(lines, line => line.Text == "  📦 Pester (Min: 5.7.1)");
        Assert.Contains(lines, line => line.Text == "Additional modules:");
        Assert.Contains(lines, line => line.Text == "  ✅ PSWriteColor");
        Assert.DoesNotContain(lines, line => line.Text == "  ✅ Pester");
    }

    [Fact]
    public void CreateCompletionSummary_ReflectsFailureState()
    {
        var result = new ModuleTestSuiteResult(
            projectPath: @"C:\Repo",
            testPath: @"C:\Repo\Tests",
            moduleName: "MyModule",
            moduleVersion: "1.0.0",
            manifestPath: @"C:\Repo\MyModule.psd1",
            requiredModules: Array.Empty<RequiredModuleReference>(),
            dependencyResults: Array.Empty<ModuleDependencyInstallResult>(),
            moduleImported: true,
            exportedFunctionCount: null,
            exportedCmdletCount: null,
            exportedAliasCount: null,
            pesterVersion: null,
            totalCount: 10,
            passedCount: 8,
            failedCount: 2,
            skippedCount: 0,
            duration: TimeSpan.FromSeconds(12),
            coveragePercent: null,
            failureAnalysis: null,
            exitCode: 1,
            stdOut: string.Empty,
            stdErr: string.Empty,
            resultsXmlPath: null);

        var lines = new ModuleTestSuiteDisplayService().CreateCompletionSummary(result);

        Assert.Contains(lines, line => line.Text == "=== Test Suite Failed ===" && line.Color == ConsoleColor.Red);
        Assert.Contains(lines, line => line.Text == "Tests: 8/10 passed" && line.Color == ConsoleColor.Yellow);
        Assert.Contains(lines, line => line.Text == "Duration: 00:00:12" && line.Color == ConsoleColor.Green);
    }

    [Fact]
    public void CreateFailureSummary_UsesDetailedFailureDisplayServiceOutput()
    {
        var analysis = new ModuleTestFailureAnalysis
        {
            Source = "TestResults.xml",
            Timestamp = DateTime.Now,
            TotalCount = 2,
            PassedCount = 1,
            FailedCount = 1,
            SkippedCount = 0,
            FailedTests =
            [
                new ModuleTestFailureInfo
                {
                    Name = "It fails",
                    ErrorMessage = "Boom",
                    Duration = TimeSpan.FromSeconds(1)
                }
            ]
        };

        var lines = new ModuleTestSuiteDisplayService().CreateFailureSummary(analysis, detailed: true);

        Assert.Contains(lines, line => line.Text == "=== Module Test Failure Analysis ===");
        Assert.Contains(lines, line => line.Text == "- It fails");
        Assert.Contains(lines, line => line.Text == "   Boom");
    }
}
