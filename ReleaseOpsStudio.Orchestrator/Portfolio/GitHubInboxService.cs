using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ReleaseOpsStudio.Domain.Portfolio;

namespace ReleaseOpsStudio.Orchestrator.Portfolio;

public sealed class GitHubInboxService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly IGitRemoteResolver _gitRemoteResolver;

    public GitHubInboxService()
        : this(CreateHttpClient(), new GitRemoteResolver()) {
    }

    internal GitHubInboxService(HttpClient httpClient, IGitRemoteResolver gitRemoteResolver)
    {
        _httpClient = httpClient;
        _gitRemoteResolver = gitRemoteResolver;
    }

    public async Task<IReadOnlyList<RepositoryPortfolioItem>> PopulateInboxAsync(
        IReadOnlyList<RepositoryPortfolioItem> items,
        GitHubInboxOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new GitHubInboxOptions();
        var maxRepositories = Math.Max(0, options.MaxRepositories);
        var enriched = new List<RepositoryPortfolioItem>(items.Count);

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (index >= maxRepositories)
            {
                enriched.Add(item with {
                    GitHubInbox = new RepositoryGitHubInbox(
                        RepositoryGitHubInboxStatus.NotProbed,
                        RepositorySlug: null,
                        OpenPullRequestCount: null,
                        LatestWorkflowFailed: null,
                        LatestReleaseTag: null,
                        Summary: "GitHub inbox deferred.",
                        Detail: $"Only the first {maxRepositories} repositories are probed during this refresh.")
                });
                continue;
            }

            enriched.Add(item with {
                GitHubInbox = await ProbeAsync(item, cancellationToken).ConfigureAwait(false)
            });
        }

        return enriched;
    }

    private async Task<RepositoryGitHubInbox> ProbeAsync(RepositoryPortfolioItem item, CancellationToken cancellationToken)
    {
        var originUrl = await _gitRemoteResolver.ResolveOriginUrlAsync(item.RootPath, cancellationToken).ConfigureAwait(false);
        if (!TryParseGitHubSlug(originUrl, out var owner, out var repo))
        {
            return new RepositoryGitHubInbox(
                RepositoryGitHubInboxStatus.Unavailable,
                RepositorySlug: null,
                OpenPullRequestCount: null,
                LatestWorkflowFailed: null,
                LatestReleaseTag: null,
                Summary: "GitHub origin not detected.",
                Detail: string.IsNullOrWhiteSpace(originUrl)
                    ? "The repository does not expose an origin remote yet."
                    : $"Origin remote {originUrl} is not a supported GitHub URL.");
        }

        try
        {
            var openPullRequests = await GetOpenPullRequestCountAsync(owner, repo, cancellationToken).ConfigureAwait(false);
            var latestReleaseTag = await GetLatestReleaseTagAsync(owner, repo, cancellationToken).ConfigureAwait(false);
            var latestWorkflow = await GetLatestWorkflowStatusAsync(owner, repo, item.Git.BranchName, cancellationToken).ConfigureAwait(false);

            var status = DetermineStatus(openPullRequests, latestWorkflow, item.Git.AheadCount);
            return new RepositoryGitHubInbox(
                status,
                RepositorySlug: $"{owner}/{repo}",
                OpenPullRequestCount: openPullRequests,
                LatestWorkflowFailed: latestWorkflow,
                LatestReleaseTag: latestReleaseTag,
                Summary: BuildSummary(openPullRequests, latestWorkflow, latestReleaseTag, item.Git.AheadCount),
                Detail: BuildDetail(item, latestReleaseTag, latestWorkflow));
        }
        catch (HttpRequestException exception)
        {
            return new RepositoryGitHubInbox(
                RepositoryGitHubInboxStatus.Unavailable,
                RepositorySlug: $"{owner}/{repo}",
                OpenPullRequestCount: null,
                LatestWorkflowFailed: null,
                LatestReleaseTag: null,
                Summary: "GitHub probe unavailable.",
                Detail: FirstLine(exception.Message) ?? "HTTP probe failed.");
        }
        catch (TaskCanceledException)
        {
            return new RepositoryGitHubInbox(
                RepositoryGitHubInboxStatus.Unavailable,
                RepositorySlug: $"{owner}/{repo}",
                OpenPullRequestCount: null,
                LatestWorkflowFailed: null,
                LatestReleaseTag: null,
                Summary: "GitHub probe timed out.",
                Detail: "The GitHub inbox request timed out before a response was received.");
        }
        catch (JsonException exception)
        {
            return new RepositoryGitHubInbox(
                RepositoryGitHubInboxStatus.Unavailable,
                RepositorySlug: $"{owner}/{repo}",
                OpenPullRequestCount: null,
                LatestWorkflowFailed: null,
                LatestReleaseTag: null,
                Summary: "GitHub probe returned unexpected data.",
                Detail: FirstLine(exception.Message) ?? "JSON parsing failed.");
        }
    }

    private async Task<int> GetOpenPullRequestCountAsync(string owner, string repo, CancellationToken cancellationToken)
    {
        using var request = CreateRequest($"/repos/{owner}/{repo}/pulls?state=open&per_page=100");
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return 0;
        }

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.GetArrayLength()
            : 0;
    }

    private async Task<string?> GetLatestReleaseTagAsync(string owner, string repo, CancellationToken cancellationToken)
    {
        using var request = CreateRequest($"/repos/{owner}/{repo}/releases/latest");
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("tag_name", out var tagName)
            ? tagName.GetString()
            : null;
    }

    private async Task<bool?> GetLatestWorkflowStatusAsync(string owner, string repo, string? branchName, CancellationToken cancellationToken)
    {
        var branchQuery = string.IsNullOrWhiteSpace(branchName) || string.Equals(branchName, "-", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : $"&branch={Uri.EscapeDataString(branchName)}";

        using var request = CreateRequest($"/repos/{owner}/{repo}/actions/runs?per_page=1{branchQuery}");
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("workflow_runs", out var workflowRuns) || workflowRuns.GetArrayLength() == 0)
        {
            return null;
        }

        var latest = workflowRuns[0];
        var conclusion = latest.TryGetProperty("conclusion", out var conclusionValue)
            ? conclusionValue.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(conclusion))
        {
            return null;
        }

        return !string.Equals(conclusion, "success", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(conclusion, "neutral", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(conclusion, "skipped", StringComparison.OrdinalIgnoreCase);
    }

    private static RepositoryGitHubInboxStatus DetermineStatus(int openPullRequests, bool? latestWorkflowFailed, int localAheadCount)
    {
        if (openPullRequests > 0 || latestWorkflowFailed == true || localAheadCount > 0)
        {
            return RepositoryGitHubInboxStatus.Attention;
        }

        return RepositoryGitHubInboxStatus.Ready;
    }

    private static string BuildSummary(int openPullRequests, bool? latestWorkflowFailed, string? latestReleaseTag, int localAheadCount)
    {
        var parts = new List<string>();
        parts.Add(openPullRequests == 0 ? "No open PRs" : $"{openPullRequests} open PR(s)");
        parts.Add(latestWorkflowFailed == true ? "latest workflow failed" : latestWorkflowFailed == false ? "latest workflow passed" : "workflow status unavailable");
        parts.Add(string.IsNullOrWhiteSpace(latestReleaseTag) ? "no release tag detected" : $"latest release {latestReleaseTag}");

        if (localAheadCount > 0)
        {
            parts.Add($"{localAheadCount} local commit(s) ahead");
        }

        return string.Join(", ", parts);
    }

    private static string BuildDetail(RepositoryPortfolioItem item, string? latestReleaseTag, bool? latestWorkflowFailed)
    {
        var detailParts = new List<string> {
            item.Git.IsDirty
                ? "Local workspace is dirty, so GitHub signals should not be treated as publish-ready on their own."
                : "Local workspace is clean."
        };

        if (item.Git.AheadCount > 0)
        {
            detailParts.Add($"Current branch is ahead of upstream by {item.Git.AheadCount} commit(s).");
        }

        if (!string.IsNullOrWhiteSpace(latestReleaseTag))
        {
            detailParts.Add($"Latest detected release tag is {latestReleaseTag}.");
        }

        if (latestWorkflowFailed == true)
        {
            detailParts.Add("The latest workflow run for the current branch reported a failure.");
        }

        return string.Join(" ", detailParts);
    }

    private HttpRequestMessage CreateRequest(string relativePath)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, relativePath);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        if (!string.IsNullOrWhiteSpace(_httpClient.DefaultRequestHeaders.UserAgent.ToString()))
        {
            return request;
        }

        request.Headers.UserAgent.ParseAdd("ReleaseOpsStudio/0.1");
        return request;
    }

    private static HttpClient CreateHttpClient()
    {
        var httpClient = new HttpClient {
            BaseAddress = new Uri("https://api.github.com"),
            Timeout = TimeSpan.FromSeconds(12)
        };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ReleaseOpsStudio/0.1");

        var token = ResolveToken();
        if (!string.IsNullOrWhiteSpace(token))
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return httpClient;
    }

    private static string? ResolveToken()
    {
        var token = Environment.GetEnvironmentVariable("RELEASE_OPS_STUDIO_GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
        {
            return token;
        }

        token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }

    internal static bool TryParseGitHubSlug(string? originUrl, out string owner, out string repo)
    {
        owner = string.Empty;
        repo = string.Empty;

        if (string.IsNullOrWhiteSpace(originUrl))
        {
            return false;
        }

        var trimmed = originUrl.Trim();
        if (trimmed.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["git@github.com:".Length..];
        }
        else if (trimmed.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["https://github.com/".Length..];
        }
        else if (trimmed.StartsWith("http://github.com/", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["http://github.com/".Length..];
        }
        else
        {
            return false;
        }

        trimmed = trimmed.TrimEnd('/');
        if (trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4];
        }

        var parts = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        owner = parts[0];
        repo = parts[1];
        return true;
    }

    private static string? FirstLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));
    }
}
