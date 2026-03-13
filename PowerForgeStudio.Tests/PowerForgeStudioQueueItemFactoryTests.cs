using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioQueueItemFactoryTests
{
    [Fact]
    public void CreateFromPortfolioItem_FailedPlan_BlocksPrepareStage()
    {
        var item = new RepositoryPortfolioItem(
            new RepositoryCatalogEntry(
                Name: "DbaClientX",
                RootPath: @"C:\Support\GitHub\DbaClientX",
                RepositoryKind: ReleaseRepositoryKind.Library,
                WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
                ModuleBuildScriptPath: null,
                ProjectBuildScriptPath: @"C:\Support\GitHub\DbaClientX\Build\Build-Project.ps1",
                IsWorktree: false,
                HasWebsiteSignals: false),
            new RepositoryGitSnapshot(true, "main", "origin/main", 0, 0, 0, 0),
            new RepositoryReadiness(RepositoryReadinessKind.Ready, "Ready."),
            PlanResults: [
                new RepositoryPlanResult(
                    RepositoryPlanAdapterKind.ProjectPlan,
                    RepositoryPlanStatus.Failed,
                    "Plan failed.",
                    PlanPath: null,
                    ExitCode: 1,
                    DurationSeconds: 1.0,
                    OutputTail: null,
                    ErrorTail: "boom")
            ]);

        var queueItem = ReleaseQueueItemFactory.CreateFromPortfolioItem(item, 1, DateTimeOffset.UtcNow);

        Assert.Equal(ReleaseQueueStage.Prepare, queueItem.Stage);
        Assert.Equal(ReleaseQueueItemStatus.Blocked, queueItem.Status);
        Assert.Equal("prepare.blocked.plan", queueItem.CheckpointKey);
        Assert.Equal("boom", queueItem.Summary);
    }
}
