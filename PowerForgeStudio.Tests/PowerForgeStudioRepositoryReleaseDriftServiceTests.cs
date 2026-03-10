using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Orchestrator.Portfolio;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioRepositoryReleaseDriftServiceTests
{
    [Fact]
    public void PopulateReleaseDrift_AheadOfUpstream_FlagsAttention()
    {
        var repository = new RepositoryPortfolioItem(
            new RepositoryCatalogEntry(
                Name: "DbaClientX",
                RootPath: @"C:\Support\GitHub\DbaClientX",
                RepositoryKind: ReleaseRepositoryKind.Mixed,
                WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
                ModuleBuildScriptPath: @"C:\Support\GitHub\DbaClientX\Build\Build-Module.ps1",
                ProjectBuildScriptPath: @"C:\Support\GitHub\DbaClientX\Build\Build-Project.ps1",
                IsWorktree: false,
                HasWebsiteSignals: false),
            new RepositoryGitSnapshot(
                IsGitRepository: true,
                BranchName: "main",
                UpstreamBranch: "origin/main",
                AheadCount: 3,
                BehindCount: 0,
                TrackedChangeCount: 0,
                UntrackedChangeCount: 0),
            new RepositoryReadiness(RepositoryReadinessKind.Ready, "Clean and ready."),
            GitHubInbox: new RepositoryGitHubInbox(
                RepositoryGitHubInboxStatus.Ready,
                RepositorySlug: "EvotecIT/DbaClientX",
                OpenPullRequestCount: 0,
                LatestWorkflowFailed: false,
                LatestReleaseTag: "v0.2.0",
                Summary: "No open PRs, latest workflow passed, latest release v0.2.0, 3 local commit(s) ahead",
                Detail: "Current branch is ahead."));

        var service = new RepositoryReleaseDriftService();
        var drift = Assert.Single(service.PopulateReleaseDrift([repository])).ReleaseDrift;

        Assert.NotNull(drift);
        Assert.Equal(RepositoryReleaseDriftStatus.Attention, drift.Status);
        Assert.Contains("ahead", drift.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PopulateReleaseDrift_CleanRepoWithReleaseTag_IsAligned()
    {
        var repository = new RepositoryPortfolioItem(
            new RepositoryCatalogEntry(
                Name: "OfficeIMO",
                RootPath: @"C:\Support\GitHub\OfficeIMO",
                RepositoryKind: ReleaseRepositoryKind.Library,
                WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
                ModuleBuildScriptPath: null,
                ProjectBuildScriptPath: @"C:\Support\GitHub\OfficeIMO\Build\Build-Project.ps1",
                IsWorktree: false,
                HasWebsiteSignals: false),
            new RepositoryGitSnapshot(true, "main", "origin/main", 0, 0, 0, 0),
            new RepositoryReadiness(RepositoryReadinessKind.Ready, "Clean and ready."),
            GitHubInbox: new RepositoryGitHubInbox(
                RepositoryGitHubInboxStatus.Ready,
                RepositorySlug: "EvotecIT/OfficeIMO",
                OpenPullRequestCount: 0,
                LatestWorkflowFailed: false,
                LatestReleaseTag: "v1.0.0",
                Summary: "No open PRs, latest workflow passed, latest release v1.0.0",
                Detail: "Local workspace is clean."));

        var service = new RepositoryReleaseDriftService();
        var drift = Assert.Single(service.PopulateReleaseDrift([repository])).ReleaseDrift;

        Assert.NotNull(drift);
        Assert.Equal(RepositoryReleaseDriftStatus.Aligned, drift.Status);
        Assert.Contains("v1.0.0", drift.Summary);
    }
}

