using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Portfolio;

namespace PowerForgeStudio.Orchestrator.Portfolio;

public sealed class RepositoryGitPreflightService
{
    public IReadOnlyList<RepositoryGitDiagnostic> Assess(RepositoryCatalogEntry entry, RepositoryGitSnapshot gitSnapshot)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(gitSnapshot);

        var diagnostics = new List<RepositoryGitDiagnostic>();

        if (!gitSnapshot.IsGitRepository)
        {
            diagnostics.Add(new RepositoryGitDiagnostic(
                RepositoryGitDiagnosticCode.GitUnavailable,
                RepositoryGitDiagnosticSeverity.Blocked,
                "Git metadata unavailable",
                "Git metadata could not be read for this repository.",
                "PowerForgeStudio could not inspect git status here. Check that the folder is a valid git repository or worktree, and confirm git is installed and reachable on PATH."));
            return diagnostics;
        }

        if (string.IsNullOrWhiteSpace(gitSnapshot.BranchName)
            || string.Equals(gitSnapshot.BranchName, "(detached)", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(new RepositoryGitDiagnostic(
                RepositoryGitDiagnosticCode.DetachedHead,
                RepositoryGitDiagnosticSeverity.Blocked,
                "Detached HEAD",
                "Detached HEAD or missing branch name detected.",
                "Release work should not continue from a detached HEAD because commits, tags, and publish receipts will be hard to reconcile. Switch to a named branch or worktree branch first."));
        }

        if (gitSnapshot.IsDirty)
        {
            diagnostics.Add(new RepositoryGitDiagnostic(
                RepositoryGitDiagnosticCode.DirtyWorkingTree,
                RepositoryGitDiagnosticSeverity.Attention,
                "Working tree is dirty",
                $"{gitSnapshot.TrackedChangeCount} tracked and {gitSnapshot.UntrackedChangeCount} untracked local changes are present.",
                "Build and release steps can still run, but reproducibility is weaker when the repository contains uncommitted edits or untracked files."));
        }

        if (gitSnapshot.BehindCount > 0)
        {
            diagnostics.Add(new RepositoryGitDiagnostic(
                RepositoryGitDiagnosticCode.BehindUpstream,
                RepositoryGitDiagnosticSeverity.Attention,
                "Behind upstream",
                $"Local branch is behind upstream by {gitSnapshot.BehindCount} commit(s).",
                "Pull or rebase before release work so build outputs, tags, and publish actions match the latest remote state."));
        }

        if (string.IsNullOrWhiteSpace(gitSnapshot.UpstreamBranch))
        {
            diagnostics.Add(new RepositoryGitDiagnostic(
                RepositoryGitDiagnosticCode.NoUpstream,
                RepositoryGitDiagnosticSeverity.Attention,
                "No upstream branch",
                "No upstream branch is configured.",
                "Git can still commit locally, but push, ahead/behind checks, and PR-oriented release flows are less reliable until an upstream branch is configured."));
        }

        var branchName = NormalizeBranch(gitSnapshot.BranchName);
        if (IsBaseBranch(branchName))
        {
            if (gitSnapshot.AheadCount > 0)
            {
                diagnostics.Add(new RepositoryGitDiagnostic(
                    RepositoryGitDiagnosticCode.ProtectedBaseBranchFlow,
                    RepositoryGitDiagnosticSeverity.Attention,
                    "PR branch required",
                    $"Local commits are sitting on {branchName}; direct push is likely blocked.",
                    $"Protected GitHub branches commonly reject direct pushes to {branchName} when required checks or PR review policies are enabled. Create or move the work onto a feature/worktree branch and merge through a pull request instead."));
            }
            else
            {
                diagnostics.Add(new RepositoryGitDiagnostic(
                    RepositoryGitDiagnosticCode.ProtectedBaseBranchFlow,
                    RepositoryGitDiagnosticSeverity.Info,
                    "Base branch flow",
                    $"{branchName} looks like the protected base branch; expect PR-based updates.",
                    $"Scanning and plan-mode work are fine on {branchName}, but release mutations are usually safer from a feature branch or worktree that will land through a pull request."));
            }
        }

        return diagnostics
            .OrderByDescending(diagnostic => diagnostic.Severity)
            .ThenBy(diagnostic => diagnostic.Code)
            .ToArray();
    }

    private static bool IsBaseBranch(string? branchName)
        => string.Equals(branchName, "main", StringComparison.OrdinalIgnoreCase)
           || string.Equals(branchName, "master", StringComparison.OrdinalIgnoreCase);

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
}
