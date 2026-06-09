namespace PowerForge.Tests;

public sealed class GitHubHousekeepingReportServiceTests
{
    [Fact]
    public void BuildMarkdown_ShouldIncludeSectionSummaryAndDetails()
    {
        var service = new GitHubHousekeepingReportService();
        var report = service.CreateSuccessReport(new GitHubHousekeepingResult
        {
            Repository = "EvotecIT/TestRepo",
            DryRun = true,
            Success = true,
            RequestedSections = ["artifacts", "caches"],
            CompletedSections = ["artifacts", "caches"],
            Artifacts = new GitHubArtifactCleanupResult
            {
                Success = true,
                ScannedArtifacts = 3,
                MatchedArtifacts = 1,
                KeptByRecentWindow = 0,
                KeptByAgeThreshold = 0,
                PlannedDeletes = 1,
                PlannedDeleteBytes = 1024,
                Planned =
                [
                    new GitHubArtifactCleanupItem
                    {
                        Name = "github-pages",
                        SizeInBytes = 1024,
                        Reason = "older duplicate"
                    }
                ]
            },
            Caches = new GitHubActionsCacheCleanupResult
            {
                Success = true,
                ScannedCaches = 29,
                MatchedCaches = 29,
                KeptByRecentWindow = 25,
                KeptByAgeThreshold = 4,
                UsageBefore = new GitHubActionsCacheUsage
                {
                    ActiveCachesCount = 29,
                    ActiveCachesSizeInBytes = 10053309332
                }
            }
        });

        var markdown = service.BuildMarkdown(report);

        Assert.Contains("PowerForge GitHub Housekeeping Report", markdown, StringComparison.Ordinal);
        Assert.Contains("Storage Summary", markdown, StringComparison.Ordinal);
        Assert.Contains("Selection Breakdown", markdown, StringComparison.Ordinal);
        Assert.Contains("Planned artifacts (1)", markdown, StringComparison.Ordinal);
        Assert.Contains("github-pages", markdown, StringComparison.Ordinal);
        Assert.Contains("29 caches", markdown, StringComparison.Ordinal);
        Assert.Contains("nothing eligible", markdown, StringComparison.Ordinal);
        Assert.Contains("all matched items were retained by current policy", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMarkdown_ShouldRenderFailureWithoutResult()
    {
        var service = new GitHubHousekeepingReportService();

        var markdown = service.BuildMarkdown(service.CreateFailureReport(1, "Bad credentials"));

        Assert.Contains("Housekeeping failed before section results were produced", markdown, StringComparison.Ordinal);
        Assert.Contains("Bad credentials", markdown, StringComparison.Ordinal);
    }
}
