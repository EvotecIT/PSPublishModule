using System.Net;
using System.Text;
using System.Text.Json;
using PowerForgeStudio.Domain.Hub;
using PowerForgeStudio.Orchestrator.Hub;

namespace PowerForgeStudio.Tests;

public sealed class GitHubProjectActionServiceTests
{
    [Fact]
    public async Task ApprovePullRequestRechecksExactHeadAndSendsCapturedCommit()
    {
        var head = new string('a', 40);
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, $"{{\"head\":{{\"sha\":\"{head}\"}}}}")
            : Json(HttpStatusCode.OK, """{"html_url":"https://github.com/EvotecIT/PSPublishModule/pull/42#pullrequestreview-5"}"""));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com") };
        using var service = new GitHubProjectActionService(client);
        var plan = new GitHubProjectActionPlan("EvotecIT/PSPublishModule", 42, true,
            GitHubProjectActionKind.ApprovePullRequest, "open", head, "Reviewed locally.");

        var receipt = await service.ExecuteAsync(plan);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("/repos/EvotecIT/PSPublishModule/pulls/42", handler.Requests[0].Path);
        var mutation = handler.Requests[1];
        Assert.Equal(HttpMethod.Post, mutation.Method);
        Assert.Equal("/repos/EvotecIT/PSPublishModule/pulls/42/reviews", mutation.Path);
        using var body = JsonDocument.Parse(mutation.Body!);
        Assert.Equal(head, body.RootElement.GetProperty("commit_id").GetString());
        Assert.Equal("APPROVE", body.RootElement.GetProperty("event").GetString());
        Assert.Equal("Reviewed locally.", body.RootElement.GetProperty("body").GetString());
        Assert.Equal(GitHubProjectActionKind.ApprovePullRequest, receipt.Kind);
        Assert.Null(receipt.HtmlUrl);
    }

    [Fact]
    public async Task MovedPullRequestHeadStopsBeforeMutation()
    {
        var movedHead = new string('b', 40);
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK,
            $"{{\"head\":{{\"sha\":\"{movedHead}\"}}}}"));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com") };
        using var service = new GitHubProjectActionService(client);
        var plan = new GitHubProjectActionPlan("EvotecIT/PSPublishModule", 42, true,
            GitHubProjectActionKind.RequestPullRequestChanges, "open", new string('a', 40), "Please revise.");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync(plan));

        Assert.Contains("head changed", error.Message);
        Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
    }

    [Fact]
    public async Task CloseIssueRechecksStateAndUsesPatchWithoutCommentText()
    {
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, """{"state":"open"}""")
            : Json(HttpStatusCode.OK, """{"html_url":"https://github.com/EvotecIT/PSPublishModule/issues/17"}"""));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com") };
        using var service = new GitHubProjectActionService(client);
        var plan = new GitHubProjectActionPlan("EvotecIT/PSPublishModule", 17, false,
            GitHubProjectActionKind.CloseIssue, "open", "", "");

        await service.ExecuteAsync(plan);

        Assert.Equal(2, handler.Requests.Count);
        var mutation = handler.Requests[1];
        Assert.Equal(HttpMethod.Patch, mutation.Method);
        Assert.Equal("/repos/EvotecIT/PSPublishModule/issues/17", mutation.Path);
        using var body = JsonDocument.Parse(mutation.Body!);
        Assert.Equal("closed", body.RootElement.GetProperty("state").GetString());
        Assert.False(body.RootElement.TryGetProperty("body", out _));
    }

    [Theory]
    [InlineData(GitHubProjectActionKind.Comment)]
    [InlineData(GitHubProjectActionKind.CloseIssue)]
    public async Task IssueActionsRejectPullRequestShapedEvidenceBeforeMutation(GitHubProjectActionKind kind)
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK,
            """{"state":"open","pull_request":{"url":"https://api.github.com/repos/EvotecIT/PSPublishModule/pulls/17"}}"""));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com") };
        using var service = new GitHubProjectActionService(client);
        var plan = new GitHubProjectActionPlan("EvotecIT/PSPublishModule", 17, false, kind, "open", "",
            kind == GitHubProjectActionKind.Comment ? "Reviewing this issue." : "");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync(plan));

        Assert.Contains("pull request", error.Message);
        Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("{bad json", false)]
    [InlineData("{oversized}", false)]
    [InlineData("{canceled}", true)]
    public async Task AcceptedWriteRemainsTerminalWhenOptionalResponseIsUnreadable(string responseBody, bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, """{"state":"open"}""")
            : AcceptedResponse(request, responseBody, cancel ? cancellation : null));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com") };
        using var service = new GitHubProjectActionService(client);

        var receipt = await service.ExecuteAsync(new("EvotecIT/PSPublishModule", 17, false,
            GitHubProjectActionKind.Comment, "open", "", "One exact comment."), cancellation.Token);

        Assert.Equal(GitHubProjectActionKind.Comment, receipt.Kind);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
    }

    [Fact]
    public async Task IssueStateActionMustMatchReviewedDirection()
    {
        using var client = new HttpClient(new RecordingHandler(_ => throw new InvalidOperationException()))
        {
            BaseAddress = new Uri("https://api.github.com")
        };
        using var service = new GitHubProjectActionService(client);
        await Assert.ThrowsAsync<ArgumentException>(() => service.ExecuteAsync(new(
            "EvotecIT/PSPublishModule", 17, false, GitHubProjectActionKind.CloseIssue, "closed", "", "")));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ExecuteAsync(new(
            "EvotecIT/PSPublishModule", 17, false, GitHubProjectActionKind.ReopenIssue, "open", "", "")));
    }

    [Fact]
    public async Task CommentRequiresTextAndRejectsInvalidTargetKind()
    {
        using var client = new HttpClient(new RecordingHandler(_ => throw new InvalidOperationException()))
        {
            BaseAddress = new Uri("https://api.github.com")
        };
        using var service = new GitHubProjectActionService(client);

        await Assert.ThrowsAsync<ArgumentException>(() => service.ExecuteAsync(new(
            "EvotecIT/PSPublishModule", 17, false, GitHubProjectActionKind.Comment, "open", "", "  ")));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ExecuteAsync(new(
            "EvotecIT/PSPublishModule", 17, false, GitHubProjectActionKind.ApprovePullRequest, "open", "", "")));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage AcceptedResponse(RequestSnapshot request, string body, CancellationTokenSource? cancellation)
    {
        if (cancellation is not null) cancellation.Cancel();
        return new(HttpStatusCode.Created)
        {
            Content = body switch
            {
                "{oversized}" => new StringContent(new string('a', 600_000)),
                "{canceled}" => new StringContent("""{"html_url":"https://github.com/ignored"}"""),
                _ => new StringContent(body)
            }
        };
    }

    private sealed record RequestSnapshot(HttpMethod Method, string Path, string? Body, string? ApiVersion);

    private sealed class RecordingHandler(Func<RequestSnapshot, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<RequestSnapshot> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var snapshot = new RequestSnapshot(
                request.Method,
                request.RequestUri?.AbsolutePath ?? "",
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.TryGetValues("X-GitHub-Api-Version", out var values) ? values.SingleOrDefault() : null);
            Requests.Add(snapshot);
            Assert.Equal("2026-03-10", snapshot.ApiVersion);
            return response(snapshot);
        }
    }
}
