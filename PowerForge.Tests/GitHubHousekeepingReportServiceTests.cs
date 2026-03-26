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
                UsageBefore = new GitHubActionsCacheUsage
                {
                    ActiveCachesCount = 3,
                    ActiveCachesSizeInBytes = 4096
                }
            }
        });

        var markdown = service.BuildMarkdown(report);

        Assert.Contains("PowerForge GitHub Housekeeping Report", markdown, StringComparison.Ordinal);
        Assert.Contains("Storage Summary", markdown, StringComparison.Ordinal);
        Assert.Contains("Planned artifacts (1)", markdown, StringComparison.Ordinal);
        Assert.Contains("github-pages", markdown, StringComparison.Ordinal);
        Assert.Contains("3 caches", markdown, StringComparison.Ordinal);
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
