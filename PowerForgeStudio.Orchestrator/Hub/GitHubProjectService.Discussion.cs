using System.Text.Json;
using PowerForgeStudio.Domain.Hub;

namespace PowerForgeStudio.Orchestrator.Hub;

public sealed partial class GitHubProjectService
{
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

}
