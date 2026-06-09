using Xunit;

namespace PowerForge.Tests;

public sealed class DotNetRepositoryReleaseSummaryServiceTests
{
    [Fact]
    public void CreateSummary_ComputesRowsAndTotals()
    {
        var result = new DotNetRepositoryReleaseResult
        {
            ResolvedVersion = "2.0.5"
        };
        result.Projects.Add(new DotNetRepositoryProjectResult
        {
            ProjectName = "LibraryA",
            IsPackable = true,
            OldVersion = "2.0.4",
            NewVersion = "2.0.5"
        });
        result.Projects[0].Packages.Add("LibraryA.2.0.5.nupkg");
        result.Projects.Add(new DotNetRepositoryProjectResult
        {
            ProjectName = "LibraryB",
            IsPackable = false
        });
        result.Projects.Add(new DotNetRepositoryProjectResult
        {
            ProjectName = "LibraryC",
            IsPackable = true,
            ErrorMessage = "Package signing failed because the configured certificate was not found."
        });
        result.PublishedPackages.Add("LibraryA.2.0.5.nupkg");
        result.SkippedDuplicatePackages.Add("LibraryOld.2.0.5.nupkg");
        result.FailedPackages.Add("LibraryC.2.0.5.nupkg");

        var summary = new DotNetRepositoryReleaseSummaryService().CreateSummary(result, maxErrorLength: 32);

        Assert.Equal(3, summary.Projects.Count);
        Assert.Equal("LibraryA", summary.Projects[0].ProjectName);
        Assert.Equal(DotNetRepositoryReleaseProjectStatus.Ok, summary.Projects[0].Status);
        Assert.Equal("2.0.4 -> 2.0.5", summary.Projects[0].VersionDisplay);
        Assert.Equal("LibraryB", summary.Projects[1].ProjectName);
        Assert.Equal(DotNetRepositoryReleaseProjectStatus.Skipped, summary.Projects[1].Status);
        Assert.Equal("LibraryC", summary.Projects[2].ProjectName);
        Assert.Equal(DotNetRepositoryReleaseProjectStatus.Failed, summary.Projects[2].Status);
        Assert.Equal("Package signing failed becaus...", summary.Projects[2].ErrorPreview);

        Assert.Equal(3, summary.Totals.ProjectCount);
        Assert.Equal(2, summary.Totals.PackableCount);
        Assert.Equal(1, summary.Totals.FailedProjectCount);
        Assert.Equal(1, summary.Totals.PackageCount);
        Assert.Equal(1, summary.Totals.PublishedPackageCount);
        Assert.Equal(1, summary.Totals.SkippedDuplicatePackageCount);
        Assert.Equal(1, summary.Totals.FailedPublishCount);
        Assert.Equal("2.0.5", summary.Totals.ResolvedVersion);
    }
}
