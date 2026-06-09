namespace PowerForgeStudio.Domain.Hub;

public sealed record ProjectScanSnapshot(
    ProjectEntry Entry,
    ProjectGitStatus? GitStatus,
    int? OpenPullRequestCount,
    int? OpenIssueCount,
    bool? LatestWorkflowFailed,
    string? LatestReleaseTag)
{
    public bool NeedsAttention =>
        (OpenPullRequestCount ?? 0) > 0
        || LatestWorkflowFailed == true
        || (GitStatus?.IsDirty ?? false);

    public string ActivitySummary
    {
        get
        {
            var parts = new List<string>();
            if (OpenPullRequestCount > 0)
            {
                parts.Add($"{OpenPullRequestCount} PR(s)");
            }

            if (OpenIssueCount > 0)
            {
                parts.Add($"{OpenIssueCount} issue(s)");
            }

            if (GitStatus?.IsDirty == true)
            {
                parts.Add("dirty");
            }

            if (LatestWorkflowFailed == true)
            {
                parts.Add("CI failing");
            }

            return parts.Count == 0 ? "No activity" : string.Join(", ", parts);
        }
    }
}
