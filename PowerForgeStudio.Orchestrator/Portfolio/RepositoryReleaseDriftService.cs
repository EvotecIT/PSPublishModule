using PowerForgeStudio.Domain.Portfolio;

namespace PowerForgeStudio.Orchestrator.Portfolio;

public sealed class RepositoryReleaseDriftService
{
    public IReadOnlyList<RepositoryPortfolioItem> PopulateReleaseDrift(IReadOnlyList<RepositoryPortfolioItem> items)
    {
        return items
            .Select(item => item with {
                ReleaseDrift = Assess(item)
            })
            .ToArray();
    }

    private static RepositoryReleaseDrift Assess(RepositoryPortfolioItem item)
    {
        var inbox = item.GitHubInbox;
        if (inbox is null || inbox.Status == RepositoryGitHubInboxStatus.NotProbed)
        {
            return new RepositoryReleaseDrift(
                RepositoryReleaseDriftStatus.Unknown,
                "Release drift not assessed.",
                "GitHub inbox data is not available yet, so release drift cannot be compared safely.");
        }

        if (inbox.Status == RepositoryGitHubInboxStatus.Unavailable)
        {
            return new RepositoryReleaseDrift(
                RepositoryReleaseDriftStatus.Unknown,
                "Release drift unavailable.",
                inbox.Detail);
        }

        if (item.Git.IsDirty)
        {
            return new RepositoryReleaseDrift(
                RepositoryReleaseDriftStatus.Attention,
                "Local changes exist beyond the latest release signal.",
                BuildDetail(item, inbox, "The local workspace is dirty, so it has already drifted away from the last known release boundary."));
        }

        if (item.Git.AheadCount > 0)
        {
            return new RepositoryReleaseDrift(
                RepositoryReleaseDriftStatus.Attention,
                $"Branch is ahead of the latest release signal by {item.Git.AheadCount} commit(s).",
                BuildDetail(item, inbox, $"Current branch is ahead of upstream by {item.Git.AheadCount} commit(s), which usually means release work has advanced beyond {inbox.LatestReleaseTag ?? "the last detected GitHub release"}."));
        }

        if ((inbox.OpenPullRequestCount ?? 0) > 0)
        {
            return new RepositoryReleaseDrift(
                RepositoryReleaseDriftStatus.Attention,
                $"{inbox.OpenPullRequestCount} open PR(s) suggest unreleased work is waiting.",
                BuildDetail(item, inbox, "Open pull requests indicate release-adjacent work is still moving through review."));
        }

        if (string.IsNullOrWhiteSpace(inbox.LatestReleaseTag))
        {
            return new RepositoryReleaseDrift(
                RepositoryReleaseDriftStatus.Attention,
                "No GitHub release tag detected yet.",
                BuildDetail(item, inbox, "The repository looks clean locally, but no latest GitHub release tag was detected to anchor drift."));
        }

        return new RepositoryReleaseDrift(
            RepositoryReleaseDriftStatus.Aligned,
            $"No release drift detected past {inbox.LatestReleaseTag}.",
            BuildDetail(item, inbox, "Local git state and lightweight GitHub signals are aligned with the latest detected release boundary."));
    }

    private static string BuildDetail(RepositoryPortfolioItem item, RepositoryGitHubInbox inbox, string firstSentence)
    {
        var parts = new List<string> {
            firstSentence
        };

        if (!string.IsNullOrWhiteSpace(inbox.RepositorySlug))
        {
            parts.Add($"GitHub repo: {inbox.RepositorySlug}.");
        }

        if (!string.IsNullOrWhiteSpace(inbox.LatestReleaseTag))
        {
            parts.Add($"Latest detected release tag: {inbox.LatestReleaseTag}.");
        }

        if (inbox.LatestWorkflowFailed == true)
        {
            parts.Add("The latest workflow run also reported a failure.");
        }

        if (item.Git.BehindCount > 0)
        {
            parts.Add($"Local branch is behind upstream by {item.Git.BehindCount} commit(s).");
        }

        return string.Join(" ", parts);
    }
}
