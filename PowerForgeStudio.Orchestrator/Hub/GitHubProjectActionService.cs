using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using PowerForgeStudio.Domain.Hub;

namespace PowerForgeStudio.Orchestrator.Hub;

/// <summary>Bounded GitHub mutations with target-state and exact-PR-head preflight checks.</summary>
public sealed class GitHubProjectActionService : IGitHubProjectActionService, IDisposable
{
    private const int MaximumResponseBytes = 512 * 1024;
    private const int MaximumBodyCharacters = 65_536;
    private const string GitHubApiVersion = "2026-03-10";
    private readonly Lazy<HttpClient> _client;
    private readonly bool _ownsClient;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _disposed;

    public GitHubProjectActionService()
    {
        _client = new(() => GitHubHttpClientFactory.Create(TimeSpan.FromSeconds(30)));
        _ownsClient = true;
    }

    internal GitHubProjectActionService(HttpClient client, bool ownsClient = false)
    {
        _client = new(() => client);
        _ownsClient = ownsClient;
    }

    public async Task<GitHubProjectActionReceipt> ExecuteAsync(
        GitHubProjectActionPlan plan,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        Validate(plan);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        await _gate.WaitAsync(deadline.Token).ConfigureAwait(false);
        try
        {
            var client = await Task.Run(() => _client.Value, deadline.Token).ConfigureAwait(false);
            if (Volatile.Read(ref _disposed) != 0)
            {
                if (_ownsClient) client.Dispose();
                throw new ObjectDisposedException(nameof(GitHubProjectActionService));
            }
            var repository = GitHubProjectService.RepositoryPath(plan.RepositorySlug);
            if (plan.IsPullRequest)
            {
                var currentHead = await ReadPropertyAsync(
                    client,
                    $"{repository}/pulls/{plan.Number}",
                    static root => root.GetProperty("head").GetProperty("sha").GetString(),
                    deadline.Token).ConfigureAwait(false);
                if (!string.Equals(currentHead, plan.ExpectedHeadSha, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The pull-request head changed after review. Reload the PR before submitting an action.");
            }
            else
            {
                var issue = await ReadPropertyAsync(
                    client,
                    $"{repository}/issues/{plan.Number}",
                    static root => new IssueEvidence(
                        root.GetProperty("state").GetString(),
                        root.TryGetProperty("pull_request", out _)),
                    deadline.Token).ConfigureAwait(false);
                if (issue?.IsPullRequest == true)
                    throw new InvalidOperationException("The reviewed issue target now resolves to a pull request. Reload it before submitting an action.");
                if (!string.Equals(issue?.State, plan.ExpectedState, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The issue state changed after review. Reload the issue before submitting an action.");
            }

            var (method, path, payload) = BuildRequest(repository, plan);
            using var request = CreateRequest(method, path, payload);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new GitHubAccessException(response.StatusCode);
            // A successful status is terminal mutation evidence. Optional response metadata must never
            // turn an accepted write into a retryable failure and duplicate a comment or review.
            return new(plan.Kind, plan.RepositorySlug, plan.Number, plan.IsPullRequest, null, DateTimeOffset.UtcNow);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<T?> ReadPropertyAsync<T>(HttpClient client, string path, Func<JsonElement, T?> selector, CancellationToken token)
    {
        using var request = CreateRequest(HttpMethod.Get, path, null);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new GitHubAccessException(response.StatusCode);
        using var document = await ReadDocumentAsync(response, token).ConfigureAwait(false);
        try { return selector(document.RootElement); }
        catch (KeyNotFoundException) { throw new InvalidDataException("GitHub returned incomplete target evidence."); }
        catch (InvalidOperationException) { throw new InvalidDataException("GitHub returned invalid target evidence."); }
    }

    private static (HttpMethod Method, string Path, object Payload) BuildRequest(string repository, GitHubProjectActionPlan plan)
        => plan.Kind switch
        {
            GitHubProjectActionKind.Comment => (HttpMethod.Post, $"{repository}/issues/{plan.Number}/comments", new { body = plan.Body }),
            GitHubProjectActionKind.CloseIssue => (HttpMethod.Patch, $"{repository}/issues/{plan.Number}", new { state = "closed" }),
            GitHubProjectActionKind.ReopenIssue => (HttpMethod.Patch, $"{repository}/issues/{plan.Number}", new { state = "open" }),
            GitHubProjectActionKind.ApprovePullRequest => (HttpMethod.Post, $"{repository}/pulls/{plan.Number}/reviews",
                new { commit_id = plan.ExpectedHeadSha, body = plan.Body, @event = "APPROVE" }),
            GitHubProjectActionKind.RequestPullRequestChanges => (HttpMethod.Post, $"{repository}/pulls/{plan.Number}/reviews",
                new { commit_id = plan.ExpectedHeadSha, body = plan.Body, @event = "REQUEST_CHANGES" }),
            _ => throw new ArgumentOutOfRangeException(nameof(plan))
        };

    private static HttpRequestMessage CreateRequest(HttpMethod method, string path, object? payload)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", GitHubApiVersion);
        if (payload is not null) request.Content = JsonContent.Create(payload);
        return request;
    }

    private static async Task<JsonDocument> ReadDocumentAsync(HttpResponseMessage response, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength > MaximumResponseBytes)
            throw new InvalidDataException("GitHub response exceeds the action-response limit.");
        await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var bytes = new byte[8192];
        int read;
        while ((read = await source.ReadAsync(bytes, token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaximumResponseBytes)
                throw new InvalidDataException("GitHub response exceeds the action-response limit.");
            buffer.Write(bytes, 0, read);
        }
        return JsonDocument.Parse(buffer.ToArray());
    }

    private static void Validate(GitHubProjectActionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _ = GitHubProjectService.RepositoryPath(plan.RepositorySlug);
        if (plan.Number <= 0) throw new ArgumentOutOfRangeException(nameof(plan));
        if (plan.Body.Length > MaximumBodyCharacters) throw new ArgumentException("GitHub action text cannot exceed 65,536 characters.", nameof(plan));
        var requiresBody = plan.Kind is GitHubProjectActionKind.Comment or GitHubProjectActionKind.RequestPullRequestChanges;
        if (requiresBody && string.IsNullOrWhiteSpace(plan.Body)) throw new ArgumentException("This GitHub action requires text.", nameof(plan));
        if (plan.IsPullRequest)
        {
            if (plan.Kind is GitHubProjectActionKind.CloseIssue or GitHubProjectActionKind.ReopenIssue)
                throw new ArgumentException("Issue state actions cannot target a pull request.", nameof(plan));
            if (!Regex.IsMatch(plan.ExpectedHeadSha ?? "", @"\A(?:[a-fA-F0-9]{40}|[a-fA-F0-9]{64})\z"))
                throw new ArgumentException("A full reviewed PR head SHA is required.", nameof(plan));
        }
        else
        {
            if (plan.Kind is GitHubProjectActionKind.ApprovePullRequest or GitHubProjectActionKind.RequestPullRequestChanges)
                throw new ArgumentException("Pull-request reviews cannot target an issue.", nameof(plan));
            if (plan.ExpectedState is not ("open" or "closed"))
                throw new ArgumentException("The observed issue state must be open or closed.", nameof(plan));
            if (plan.Kind == GitHubProjectActionKind.CloseIssue && plan.ExpectedState != "open")
                throw new ArgumentException("Closing requires a reviewed open issue.", nameof(plan));
            if (plan.Kind == GitHubProjectActionKind.ReopenIssue && plan.ExpectedState != "closed")
                throw new ArgumentException("Reopening requires a reviewed closed issue.", nameof(plan));
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_ownsClient && _client.IsValueCreated) _client.Value.Dispose();
    }

    private sealed record IssueEvidence(string? State, bool IsPullRequest);
}
