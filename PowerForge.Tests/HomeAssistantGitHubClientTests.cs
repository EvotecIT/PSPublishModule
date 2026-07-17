using System.Net;
using System.Text;

namespace PowerForge.Tests;

public sealed class HomeAssistantGitHubClientTests {
    [Fact]
    public void GetCheckSummary_ExcludesOnlyChecksFromTheCurrentGitHubActionsRun() {
        const long workflowRunId = 29561117925L;
        using var httpClient = CreateHttpClient($$"""
            {
              "check_runs": [
                {
                  "name": "release / prepare",
                  "status": "in_progress",
                  "conclusion": null,
                  "details_url": "https://github.com/EvotecIT/example/actions/runs/{{workflowRunId}}/job/1",
                  "app": { "slug": "github-actions" }
                },
                {
                  "name": "tests",
                  "status": "completed",
                  "conclusion": "success",
                  "details_url": "https://github.com/EvotecIT/example/actions/runs/29560000000/job/2",
                  "app": { "slug": "github-actions" }
                }
              ]
            }
            """);
        var client = CreateClient(httpClient);

        var summary = client.GetCheckSummary("head-sha", workflowRunId);

        Assert.Equal(1, summary.Total);
        Assert.Empty(summary.BlockingChecks);
    }

    [Fact]
    public void GetCheckSummary_KeepsPendingChecksFromOtherWorkflowRunsBlocking() {
        const long workflowRunId = 29561117925L;
        using var httpClient = CreateHttpClient("""
            {
              "check_runs": [
                {
                  "name": "release / prepare",
                  "status": "in_progress",
                  "conclusion": null,
                  "details_url": "https://github.com/EvotecIT/example/actions/runs/29561117924/job/1",
                  "app": { "slug": "github-actions" }
                }
              ]
            }
            """);
        var client = CreateClient(httpClient);

        var summary = client.GetCheckSummary("head-sha", workflowRunId);

        Assert.Equal(1, summary.Total);
        Assert.Equal("release / prepare: in_progress", Assert.Single(summary.BlockingChecks));
    }

    [Fact]
    public void GetCheckSummary_DoesNotTrustAnotherAppUsingTheCurrentWorkflowUrl() {
        const long workflowRunId = 29561117925L;
        using var httpClient = CreateHttpClient($$"""
            {
              "check_runs": [
                {
                  "name": "external validation",
                  "status": "completed",
                  "conclusion": "failure",
                  "details_url": "https://github.com/EvotecIT/example/actions/runs/{{workflowRunId}}/job/1",
                  "app": { "slug": "external-checks" }
                }
              ]
            }
            """);
        var client = CreateClient(httpClient);

        var summary = client.GetCheckSummary("head-sha", workflowRunId);

        Assert.Equal(1, summary.Total);
        Assert.Equal("external validation: failure", Assert.Single(summary.BlockingChecks));
    }

    [Fact]
    public void GetCheckSummary_KeepsNonReleaseChecksFromTheCurrentWorkflowRunBlocking() {
        const long workflowRunId = 29561117925L;
        using var httpClient = CreateHttpClient($$"""
            {
              "check_runs": [
                {
                  "name": "security validation",
                  "status": "completed",
                  "conclusion": "failure",
                  "details_url": "https://github.com/EvotecIT/example/actions/runs/{{workflowRunId}}/job/1",
                  "app": { "slug": "github-actions" }
                }
              ]
            }
            """);
        var client = CreateClient(httpClient);

        var summary = client.GetCheckSummary("head-sha", workflowRunId);

        Assert.Equal(1, summary.Total);
        Assert.Equal("security validation: failure", Assert.Single(summary.BlockingChecks));
    }

    [Fact]
    public void GetCheckSummary_LeavesNoValidationChecksWhenOnlyTheCurrentRunExists() {
        const long workflowRunId = 29561117925L;
        using var httpClient = CreateHttpClient($$"""
            {
              "check_runs": [
                {
                  "name": "release / prepare",
                  "status": "in_progress",
                  "conclusion": null,
                  "details_url": "https://github.com/EvotecIT/example/actions/runs/{{workflowRunId}}/job/1",
                  "app": { "slug": "github-actions" }
                }
              ]
            }
            """);
        var client = CreateClient(httpClient);

        var summary = client.GetCheckSummary("head-sha", workflowRunId);

        Assert.Equal(0, summary.Total);
        Assert.Empty(summary.BlockingChecks);
    }

    [Fact]
    public void GetCheckSummary_DoesNotCountCompletedFailedReleaseAttemptsAsValidation() {
        const long currentWorkflowRunId = 29570000000L;
        using var httpClient = CreateHttpClient(uri => uri.AbsolutePath switch {
            "/repos/EvotecIT/example/commits/head-sha/check-runs" => """
                {
                  "check_runs": [
                    {
                      "name": "release / prepare",
                      "status": "completed",
                      "conclusion": "failure",
                      "details_url": "https://github.com/EvotecIT/example/actions/runs/29561117925/job/1",
                      "app": { "slug": "github-actions" }
                    },
                    {
                      "name": "release / build",
                      "status": "completed",
                      "conclusion": "skipped",
                      "details_url": "https://github.com/EvotecIT/example/actions/runs/29561117925/job/2",
                      "app": { "slug": "github-actions" }
                    },
                    {
                      "name": "release / publish",
                      "status": "completed",
                      "conclusion": "skipped",
                      "details_url": "https://github.com/EvotecIT/example/actions/runs/29561117925/job/3",
                      "app": { "slug": "github-actions" }
                    }
                  ]
                }
                """,
            "/repos/EvotecIT/example/actions/runs/29570000000" => """
                {
                  "id": 29570000000,
                  "path": ".github/workflows/release.yml",
                  "event": "workflow_dispatch",
                  "head_sha": "current-main-sha",
                  "status": "in_progress",
                  "conclusion": null
                }
                """,
            "/repos/EvotecIT/example/actions/runs/29561117925" => """
                {
                  "id": 29561117925,
                  "path": ".github/workflows/release.yml",
                  "event": "pull_request_target",
                  "head_sha": "head-sha",
                  "status": "completed",
                  "conclusion": "failure"
                }
                """,
            _ => throw new InvalidOperationException($"Unexpected GitHub API path: {uri.AbsolutePath}")
        });
        var client = CreateClient(httpClient);

        var summary = client.GetCheckSummary("head-sha", currentWorkflowRunId);

        Assert.Equal(0, summary.Total);
        Assert.Empty(summary.BlockingChecks);
    }

    [Fact]
    public void GetCheckSummary_KeepsFailedJobsFromAnInProgressReleaseWorkflowBlocking() {
        const long currentWorkflowRunId = 29570000000L;
        using var httpClient = CreateHttpClient(uri => uri.AbsolutePath switch {
            "/repos/EvotecIT/example/commits/head-sha/check-runs" => """
                {
                  "check_runs": [
                    {
                      "name": "release / prepare",
                      "status": "completed",
                      "conclusion": "failure",
                      "details_url": "https://github.com/EvotecIT/example/actions/runs/29561117925/job/1",
                      "app": { "slug": "github-actions" }
                    }
                  ]
                }
                """,
            "/repos/EvotecIT/example/actions/runs/29570000000" => """
                {
                  "id": 29570000000,
                  "path": ".github/workflows/release.yml",
                  "event": "workflow_dispatch",
                  "head_sha": "current-main-sha",
                  "status": "in_progress",
                  "conclusion": null
                }
                """,
            "/repos/EvotecIT/example/actions/runs/29561117925" => """
                {
                  "id": 29561117925,
                  "path": ".github/workflows/release.yml",
                  "event": "pull_request_target",
                  "head_sha": "head-sha",
                  "status": "in_progress",
                  "conclusion": null
                }
                """,
            _ => throw new InvalidOperationException($"Unexpected GitHub API path: {uri.AbsolutePath}")
        });
        var client = CreateClient(httpClient);

        var summary = client.GetCheckSummary("head-sha", currentWorkflowRunId);

        Assert.Equal(1, summary.Total);
        Assert.Equal("release / prepare: failure", Assert.Single(summary.BlockingChecks));
    }

    [Theory]
    [InlineData(".github/workflows/security.yml", "pull_request_target", "head-sha")]
    [InlineData(".github/workflows/release.yml", "pull_request", "head-sha")]
    [InlineData(".github/workflows/release.yml", "pull_request_target", "different-head-sha")]
    public void GetCheckSummary_KeepsPriorChecksBlockingWhenTrustedReleaseIdentityDoesNotMatch(
        string candidatePath,
        string candidateEvent,
        string candidateHeadSha) {
        const long currentWorkflowRunId = 29570000000L;
        using var httpClient = CreateHttpClient(uri => uri.AbsolutePath switch {
            "/repos/EvotecIT/example/commits/head-sha/check-runs" => """
                {
                  "check_runs": [
                    {
                      "name": "release / prepare",
                      "status": "completed",
                      "conclusion": "failure",
                      "details_url": "https://github.com/EvotecIT/example/actions/runs/29561117925/job/1",
                      "app": { "slug": "github-actions" }
                    }
                  ]
                }
                """,
            "/repos/EvotecIT/example/actions/runs/29570000000" => """
                {
                  "id": 29570000000,
                  "path": ".github/workflows/release.yml",
                  "event": "workflow_dispatch",
                  "head_sha": "current-main-sha"
                }
                """,
            "/repos/EvotecIT/example/actions/runs/29561117925" => $$"""
                {
                  "id": 29561117925,
                  "path": "{{candidatePath}}",
                  "event": "{{candidateEvent}}",
                  "head_sha": "{{candidateHeadSha}}",
                  "status": "completed",
                  "conclusion": "failure"
                }
                """,
            _ => throw new InvalidOperationException($"Unexpected GitHub API path: {uri.AbsolutePath}")
        });
        var client = CreateClient(httpClient);

        var summary = client.GetCheckSummary("head-sha", currentWorkflowRunId);

        Assert.Equal(1, summary.Total);
        Assert.Equal("release / prepare: failure", Assert.Single(summary.BlockingChecks));
    }

    private static HomeAssistantGitHubClient CreateClient(HttpClient httpClient)
        => new("EvotecIT", "example", "token", "https://api.github.test", httpClient);

    private static HttpClient CreateHttpClient(string responseBody)
        => CreateHttpClient(_ => responseBody);

    private static HttpClient CreateHttpClient(Func<Uri, string> responseFactory)
        => new(new JsonResponseHandler(responseFactory));

    private sealed class JsonResponseHandler(Func<Uri, string> responseFactory) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(
                    responseFactory(request.RequestUri ?? throw new InvalidOperationException("GitHub API request URI was missing.")),
                    Encoding.UTF8,
                    "application/json")
            });
    }
}
