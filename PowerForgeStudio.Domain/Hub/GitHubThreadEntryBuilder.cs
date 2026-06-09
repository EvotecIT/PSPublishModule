using System.Text;

namespace PowerForgeStudio.Domain.Hub;

public static class GitHubThreadEntryBuilder
{
    public static IReadOnlyList<GitHubThreadEntry> BuildIssueEntries(GitHubIssue issue, GitHubIssueDetail? detail)
    {
        var entries = new List<GitHubThreadEntry>
        {
            new(
                GitHubThreadEntryKind.Description,
                "Issue description",
                issue.AuthorLogin,
                issue.CreatedAt,
                string.IsNullOrWhiteSpace(issue.BodyMarkdown) ? "_No description provided._" : issue.BodyMarkdown!,
                issue.HtmlUrl)
        };

        if (detail is null)
        {
            return entries;
        }

        entries.AddRange(detail.Comments
            .OrderBy(comment => comment.CreatedAt)
            .Select(comment => new GitHubThreadEntry(
                GitHubThreadEntryKind.Comment,
                "Comment",
                comment.AuthorLogin,
                comment.CreatedAt,
                string.IsNullOrWhiteSpace(comment.BodyMarkdown) ? "_No comment body provided._" : comment.BodyMarkdown,
                comment.HtmlUrl,
                comment.Path)));

        entries.AddRange(detail.TimelineEvents
            .OrderBy(timelineEvent => timelineEvent.CreatedAt)
            .Select(timelineEvent => new GitHubThreadEntry(
                GitHubThreadEntryKind.TimelineEvent,
                timelineEvent.EventName.Replace('_', ' '),
                timelineEvent.ActorLogin,
                timelineEvent.CreatedAt,
                timelineEvent.Markdown)));

        return entries
            .OrderBy(entry => entry.CreatedAt)
            .ThenBy(entry => entry.Kind == GitHubThreadEntryKind.Description ? 0 : 1)
            .ToList();
    }

    public static IReadOnlyList<GitHubThreadEntry> BuildPullRequestEntries(GitHubPullRequest pullRequest, GitHubPullRequestDetail? detail)
    {
        var entries = new List<GitHubThreadEntry>
        {
            new(
                GitHubThreadEntryKind.Description,
                "Pull request description",
                pullRequest.AuthorLogin,
                pullRequest.CreatedAt,
                string.IsNullOrWhiteSpace(pullRequest.BodyMarkdown) ? "_No description provided._" : pullRequest.BodyMarkdown!,
                pullRequest.HtmlUrl)
        };

        if (detail is null)
        {
            return entries;
        }

        entries.AddRange(detail.Comments
            .Where(comment => comment.Kind == GitHubDiscussionCommentKind.IssueComment)
            .OrderBy(comment => comment.CreatedAt)
            .Select(comment => new GitHubThreadEntry(
                GitHubThreadEntryKind.Comment,
                "Comment",
                comment.AuthorLogin,
                comment.CreatedAt,
                string.IsNullOrWhiteSpace(comment.BodyMarkdown) ? "_No comment body provided._" : comment.BodyMarkdown,
                comment.HtmlUrl)));

        entries.AddRange(BuildReviewThreadEntries(detail.Comments));

        entries.AddRange(detail.TimelineEvents
            .OrderBy(timelineEvent => timelineEvent.CreatedAt)
            .Select(timelineEvent => new GitHubThreadEntry(
                GitHubThreadEntryKind.TimelineEvent,
                timelineEvent.EventName.Replace('_', ' '),
                timelineEvent.ActorLogin,
                timelineEvent.CreatedAt,
                timelineEvent.Markdown)));

        return entries
            .OrderBy(entry => entry.CreatedAt)
            .ThenBy(entry => entry.Kind == GitHubThreadEntryKind.Description ? 0 : 1)
            .ToList();
    }

    private static IReadOnlyList<GitHubThreadEntry> BuildReviewThreadEntries(IReadOnlyList<GitHubDiscussionComment> comments)
    {
        var reviewComments = comments
            .Where(comment => comment.Kind == GitHubDiscussionCommentKind.PullRequestReviewComment)
            .OrderBy(comment => comment.CreatedAt)
            .ToList();
        if (reviewComments.Count == 0)
        {
            return [];
        }

        var byId = reviewComments.ToDictionary(comment => comment.Id);
        var repliesByParent = reviewComments
            .Where(comment => comment.ParentCommentId is not null)
            .GroupBy(comment => comment.ParentCommentId!.Value)
            .ToDictionary(group => group.Key, group => group.OrderBy(comment => comment.CreatedAt).ToList());

        var roots = reviewComments
            .Where(comment => comment.ParentCommentId is null || !byId.ContainsKey(comment.ParentCommentId.Value))
            .ToList();

        return roots.Select(root => new GitHubThreadEntry(
            GitHubThreadEntryKind.ReviewThread,
            "Review thread",
            root.AuthorLogin,
            root.CreatedAt,
            BuildReviewThreadMarkdown(root, FlattenReplies(root.Id, repliesByParent)),
            root.HtmlUrl,
            root.Path))
            .ToList();
    }

    private static IReadOnlyList<GitHubDiscussionComment> FlattenReplies(
        long parentCommentId,
        IReadOnlyDictionary<long, List<GitHubDiscussionComment>> repliesByParent)
    {
        if (!repliesByParent.TryGetValue(parentCommentId, out var directReplies))
        {
            return [];
        }

        var replies = new List<GitHubDiscussionComment>();
        foreach (var reply in directReplies)
        {
            replies.Add(reply);
            replies.AddRange(FlattenReplies(reply.Id, repliesByParent));
        }

        return replies;
    }

    private static string BuildReviewThreadMarkdown(
        GitHubDiscussionComment root,
        IReadOnlyList<GitHubDiscussionComment> replies)
    {
        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(root.Path))
        {
            builder.Append("File: `");
            builder.Append(root.Path.Trim());
            builder.Append('`');
            if (root.StartLine is not null || root.Line is not null)
            {
                builder.Append(" on line ");
                builder.Append(root.StartLine is not null && root.Line is not null && root.StartLine != root.Line
                    ? $"{root.StartLine}-{root.Line}"
                    : (root.Line ?? root.StartLine)!.Value.ToString());
            }

            builder.AppendLine();
            builder.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(root.DiffHunk))
        {
            builder.AppendLine("```diff");
            builder.AppendLine(root.DiffHunk.Trim());
            builder.AppendLine("```");
            builder.AppendLine();
        }

        builder.AppendLine(string.IsNullOrWhiteSpace(root.BodyMarkdown)
            ? "_No comment body provided._"
            : root.BodyMarkdown.Trim());

        foreach (var reply in replies)
        {
            builder.AppendLine();
            builder.Append("**Reply from ");
            builder.Append(reply.AuthorLogin ?? "Unknown author");
            builder.Append(" on ");
            builder.Append(reply.CreatedAt.ToString("yyyy-MM-dd HH:mm"));
            builder.AppendLine("**");
            builder.AppendLine();
            builder.AppendLine(string.IsNullOrWhiteSpace(reply.BodyMarkdown)
                ? "_No reply body provided._"
                : reply.BodyMarkdown.Trim());
        }

        return builder.ToString().Trim();
    }
}
