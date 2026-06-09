using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Orchestrator.Portfolio;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioRepositoryPortfolioDashboardServiceTests
{
    private readonly RepositoryPortfolioDashboardService _service = new();

    [Fact]
    public void BuildCards_ProjectsReusableDashboardCounts()
    {
        var ready = CreateRepository(
            name: "ReadyRepo",
            rootPath: @"C:\Support\GitHub\ReadyRepo",
            readinessKind: RepositoryReadinessKind.Ready,
            planResults: [
                new RepositoryPlanResult(
                    RepositoryPlanAdapterKind.ProjectPlan,
                    RepositoryPlanStatus.Succeeded,
                    "Plan generated.",
                    PlanPath: @"C:\Temp\ready.project.plan.json",
                    ExitCode: 0,
                    DurationSeconds: 0.9)
            ]);
        var waitingUsb = CreateRepository(
            name: "UsbRepo",
            rootPath: @"C:\Support\GitHub\UsbRepo",
            readinessKind: RepositoryReadinessKind.Ready,
            planResults: [
                new RepositoryPlanResult(
                    RepositoryPlanAdapterKind.ProjectPlan,
                    RepositoryPlanStatus.Succeeded,
                    "Plan generated.",
                    PlanPath: @"C:\Temp\usb.project.plan.json",
                    ExitCode: 0,
                    DurationSeconds: 0.9)
            ]);
        var failed = CreateRepository(
            name: "FailedRepo",
            rootPath: @"C:\Support\GitHub\FailedRepo",
            readinessKind: RepositoryReadinessKind.Attention);

        var queueSession = new ReleaseQueueSession(
            SessionId: Guid.NewGuid().ToString("N"),
            WorkspaceRoot: @"C:\Support\GitHub",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            Summary: new ReleaseQueueSummary(3, 0, 1, 0, 0, 1),
            Items: [
                new ReleaseQueueItem(
                    RootPath: waitingUsb.RootPath,
                    RepositoryName: waitingUsb.Name,
                    RepositoryKind: waitingUsb.RepositoryKind,
                    WorkspaceKind: waitingUsb.WorkspaceKind,
                    QueueOrder: 1,
                    Stage: ReleaseQueueStage.Sign,
                    Status: ReleaseQueueItemStatus.WaitingApproval,
                    Summary: "Waiting for USB approval.",
                    CheckpointKey: "sign.waiting.usb",
                    CheckpointStateJson: null,
                    UpdatedAtUtc: DateTimeOffset.UtcNow),
                new ReleaseQueueItem(
                    RootPath: failed.RootPath,
                    RepositoryName: failed.Name,
                    RepositoryKind: failed.RepositoryKind,
                    WorkspaceKind: failed.WorkspaceKind,
                    QueueOrder: 2,
                    Stage: ReleaseQueueStage.Build,
                    Status: ReleaseQueueItemStatus.Failed,
                    Summary: "Build failed.",
                    CheckpointKey: "build.failed",
                    CheckpointStateJson: null,
                    UpdatedAtUtc: DateTimeOffset.UtcNow)
            ]);

        var cards = _service.BuildCards([ready, waitingUsb, failed], queueSession);

        Assert.Collection(
            cards,
            card => {
                Assert.Equal("ready-today", card.Key);
                Assert.Equal("2", card.CountDisplay);
            },
            card => {
                Assert.Equal("usb-waiting", card.Key);
                Assert.Equal("1", card.CountDisplay);
            },
            card => {
                Assert.Equal("publish-ready", card.Key);
                Assert.Equal("0", card.CountDisplay);
            },
            card => {
                Assert.Equal("verify-ready", card.Key);
                Assert.Equal("0", card.CountDisplay);
            },
            card => {
                Assert.Equal("failed", card.Key);
                Assert.Equal("1", card.CountDisplay);
            });
    }

    private static RepositoryPortfolioItem CreateRepository(
        string name,
        string rootPath,
        RepositoryReadinessKind readinessKind,
        IReadOnlyList<RepositoryPlanResult>? planResults = null)
    {
        return new RepositoryPortfolioItem(
            new RepositoryCatalogEntry(
                Name: name,
                RootPath: rootPath,
                RepositoryKind: ReleaseRepositoryKind.Library,
                WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
                ModuleBuildScriptPath: null,
                ProjectBuildScriptPath: Path.Combine(rootPath, "Build", "Build-Project.ps1"),
                IsWorktree: false,
                HasWebsiteSignals: false),
            new RepositoryGitSnapshot(true, "main", "origin/main", 0, 0, 0, 0),
            new RepositoryReadiness(readinessKind, readinessKind.ToString()),
            PlanResults: planResults);
    }
}
