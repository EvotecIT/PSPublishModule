using System;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModuleTestFailureDisplayServiceTests
{
    [Fact]
    public void CreateSummary_IncludesFailedTestsAndStatistics()
    {
        var service = new ModuleTestFailureDisplayService();
        var analysis = new ModuleTestFailureAnalysis
        {
            Source = "PesterResults",
            Timestamp = DateTime.Now,
            TotalCount = 3,
            PassedCount = 2,
            FailedCount = 1,
            FailedTests = new[]
            {
                new ModuleTestFailureInfo
                {
                    Name = "Broken.Test",
                    ErrorMessage = "boom"
                }
            }
        };

        var lines = service.CreateSummary(analysis, showSuccessful: false);

        Assert.Contains(lines, line => line.Text == "=== Module Test Results Summary ===");
        Assert.Contains(lines, line => line.Text == "   Total Tests: 3");
        Assert.Contains(lines, line => line.Text == "   Failed: 1");
        Assert.Contains(lines, line => line.Text == "   - Broken.Test");
    }

    [Fact]
    public void CreateDetailed_IncludesFailureBodyAndFooter()
    {
        var service = new ModuleTestFailureDisplayService();
        var analysis = new ModuleTestFailureAnalysis
        {
            Source = "PesterResults",
            Timestamp = DateTime.Now,
            TotalCount = 1,
            PassedCount = 0,
            FailedCount = 1,
            FailedTests = new[]
            {
                new ModuleTestFailureInfo
                {
                    Name = "Broken.Test",
                    ErrorMessage = "line one\nline two",
                    Duration = TimeSpan.FromSeconds(1)
                }
            }
        };

        var lines = service.CreateDetailed(analysis);

        Assert.Contains(lines, line => line.Text == "- Broken.Test");
        Assert.Contains(lines, line => line.Text == "   line one");
        Assert.Contains(lines, line => line.Text == "   line two");
        Assert.Contains(lines, line => line.Text.StartsWith("=== Summary: 1 test failed ===", StringComparison.Ordinal));
    }
}
