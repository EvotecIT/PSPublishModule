using PowerForgeStudio.Domain.Portfolio;

namespace PowerForgeStudio.Orchestrator.Portfolio;

public sealed class RepositoryGitQuickActionService
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public IReadOnlyList<RepositoryGitQuickAction> BuildActions(
        RepositoryPortfolioItem? repository,
        IReadOnlyList<RepositoryGitRemediationStep> remediationSteps)
    {
        if (repository is null)
        {
            return [];
        }

        var actions = new List<RepositoryGitQuickAction>();
        var gitHubInbox = repository.GitHubInbox;
        var repositorySlug = gitHubInbox?.RepositorySlug;

        if (!string.IsNullOrWhiteSpace(repositorySlug))
        {
            actions.Add(new RepositoryGitQuickAction(
                Title: "Open Pull Requests",
                Summary: "Review open pull requests and branch policy on GitHub.",
                Kind: RepositoryGitQuickActionKind.BrowserUrl,
                Payload: $"https://github.com/{repositorySlug}/pulls",
                ExecuteLabel: "Open PRs",
                IsPrimary: true));

            var probedBranch = gitHubInbox?.ProbedBranch;
            if (!string.IsNullOrWhiteSpace(probedBranch))
            {
                actions.Add(new RepositoryGitQuickAction(
                    Title: "Open Branch on GitHub",
                    Summary: "Inspect the current branch, recent commits, and checks on GitHub.",
                    Kind: RepositoryGitQuickActionKind.BrowserUrl,
                    Payload: $"https://github.com/{repositorySlug}/tree/{Uri.EscapeDataString(probedBranch)}",
                    ExecuteLabel: "Open Branch"));
            }

            if (!string.IsNullOrWhiteSpace(gitHubInbox?.DefaultBranch)
                && !string.IsNullOrWhiteSpace(gitHubInbox?.ProbedBranch)
                && !Comparer.Equals(gitHubInbox.DefaultBranch, gitHubInbox.ProbedBranch))
            {
                actions.Add(new RepositoryGitQuickAction(
                    Title: "Open Compare View",
                    Summary: "Open the GitHub compare screen for the current branch against the default branch.",
                    Kind: RepositoryGitQuickActionKind.BrowserUrl,
                    Payload: $"https://github.com/{repositorySlug}/compare/{Uri.EscapeDataString(gitHubInbox.DefaultBranch)}...{Uri.EscapeDataString(gitHubInbox.ProbedBranch)}",
                    ExecuteLabel: "Open Compare",
                    IsPrimary: true));
            }
        }

        foreach (var step in remediationSteps)
        {
            if (!CanExecuteDirectly(step.CommandText))
            {
                continue;
            }

            actions.Add(new RepositoryGitQuickAction(
                Title: step.Title,
                Summary: step.Summary,
                Kind: RepositoryGitQuickActionKind.GitCommand,
                Payload: step.CommandText,
                ExecuteLabel: "Run Here",
                IsPrimary: step.IsPrimary));
        }

        return actions
            .GroupBy(action => $"{action.Kind}|{action.Payload}", Comparer)
            .Select(group => group
                .OrderByDescending(action => action.IsPrimary)
                .ThenBy(action => action.Title, Comparer)
                .First())
            .OrderByDescending(action => action.IsPrimary)
            .ThenBy(action => action.Kind)
            .ThenBy(action => action.Title, Comparer)
            .ToArray();
    }

    private static bool CanExecuteDirectly(string commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return false;
        }

        return commandText.StartsWith("git status", StringComparison.OrdinalIgnoreCase)
               || commandText.StartsWith("git rev-parse", StringComparison.OrdinalIgnoreCase)
               || commandText.StartsWith("git pull --rebase", StringComparison.OrdinalIgnoreCase)
               || commandText.StartsWith("git switch -c ", StringComparison.OrdinalIgnoreCase);
    }
}
