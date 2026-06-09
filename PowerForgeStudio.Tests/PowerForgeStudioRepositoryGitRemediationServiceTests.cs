using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Orchestrator.Portfolio;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioRepositoryGitRemediationServiceTests
{
    private readonly RepositoryGitRemediationService _service = new();

    [Fact]
    public void BuildSteps_MainBranchAhead_SuggestsPrBranchFlow()
    {
        var repository = CreateRepository(
            branchName: "main",
            diagnostics: [
                new RepositoryGitDiagnostic(
                    RepositoryGitDiagnosticCode.ProtectedBaseBranchFlow,
                    RepositoryGitDiagnosticSeverity.Attention,
                    "PR branch required",
                    "Local commits are sitting on main; direct push is likely blocked.",
                    "Move the work onto a feature branch and publish that branch instead.")
            ],
            aheadCount: 2);

        var steps = _service.BuildSteps(repository);

        Assert.Contains(steps, step => step.CommandText == "git status --short --branch");
        Assert.Contains(steps, step => step.CommandText == "git switch -c codex/pspublishmodule-release-flow");
        Assert.Contains(steps, step => step.CommandText == "git push --set-upstream origin codex/pspublishmodule-release-flow");
    }

    [Fact]
    public void BuildSteps_NoUpstream_SuggestsSetUpstream()
    {
        var repository = CreateRepository(
            branchName: "codex/my-branch",
            diagnostics: [
                new RepositoryGitDiagnostic(
                    RepositoryGitDiagnosticCode.NoUpstream,
                    RepositoryGitDiagnosticSeverity.Attention,
                    "No upstream branch",
                    "No upstream branch is configured.",
                    "Push once with upstream tracking.")
            ]);

        var steps = _service.BuildSteps(repository);

        Assert.Contains(steps, step => step.CommandText == "git push --set-upstream origin codex/my-branch");
    }

    [Fact]
    public void BuildSteps_NullRepository_ReturnsPlaceholder()
    {
        var steps = _service.BuildSteps(null);

        var step = Assert.Single(steps);
        Assert.Equal("Select a repository", step.Title);
    }

    private static RepositoryPortfolioItem CreateRepository(
        string branchName,
        IReadOnlyList<RepositoryGitDiagnostic> diagnostics,
        int aheadCount = 0)
    {
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
                AheadCount: aheadCount,
                BehindCount: 0,
                TrackedChangeCount: 0,
                UntrackedChangeCount: 0,
                Diagnostics: diagnostics),
            new RepositoryReadiness(RepositoryReadinessKind.Attention, "Needs git action."));
    }
}
