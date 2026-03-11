using System.Text;
using PowerForgeStudio.Domain.Portfolio;

namespace PowerForgeStudio.Orchestrator.Portfolio;

public sealed class RepositoryGitRemediationService
{
    public IReadOnlyList<RepositoryGitRemediationStep> BuildSteps(RepositoryPortfolioItem? repository)
    {
        if (repository is null)
        {
            return [
                new RepositoryGitRemediationStep(
                    Title: "Select a repository",
                    Summary: "Choose a managed repository to see git remediation guidance and exact command suggestions.",
                    CommandText: "git status --short --branch")
            ];
        }

        var steps = new List<RepositoryGitRemediationStep>();
        var branchName = NormalizeBranch(repository.Git.BranchName);
        var repoSlug = SanitizeBranchSegment(repository.Name);
        var suggestedBranch = string.IsNullOrWhiteSpace(branchName) || IsBaseBranch(branchName)
            ? $"codex/{repoSlug}-release-flow"
            : branchName;

        steps.Add(new RepositoryGitRemediationStep(
            Title: "Inspect current git state",
            Summary: "Start by confirming the branch, upstream, and local file status before changing anything.",
            CommandText: "git status --short --branch",
            IsPrimary: repository.Git.GitDiagnostics.Count == 0));

        foreach (var diagnostic in repository.Git.GitDiagnostics)
        {
            switch (diagnostic.Code)
            {
                case RepositoryGitDiagnosticCode.GitUnavailable:
                    steps.Add(new RepositoryGitRemediationStep(
                        Title: "Verify repository root",
                        Summary: "Open the expected repo/worktree folder and confirm git can see it before any release action runs.",
                        CommandText: "git rev-parse --show-toplevel"));
                    break;

                case RepositoryGitDiagnosticCode.DetachedHead:
                    steps.Add(new RepositoryGitRemediationStep(
                        Title: "Create a working branch",
                        Summary: "Detached HEAD should be turned into a named branch before build, tag, or publish work continues.",
                        CommandText: $"git switch -c {suggestedBranch}",
                        IsPrimary: true));
                    break;

                case RepositoryGitDiagnosticCode.NoUpstream:
                    if (!string.IsNullOrWhiteSpace(branchName))
                    {
                        steps.Add(new RepositoryGitRemediationStep(
                            Title: "Set upstream branch",
                            Summary: "Push the current branch once with upstream tracking so ahead/behind and PR flow stay reliable.",
                            CommandText: $"git push --set-upstream origin {branchName}",
                            IsPrimary: true));
                    }
                    break;

                case RepositoryGitDiagnosticCode.DirtyWorkingTree:
                    steps.Add(new RepositoryGitRemediationStep(
                        Title: "Inspect local changes",
                        Summary: "Review modified and untracked files before deciding whether they belong in this release flow.",
                        CommandText: "git status --short",
                        IsPrimary: true));
                    break;

                case RepositoryGitDiagnosticCode.BehindUpstream:
                    steps.Add(new RepositoryGitRemediationStep(
                        Title: "Rebase onto upstream",
                        Summary: "Sync with remote commits so the release run starts from the latest shared branch state.",
                        CommandText: "git pull --rebase",
                        IsPrimary: true));
                    break;

                case RepositoryGitDiagnosticCode.ProtectedBaseBranchFlow:
                    steps.Add(new RepositoryGitRemediationStep(
                        Title: "Move work to a PR branch",
                        Summary: "Protected base branches usually need a feature/worktree branch before you can push or open a PR.",
                        CommandText: $"git switch -c {suggestedBranch}",
                        IsPrimary: repository.Git.AheadCount > 0));

                    steps.Add(new RepositoryGitRemediationStep(
                        Title: "Publish the PR branch",
                        Summary: "Push the new branch with tracking so GitHub can open a PR and run required checks.",
                        CommandText: $"git push --set-upstream origin {suggestedBranch}",
                        IsPrimary: repository.Git.AheadCount > 0));
                    break;
            }
        }

        return Deduplicate(steps);
    }

    private static IReadOnlyList<RepositoryGitRemediationStep> Deduplicate(List<RepositoryGitRemediationStep> steps)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deduplicated = new List<RepositoryGitRemediationStep>();
        foreach (var step in steps)
        {
            var key = $"{step.Title}|{step.CommandText}";
            if (!seen.Add(key))
            {
                continue;
            }

            deduplicated.Add(step);
        }

        return deduplicated;
    }

    private static string? NormalizeBranch(string? branchName)
    {
        if (string.IsNullOrWhiteSpace(branchName))
        {
            return null;
        }

        var normalized = branchName.Trim();
        return string.Equals(normalized, "(detached)", StringComparison.OrdinalIgnoreCase)
            ? null
            : normalized;
    }

    private static bool IsBaseBranch(string? branchName)
        => string.Equals(branchName, "main", StringComparison.OrdinalIgnoreCase)
           || string.Equals(branchName, "master", StringComparison.OrdinalIgnoreCase);

    private static string SanitizeBranchSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "repo";
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value.ToLowerInvariant())
        {
            if ((character >= 'a' && character <= 'z') || (character >= '0' && character <= '9'))
            {
                builder.Append(character);
            }
            else if (builder.Length == 0 || builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-');
    }
}
