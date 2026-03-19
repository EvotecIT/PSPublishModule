using System.Text;

namespace PowerForgeStudio.Domain.Hub;

public static class GitHubDiscussionMarkdownBuilder
{
    public static string BuildIssueThread(GitHubIssue issue, GitHubIssueDetail? detail)
        => BuildThread(GitHubThreadEntryBuilder.BuildIssueEntries(issue, detail));

    public static string BuildPullRequestThread(GitHubPullRequest pullRequest, GitHubPullRequestDetail? detail)
        => BuildThread(GitHubThreadEntryBuilder.BuildPullRequestEntries(pullRequest, detail));

    private static string BuildThread(IReadOnlyList<GitHubThreadEntry> entries)
    {
        if (entries.Count == 0)
        {
            return "_No discussion available._";
        }

        var builder = new StringBuilder();
        var first = entries[0];
        builder.AppendLine(string.IsNullOrWhiteSpace(first.Markdown)
            ? "_No description provided._"
            : first.Markdown.Trim());

        if (entries.Count == 1)
        {
            return builder.ToString().Trim();
        }

        builder.AppendLine();
        builder.AppendLine("---");
        builder.AppendLine();
        builder.AppendLine("## Discussion");

        foreach (var entry in entries.Skip(1))
        {
            builder.AppendLine();
            builder.Append("### ");
            builder.Append(entry.AuthorLogin ?? "Unknown actor");
            builder.Append(entry.Kind == GitHubThreadEntryKind.TimelineEvent ? " activity on " : " commented on ");
            builder.AppendLine(entry.CreatedAt.ToString("yyyy-MM-dd HH:mm"));

            if (!string.IsNullOrWhiteSpace(entry.Path))
            {
                builder.Append("Path: `");
                builder.Append(entry.Path.Trim());
                builder.AppendLine("`");
                builder.AppendLine();
            }

            builder.AppendLine(string.IsNullOrWhiteSpace(entry.Markdown)
                ? "_No activity details provided._"
                : entry.Markdown.Trim());
        }

        return builder.ToString().Trim();
    }
}
