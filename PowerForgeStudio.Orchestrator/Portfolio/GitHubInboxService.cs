using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using PowerForgeStudio.Domain.Portfolio;

namespace PowerForgeStudio.Orchestrator.Portfolio;

public sealed class GitHubInboxService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly IGitRemoteResolver _gitRemoteResolver;
    private readonly bool _ownsHttpClient;

    public GitHubInboxService()
        : this(CreateHttpClient(), new GitRemoteResolver(), ownsHttpClient: true) {
    }

    internal GitHubInboxService(HttpClient httpClient, IGitRemoteResolver gitRemoteResolver)
        : this(httpClient, gitRemoteResolver, ownsHttpClient: false)
    {
    }

    internal GitHubInboxService(HttpClient httpClient, IGitRemoteResolver gitRemoteResolver, bool ownsHttpClient)
    {
        _httpClient = httpClient;
        _gitRemoteResolver = gitRemoteResolver;
        _ownsHttpClient = ownsHttpClient;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
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
                        DefaultBranch: null,
                        ProbedBranch: null,
                        IsDefaultBranch: null,
                        BranchProtectionEnabled: null,
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
                DefaultBranch: null,
                ProbedBranch: null,
                IsDefaultBranch: null,
                BranchProtectionEnabled: null,
                Summary: "GitHub origin not detected.",
                Detail: string.IsNullOrWhiteSpace(originUrl)
                    ? "The repository does not expose an origin remote yet."
                    : $"Origin remote {originUrl} is not a supported GitHub URL.");
        }

        try
        {
            var repositoryMetadata = await GetRepositoryMetadataAsync(owner, repo, cancellationToken).ConfigureAwait(false);
            var probedBranch = ResolveGovernanceBranch(item.Git.BranchName, repositoryMetadata.DefaultBranch);
            var openPullRequests = await GetOpenPullRequestCountAsync(owner, repo, cancellationToken).ConfigureAwait(false);
            var latestReleaseTag = await GetLatestReleaseTagAsync(owner, repo, cancellationToken).ConfigureAwait(false);
            var latestWorkflow = await GetLatestWorkflowStatusAsync(owner, repo, item.Git.BranchName, cancellationToken).ConfigureAwait(false);
            var branchProtectionEnabled = await GetBranchProtectionStatusAsync(owner, repo, probedBranch, cancellationToken).ConfigureAwait(false);
            var isDefaultBranch = !string.IsNullOrWhiteSpace(probedBranch)
                && !string.IsNullOrWhiteSpace(repositoryMetadata.DefaultBranch)
                && string.Equals(probedBranch, repositoryMetadata.DefaultBranch, StringComparison.OrdinalIgnoreCase);
            if (openPullRequests is null)
            {
                return new RepositoryGitHubInbox(
                    RepositoryGitHubInboxStatus.Unavailable,
                    RepositorySlug: $"{owner}/{repo}",
                    OpenPullRequestCount: null,
                    LatestWorkflowFailed: latestWorkflow,
                    LatestReleaseTag: latestReleaseTag,
                    DefaultBranch: repositoryMetadata.DefaultBranch,
                    ProbedBranch: probedBranch,
                    IsDefaultBranch: isDefaultBranch,
                    BranchProtectionEnabled: branchProtectionEnabled,
                    Summary: "GitHub pull request probe unavailable.",
                    Detail: "GitHub did not allow PowerForgeStudio to enumerate open pull requests for this repository. The remote may be inaccessible, renamed, or require a different token scope.");
            }

            var status = DetermineStatus(openPullRequests, latestWorkflow, item.Git.AheadCount);
            return new RepositoryGitHubInbox(
                status,
                RepositorySlug: $"{owner}/{repo}",
                OpenPullRequestCount: openPullRequests,
                LatestWorkflowFailed: latestWorkflow,
                LatestReleaseTag: latestReleaseTag,
                DefaultBranch: repositoryMetadata.DefaultBranch,
                ProbedBranch: probedBranch,
                IsDefaultBranch: isDefaultBranch,
                BranchProtectionEnabled: branchProtectionEnabled,
                Summary: BuildSummary(openPullRequests, latestWorkflow, latestReleaseTag, item.Git.AheadCount, repositoryMetadata.DefaultBranch, probedBranch, branchProtectionEnabled),
                Detail: BuildDetail(item, latestReleaseTag, latestWorkflow, repositoryMetadata.DefaultBranch, probedBranch, branchProtectionEnabled));
        }
        catch (HttpRequestException exception)
        {
            return new RepositoryGitHubInbox(
                RepositoryGitHubInboxStatus.Unavailable,
                RepositorySlug: $"{owner}/{repo}",
                OpenPullRequestCount: null,
                LatestWorkflowFailed: null,
                LatestReleaseTag: null,
                DefaultBranch: null,
                ProbedBranch: null,
                IsDefaultBranch: null,
                BranchProtectionEnabled: null,
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
                DefaultBranch: null,
                ProbedBranch: null,
                IsDefaultBranch: null,
                BranchProtectionEnabled: null,
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
                DefaultBranch: null,
                ProbedBranch: null,
                IsDefaultBranch: null,
                BranchProtectionEnabled: null,
                Summary: "GitHub probe returned unexpected data.",
                Detail: FirstLine(exception.Message) ?? "JSON parsing failed.");
        }
    }

    private async Task<RepositoryMetadata> GetRepositoryMetadataAsync(string owner, string repo, CancellationToken cancellationToken)
    {
        using var request = CreateRequest($"/repos/{owner}/{repo}");
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new RepositoryMetadata(DefaultBranch: null);
        }

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        return new RepositoryMetadata(
            DefaultBranch: document.RootElement.TryGetProperty("default_branch", out var defaultBranch)
                ? defaultBranch.GetString()
                : null);
    }

    private async Task<int?> GetOpenPullRequestCountAsync(string owner, string repo, CancellationToken cancellationToken)
    {
        var total = 0;
        for (var page = 1; page <= 10; page++)
        {
            using var request = CreateRequest($"/repos/{owner}/{repo}/pulls?state=open&per_page=100&page={page}");
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return total;
            }

            var pageCount = document.RootElement.GetArrayLength();
            total += pageCount;
            if (pageCount < 100)
            {
                return total;
            }
        }

        return total;
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

    private async Task<bool?> GetBranchProtectionStatusAsync(string owner, string repo, string? branchName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(branchName))
        {
            return null;
        }

        using var request = CreateRequest($"/repos/{owner}/{repo}/branches/{Uri.EscapeDataString(branchName)}");
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("protected", out var protectedProperty)
            ? protectedProperty.GetBoolean()
            : null;
    }

    private static RepositoryGitHubInboxStatus DetermineStatus(int? openPullRequests, bool? latestWorkflowFailed, int localAheadCount)
    {
        if (openPullRequests > 0 || latestWorkflowFailed == true || localAheadCount > 0)
        {
            return RepositoryGitHubInboxStatus.Attention;
        }

        return RepositoryGitHubInboxStatus.Ready;
    }

    private static string BuildSummary(
        int? openPullRequests,
        bool? latestWorkflowFailed,
        string? latestReleaseTag,
        int localAheadCount,
        string? defaultBranch,
        string? probedBranch,
        bool? branchProtectionEnabled)
    {
        var parts = new List<string>();
        parts.Add(openPullRequests switch
        {
            null => "PR status unavailable",
            0 => "No open PRs",
            _ => $"{openPullRequests} open PR(s)"
        });
        parts.Add(latestWorkflowFailed == true ? "latest workflow failed" : latestWorkflowFailed == false ? "latest workflow passed" : "workflow status unavailable");
        parts.Add(string.IsNullOrWhiteSpace(latestReleaseTag) ? "no release tag detected" : $"latest release {latestReleaseTag}");
        if (!string.IsNullOrWhiteSpace(defaultBranch))
        {
            parts.Add($"default branch {defaultBranch}");
        }
        if (!string.IsNullOrWhiteSpace(probedBranch))
        {
            parts.Add(branchProtectionEnabled == true
                ? $"{probedBranch} protected"
                : branchProtectionEnabled == false
                    ? $"{probedBranch} not protected"
                    : $"{probedBranch} protection unknown");
        }

        if (localAheadCount > 0)
        {
            parts.Add($"{localAheadCount} local commit(s) ahead");
        }

        return string.Join(", ", parts);
    }

    private static string BuildDetail(
        RepositoryPortfolioItem item,
        string? latestReleaseTag,
        bool? latestWorkflowFailed,
        string? defaultBranch,
        string? probedBranch,
        bool? branchProtectionEnabled)
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

        if (!string.IsNullOrWhiteSpace(defaultBranch))
        {
            detailParts.Add($"GitHub reports {defaultBranch} as the default branch.");
        }

        if (!string.IsNullOrWhiteSpace(probedBranch))
        {
            detailParts.Add(branchProtectionEnabled == true
                ? $"GitHub confirms {probedBranch} is protected."
                : branchProtectionEnabled == false
                    ? $"GitHub does not mark {probedBranch} as protected."
                    : $"GitHub branch protection could not be confirmed for {probedBranch}.");
        }

        if (latestWorkflowFailed == true)
        {
            detailParts.Add("The latest workflow run for the current branch reported a failure.");
        }

        return string.Join(" ", detailParts);
    }

    private static string? ResolveGovernanceBranch(string? branchName, string? defaultBranch)
        => string.IsNullOrWhiteSpace(branchName) || string.Equals(branchName, "-", StringComparison.OrdinalIgnoreCase)
            ? defaultBranch
            : branchName;

    private HttpRequestMessage CreateRequest(string relativePath)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, relativePath);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        if (!string.IsNullOrWhiteSpace(_httpClient.DefaultRequestHeaders.UserAgent.ToString()))
        {
            return request;
        }

        request.Headers.UserAgent.ParseAdd("PowerForgeStudio/0.1");
        return request;
    }

    private static HttpClient CreateHttpClient()
    {
        var httpClient = new HttpClient {
            BaseAddress = new Uri("https://api.github.com"),
            Timeout = TimeSpan.FromSeconds(12)
        };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PowerForgeStudio/0.1");

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

    private sealed record RepositoryMetadata(string? DefaultBranch);
}
