using System;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModuleTestSuiteDisplayServiceTests
{
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
