using System.Net;
using System.Text;
using System.Text.Json;
using PowerForgeStudio.Orchestrator.Hub;

namespace PowerForgeStudio.Tests;

public sealed class GitHubProjectReadContractTests
{
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task AccessErrorsAreNotEmptyListsOrCountsAndDoNotExposeBodies(HttpStatusCode status)
    {
        using var client = Client(_ => new(status) { Content = new StringContent("private-server-detail") });
        using var service = new GitHubProjectService(client);
        var error = await Assert.ThrowsAsync<GitHubAccessException>(() => service.FetchIssuesAsync("owner/repo"));
        Assert.Equal(status, error.StatusCode);
        Assert.DoesNotContain("private-server-detail", error.Message);
        await Assert.ThrowsAsync<GitHubAccessException>(() => service.GetOpenIssueCountAsync("owner/repo"));
        await Assert.ThrowsAsync<GitHubAccessException>(() => service.FetchPullRequestDetailAsync("owner/repo", 1));
    }

    [Fact]
    public async Task PaginationCoverageCountsRawItemsBeforeFilteringPullRequests()
    {
        var pages = 0;
        using var client = Client(_ => { pages++; return Json(JsonSerializer.Serialize(Enumerable.Repeat(new { pull_request = new { } }, 100))); });
        using var service = new GitHubProjectService(client);
        var result = await service.FetchIssuesAsync("owner/repo");
        Assert.Empty(result); Assert.True(result.HasMore); Assert.Equal(5, pages);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetOpenIssueCountAsync("owner/repo"));
    }

    [Fact]
    public async Task FailureOnLaterPageDoesNotReturnPartialSuccess()
    {
        var calls = 0;
        using var client = Client(_ => ++calls == 1 ? Json(JsonSerializer.Serialize(Enumerable.Repeat(new { pull_request = new { } }, 100))) : new(HttpStatusCode.Forbidden));
        using var service = new GitHubProjectService(client);
        await Assert.ThrowsAsync<GitHubAccessException>(() => service.FetchIssuesAsync("owner/repo"));
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("owner/repo?state=all")]
    [InlineData("owner/..")]
    [InlineData("owner/repo/extra")]
    [InlineData("https://evil.example")]
    public async Task InvalidRepositoryNeverMakesRequest(string slug)
    {
        using var client = Client(_ => throw new InvalidOperationException("Must not send"));
        using var service = new GitHubProjectService(client);
        await Assert.ThrowsAsync<ArgumentException>(() => service.FetchIssuesAsync(slug));
    }

    [Fact]
    public async Task MalformedAndOversizedResponsesFailExplicitly()
    {
        using var malformedClient = Client(_ => Json("{}"));
        using var malformed = new GitHubProjectService(malformedClient);
        await Assert.ThrowsAsync<InvalidDataException>(() => malformed.FetchIssuesAsync("owner/repo"));
        using var largeClient = Client(_ => Json(new string('x', 4 * 1024 * 1024 + 1)));
        using var large = new GitHubProjectService(largeClient);
        await Assert.ThrowsAsync<InvalidDataException>(() => large.FetchIssuesAsync("owner/repo"));
    }

    [Fact]
    public async Task ChecksUseCapturedHeadAndOnlyLatestStatusPerContext()
    {
        var sha = new string('a', 40);
        using var client = Client(request =>
        {
            Assert.Contains("/commits/" + sha + "/", request.RequestUri!.AbsolutePath);
            Assert.Equal("2026-03-10", Assert.Single(request.Headers.GetValues("X-GitHub-Api-Version")));
            return request.RequestUri.AbsolutePath.EndsWith("check-runs")
                ? Json("""{"check_runs":[{"name":"Build Windows","conclusion":"failure","status":"completed"}]}""")
                : Json("""[{"context":"review","state":"success"},{"context":"review","state":"failure"}]""");
        });
        using var service = new GitHubProjectService(client);
        var result = await service.FetchChecksAsync("owner/repo", sha);
        Assert.Equal(2, result.Count); Assert.False(result.HasMore);
        Assert.True(result[0].IsFailure); Assert.Equal("success", result[1].State);
    }

    [Fact]
    public async Task MergedPullRequestEvidenceRequiresExactHeadAndBase()
    {
        var head = new string('a', 40);
        using var client = Client(request =>
        {
            Assert.Contains("/repos/owner/repo/commits/" + head + "/pulls", request.RequestUri!.AbsolutePath);
            return Json($$"""
                [{"number":7,"title":"Wrong base","state":"closed","user":{"login":"dev"},"head":{"ref":"feature","sha":"{{head}}"},"base":{"ref":"release"},"labels":[],"additions":1,"deletions":0,"changed_files":1,"created_at":"2026-09-01T00:00:00Z","merged_at":"2026-09-02T00:00:00Z"},
                 {"number":8,"title":"Exact evidence","state":"closed","user":{"login":"dev"},"head":{"ref":"feature","sha":"{{head}}"},"base":{"ref":"main"},"labels":[],"additions":1,"deletions":0,"changed_files":1,"created_at":"2026-09-01T00:00:00Z","merged_at":"2026-09-02T00:00:00Z","html_url":"https://github.com/owner/repo/pull/8"}]
                """);
        });
        using var service = new GitHubProjectService(client);

        var result = await service.FindMergedPullRequestByHeadAsync("owner/repo", head, "main");

        Assert.NotNull(result);
        Assert.Equal(8, result.Number);
        Assert.Equal(head, result.HeadSha);
        Assert.Equal("main", result.BaseBranch);
    }

    [Fact]
    public async Task DisposeCancelsPendingRequestWithoutSemaphoreReleaseFailure()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new HttpClient(new AsyncHandler(async token => { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); return Json("[]"); })) { BaseAddress = new("https://api.github.com") };
        var service = new GitHubProjectService(client);
        var read = service.FetchIssuesAsync("owner/repo");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        service.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
    }

    private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> handler) => new(new Handler(handler)) { BaseAddress = new("https://api.github.com") };
    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(action(request));
    }
    private sealed class AsyncHandler(Func<CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => action(cancellationToken);
    }
}
