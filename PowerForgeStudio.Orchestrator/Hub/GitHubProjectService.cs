using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using PowerForgeStudio.Domain.Hub;

namespace PowerForgeStudio.Orchestrator.Hub;

public sealed class GitHubProjectService : IDisposable
{
    private const int MaxPagedRequestCount = 5;
    private const string GitHubApiVersion = "2026-03-10";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _concurrencyGate;
    private readonly bool _ownsHttpClient;
    private volatile int _rateLimitRemaining = int.MaxValue;

    public GitHubProjectService()
        : this(GitHubHttpClientFactory.Create(), ownsHttpClient: true)
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
            for (var page = 1; page <= MaxPagedRequestCount; page++)
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

            TraceIfPagedResultMayBeTruncated("issues", $"{parts[0]}/{parts[1]}", issues.Count);
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
            for (var page = 1; page <= MaxPagedRequestCount; page++)
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

            TraceIfPagedResultMayBeTruncated("pull requests", $"{parts[0]}/{parts[1]}", pullRequests.Count);
            return pullRequests;
        }
        finally
        {
            _concurrencyGate.Release();
        }
    }

    public async Task<GitHubIssueDetail?> FetchIssueDetailAsync(
        string slug,
        int issueNumber,
        CancellationToken cancellationToken = default)
    {
        var parts = slug.Split('/');
        if (parts.Length != 2)
        {
            return null;
        }

        GitHubIssue issue;
        await _concurrencyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var request = CreateRequest($"/repos/{parts[0]}/{parts[1]}/issues/{issueNumber}");
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            TrackRateLimit(response);

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            issue = ParseIssue(document.RootElement);
        }
        finally
        {
            _concurrencyGate.Release();
        }

        var comments = await FetchIssueCommentsCoreAsync(parts[0], parts[1], issueNumber, cancellationToken).ConfigureAwait(false);
        var timelineEvents = await FetchTimelineEventsCoreAsync(parts[0], parts[1], issueNumber, cancellationToken).ConfigureAwait(false);
        return new GitHubIssueDetail(issue, comments, timelineEvents);
    }

    public async Task<GitHubPullRequestDetail?> FetchPullRequestDetailAsync(
        string slug,
        int pullRequestNumber,
        CancellationToken cancellationToken = default)
    {
        var parts = slug.Split('/');
        if (parts.Length != 2)
        {
            return null;
        }

        GitHubPullRequest pullRequest;
        await _concurrencyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var request = CreateRequest($"/repos/{parts[0]}/{parts[1]}/pulls/{pullRequestNumber}");
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            TrackRateLimit(response);

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            pullRequest = ParsePullRequest(document.RootElement);
        }
        finally
        {
            _concurrencyGate.Release();
        }

        var issueComments = await FetchIssueCommentsCoreAsync(parts[0], parts[1], pullRequestNumber, cancellationToken).ConfigureAwait(false);
        var reviewComments = await FetchPullRequestReviewCommentsCoreAsync(parts[0], parts[1], pullRequestNumber, cancellationToken).ConfigureAwait(false);
        var comments = issueComments
            .Concat(reviewComments)
            .OrderBy(comment => comment.CreatedAt)
            .ToList();
        var timelineEvents = await FetchTimelineEventsCoreAsync(parts[0], parts[1], pullRequestNumber, cancellationToken).ConfigureAwait(false);

        return new GitHubPullRequestDetail(pullRequest, comments, timelineEvents);
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
            for (var page = 1; page <= MaxPagedRequestCount; page++)
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

            TraceIfPagedResultMayBeTruncated("open pull requests", slug, total);
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
                : null,
            HtmlUrl: element.TryGetProperty("html_url", out var htmlUrl) ? htmlUrl.GetString() : null,
            BodyMarkdown: element.TryGetProperty("body", out var body) && body.ValueKind != JsonValueKind.Null
                ? body.GetString()
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
                : null,
            HtmlUrl: element.TryGetProperty("html_url", out var htmlUrl) ? htmlUrl.GetString() : null,
            BodyMarkdown: element.TryGetProperty("body", out var body) && body.ValueKind != JsonValueKind.Null
                ? body.GetString()
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

    private async Task<IReadOnlyList<GitHubDiscussionComment>> FetchIssueCommentsCoreAsync(
        string owner,
        string repository,
        int issueNumber,
        CancellationToken cancellationToken)
    {
        var comments = new List<GitHubDiscussionComment>();
        for (var page = 1; page <= MaxPagedRequestCount; page++)
        {
            using var request = CreateRequest($"/repos/{owner}/{repository}/issues/{issueNumber}/comments?per_page=100&page={page}");
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            TrackRateLimit(response);

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
            {
                return comments;
            }

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return comments;
            }

            foreach (var item in document.RootElement.EnumerateArray())
            {
                comments.Add(ParseDiscussionComment(item, GitHubDiscussionCommentKind.IssueComment));
            }

            if (document.RootElement.GetArrayLength() < 100)
            {
                break;
            }
        }

        TraceIfPagedResultMayBeTruncated("issue comments", $"{owner}/{repository}#{issueNumber}", comments.Count);
        return comments;
    }

    private async Task<IReadOnlyList<GitHubDiscussionComment>> FetchPullRequestReviewCommentsCoreAsync(
        string owner,
        string repository,
        int pullRequestNumber,
        CancellationToken cancellationToken)
    {
        var comments = new List<GitHubDiscussionComment>();
        for (var page = 1; page <= MaxPagedRequestCount; page++)
        {
            using var request = CreateRequest($"/repos/{owner}/{repository}/pulls/{pullRequestNumber}/comments?per_page=100&page={page}");
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            TrackRateLimit(response);

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
            {
                return comments;
            }

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return comments;
            }

            foreach (var item in document.RootElement.EnumerateArray())
            {
                comments.Add(ParseDiscussionComment(item, GitHubDiscussionCommentKind.PullRequestReviewComment));
            }

            if (document.RootElement.GetArrayLength() < 100)
            {
                break;
            }
        }

        TraceIfPagedResultMayBeTruncated("pull request review comments", $"{owner}/{repository}#{pullRequestNumber}", comments.Count);
        return comments;
    }

    private async Task<IReadOnlyList<GitHubTimelineEvent>> FetchTimelineEventsCoreAsync(
        string owner,
        string repository,
        int issueNumber,
        CancellationToken cancellationToken)
    {
        var events = new List<GitHubTimelineEvent>();
        for (var page = 1; page <= MaxPagedRequestCount; page++)
        {
            using var request = CreateRequest($"/repos/{owner}/{repository}/issues/{issueNumber}/timeline?per_page=100&page={page}");
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            TrackRateLimit(response);

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.Gone)
            {
                return events;
            }

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return events;
            }

            foreach (var item in document.RootElement.EnumerateArray())
            {
                var timelineEvent = ParseTimelineEvent(item);
                if (timelineEvent is not null)
                {
                    events.Add(timelineEvent);
                }
            }

            if (document.RootElement.GetArrayLength() < 100)
            {
                break;
            }
        }

        TraceIfPagedResultMayBeTruncated("timeline events", $"{owner}/{repository}#{issueNumber}", events.Count);
        return events;
    }

    private static GitHubDiscussionComment ParseDiscussionComment(JsonElement element, GitHubDiscussionCommentKind kind)
    {
        return new GitHubDiscussionComment(
            Id: element.TryGetProperty("id", out var id) ? id.GetInt64() : 0,
            Kind: kind,
            AuthorLogin: element.TryGetProperty("user", out var user)
                ? user.TryGetProperty("login", out var authorLogin) ? authorLogin.GetString() : null
                : null,
            BodyMarkdown: element.TryGetProperty("body", out var body) && body.ValueKind != JsonValueKind.Null
                ? body.GetString() ?? string.Empty
                : string.Empty,
            CreatedAt: GetCreatedAtOrNow(element),
            HtmlUrl: element.TryGetProperty("html_url", out var htmlUrl) ? htmlUrl.GetString() : null,
            Path: element.TryGetProperty("path", out var path) && path.ValueKind != JsonValueKind.Null
                ? path.GetString()
                : null,
            ParentCommentId: element.TryGetProperty("in_reply_to_id", out var inReplyToId) && inReplyToId.ValueKind != JsonValueKind.Null
                ? inReplyToId.GetInt64()
                : null,
            PullRequestReviewId: element.TryGetProperty("pull_request_review_id", out var reviewId) && reviewId.ValueKind != JsonValueKind.Null
                ? reviewId.GetInt64()
                : null,
            Line: element.TryGetProperty("line", out var line) && line.ValueKind != JsonValueKind.Null
                ? line.GetInt32()
                : null,
            StartLine: element.TryGetProperty("start_line", out var startLine) && startLine.ValueKind != JsonValueKind.Null
                ? startLine.GetInt32()
                : null,
            DiffHunk: element.TryGetProperty("diff_hunk", out var diffHunk) && diffHunk.ValueKind != JsonValueKind.Null
                ? diffHunk.GetString()
                : null);
    }

    private static GitHubTimelineEvent? ParseTimelineEvent(JsonElement element)
    {
        if (!element.TryGetProperty("event", out var eventProperty))
        {
            return null;
        }

        var eventName = eventProperty.GetString() ?? string.Empty;
        if (string.Equals(eventName, "commented", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var markdown = BuildTimelineEventMarkdown(element, eventName);
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return null;
        }

        return new GitHubTimelineEvent(
            Id: element.TryGetProperty("id", out var id) ? id.GetInt64() : 0,
            EventName: eventName,
            ActorLogin: GetUserLogin(element, "actor") ?? GetUserLogin(element, "user"),
            CreatedAt: GetCreatedAtOrNow(element),
            Markdown: markdown);
    }

    private static DateTimeOffset GetCreatedAtOrNow(JsonElement element)
    {
        if (element.TryGetProperty("created_at", out var createdAt) && createdAt.ValueKind == JsonValueKind.String)
        {
            return createdAt.GetDateTimeOffset();
        }

        return DateTimeOffset.UtcNow;
    }

    private static string BuildTimelineEventMarkdown(JsonElement element, string eventName)
    {
        var actor = GetUserLogin(element, "actor") ?? GetUserLogin(element, "user") ?? "Unknown actor";
        return eventName switch
        {
            "assigned" => $"{actor} assigned {FormatUser(GetUserLogin(element, "assignee"))}.",
            "unassigned" => $"{actor} unassigned {FormatUser(GetUserLogin(element, "assignee"))}.",
            "labeled" => $"{actor} added label {FormatLabel(element)}.",
            "unlabeled" => $"{actor} removed label {FormatLabel(element)}.",
            "closed" => $"{actor} closed this item.",
            "reopened" => $"{actor} reopened this item.",
            "merged" => $"{actor} merged this pull request.",
            "locked" => $"{actor} locked the conversation.",
            "unlocked" => $"{actor} unlocked the conversation.",
            "pinned" => $"{actor} pinned this item.",
            "unpinned" => $"{actor} unpinned this item.",
            "review_requested" => $"{actor} requested review from {FormatReviewer(element)}.",
            "review_request_removed" => $"{actor} removed the review request for {FormatReviewer(element)}.",
            "reviewed" => BuildReviewedMarkdown(element, actor),
            "ready_for_review" => $"{actor} marked this pull request ready for review.",
            "convert_to_draft" => $"{actor} converted this pull request to draft.",
            "renamed" => BuildRenamedMarkdown(element, actor),
            "cross-referenced" => BuildCrossReferenceMarkdown(element, actor, "referenced this from"),
            "connected" => BuildCrossReferenceMarkdown(element, actor, "linked this to"),
            "disconnected" => BuildCrossReferenceMarkdown(element, actor, "unlinked this from"),
            "referenced" => BuildCommitReferenceMarkdown(element, actor),
            "committed" => BuildCommitMarkdown(element, actor),
            "mentioned" => $"{actor} mentioned this item.",
            "subscribed" => $"{actor} subscribed to notifications.",
            "unsubscribed" => $"{actor} unsubscribed from notifications.",
            "milestoned" => $"{actor} added this to milestone {FormatMilestone(element)}.",
            "demilestoned" => $"{actor} removed this from milestone {FormatMilestone(element)}.",
            "head_ref_deleted" => $"{actor} deleted the pull request head branch.",
            "head_ref_restored" => $"{actor} restored the pull request head branch.",
            "head_ref_force_pushed" => $"{actor} force-pushed the pull request branch.",
            "base_ref_changed" => $"{actor} changed the base branch.",
            "automatic_base_change_succeeded" => $"{actor} changed the base branch automatically.",
            "automatic_base_change_failed" => $"{actor} attempted an automatic base branch change, but it failed.",
            "marked_as_duplicate" => $"{actor} marked this as duplicate.",
            "unmarked_as_duplicate" => $"{actor} removed the duplicate marker.",
            "converted_to_discussion" => $"{actor} converted this issue to a discussion.",
            _ => $"{actor} triggered `{eventName}`."
        };
    }

    private static string BuildReviewedMarkdown(JsonElement element, string actor)
    {
        var state = element.TryGetProperty("state", out var stateElement) && stateElement.ValueKind == JsonValueKind.String
            ? stateElement.GetString()
            : null;
        var body = element.TryGetProperty("body", out var bodyElement) && bodyElement.ValueKind == JsonValueKind.String
            ? bodyElement.GetString()
            : null;

        var summary = string.IsNullOrWhiteSpace(state)
            ? $"{actor} submitted a review."
            : $"{actor} submitted a `{state}` review.";

        if (string.IsNullOrWhiteSpace(body))
        {
            return summary;
        }

        return summary + Environment.NewLine + Environment.NewLine + body!.Trim();
    }

    private static string BuildRenamedMarkdown(JsonElement element, string actor)
    {
        if (!element.TryGetProperty("rename", out var rename) || rename.ValueKind != JsonValueKind.Object)
        {
            return $"{actor} renamed this item.";
        }

        var from = rename.TryGetProperty("from", out var fromElement) ? fromElement.GetString() : null;
        var to = rename.TryGetProperty("to", out var toElement) ? toElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
        {
            return $"{actor} renamed this item.";
        }

        return $"{actor} renamed this from **{from!.Trim()}** to **{to!.Trim()}**.";
    }

    private static string BuildCrossReferenceMarkdown(JsonElement element, string actor, string prefix)
    {
        if (!element.TryGetProperty("source", out var source) || source.ValueKind != JsonValueKind.Object)
        {
            return $"{actor} {prefix} another item.";
        }

        if (!source.TryGetProperty("issue", out var issue) || issue.ValueKind != JsonValueKind.Object)
        {
            return $"{actor} {prefix} another item.";
        }

        var number = issue.TryGetProperty("number", out var numberElement) ? numberElement.GetInt32() : 0;
        var title = issue.TryGetProperty("title", out var titleElement) ? titleElement.GetString() : null;
        var htmlUrl = issue.TryGetProperty("html_url", out var htmlUrlElement) ? htmlUrlElement.GetString() : null;
        var linked = string.IsNullOrWhiteSpace(htmlUrl)
            ? $"#{number}"
            : $"[#{number}](<{htmlUrl}>)";

        return string.IsNullOrWhiteSpace(title)
            ? $"{actor} {prefix} {linked}."
            : $"{actor} {prefix} {linked} ({title!.Trim()}).";
    }

    private static string BuildCommitReferenceMarkdown(JsonElement element, string actor)
    {
        var commitId = element.TryGetProperty("commit_id", out var commitIdElement) ? commitIdElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(commitId))
        {
            return $"{actor} referenced this from a commit.";
        }

        var shortCommit = commitId!.Length > 7 ? commitId[..7] : commitId;
        return $"{actor} referenced this from commit `{shortCommit}`.";
    }

    private static string BuildCommitMarkdown(JsonElement element, string actor)
    {
        var commitId = element.TryGetProperty("sha", out var shaElement) && shaElement.ValueKind == JsonValueKind.String
            ? shaElement.GetString()
            : element.TryGetProperty("commit_id", out var commitIdElement) ? commitIdElement.GetString() : null;
        var shortCommit = string.IsNullOrWhiteSpace(commitId)
            ? "a commit"
            : $"`{(commitId!.Length > 7 ? commitId[..7] : commitId)}`";

        var message = element.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String
            ? messageElement.GetString()
            : null;

        return string.IsNullOrWhiteSpace(message)
            ? $"{actor} pushed {shortCommit}."
            : $"{actor} pushed {shortCommit}: {message!.Trim()}";
    }

    private static string FormatUser(string? login)
        => string.IsNullOrWhiteSpace(login) ? "an unknown user" : $"@{login.Trim()}";

    private static string FormatReviewer(JsonElement element)
    {
        var user = GetUserLogin(element, "requested_reviewer");
        if (!string.IsNullOrWhiteSpace(user))
        {
            return $"@{user.Trim()}";
        }

        if (element.TryGetProperty("requested_team", out var team) && team.ValueKind == JsonValueKind.Object)
        {
            var name = team.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            if (!string.IsNullOrWhiteSpace(name))
            {
                return $"team **{name.Trim()}**";
            }
        }

        return "an unknown reviewer";
    }

    private static string FormatLabel(JsonElement element)
    {
        if (element.TryGetProperty("label", out var label) && label.ValueKind == JsonValueKind.Object)
        {
            var name = label.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            if (!string.IsNullOrWhiteSpace(name))
            {
                return $"`{name.Trim()}`";
            }
        }

        return "an unknown label";
    }

    private static string FormatMilestone(JsonElement element)
    {
        if (element.TryGetProperty("milestone", out var milestone) && milestone.ValueKind == JsonValueKind.Object)
        {
            var title = milestone.TryGetProperty("title", out var titleElement) ? titleElement.GetString() : null;
            if (!string.IsNullOrWhiteSpace(title))
            {
                return $"**{title.Trim()}**";
            }
        }

        return "an unknown milestone";
    }

    private static string? GetUserLogin(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return property.TryGetProperty("login", out var login) && login.ValueKind == JsonValueKind.String
            ? login.GetString()
            : null;
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

    private static void TraceIfPagedResultMayBeTruncated(string entityName, string context, int itemCount)
    {
        if (itemCount < MaxPagedRequestCount * 100)
        {
            return;
        }

        Trace.TraceWarning($"GitHub {entityName} for {context} may be truncated at {itemCount} items.");
    }

    private HttpRequestMessage CreateRequest(string relativePath)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, relativePath);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("PowerForgeStudio/0.1");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", GitHubApiVersion);
        return request;
    }

    // HttpClient creation and token resolution delegated to GitHubHttpClientFactory
}
