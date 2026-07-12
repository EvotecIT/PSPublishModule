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
        result.Projects[0].SymbolPackages.Add("LibraryA.2.0.5.snupkg");
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
        Assert.Equal(2, summary.Projects[0].PackageCount);
        Assert.Equal("LibraryB", summary.Projects[1].ProjectName);
        Assert.Equal(DotNetRepositoryReleaseProjectStatus.Skipped, summary.Projects[1].Status);
        Assert.Equal("LibraryC", summary.Projects[2].ProjectName);
        Assert.Equal(DotNetRepositoryReleaseProjectStatus.Failed, summary.Projects[2].Status);
        Assert.Equal("Package signing failed becaus...", summary.Projects[2].ErrorPreview);

        Assert.Equal(3, summary.Totals.ProjectCount);
        Assert.Equal(2, summary.Totals.PackableCount);
        Assert.Equal(1, summary.Totals.FailedProjectCount);
        Assert.Equal(2, summary.Totals.PackageCount);
        Assert.Equal(1, summary.Totals.PublishedPackageCount);
        Assert.Equal(1, summary.Totals.SkippedDuplicatePackageCount);
        Assert.Equal(1, summary.Totals.FailedPublishCount);
        Assert.Equal("2.0.5", summary.Totals.ResolvedVersion);
    }

    [Fact]
    public void CreateFailureReport_IncludesEveryFailureWithoutTruncation()
    {
        var longDetail = new string('x', 2500) + " END-OF-DETAIL";
        var result = new DotNetRepositoryReleaseResult
        {
            Success = false,
            ErrorMessage = "One or more projects failed: ProjectA: first failure; ProjectB: " + longDetail
        };
        result.Projects.Add(new DotNetRepositoryProjectResult
        {
            ProjectName = "ProjectA",
            IsPackable = true,
            ErrorMessage = "first failure"
        });
        result.Projects.Add(new DotNetRepositoryProjectResult
        {
            ProjectName = "ProjectB",
            IsPackable = true,
            ErrorMessage = "ProjectB: " + longDetail
        });
        result.FailedPackages.Add("ProjectA.1.0.0.nupkg");
        result.FailedPackages.Add("ProjectB.1.0.0.nupkg");

        var report = new DotNetRepositoryReleaseSummaryService().CreateFailureReport(result);

        Assert.StartsWith("Project build failed: 2 of 2 project(s) failed.", report, StringComparison.Ordinal);
        Assert.Contains("Detail: ProjectA: first failure", report, StringComparison.Ordinal);
        Assert.Contains("Detail: ProjectB: ", report, StringComparison.Ordinal);
        Assert.DoesNotContain("Detail: ProjectB: ProjectB:", report, StringComparison.Ordinal);
        Assert.Contains("END-OF-DETAIL", report, StringComparison.Ordinal);
        Assert.Contains("Detail: Failed package publish: ProjectA.1.0.0.nupkg", report, StringComparison.Ordinal);
        Assert.Contains("Detail: Failed package publish: ProjectB.1.0.0.nupkg", report, StringComparison.Ordinal);
        Assert.DoesNotContain("One or more projects failed:", report, StringComparison.Ordinal);
    }
}
