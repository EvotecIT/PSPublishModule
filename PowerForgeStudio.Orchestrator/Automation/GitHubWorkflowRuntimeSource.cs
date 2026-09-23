using System.Text.Json;
using PowerForgeStudio.Domain.Automation;
using PowerForgeStudio.Orchestrator.Hub;
using PowerForgeStudio.Orchestrator.Portfolio;

namespace PowerForgeStudio.Orchestrator.Automation;

/// <summary>Joins bounded GitHub workflow and scheduled-run observations to local cron definitions.</summary>
internal sealed class GitHubWorkflowRuntimeSource : IDisposable
{
    private const int MaximumRepositories = 20;
    private const int MaximumResponseBytes = 4 * 1024 * 1024;
    private readonly Lazy<HttpClient> _client;
    private readonly bool _ownsClient;
    private readonly Func<string, CancellationToken, Task<string?>> _resolveSlug;
    private int _disposed;

    internal GitHubWorkflowRuntimeSource(HttpClient? client = null,
        Func<string, CancellationToken, Task<string?>>? resolveSlug = null)
    {
        _client = new(() => client ?? GitHubHttpClientFactory.Create(TimeSpan.FromSeconds(15)));
        _ownsClient = client is null;
        _resolveSlug = resolveSlug ?? ResolveSlugAsync;
    }

    internal async Task<AutomationSourceResult> EnrichAsync(
        AutomationSourceResult local,
        CancellationToken cancellationToken)
    {
        if (local.Entries.Count == 0) return local;
        var roots = local.Entries.Select(static item => item.Id.Split('|')[0])
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .Take(MaximumRepositories + 1).ToArray();
        var partial = roots.Length > MaximumRepositories;
        var evidence = new Dictionary<string, RuntimeEvidence>(StringComparer.OrdinalIgnoreCase);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(65));
        using var concurrency = new SemaphoreSlim(4, 4);
        var results = await Task.WhenAll(roots.Take(MaximumRepositories).Select(async root =>
        {
            var entered = false;
            try
            {
                await concurrency.WaitAsync(deadline.Token).ConfigureAwait(false);
                entered = true;
                return await ReadRepositoryAsync(root, deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new RepositoryRuntime(root, [], true);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return new RepositoryRuntime(root, [], true);
            }
            finally { if (entered) concurrency.Release(); }
        })).ConfigureAwait(false);
        foreach (var result in results)
        {
            partial |= result.Partial;
            foreach (var item in result.Entries)
                evidence[$"{result.Root}|{item.Path}"] = item;
        }

        var enriched = local.Entries.Select(entry =>
        {
            var root = entry.Id.Split('|')[0];
            var path = Path.GetRelativePath(root, entry.SourcePath).Replace('\\', '/');
            if (!evidence.TryGetValue($"{root}|{path}", out var observed)) return entry;
            var enabled = string.Equals(observed.WorkflowState, "active", StringComparison.OrdinalIgnoreCase);
            var disabled = observed.WorkflowState.StartsWith("disabled", StringComparison.OrdinalIgnoreCase);
            var last = observed.Run;
            var failed = enabled && last?.Conclusion is ("failure" or "timed_out" or "action_required" or "startup_failure");
            var state = disabled ? "Disabled" : failed ? "Failed" : enabled ? "Enabled" : "Unknown";
            var lastResult = last is null ? "No scheduled run in latest 100 repository runs"
                : $"{last.Status}{(last.Conclusion is null ? "" : " · " + last.Conclusion)} · schedule · run #{last.RunNumber}";
            return entry with
            {
                State = state,
                LastRunAt = last?.CreatedAt,
                LastResult = lastResult,
                LatestRunUrl = last?.Url,
                HasRuntimeEvidence = true,
                IsEnabled = enabled,
                Detail = $"GitHub workflow state checked. {lastResult}. Next occurrence is not verified."
            };
        }).ToArray();
        var verified = enriched.Count(static entry => entry.HasRuntimeEvidence);
        partial |= local.State.State == "Partial";
        return new(enriched, new("GitHub Actions", partial ? "Partial" : verified > 0 ? "Runtime observed" : local.State.State,
            enriched.Length, $"{verified} local schedule definition(s) matched remote workflows. " +
            (partial ? "Some repositories or remote listings were unavailable or capped. " : "") +
            "Scheduled runs are bounded to the latest 100 repository runs; next occurrence is not inferred."));
    }

    private async Task<RepositoryRuntime> ReadRepositoryAsync(string root, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var slug = await _resolveSlug(root, token).ConfigureAwait(false);
        if (slug is null) return new(root, [], true);
        var repoPath = GitHubProjectService.RepositoryPath(slug);
        var client = await Task.Run(() => _client.Value, token).ConfigureAwait(false);
        if (Volatile.Read(ref _disposed) != 0)
        {
            if (_ownsClient) client.Dispose();
            throw new ObjectDisposedException(nameof(GitHubWorkflowRuntimeSource));
        }
        using var workflows = await ReadJsonAsync(client, $"{repoPath}/actions/workflows?per_page=100&page=1", token).ConfigureAwait(false);
        using var runs = await ReadJsonAsync(client, $"{repoPath}/actions/runs?event=schedule&per_page=100&page=1", token).ConfigureAwait(false);
        var workflowRoot = workflows.RootElement;
        var runRoot = runs.RootElement;
        var partial = workflowRoot.GetProperty("total_count").GetInt32() > 100 || runRoot.GetProperty("total_count").GetInt32() > 100;
        var latest = new Dictionary<long, ScheduledRun>();
        foreach (var item in runRoot.GetProperty("workflow_runs").EnumerateArray())
        {
            var id = item.GetProperty("workflow_id").GetInt64();
            var run = new ScheduledRun(
                item.TryGetProperty("status", out var status) ? status.GetString() ?? "Unknown" : "Unknown",
                item.TryGetProperty("conclusion", out var conclusion) && conclusion.ValueKind == JsonValueKind.String ? conclusion.GetString() : null,
                item.TryGetProperty("created_at", out var created) && created.TryGetDateTimeOffset(out var timestamp) ? timestamp : null,
                item.TryGetProperty("run_number", out var number) ? number.GetInt32() : 0,
                item.TryGetProperty("id", out var runId) && runId.TryGetInt64(out var value) && value > 0
                    ? $"https://github.com/{slug}/actions/runs/{value}" : null);
            if (!latest.TryGetValue(id, out var previous) || run.CreatedAt > previous.CreatedAt)
                latest[id] = run;
        }
        var entries = new List<RuntimeEvidence>();
        foreach (var item in workflowRoot.GetProperty("workflows").EnumerateArray())
        {
            var path = item.GetProperty("path").GetString();
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith(".github/workflows/", StringComparison.OrdinalIgnoreCase)) continue;
            var id = item.GetProperty("id").GetInt64();
            latest.TryGetValue(id, out var run);
            entries.Add(new(path.Replace('\\', '/'), item.GetProperty("state").GetString() ?? "Unknown", run));
        }
        return new(root, entries, partial);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpClient client, string path, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new GitHubAccessException(response.StatusCode);
        if (response.Content.Headers.ContentLength > MaximumResponseBytes)
            throw new InvalidDataException("GitHub workflow response exceeds the inventory limit.");
        await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var bytes = new byte[8192];
        int read;
        while ((read = await source.ReadAsync(bytes, token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaximumResponseBytes)
                throw new InvalidDataException("GitHub workflow response exceeds the inventory limit.");
            buffer.Write(bytes, 0, read);
        }
        return JsonDocument.Parse(buffer.ToArray());
    }

    private static async Task<string?> ResolveSlugAsync(string root, CancellationToken token)
    {
        var remote = await new GitRemoteResolver().ResolveOriginUrlAsync(root, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return GitHubInboxService.TryParseGitHubSlug(remote, out var owner, out var repo) ? owner + "/" + repo : null;
    }

    private sealed record RepositoryRuntime(string Root, IReadOnlyList<RuntimeEvidence> Entries, bool Partial);
    private sealed record RuntimeEvidence(string Path, string WorkflowState, ScheduledRun? Run);
    private sealed record ScheduledRun(string Status, string? Conclusion, DateTimeOffset? CreatedAt, int RunNumber, string? Url);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_ownsClient && _client.IsValueCreated) _client.Value.Dispose();
    }
}
