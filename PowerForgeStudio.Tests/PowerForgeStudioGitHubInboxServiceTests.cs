using System.Net;
using System.Net.Http;
using System.Text;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Orchestrator.Portfolio;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioGitHubInboxServiceTests
{
    [Theory]
    [InlineData("https://github.com/EvotecIT/DbaClientX.git", "EvotecIT", "DbaClientX")]
    [InlineData("http://github.com/EvotecIT/DbaClientX", "EvotecIT", "DbaClientX")]
    [InlineData("git@github.com:EvotecIT/DbaClientX.git", "EvotecIT", "DbaClientX")]
    public void TryParseGitHubSlug_SupportedOrigins_ReturnsOwnerAndRepo(string originUrl, string expectedOwner, string expectedRepo)
    {
        var parsed = GitHubInboxService.TryParseGitHubSlug(originUrl, out var owner, out var repo);

        Assert.True(parsed);
        Assert.Equal(expectedOwner, owner);
        Assert.Equal(expectedRepo, repo);
    }

    [Fact]
    public async Task PopulateInboxAsync_GitHubRepository_EnrichesPortfolioWithAttentionSignals()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request => CreateResponse(request.RequestUri))) {
            BaseAddress = new Uri("https://api.github.com")
        };
        var repository = new RepositoryPortfolioItem(
            new RepositoryCatalogEntry(
                Name: "DbaClientX",
                RootPath: @"C:\Support\GitHub\DbaClientX",
                RepositoryKind: ReleaseRepositoryKind.Mixed,
                WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
                ModuleBuildScriptPath: @"C:\Support\GitHub\DbaClientX\Build\Build-Module.ps1",
                ProjectBuildScriptPath: @"C:\Support\GitHub\DbaClientX\Build\Build-Project.ps1",
                IsWorktree: false,
                HasWebsiteSignals: false),
            new RepositoryGitSnapshot(
                IsGitRepository: true,
                BranchName: "main",
                UpstreamBranch: "origin/main",
                AheadCount: 2,
                BehindCount: 0,
                TrackedChangeCount: 0,
                UntrackedChangeCount: 0),
            new RepositoryReadiness(RepositoryReadinessKind.Ready, "Clean and ready."));

        var service = new GitHubInboxService(httpClient, new StubGitRemoteResolver("https://github.com/EvotecIT/DbaClientX.git"));
        var result = await service.PopulateInboxAsync([repository], new GitHubInboxOptions {
            MaxRepositories = 5
        });

        var inbox = Assert.Single(result).GitHubInbox;
        Assert.NotNull(inbox);
        Assert.Equal(RepositoryGitHubInboxStatus.Attention, inbox.Status);
        Assert.Equal("EvotecIT/DbaClientX", inbox.RepositorySlug);
        Assert.Equal(2, inbox.OpenPullRequestCount);
        Assert.True(inbox.LatestWorkflowFailed);
        Assert.Equal("v0.2.0", inbox.LatestReleaseTag);
        Assert.Contains("2 open PR(s)", inbox.Summary);
    }

    [Fact]
    public async Task PopulateInboxAsync_NonGitHubRemote_ReturnsUnavailableInbox()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => throw new InvalidOperationException("No HTTP call expected."))) {
            BaseAddress = new Uri("https://api.github.com")
        };
        var repository = new RepositoryPortfolioItem(
            new RepositoryCatalogEntry(
                Name: "LocalOnly",
                RootPath: @"C:\Support\GitHub\LocalOnly",
                RepositoryKind: ReleaseRepositoryKind.Library,
                WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
                ModuleBuildScriptPath: null,
                ProjectBuildScriptPath: @"C:\Support\GitHub\LocalOnly\Build\Build-Project.ps1",
                IsWorktree: false,
                HasWebsiteSignals: false),
            new RepositoryGitSnapshot(true, "main", "origin/main", 0, 0, 0, 0),
            new RepositoryReadiness(RepositoryReadinessKind.Ready, "Clean and ready."));

        var service = new GitHubInboxService(httpClient, new StubGitRemoteResolver("ssh://git.example.com/local/repo.git"));
        var result = await service.PopulateInboxAsync([repository], new GitHubInboxOptions {
            MaxRepositories = 5
        });

        var inbox = Assert.Single(result).GitHubInbox;
        Assert.NotNull(inbox);
        Assert.Equal(RepositoryGitHubInboxStatus.Unavailable, inbox.Status);
        Assert.Contains("not a supported GitHub URL", inbox.Detail);
    }

    private static HttpResponseMessage CreateResponse(Uri? uri)
    {
        var pathAndQuery = uri?.PathAndQuery ?? string.Empty;
        if (pathAndQuery.Contains("/pulls?", StringComparison.OrdinalIgnoreCase))
        {
            return Json("""[{ "number": 1 }, { "number": 2 }]""");
        }

        if (pathAndQuery.Contains("/releases/latest", StringComparison.OrdinalIgnoreCase))
        {
            return Json("""{ "tag_name": "v0.2.0" }""");
        }

        if (pathAndQuery.Contains("/actions/runs?", StringComparison.OrdinalIgnoreCase))
        {
            return Json("""{ "workflow_runs": [ { "conclusion": "failure" } ] }""");
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json(string json)
        => new(HttpStatusCode.OK) {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubGitRemoteResolver(string? originUrl) : IGitRemoteResolver
    {
        public Task<string?> ResolveOriginUrlAsync(string repositoryRoot, CancellationToken cancellationToken = default)
            => Task.FromResult(originUrl);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}

