using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Wpf.ViewModels;

namespace PowerForgeStudio.Wpf.Tests;

public sealed class PortfolioInteractionServiceTests
{
    [Fact]
    public void ApplyRepositoryFamily_ReturnsFamilyKeyAndSchedulesSave()
    {
        var portfolioOverview = new PortfolioOverviewViewModel();
        var service = new PortfolioInteractionService();
        var family = new RepositoryWorkspaceFamilySnapshot(
            FamilyKey: "repoalpha",
            DisplayName: "Repo.Alpha",
            PrimaryRootPath: @"C:\Support\GitHub\Repo.Alpha",
            TotalMembers: 2,
            WorktreeMembers: 1,
            AttentionMembers: 0,
            ReadyMembers: 2,
            QueueActiveMembers: 0,
            MemberSummary: "1 primary | 1 worktree | 0 review | 0 temp");

        var result = service.ApplyRepositoryFamily(portfolioOverview, family);

        Assert.True(result.Handled);
        Assert.Equal("repoalpha", result.SelectedRepositoryFamilyKey);
        Assert.True(result.ShouldScheduleSave);
        Assert.Equal("Repository family applied: Repo.Alpha.", portfolioOverview.ViewMemory);
        Assert.Null(portfolioOverview.SelectedPreset);
    }

    [Fact]
    public void ApplyReleaseInboxItem_UpdatesFocusSearchAndSelectedRoot()
    {
        var portfolioOverview = new PortfolioOverviewViewModel();
        var service = new PortfolioInteractionService();
        var repository = CreateRepository("Repo.Attention", @"C:\Support\GitHub\Repo.Attention", RepositoryReadinessKind.Attention);
        var item = new RepositoryReleaseInboxItem(
            RootPath: repository.RootPath,
            RepositoryName: repository.Name,
            Title: "Investigate queue failure",
            Detail: "A failed build needs attention.",
            Badge: "Failed",
            FocusMode: RepositoryPortfolioFocusMode.Attention,
            SearchText: "Attention",
            PresetKey: "attention",
            Priority: 0);

        var result = service.ApplyReleaseInboxItem(
            portfolioOverview,
            [repository],
            item,
            ResolvePreset);

        Assert.True(result.Handled);
        Assert.Equal(repository.RootPath, result.SelectedRepositoryRootPath);
        Assert.Equal(repository.FamilyKey, result.SelectedRepositoryFamilyKey);
        Assert.False(result.ShouldScheduleSave);
        Assert.Equal(RepositoryPortfolioFocusMode.Attention, portfolioOverview.SelectedFocus.Mode);
        Assert.Equal("Attention", portfolioOverview.SearchText);
        Assert.Equal("Release inbox item applied: Failed for Repo.Attention.", portfolioOverview.ViewMemory);
        Assert.Equal("attention", portfolioOverview.SelectedPreset?.Key);
    }

    private static PortfolioQuickPreset? ResolvePreset(string? presetKey, RepositoryPortfolioFocusMode focusMode, string searchText)
    {
        var portfolioOverview = new PortfolioOverviewViewModel();
        return portfolioOverview.QuickPresets.FirstOrDefault(preset =>
            string.Equals(preset.Key, presetKey, StringComparison.OrdinalIgnoreCase)
            || (preset.FocusMode == focusMode && string.Equals(preset.SearchText, searchText, StringComparison.Ordinal)));
    }

    private static RepositoryPortfolioItem CreateRepository(string name, string rootPath, RepositoryReadinessKind readinessKind)
        => new(
            new RepositoryCatalogEntry(
                Name: name,
                RootPath: rootPath,
                RepositoryKind: ReleaseRepositoryKind.Module,
                WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
                ModuleBuildScriptPath: Path.Combine(rootPath, "Build", "Build-Module.ps1"),
                ProjectBuildScriptPath: null,
                IsWorktree: false,
                HasWebsiteSignals: false),
            new RepositoryGitSnapshot(
                IsGitRepository: true,
                BranchName: "main",
                UpstreamBranch: "origin/main",
                AheadCount: 0,
                BehindCount: 0,
                TrackedChangeCount: 0,
                UntrackedChangeCount: 0),
            new RepositoryReadiness(readinessKind, readinessKind.ToString()),
            PlanResults: [],
            GitHubInbox: null,
            ReleaseDrift: null,
            WorkspaceFamilyKey: "repoattention",
            WorkspaceFamilyName: name);
}
