using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using PowerForgeStudio.Domain.Hub;

namespace PowerForgeStudio.Orchestrator.Hub;

public sealed class GitHubProjectService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _concurrencyGate;
    private readonly bool _ownsHttpClient;
    private volatile int _rateLimitRemaining = int.MaxValue;

    public GitHubProjectService()
        : this(CreateHttpClient(), ownsHttpClient: true)
    {
    }

    internal GitHubProjectService(HttpClient httpClient, bool ownsHttpClient = false)
    {
        _httpClient = httpClient;
        _ownsHttpClient = ownsHttpClient;
        _concurrencyGate = new SemaphoreSlim(5, 5);
    }

    public bool IsRateLimited => Interlocked.CompareExchange(ref _rateLimitRemaining, 0, 0) < 100;

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }

        _concurrencyGate.Dispose();
    }

    public async Task<IReadOnlyList<GitHubIssue>> FetchIssuesAsync(
        string slug,
        string state = "open",
        CancellationToken cancellationToken = default)
    {
        var parts = slug.Split('/');
        if (parts.Length != 2)
        {
            return [];
        }

        await _concurrencyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var issues = new List<GitHubIssue>();
            for (var page = 1; page <= 5; page++)
            {
                using var request = CreateRequest(
                    $"/repos/{parts[0]}/{parts[1]}/issues?state={state}&per_page=100&page={page}");
                using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                TrackRateLimit(response);

                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
                {
                    return issues;
                }

                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var document = JsonDocument.Parse(json);

                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return issues;
                }

                foreach (var item in document.RootElement.EnumerateArray())
                {
                    // Skip pull requests (GitHub returns PRs in the issues endpoint)
                    if (item.TryGetProperty("pull_request", out _))
                    {
                        continue;
                    }

                    issues.Add(ParseIssue(item));
                }

                if (document.RootElement.GetArrayLength() < 100)
                {
                    break;
                }
            }

            return issues;
        }
        finally
        {
            _concurrencyGate.Release();
        }
    }

    public async Task<IReadOnlyList<GitHubPullRequest>> FetchPullRequestsAsync(
        string slug,
        string state = "open",
        CancellationToken cancellationToken = default)
    {
        var parts = slug.Split('/');
        if (parts.Length != 2)
        {
            return [];
        }

        await _concurrencyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var pullRequests = new List<GitHubPullRequest>();
            for (var page = 1; page <= 5; page++)
            {
                using var request = CreateRequest(
                    $"/repos/{parts[0]}/{parts[1]}/pulls?state={state}&per_page=100&page={page}");
                using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                TrackRateLimit(response);

                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
                {
                    return pullRequests;
                }

                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var document = JsonDocument.Parse(json);

                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return pullRequests;
                }

                foreach (var item in document.RootElement.EnumerateArray())
                {
                    pullRequests.Add(ParsePullRequest(item));
                }

                if (document.RootElement.GetArrayLength() < 100)
                {
                    break;
                }
            }

            return pullRequests;
        }
        finally
        {
            _concurrencyGate.Release();
        }
    }

    public async Task<int> GetOpenPullRequestCountAsync(string slug, CancellationToken cancellationToken = default)
    {
        var parts = slug.Split('/');
        if (parts.Length != 2)
        {
            return 0;
        }

        await _concurrencyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var total = 0;
            for (var page = 1; page <= 5; page++)
            {
                using var request = CreateRequest($"/repos/{parts[0]}/{parts[1]}/pulls?state=open&per_page=100&page={page}");
                using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                TrackRateLimit(response);

                if (!response.IsSuccessStatusCode)
                {
                    return total;
                }

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
                    break;
                }
            }

            return total;
        }
        finally
        {
            _concurrencyGate.Release();
        }
    }

    public async Task<int> GetOpenIssueCountAsync(string slug, CancellationToken cancellationToken = default)
    {
        var parts = slug.Split('/');
        if (parts.Length != 2)
        {
            return 0;
        }

        await _concurrencyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var request = CreateRequest($"/repos/{parts[0]}/{parts[1]}");
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            TrackRateLimit(response);

            if (!response.IsSuccessStatusCode)
            {
                return 0;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("open_issues_count", out var count)
                ? count.GetInt32()
                : 0;
        }
        finally
        {
            _concurrencyGate.Release();
        }
    }

    private static GitHubIssue ParseIssue(JsonElement element)
    {
        var labels = new List<string>();
        if (element.TryGetProperty("labels", out var labelsArray))
        {
            foreach (var label in labelsArray.EnumerateArray())
            {
                if (label.TryGetProperty("name", out var name))
                {
                    labels.Add(name.GetString() ?? string.Empty);
                }
            }
        }

        var assignees = new List<string>();
        if (element.TryGetProperty("assignees", out var assigneesArray))
        {
            foreach (var assignee in assigneesArray.EnumerateArray())
            {
                if (assignee.TryGetProperty("login", out var login))
                {
                    assignees.Add(login.GetString() ?? string.Empty);
                }
            }
        }

        return new GitHubIssue(
            Number: element.GetProperty("number").GetInt32(),
            Title: element.GetProperty("title").GetString() ?? string.Empty,
            State: element.GetProperty("state").GetString() ?? "open",
            AuthorLogin: element.TryGetProperty("user", out var user)
                ? user.TryGetProperty("login", out var authorLogin) ? authorLogin.GetString() : null
                : null,
            Labels: labels,
            Assignees: assignees,
            CreatedAt: element.GetProperty("created_at").GetDateTimeOffset(),
            ClosedAt: element.TryGetProperty("closed_at", out var closedAt) && closedAt.ValueKind != JsonValueKind.Null
                ? closedAt.GetDateTimeOffset()
                : null);
    }

    private static GitHubPullRequest ParsePullRequest(JsonElement element)
    {
        var labels = new List<string>();
        if (element.TryGetProperty("labels", out var labelsArray))
        {
            foreach (var label in labelsArray.EnumerateArray())
            {
                if (label.TryGetProperty("name", out var name))
                {
                    labels.Add(name.GetString() ?? string.Empty);
                }
            }
        }

        return new GitHubPullRequest(
            Number: element.GetProperty("number").GetInt32(),
            Title: element.GetProperty("title").GetString() ?? string.Empty,
            State: element.GetProperty("state").GetString() ?? "open",
            AuthorLogin: element.TryGetProperty("user", out var user)
                ? user.TryGetProperty("login", out var authorLogin) ? authorLogin.GetString() : null
                : null,
            HeadBranch: element.TryGetProperty("head", out var head)
                ? head.TryGetProperty("ref", out var headRef) ? headRef.GetString() ?? string.Empty : string.Empty
                : string.Empty,
            BaseBranch: element.TryGetProperty("base", out var baseEl)
                ? baseEl.TryGetProperty("ref", out var baseRef) ? baseRef.GetString() ?? string.Empty : string.Empty
                : string.Empty,
            ReviewStatus: GitHubPrReviewStatus.None,
            MergeStatus: ParseMergeStatus(element),
            Labels: labels,
            Additions: element.TryGetProperty("additions", out var additions) ? additions.GetInt32() : 0,
            Deletions: element.TryGetProperty("deletions", out var deletions) ? deletions.GetInt32() : 0,
            ChangedFiles: element.TryGetProperty("changed_files", out var changed) ? changed.GetInt32() : 0,
            CreatedAt: element.GetProperty("created_at").GetDateTimeOffset(),
            MergedAt: element.TryGetProperty("merged_at", out var mergedAt) && mergedAt.ValueKind != JsonValueKind.Null
                ? mergedAt.GetDateTimeOffset()
                : null);
    }

    private static GitHubPrMergeStatus ParseMergeStatus(JsonElement element)
    {
        if (!element.TryGetProperty("mergeable_state", out var state))
        {
            return GitHubPrMergeStatus.Unknown;
        }

        return state.GetString() switch
        {
            "clean" => GitHubPrMergeStatus.Clean,
            "blocked" => GitHubPrMergeStatus.Blocked,
            "behind" => GitHubPrMergeStatus.Behind,
            "dirty" => GitHubPrMergeStatus.Conflicting,
            _ => GitHubPrMergeStatus.Unknown
        };
    }

    private void TrackRateLimit(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var values))
        {
            var value = values.FirstOrDefault();
            if (int.TryParse(value, out var remaining))
            {
                Interlocked.Exchange(ref _rateLimitRemaining, remaining);
            }
        }
    }

    private HttpRequestMessage CreateRequest(string relativePath)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, relativePath);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("PowerForgeStudio/0.1");
        return request;
    }

    private static HttpClient CreateHttpClient()
    {
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://api.github.com"),
            Timeout = TimeSpan.FromSeconds(15)
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
        if (!string.IsNullOrWhiteSpace(token))
        {
            return token;
        }

        token = Environment.GetEnvironmentVariable("GH_TOKEN");
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }
}
