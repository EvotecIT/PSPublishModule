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

        var builder = new MarkdownThreadBuilder();
        var first = entries[0];
        builder.Paragraph(string.IsNullOrWhiteSpace(first.Markdown)
            ? "_No description provided._"
            : first.Markdown.Trim());

        if (entries.Count == 1)
        {
            return builder.ToString().Trim();
        }

        builder.BlankLine();
        builder.HorizontalRule();
        builder.BlankLine();
        builder.Heading(2, "Discussion");

        foreach (var entry in entries.Skip(1))
        {
            builder.BlankLine();
            builder.Heading(3, $"{entry.AuthorLogin ?? "Unknown actor"}{(entry.Kind == GitHubThreadEntryKind.TimelineEvent ? " activity on " : " commented on ")}{entry.CreatedAt:yyyy-MM-dd HH:mm}");

            if (!string.IsNullOrWhiteSpace(entry.Path))
            {
                builder.InlineLine("Path: `", entry.Path.Trim(), "`");
                builder.BlankLine();
            }

            builder.Paragraph(string.IsNullOrWhiteSpace(entry.Markdown)
                ? "_No activity details provided._"
                : entry.Markdown.Trim());
        }

        return builder.ToString().Trim();
    }
}
