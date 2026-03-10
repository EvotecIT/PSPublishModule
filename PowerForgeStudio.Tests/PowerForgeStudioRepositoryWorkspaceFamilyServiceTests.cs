using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Orchestrator.Portfolio;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioRepositoryWorkspaceFamilyServiceTests
{
    private readonly RepositoryWorkspaceFamilyService _service = new();

    [Fact]
    public void AnnotateFamilies_GroupsPrimaryRepositoryAndWorktreeUnderOneFamily()
    {
        var primary = CreateRepository(
            name: "DbaClientX",
            rootPath: @"C:\Support\GitHub\DbaClientX",
            workspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            readinessKind: RepositoryReadinessKind.Ready);
        var worktree = CreateRepository(
            name: "DbaClientX-release-ops-studio-foundation",
            rootPath: @"C:\Support\GitHub\_worktrees\DbaClientX-release-ops-studio-foundation",
            workspaceKind: ReleaseWorkspaceKind.Worktree,
            readinessKind: RepositoryReadinessKind.Attention);

        var annotated = _service.AnnotateFamilies([primary, worktree]);

        Assert.All(annotated, item => Assert.Equal("DbaClientX", item.FamilyDisplayName));
        Assert.All(annotated, item => Assert.Equal("dbaclientx", item.FamilyKey));
    }

    [Fact]
    public void BuildFamilies_SummarizesAttentionAndQueuePressurePerFamily()
    {
        var primary = CreateRepository(
            name: "DbaClientX",
            rootPath: @"C:\Support\GitHub\DbaClientX",
            workspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            readinessKind: RepositoryReadinessKind.Ready,
            familyKey: "dbaclientx",
            familyName: "DbaClientX");
        var worktree = CreateRepository(
            name: "DbaClientX-review",
            rootPath: @"C:\Support\GitHub\DbaClientX-review",
            workspaceKind: ReleaseWorkspaceKind.ReviewClone,
            readinessKind: RepositoryReadinessKind.Attention,
            familyKey: "dbaclientx",
            familyName: "DbaClientX");

        var queueSession = new ReleaseQueueSession(
            SessionId: Guid.NewGuid().ToString("N"),
            WorkspaceRoot: @"C:\Support\GitHub",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            Summary: new ReleaseQueueSummary(2, 0, 0, 0, 0, 0),
            Items: [
                new ReleaseQueueItem(
                    RootPath: worktree.RootPath,
                    RepositoryName: worktree.Name,
                    RepositoryKind: worktree.RepositoryKind,
                    WorkspaceKind: worktree.WorkspaceKind,
                    QueueOrder: 1,
                    Stage: ReleaseQueueStage.Sign,
                    Status: ReleaseQueueItemStatus.WaitingApproval,
                    Summary: "Waiting for USB token.",
                    CheckpointKey: "sign.waiting.usb",
                    CheckpointStateJson: null,
                    UpdatedAtUtc: DateTimeOffset.UtcNow)
            ]);

        var family = Assert.Single(_service.BuildFamilies([primary, worktree], queueSession));

        Assert.Equal("DbaClientX", family.DisplayName);
        Assert.Equal(2, family.TotalMembers);
        Assert.Equal(0, family.WorktreeMembers);
        Assert.Equal(1, family.AttentionMembers);
        Assert.Equal(1, family.QueueActiveMembers);
    }

    [Fact]
    public void BuildFamilyLane_ReturnsQueueAwareLaneForSelectedFamily()
    {
        var primary = CreateRepository(
            name: "DbaClientX",
            rootPath: @"C:\Support\GitHub\DbaClientX",
            workspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            readinessKind: RepositoryReadinessKind.Ready,
            planResults: [
                new RepositoryPlanResult(
                    RepositoryPlanAdapterKind.ProjectPlan,
                    RepositoryPlanStatus.Succeeded,
                    "Project plan succeeded.",
                    PlanPath: @"C:\Temp\dba.project.plan.json",
                    ExitCode: 0,
                    DurationSeconds: 1.1)
            ],
            familyKey: "dbaclientx",
            familyName: "DbaClientX");
        var worktree = CreateRepository(
            name: "DbaClientX-release-ops",
            rootPath: @"C:\Support\GitHub\DbaClientX.PowerForgeStudio",
            workspaceKind: ReleaseWorkspaceKind.Worktree,
            readinessKind: RepositoryReadinessKind.Ready,
            planResults: [
                new RepositoryPlanResult(
                    RepositoryPlanAdapterKind.ProjectPlan,
                    RepositoryPlanStatus.Succeeded,
                    "Project plan succeeded.",
                    PlanPath: @"C:\Temp\dba.releaseops.plan.json",
                    ExitCode: 0,
                    DurationSeconds: 1.4)
            ],
            familyKey: "dbaclientx",
            familyName: "DbaClientX");

        var queueSession = new ReleaseQueueSession(
            SessionId: Guid.NewGuid().ToString("N"),
            WorkspaceRoot: @"C:\Support\GitHub",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            Summary: new ReleaseQueueSummary(2, 0, 0, 1, 0, 0),
            Items: [
                new ReleaseQueueItem(
                    RootPath: primary.RootPath,
                    RepositoryName: primary.Name,
                    RepositoryKind: primary.RepositoryKind,
                    WorkspaceKind: primary.WorkspaceKind,
                    QueueOrder: 1,
                    Stage: ReleaseQueueStage.Publish,
                    Status: ReleaseQueueItemStatus.ReadyToRun,
                    Summary: "Signed outputs are ready to publish.",
                    CheckpointKey: "publish.ready",
                    CheckpointStateJson: null,
                    UpdatedAtUtc: DateTimeOffset.UtcNow),
                new ReleaseQueueItem(
                    RootPath: worktree.RootPath,
                    RepositoryName: worktree.Name,
                    RepositoryKind: worktree.RepositoryKind,
                    WorkspaceKind: worktree.WorkspaceKind,
                    QueueOrder: 2,
                    Stage: ReleaseQueueStage.Sign,
                    Status: ReleaseQueueItemStatus.WaitingApproval,
                    Summary: "Waiting for USB approval.",
                    CheckpointKey: "sign.waiting.usb",
                    CheckpointStateJson: null,
                    UpdatedAtUtc: DateTimeOffset.UtcNow)
            ]);

        var lane = _service.BuildFamilyLane([primary, worktree], queueSession, "dbaclientx");

        Assert.NotNull(lane);
        Assert.Equal("DbaClientX", lane!.DisplayName);
        Assert.Equal(1, lane.PublishReadyCount);
        Assert.Equal(1, lane.UsbWaitingCount);
        Assert.Equal(0, lane.FailedCount);
        Assert.Collection(
            lane.Members,
            first => Assert.Equal("USB Waiting", first.LaneDisplay),
            second => Assert.Equal("Publish Ready", second.LaneDisplay));
    }

    private static RepositoryPortfolioItem CreateRepository(
        string name,
        string rootPath,
        ReleaseWorkspaceKind workspaceKind,
        RepositoryReadinessKind readinessKind,
        IReadOnlyList<RepositoryPlanResult>? planResults = null,
        string? familyKey = null,
        string? familyName = null)
    {
        return new RepositoryPortfolioItem(
            new RepositoryCatalogEntry(
                Name: name,
                RootPath: rootPath,
                RepositoryKind: ReleaseRepositoryKind.Library,
                WorkspaceKind: workspaceKind,
                ModuleBuildScriptPath: null,
                ProjectBuildScriptPath: Path.Combine(rootPath, "Build", "Build-Project.ps1"),
                IsWorktree: workspaceKind == ReleaseWorkspaceKind.Worktree,
                HasWebsiteSignals: false),
            new RepositoryGitSnapshot(true, "main", "origin/main", 0, 0, 0, 0),
            new RepositoryReadiness(readinessKind, readinessKind.ToString()),
            PlanResults: planResults,
            WorkspaceFamilyKey: familyKey,
            WorkspaceFamilyName: familyName);
    }
}

