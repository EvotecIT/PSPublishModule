using System;
using Xunit;

namespace PowerForge.Tests;

public sealed class DotNetRepositoryReleaseDisplayServiceTests
{
    [Fact]
    public void CreateDisplay_FormatsProjectRowsAndConditionalTotals()
    {
        var summary = new DotNetRepositoryReleaseSummary
        {
            Projects = new[]
            {
                new DotNetRepositoryReleaseProjectSummaryRow
                {
                    ProjectName = "LibraryA",
                    IsPackable = true,
                    VersionDisplay = "1.0.0 -> 1.0.1",
                    PackageCount = 2,
                    Status = DotNetRepositoryReleaseProjectStatus.Ok,
                    ErrorPreview = string.Empty
                },
                new DotNetRepositoryReleaseProjectSummaryRow
                {
                    ProjectName = "LibraryB",
                    IsPackable = false,
                    VersionDisplay = string.Empty,
                    PackageCount = 0,
                    Status = DotNetRepositoryReleaseProjectStatus.Skipped,
                    ErrorPreview = string.Empty
                },
                new DotNetRepositoryReleaseProjectSummaryRow
                {
                    ProjectName = "LibraryC",
                    IsPackable = true,
                    VersionDisplay = "1.0.0 -> 1.0.1",
                    PackageCount = 1,
                    Status = DotNetRepositoryReleaseProjectStatus.Failed,
                    ErrorPreview = "signing failed"
                }
            },
            Totals = new DotNetRepositoryReleaseSummaryTotals
            {
                ProjectCount = 3,
                PackableCount = 2,
                FailedProjectCount = 1,
                PackageCount = 3,
                PublishedPackageCount = 2,
                SkippedDuplicatePackageCount = 1,
                FailedPublishCount = 1,
                ResolvedVersion = "1.0.1"
            }
        };

        var display = new DotNetRepositoryReleaseDisplayService().CreateDisplay(summary, isPlan: false);

        Assert.Equal("Summary", display.Title);
        Assert.Equal("Yes", display.Projects[0].Packable);
        Assert.Equal("Ok", display.Projects[0].StatusText);
        Assert.Equal(ConsoleColor.Green, display.Projects[0].StatusColor);
        Assert.Equal("Skipped", display.Projects[1].StatusText);
        Assert.Equal(ConsoleColor.Gray, display.Projects[1].StatusColor);
        Assert.Equal("Fail", display.Projects[2].StatusText);
        Assert.Equal(ConsoleColor.Red, display.Projects[2].StatusColor);
        Assert.Contains(display.Totals, row => row.Label == "Published" && row.Value == "2");
        Assert.Contains(display.Totals, row => row.Label == "Skipped duplicates" && row.Value == "1");
        Assert.Contains(display.Totals, row => row.Label == "Failed publishes" && row.Value == "1");
        Assert.Contains(display.Totals, row => row.Label == "Resolved version" && row.Value == "1.0.1");
    }
}
