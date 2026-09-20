using System.Text.Json;
using PowerForgeStudio.Domain.Hub;

namespace PowerForgeStudio.Orchestrator.Hub;

public sealed partial class GitHubProjectService
{
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
            ReviewStatus: GitHubPrReviewStatus.Unknown,
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
                : null,
            HeadSha: element.TryGetProperty("head", out var headObject) && headObject.TryGetProperty("sha", out var sha) ? sha.GetString() : null);
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

}
