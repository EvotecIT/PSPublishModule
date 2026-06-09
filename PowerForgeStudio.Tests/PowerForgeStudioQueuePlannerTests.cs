using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioQueuePlannerTests
{
    [Fact]
    public void CreateDraftQueue_FamilyScope_PreservesScopeMetadata()
    {
        var planner = new ReleaseQueuePlanner();
        var repo = new RepositoryPortfolioItem(
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
                    RepositoryPlanStatus.Succeeded,
                    "Plan generated.",
                    PlanPath: @"C:\Temp\project.plan.json",
                    ExitCode: 0,
                    DurationSeconds: 1.0)
            ],
            WorkspaceFamilyKey: "dbaclientx",
            WorkspaceFamilyName: "DbaClientX");

        var queue = planner.CreateDraftQueue(
            workspaceRoot: @"C:\Support\GitHub",
            portfolioItems: [repo],
            scopeKey: "dbaclientx",
            scopeDisplayName: "DbaClientX");

        Assert.Equal("dbaclientx", queue.ScopeKey);
        Assert.Equal("DbaClientX", queue.ScopeDisplayName);
        Assert.Single(queue.Items);
        Assert.Equal(ReleaseQueueStage.Build, queue.Items[0].Stage);
    }
}

