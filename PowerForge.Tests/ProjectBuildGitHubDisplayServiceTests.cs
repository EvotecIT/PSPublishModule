using Xunit;

namespace PowerForge.Tests;

public sealed class ProjectBuildGitHubDisplayServiceTests
{
    [Fact]
    public void CreateSummary_FormatsSingleReleaseSummary()
    {
        var summary = new ProjectBuildGitHubPublishSummary
        {
            PerProject = false,
            SummaryTag = "v1.2.3",
            SummaryReleaseUrl = "https://example.test/release/v1.2.3",
            SummaryAssetsCount = 4
        };

        var display = new ProjectBuildGitHubDisplayService().CreateSummary(summary);

        Assert.Equal("GitHub Summary", display.Title);
        Assert.Contains(display.Rows, row => row.Label == "Mode" && row.Value == "Single");
        Assert.Contains(display.Rows, row => row.Label == "Tag" && row.Value == "v1.2.3");
        Assert.Contains(display.Rows, row => row.Label == "Assets" && row.Value == "4");
        Assert.Contains(display.Rows, row => row.Label == "Release" && row.Value == "https://example.test/release/v1.2.3");
    }

    [Fact]
    public void CreateSummary_FormatsPerProjectSummary()
    {
        var summary = new ProjectBuildGitHubPublishSummary
        {
            PerProject = true
        };
        summary.Results.Add(new ProjectBuildGitHubResult { ProjectName = "A", Success = true });
        summary.Results.Add(new ProjectBuildGitHubResult { ProjectName = "B", Success = false });

        var display = new ProjectBuildGitHubDisplayService().CreateSummary(summary);

        Assert.Contains(display.Rows, row => row.Label == "Mode" && row.Value == "PerProject");
        Assert.Contains(display.Rows, row => row.Label == "Projects" && row.Value == "2");
        Assert.Contains(display.Rows, row => row.Label == "Succeeded" && row.Value == "1");
        Assert.Contains(display.Rows, row => row.Label == "Failed" && row.Value == "1");
    }
}
