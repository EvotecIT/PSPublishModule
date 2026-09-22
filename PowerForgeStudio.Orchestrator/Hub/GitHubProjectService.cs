using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using PowerForgeStudio.Domain.Hub;

namespace PowerForgeStudio.Orchestrator.Hub;

/// <summary>Bounded GitHub reads. Access failures never masquerade as empty data.</summary>
public sealed partial class GitHubProjectService : IGitHubProjectService, IGitHubReleaseCatalogService, IDisposable
{
    private const int MaxPagedRequestCount = 5;
    private const int MaximumResponseBytes = 4 * 1024 * 1024;
    private const string GitHubApiVersion = "2026-03-10";
    private readonly Lazy<HttpClient> _client;
    private readonly SemaphoreSlim _concurrencyGate = new(5, 5);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly bool _ownsHttpClient;
    private int _disposed;
    private int _rateLimitRemaining = int.MaxValue;

    public GitHubProjectService()
    {
        _client = new(() => GitHubHttpClientFactory.Create());
        _ownsHttpClient = true;
    }

    internal GitHubProjectService(HttpClient httpClient, bool ownsHttpClient = false)
    {
        _client = new(() => httpClient);
        _ownsHttpClient = ownsHttpClient;
    }

    public bool IsRateLimited => Volatile.Read(ref _rateLimitRemaining) < 100;

    public async Task<string?> ResolveRepositoryAsync(string workingCopy, CancellationToken cancellationToken = default)
    {
        var remote = await new Portfolio.GitRemoteResolver().ResolveOriginUrlAsync(workingCopy, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Portfolio.GitHubInboxService.TryParseGitHubSlug(remote, out var owner, out var repo)) return null;
        var slug = owner + "/" + repo;
        RepositoryPath(slug);
        return slug;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        if (_ownsHttpClient && _client.IsValueCreated) _client.Value.Dispose();
        // Requests can still be releasing the managed semaphore / reading the lifetime token.
        // Neither allocates a native wait handle in this service.
    }

    public Task<GitHubPage<GitHubIssue>> FetchIssuesAsync(string slug, string state = "open", CancellationToken cancellationToken = default)
        => FetchPageAsync($"{RepositoryPath(slug)}/issues?state={ValidateState(state)}", ParseIssue,
            cancellationToken, item => !item.TryGetProperty("pull_request", out _));

    public Task<GitHubPage<GitHubPullRequest>> FetchPullRequestsAsync(string slug, string state = "open", CancellationToken cancellationToken = default)
        => FetchPageAsync($"{RepositoryPath(slug)}/pulls?state={ValidateState(state)}", ParsePullRequest, cancellationToken);

    /// <summary>Finds a merged pull request whose recorded head is exactly the supplied commit.</summary>
    public async Task<GitHubPullRequest?> FindMergedPullRequestByHeadAsync(
        string slug,
        string headSha,
        string expectedBaseBranch,
        CancellationToken cancellationToken = default)
    {
        if (!Regex.IsMatch(headSha ?? "", @"\A(?:[a-fA-F0-9]{40}|[a-fA-F0-9]{64})\z"))
            throw new ArgumentException("A full commit SHA is required.", nameof(headSha));
        if (string.IsNullOrWhiteSpace(expectedBaseBranch) ||
            !Regex.IsMatch(expectedBaseBranch, @"\A[A-Za-z0-9][A-Za-z0-9._/-]*\z"))
            throw new ArgumentException("A base branch name is required.", nameof(expectedBaseBranch));

        var result = await FetchPageAsync(
            $"{RepositoryPath(slug)}/commits/{headSha}/pulls",
            ParsePullRequest,
            cancellationToken).ConfigureAwait(false);
        if (result.HasMore)
            throw new InvalidDataException("GitHub returned more associated pull requests than Studio can inspect safely.");
        return result.Items.FirstOrDefault(pull =>
            pull.MergedAt is not null &&
            string.Equals(pull.HeadSha, headSha, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(pull.BaseBranch, expectedBaseBranch, StringComparison.Ordinal));
    }

    public async Task<GitHubIssueDetail?> FetchIssueDetailAsync(string slug, int issueNumber, CancellationToken cancellationToken = default)
    {
        var path = $"{RepositoryPath(slug)}/issues/{ValidateNumber(issueNumber)}";
        using var response = await ReadJsonAsync(path, cancellationToken).ConfigureAwait(false);
        var issue = ParseIssue(response.Document.RootElement);
        var comments = await FetchPageAsync(path + "/comments", item => ParseDiscussionComment(item, GitHubDiscussionCommentKind.IssueComment), cancellationToken).ConfigureAwait(false);
        var timeline = await FetchTimelineAsync(path, cancellationToken).ConfigureAwait(false);
        return new(issue, comments.Items, timeline.Items, comments.HasMore || timeline.HasMore);
    }

    public async Task<GitHubPullRequestDetail?> FetchPullRequestDetailAsync(string slug, int pullRequestNumber, CancellationToken cancellationToken = default)
    {
        var repository = RepositoryPath(slug);
        var number = ValidateNumber(pullRequestNumber);
        var path = $"{repository}/pulls/{number}";
        using var response = await ReadJsonAsync(path, cancellationToken).ConfigureAwait(false);
        var pull = ParsePullRequest(response.Document.RootElement);
        var issueComments = await FetchPageAsync($"{repository}/issues/{number}/comments", item => ParseDiscussionComment(item, GitHubDiscussionCommentKind.IssueComment), cancellationToken).ConfigureAwait(false);
        var reviewComments = await FetchPageAsync(path + "/comments", item => ParseDiscussionComment(item, GitHubDiscussionCommentKind.PullRequestReviewComment), cancellationToken).ConfigureAwait(false);
        var timeline = await FetchTimelineAsync($"{repository}/issues/{number}", cancellationToken).ConfigureAwait(false);
        return new(pull, issueComments.Concat(reviewComments).OrderBy(item => item.CreatedAt).ToArray(), timeline.Items,
            issueComments.HasMore || reviewComments.HasMore || timeline.HasMore);
    }

    private async Task<GitHubPage<GitHubTimelineEvent>> FetchTimelineAsync(string issuePath, CancellationToken token)
    {
        var result = await FetchPageAsync(issuePath + "/timeline", ParseTimelineEvent, token).ConfigureAwait(false);
        return new(result.Items.OfType<GitHubTimelineEvent>().ToArray(), result.HasMore);
    }

    public async Task<int> GetOpenPullRequestCountAsync(string slug, CancellationToken cancellationToken = default)
    {
        var result = await FetchPullRequestsAsync(slug, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.HasMore) throw new InvalidOperationException("Pull-request count exceeds the listing limit.");
        return result.Count;
    }

    public async Task<int> GetOpenIssueCountAsync(string slug, CancellationToken cancellationToken = default)
    {
        var result = await FetchIssuesAsync(slug, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.HasMore) throw new InvalidOperationException("Issue count exceeds the listing limit.");
        return result.Count;
    }

    internal static string RepositoryPath(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug) || !Regex.IsMatch(slug, @"\A[A-Za-z0-9][A-Za-z0-9-]*/[A-Za-z0-9_.-]+\z") ||
            slug.Split('/')[1] is "." or "..")
            throw new ArgumentException("Expected a GitHub owner/repository name.", nameof(slug));
        return "/repos/" + slug;
    }

    private static string ValidateState(string state) => state is "open" or "closed" or "all" ? state
        : throw new ArgumentException("State must be open, closed, or all.", nameof(state));
    private static int ValidateNumber(int number) => number > 0 ? number : throw new ArgumentOutOfRangeException(nameof(number));

    private async Task<GitHubPage<T>> FetchPageAsync<T>(string path, Func<JsonElement, T> parse, CancellationToken token,
        Func<JsonElement, bool>? include = null, string? arrayProperty = null)
    {
        var items = new List<T>();
        var hasMore = false;
        for (var page = 1; page <= MaxPagedRequestCount; page++)
        {
            using var response = await ReadJsonAsync($"{path}{(path.Contains('?') ? '&' : '?')}per_page=100&page={page}", token).ConfigureAwait(false);
            var root = response.Document.RootElement;
            if (arrayProperty is not null) root = root.GetProperty(arrayProperty);
            if (root.ValueKind != JsonValueKind.Array) throw new InvalidDataException("GitHub returned an invalid listing.");
            foreach (var item in root.EnumerateArray())
                if (include is null || include(item)) items.Add(parse(item));
            // Link is authoritative when supplied. A full page without Link is conservatively incomplete.
            hasMore = response.HasNext ?? root.GetArrayLength() >= 100;
            if (!hasMore) break;
        }
        return new(items, hasMore);
    }

    private async Task<JsonResponse> ReadJsonAsync(string path, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        await _concurrencyGate.WaitAsync(deadline.Token).ConfigureAwait(false);
        try
        {
            // Authentication discovery can start a bounded CLI process; never block the UI thread.
            var client = await Task.Run(() => _client.Value, deadline.Token).ConfigureAwait(false);
            if (Volatile.Read(ref _disposed) != 0)
            {
                if (_ownsHttpClient) client.Dispose();
                throw new OperationCanceledException(deadline.Token);
            }
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.UserAgent.ParseAdd("PowerForgeStudio/0.1");
            request.Headers.Add("X-GitHub-Api-Version", GitHubApiVersion);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var values) && int.TryParse(values.FirstOrDefault(), out var remaining))
                Interlocked.Exchange(ref _rateLimitRemaining, remaining);
            if (!response.IsSuccessStatusCode) throw new GitHubAccessException(response.StatusCode);
            if (response.Content.Headers.ContentLength > MaximumResponseBytes) throw new InvalidDataException("GitHub response exceeds the 4 MiB limit.");
            using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var bytes = new byte[16384];
            int read;
            while ((read = await stream.ReadAsync(bytes, deadline.Token).ConfigureAwait(false)) != 0)
            {
                if (buffer.Length + read > MaximumResponseBytes) throw new InvalidDataException("GitHub response exceeds the 4 MiB limit.");
                buffer.Write(bytes, 0, read);
            }
            bool? next = response.Headers.TryGetValues("Link", out var links)
                ? links.Any(link => link.Contains("rel=\"next\"", StringComparison.OrdinalIgnoreCase)) : null;
            return new(JsonDocument.Parse(buffer.ToArray()), next);
        }
        finally { _concurrencyGate.Release(); }
    }

    private sealed record JsonResponse(JsonDocument Document, bool? HasNext) : IDisposable
    {
        public void Dispose() => Document.Dispose();
    }
}
