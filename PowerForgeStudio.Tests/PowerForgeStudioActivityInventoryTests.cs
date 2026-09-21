using System.Net;
using System.Text;
using PowerForge;
using PowerForgeStudio.Domain.Activity;
using PowerForgeStudio.Domain.Automation;
using PowerForgeStudio.Domain.Hub;
using PowerForgeStudio.Orchestrator.Activity;
using PowerForgeStudio.Orchestrator.Automation;
using PowerForgeStudio.Orchestrator.Catalog;
using PowerForgeStudio.Orchestrator.Hub;
using PowerForgeStudio.Orchestrator.Portfolio;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioActivityInventoryTests
{
    [Fact]
    public async Task InspectAsync_ProjectsOwnerEvidenceWithoutRunningBuildScript()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-activity-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var repository = Directory.CreateDirectory(Path.Combine(root, "SampleProject")).FullName;
            var build = Directory.CreateDirectory(Path.Combine(repository, "Build")).FullName;
            var marker = Path.Combine(repository, "build-ran.txt");
            await File.WriteAllTextAsync(Path.Combine(build, "Build-Project.ps1"), $"Set-Content -LiteralPath '{marker.Replace("'", "''")}' -Value ran");
            Assert.True((await new GitClient().RunRawAsync(repository, ["init", "-b", "main"])).Succeeded);

            using var http = new HttpClient(new StubHttpMessageHandler(CreateGitHubResponse)) { BaseAddress = new Uri("https://api.github.com") };
            var inbox = new GitHubInboxService(http, new StubGitRemoteResolver("https://github.com/EvotecIT/SampleProject.git"));
            using var service = new WorkspaceActivityInventoryService(
                new RepositoryCatalogScanner(), new RepositoryPortfolioService(), inbox,
                new RepositoryReleaseDriftService(), new RepositoryReleaseInboxService(),
                new FakeAutomationInventory(), new FakeGitHubProjectService());

            var snapshot = await service.InspectAsync(root, new WorkspaceActivityOptions(4, 3, 50));

            Assert.Equal(1, snapshot.RepositoryCount);
            Assert.Contains(snapshot.Entries, entry => entry.Kind == "CI" && entry.Severity == "Critical");
            Assert.Contains(snapshot.Entries, entry => entry.Kind == "Pull request");
            Assert.Contains(snapshot.Entries, entry => entry.Kind == "Issue" && entry.Title.Contains("#42", StringComparison.Ordinal));
            Assert.Contains(snapshot.Entries, entry => entry.Kind == "Schedule" && entry.State == "Failed");
            var gitHubSource = Assert.Single(snapshot.Sources, source => source.Provider == "GitHub");
            Assert.Equal("Available", gitHubSource.State);
            Assert.Equal(snapshot.Entries.Count(entry => entry.Provider == "GitHub"), gitHubSource.EntryCount);
            Assert.False(File.Exists(marker));

            var limited = await service.InspectAsync(root, new WorkspaceActivityOptions(4, 3, 2));
            Assert.True(limited.IsTruncated);
            Assert.Equal(2, limited.Entries.Count);
            Assert.True(limited.TotalEntryCount > limited.Entries.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InspectAsync_AutomationFailureRemainsVisibleWithoutDiscardingLocalEvidence()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-activity-failure-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var repository = Directory.CreateDirectory(Path.Combine(root, "SampleProject")).FullName;
            Directory.CreateDirectory(Path.Combine(repository, "Build"));
            await File.WriteAllTextAsync(Path.Combine(repository, "Build", "Build-Project.ps1"), "throw 'must not run'");
            Assert.True((await new GitClient().RunRawAsync(repository, ["init", "-b", "main"])).Succeeded);

            using var http = new HttpClient(new StubHttpMessageHandler(_ => throw new InvalidOperationException("No HTTP expected."))) { BaseAddress = new Uri("https://api.github.com") };
            using var service = new WorkspaceActivityInventoryService(
                new RepositoryCatalogScanner(), new RepositoryPortfolioService(),
                new GitHubInboxService(http, new StubGitRemoteResolver(null)),
                new RepositoryReleaseDriftService(), new RepositoryReleaseInboxService(),
                new FailingAutomationInventory(), new FakeGitHubProjectService());

            var snapshot = await service.InspectAsync(root, new WorkspaceActivityOptions(0, 0, 50));

            Assert.Contains(snapshot.Sources, source => source.Provider == "Local workspace" && source.State == "Available");
            Assert.Contains(snapshot.Sources, source => source.Provider == "Automations" && source.State == "Unavailable");
            Assert.NotEmpty(snapshot.Entries);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InspectAsync_GitHubDeadlineRetainsLocalEvidenceAndReportsUnavailableSource()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-activity-timeout-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var repository = Directory.CreateDirectory(Path.Combine(root, "SampleProject")).FullName;
            Directory.CreateDirectory(Path.Combine(repository, "Build"));
            await File.WriteAllTextAsync(Path.Combine(repository, "Build", "Build-Project.ps1"), "throw 'must not run'");
            Assert.True((await new GitClient().RunRawAsync(repository, ["init", "-b", "main"])).Succeeded);

            using var http = new HttpClient(new StubHttpMessageHandler(_ => throw new InvalidOperationException("No HTTP expected."))) { BaseAddress = new Uri("https://api.github.com") };
            using var service = new WorkspaceActivityInventoryService(
                new RepositoryCatalogScanner(), new RepositoryPortfolioService(),
                new GitHubInboxService(http, new BlockingGitRemoteResolver()),
                new RepositoryReleaseDriftService(), new RepositoryReleaseInboxService(),
                new FakeAutomationInventory(), new FakeGitHubProjectService());

            var snapshot = await service.InspectAsync(root, new WorkspaceActivityOptions(1, 1, 50, 1));

            Assert.Equal(1, snapshot.RepositoryCount);
            Assert.Contains(snapshot.Sources, source => source.Provider == "Local workspace" && source.State == "Available");
            Assert.Contains(snapshot.Sources, source => source.Provider == "GitHub" && source.State == "Unavailable"
                && source.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase));
            Assert.NotEmpty(snapshot.Entries);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "Access denied")]
    [InlineData(HttpStatusCode.TooManyRequests, "Rate limited")]
    public async Task InspectAsync_GitHubAccessStatesRemainDistinct(HttpStatusCode statusCode, string expectedState)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-activity-access-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var repository = Directory.CreateDirectory(Path.Combine(root, "SampleProject")).FullName;
            Directory.CreateDirectory(Path.Combine(repository, "Build"));
            await File.WriteAllTextAsync(Path.Combine(repository, "Build", "Build-Project.ps1"), "throw 'must not run'");
            Assert.True((await new GitClient().RunRawAsync(repository, ["init", "-b", "main"])).Succeeded);

            using var http = new HttpClient(new StubHttpMessageHandler(CreateGitHubResponse)) { BaseAddress = new Uri("https://api.github.com") };
            using var service = new WorkspaceActivityInventoryService(
                new RepositoryCatalogScanner(), new RepositoryPortfolioService(),
                new GitHubInboxService(http, new StubGitRemoteResolver("https://github.com/EvotecIT/SampleProject.git")),
                new RepositoryReleaseDriftService(), new RepositoryReleaseInboxService(),
                new FakeAutomationInventory(), new FakeGitHubProjectService(statusCode));

            var snapshot = await service.InspectAsync(root, new WorkspaceActivityOptions(1, 1, 50, 5));

            Assert.Equal(expectedState, Assert.Single(snapshot.Sources, source => source.Provider == "GitHub").State);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static HttpResponseMessage CreateGitHubResponse(HttpRequestMessage request)
    {
        var path = request.RequestUri?.PathAndQuery ?? "";
        if (path == "/repos/EvotecIT/SampleProject") return Json("""{ "default_branch": "main" }""");
        if (path.Contains("/pulls?", StringComparison.OrdinalIgnoreCase)) return Json("""[{ "number": 7 }]""");
        if (path.Contains("/releases/latest", StringComparison.OrdinalIgnoreCase)) return Json("""{ "tag_name": "v1.0.0" }""");
        if (path.Contains("/actions/runs?", StringComparison.OrdinalIgnoreCase)) return Json("""{ "workflow_runs": [ { "conclusion": "failure" } ] }""");
        if (path.Contains("/branches/main", StringComparison.OrdinalIgnoreCase)) return Json("""{ "protected": true }""");
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response(request));
    }

    private sealed class StubGitRemoteResolver(string? origin) : IGitRemoteResolver
    {
        public Task<string?> ResolveOriginUrlAsync(string repositoryRoot, CancellationToken cancellationToken = default)
            => Task.FromResult(origin);
    }

    private sealed class BlockingGitRemoteResolver : IGitRemoteResolver
    {
        public async Task<string?> ResolveOriginUrlAsync(string repositoryRoot, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }
    }

    private sealed class FakeAutomationInventory : IWorkspaceAutomationInventoryService
    {
        public Task<WorkspaceAutomationSnapshot> InspectAsync(string workspaceRoot, CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;
            WorkspaceAutomationEntry entry = new("nightly", "Nightly release", "Windows Task Scheduler", "Local", "SampleProject",
                "Daily", "Failed", now.AddHours(1), now.AddMinutes(-10), "Exit 1", true, true, true, "Task Scheduler", "Provider runtime evidence.");
            return Task.FromResult(new WorkspaceAutomationSnapshot(now, [entry],
                [new WorkspaceAutomationSourceState("Windows Task Scheduler", "Available", 1, "Runtime evidence read locally.")]));
        }
    }

    private sealed class FailingAutomationInventory : IWorkspaceAutomationInventoryService
    {
        public Task<WorkspaceAutomationSnapshot> InspectAsync(string workspaceRoot, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("provider failed");
    }

    private sealed class FakeGitHubProjectService(HttpStatusCode? failure = null) : IGitHubProjectService
    {
        public Task<GitHubPullRequest?> FindMergedPullRequestByHeadAsync(string slug, string headSha, string expectedBaseBranch, CancellationToken cancellationToken = default)
            => Task.FromResult<GitHubPullRequest?>(null);
        public Task<string?> ResolveRepositoryAsync(string workingCopy, CancellationToken cancellationToken = default) => Task.FromResult<string?>("EvotecIT/SampleProject");
        public Task<GitHubPage<GitHubIssue>> FetchIssuesAsync(string slug, string state = "open", CancellationToken cancellationToken = default)
            => failure is { } status
                ? Task.FromException<GitHubPage<GitHubIssue>>(new GitHubAccessException(status))
                : Task.FromResult(new GitHubPage<GitHubIssue>(
                    [new GitHubIssue(42, "Release tracking is stale", "open", "maintainer", ["release"], [], DateTimeOffset.UtcNow.AddDays(-1), null, $"https://github.com/{slug}/issues/42")], false));
        public Task<GitHubPage<GitHubPullRequest>> FetchPullRequestsAsync(string slug, string state = "open", CancellationToken cancellationToken = default) => Task.FromResult(new GitHubPage<GitHubPullRequest>([], false));
        public Task<GitHubIssueDetail?> FetchIssueDetailAsync(string slug, int issueNumber, CancellationToken cancellationToken = default) => Task.FromResult<GitHubIssueDetail?>(null);
        public Task<GitHubPullRequestDetail?> FetchPullRequestDetailAsync(string slug, int pullRequestNumber, CancellationToken cancellationToken = default) => Task.FromResult<GitHubPullRequestDetail?>(null);
        public Task<GitHubPullRequestFiles> FetchPullRequestFilesAsync(string slug, int number, string expectedHeadSha, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GitHubPage<GitHubCheck>> FetchChecksAsync(string slug, string headSha, CancellationToken cancellationToken = default) => Task.FromResult(new GitHubPage<GitHubCheck>([], false));
    }
}
