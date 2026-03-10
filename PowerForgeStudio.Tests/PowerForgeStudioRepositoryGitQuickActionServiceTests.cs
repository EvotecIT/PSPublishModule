using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Orchestrator.Portfolio;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioRepositoryGitQuickActionServiceTests
{
    private readonly RepositoryGitQuickActionService _service = new();

    [Fact]
    public void BuildActions_FeatureBranchWithGitHubInbox_AddsCompareAndPullRequestLinks()
    {
        var remediationSteps = new[] {
            new RepositoryGitRemediationStep(
                Title: "Inspect current git state",
                Summary: "Inspect status.",
                CommandText: "git status --short --branch",
                IsPrimary: true),
            new RepositoryGitRemediationStep(
                Title: "Publish branch",
                Summary: "Push upstream.",
                CommandText: "git push --set-upstream origin codex/release-flow",
                IsPrimary: true)
        };

        var repository = CreateRepository(
            branchName: "codex/release-flow",
            gitHubInbox: new RepositoryGitHubInbox(
                RepositoryGitHubInboxStatus.Attention,
                RepositorySlug: "EvotecIT/PSPublishModule",
                OpenPullRequestCount: 1,
                LatestWorkflowFailed: false,
                LatestReleaseTag: "v1.0.0",
                DefaultBranch: "main",
                ProbedBranch: "codex/release-flow",
                IsDefaultBranch: false,
                BranchProtectionEnabled: false,
                Summary: "GitHub attention.",
                Detail: "GitHub detail."));

        var actions = _service.BuildActions(repository, remediationSteps);

        Assert.Contains(actions, action => action.Title == "Open Pull Requests");
        Assert.Contains(actions, action => action.Title == "Open Compare View");
        Assert.Contains(actions, action => action.Kind == RepositoryGitQuickActionKind.GitCommand && action.Payload == "git status --short --branch");
        Assert.DoesNotContain(actions, action => action.Payload.StartsWith("git push --set-upstream", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildActions_DefaultBranchWithoutGitHubInbox_ReturnsOnlySafeLocalCommands()
    {
        var remediationSteps = new[] {
            new RepositoryGitRemediationStep("Inspect", "Inspect status.", "git status --short --branch", true),
            new RepositoryGitRemediationStep("Create branch", "Create branch.", "git switch -c codex/pspublishmodule-release-flow", true),
            new RepositoryGitRemediationStep("Push branch", "Push upstream.", "git push --set-upstream origin codex/pspublishmodule-release-flow", true)
        };

        var repository = CreateRepository(
            branchName: "main",
            gitHubInbox: null);

        var actions = _service.BuildActions(repository, remediationSteps);

        Assert.Contains(actions, action => action.Payload == "git switch -c codex/pspublishmodule-release-flow");
        Assert.DoesNotContain(actions, action => action.Payload.StartsWith("git push --set-upstream", StringComparison.OrdinalIgnoreCase));
        Assert.All(actions, action => Assert.NotEqual(RepositoryGitQuickActionKind.BrowserUrl, action.Kind));
    }

    private static RepositoryPortfolioItem CreateRepository(
        string branchName,
        RepositoryGitHubInbox? gitHubInbox)
    {
        var diagnostics = new List<RepositoryGitDiagnostic>();
        if (branchName == "main")
        {
            diagnostics.Add(new RepositoryGitDiagnostic(
                RepositoryGitDiagnosticCode.ProtectedBaseBranchFlow,
                RepositoryGitDiagnosticSeverity.Attention,
                "PR branch required",
                "Local commits are sitting on main; direct push is likely blocked.",
                "Move the work onto a feature branch."));
        }

        return new RepositoryPortfolioItem(
            new RepositoryCatalogEntry(
                Name: "PSPublishModule",
                RootPath: @"C:\Support\GitHub\PSPublishModule",
                RepositoryKind: ReleaseRepositoryKind.Mixed,
                WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
                ModuleBuildScriptPath: @"C:\Support\GitHub\PSPublishModule\Build\Build-Module.ps1",
                ProjectBuildScriptPath: @"C:\Support\GitHub\PSPublishModule\Build\Build-Project.ps1",
                IsWorktree: false,
                HasWebsiteSignals: false),
            new RepositoryGitSnapshot(
                IsGitRepository: true,
                BranchName: branchName,
                UpstreamBranch: "origin/main",
                AheadCount: branchName == "main" ? 1 : 0,
                BehindCount: 0,
                TrackedChangeCount: 0,
                UntrackedChangeCount: 0,
                Diagnostics: diagnostics),
            new RepositoryReadiness(RepositoryReadinessKind.Attention, "Git action needed."),
            PlanResults: null,
            GitHubInbox: gitHubInbox);
    }
}
