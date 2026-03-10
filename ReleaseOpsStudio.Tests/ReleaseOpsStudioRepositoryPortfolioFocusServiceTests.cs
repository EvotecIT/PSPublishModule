using ReleaseOpsStudio.Domain.Catalog;
using ReleaseOpsStudio.Domain.Portfolio;
using ReleaseOpsStudio.Domain.Queue;
using ReleaseOpsStudio.Orchestrator.Portfolio;

namespace ReleaseOpsStudio.Tests;

public sealed class ReleaseOpsStudioRepositoryPortfolioFocusServiceTests
{
    private readonly RepositoryPortfolioFocusService _service = new();

    [Fact]
    public void Filter_AttentionFocus_ReturnsRepositoriesWithInboxOrDriftPressure()
    {
        var ready = CreateRepository(
            name: "ReadyRepo",
            rootPath: @"C:\Support\GitHub\ReadyRepo",
            readinessKind: RepositoryReadinessKind.Ready,
            gitHubStatus: RepositoryGitHubInboxStatus.Ready,
            driftStatus: RepositoryReleaseDriftStatus.Aligned);
        var inboxAttention = CreateRepository(
            name: "InboxRepo",
            rootPath: @"C:\Support\GitHub\InboxRepo",
            readinessKind: RepositoryReadinessKind.Ready,
            gitHubStatus: RepositoryGitHubInboxStatus.Attention,
            driftStatus: RepositoryReleaseDriftStatus.Aligned);
        var driftAttention = CreateRepository(
            name: "DriftRepo",
            rootPath: @"C:\Support\GitHub\DriftRepo",
            readinessKind: RepositoryReadinessKind.Ready,
            gitHubStatus: RepositoryGitHubInboxStatus.Ready,
            driftStatus: RepositoryReleaseDriftStatus.Attention);

        var result = _service.Filter([ready, inboxAttention, driftAttention], queueSession: null, RepositoryPortfolioFocusMode.Attention, searchText: null);

        Assert.Collection(
            result,
            item => Assert.Equal("InboxRepo", item.Name),
            item => Assert.Equal("DriftRepo", item.Name));
    }

    [Fact]
    public void Filter_QueueActiveFocusWithSearch_ReturnsOnlyMatchingActionableRepositories()
    {
        var queueReady = CreateRepository(
            name: "ReleaseOpsStudio",
            rootPath: @"C:\Support\GitHub\ReleaseOpsStudio",
            readinessKind: RepositoryReadinessKind.Ready);
        var blocked = CreateRepository(
            name: "BlockedRepo",
            rootPath: @"C:\Support\GitHub\BlockedRepo",
            readinessKind: RepositoryReadinessKind.Blocked);

        var queueSession = new ReleaseQueueSession(
            SessionId: Guid.NewGuid().ToString("N"),
            WorkspaceRoot: @"C:\Support\GitHub",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            Summary: new ReleaseQueueSummary(2, 1, 0, 0, 1, 0),
            Items: [
                new ReleaseQueueItem(
                    RootPath: queueReady.RootPath,
                    RepositoryName: queueReady.Name,
                    RepositoryKind: queueReady.RepositoryKind,
                    WorkspaceKind: queueReady.WorkspaceKind,
                    QueueOrder: 1,
                    Stage: ReleaseQueueStage.Build,
                    Status: ReleaseQueueItemStatus.ReadyToRun,
                    Summary: "Ready for build execution.",
                    CheckpointKey: "build.ready",
                    CheckpointStateJson: null,
                    UpdatedAtUtc: DateTimeOffset.UtcNow),
                new ReleaseQueueItem(
                    RootPath: blocked.RootPath,
                    RepositoryName: blocked.Name,
                    RepositoryKind: blocked.RepositoryKind,
                    WorkspaceKind: blocked.WorkspaceKind,
                    QueueOrder: 2,
                    Stage: ReleaseQueueStage.Prepare,
                    Status: ReleaseQueueItemStatus.Blocked,
                    Summary: "Blocked by readiness.",
                    CheckpointKey: "prepare.blocked.readiness",
                    CheckpointStateJson: null,
                    UpdatedAtUtc: DateTimeOffset.UtcNow)
            ]);

        var result = _service.Filter([queueReady, blocked], queueSession, RepositoryPortfolioFocusMode.QueueActive, "studio");

        var item = Assert.Single(result);
        Assert.Equal("ReleaseOpsStudio", item.Name);
    }

    [Fact]
    public void Filter_ReadyFocus_RequiresSucceededPlanPreview()
    {
        var readyWithPlan = CreateRepository(
            name: "ReadyWithPlan",
            rootPath: @"C:\Support\GitHub\ReadyWithPlan",
            readinessKind: RepositoryReadinessKind.Ready,
            planResults: [
                new RepositoryPlanResult(
                    RepositoryPlanAdapterKind.ProjectPlan,
                    RepositoryPlanStatus.Succeeded,
                    "Plan generated.",
                    PlanPath: @"C:\Temp\project.plan.json",
                    ExitCode: 0,
                    DurationSeconds: 1.2)
            ]);
        var readyWithoutPlan = CreateRepository(
            name: "ReadyWithoutPlan",
            rootPath: @"C:\Support\GitHub\ReadyWithoutPlan",
            readinessKind: RepositoryReadinessKind.Ready);

        var result = _service.Filter([readyWithPlan, readyWithoutPlan], queueSession: null, RepositoryPortfolioFocusMode.Ready, searchText: null);

        var item = Assert.Single(result);
        Assert.Equal("ReadyWithPlan", item.Name);
    }

    [Fact]
    public void Filter_WaitingUsbFocus_ReturnsOnlySigningGateRepositories()
    {
        var waitingUsb = CreateRepository(
            name: "UsbRepo",
            rootPath: @"C:\Support\GitHub\UsbRepo",
            readinessKind: RepositoryReadinessKind.Ready);
        var queueActive = CreateRepository(
            name: "BuildRepo",
            rootPath: @"C:\Support\GitHub\BuildRepo",
            readinessKind: RepositoryReadinessKind.Ready);

        var queueSession = new ReleaseQueueSession(
            SessionId: Guid.NewGuid().ToString("N"),
            WorkspaceRoot: @"C:\Support\GitHub",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            Summary: new ReleaseQueueSummary(2, 1, 0, 1, 0, 0),
            Items: [
                new ReleaseQueueItem(
                    RootPath: waitingUsb.RootPath,
                    RepositoryName: waitingUsb.Name,
                    RepositoryKind: waitingUsb.RepositoryKind,
                    WorkspaceKind: waitingUsb.WorkspaceKind,
                    QueueOrder: 1,
                    Stage: ReleaseQueueStage.Sign,
                    Status: ReleaseQueueItemStatus.WaitingApproval,
                    Summary: "Waiting for USB token.",
                    CheckpointKey: "sign.waiting.usb",
                    CheckpointStateJson: null,
                    UpdatedAtUtc: DateTimeOffset.UtcNow),
                new ReleaseQueueItem(
                    RootPath: queueActive.RootPath,
                    RepositoryName: queueActive.Name,
                    RepositoryKind: queueActive.RepositoryKind,
                    WorkspaceKind: queueActive.WorkspaceKind,
                    QueueOrder: 2,
                    Stage: ReleaseQueueStage.Build,
                    Status: ReleaseQueueItemStatus.ReadyToRun,
                    Summary: "Ready to build.",
                    CheckpointKey: "build.ready",
                    CheckpointStateJson: null,
                    UpdatedAtUtc: DateTimeOffset.UtcNow)
            ]);

        var result = _service.Filter([waitingUsb, queueActive], queueSession, RepositoryPortfolioFocusMode.WaitingUsb, searchText: null);

        var item = Assert.Single(result);
        Assert.Equal("UsbRepo", item.Name);
    }

    [Fact]
    public void Filter_PublishAndVerifyReadyFocus_ReturnQueueSpecificReadyStates()
    {
        var publishRepo = CreateRepository(
            name: "PublishRepo",
            rootPath: @"C:\Support\GitHub\PublishRepo",
            readinessKind: RepositoryReadinessKind.Ready);
        var verifyRepo = CreateRepository(
            name: "VerifyRepo",
            rootPath: @"C:\Support\GitHub\VerifyRepo",
            readinessKind: RepositoryReadinessKind.Ready);
        var failedRepo = CreateRepository(
            name: "FailedRepo",
            rootPath: @"C:\Support\GitHub\FailedRepo",
            readinessKind: RepositoryReadinessKind.Attention);

        var queueSession = new ReleaseQueueSession(
            SessionId: Guid.NewGuid().ToString("N"),
            WorkspaceRoot: @"C:\Support\GitHub",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            Summary: new ReleaseQueueSummary(3, 0, 0, 0, 1, 1),
            Items: [
                new ReleaseQueueItem(
                    RootPath: publishRepo.RootPath,
                    RepositoryName: publishRepo.Name,
                    RepositoryKind: publishRepo.RepositoryKind,
                    WorkspaceKind: publishRepo.WorkspaceKind,
                    QueueOrder: 1,
                    Stage: ReleaseQueueStage.Publish,
                    Status: ReleaseQueueItemStatus.ReadyToRun,
                    Summary: "Ready to publish.",
                    CheckpointKey: "publish.ready",
                    CheckpointStateJson: null,
                    UpdatedAtUtc: DateTimeOffset.UtcNow),
                new ReleaseQueueItem(
                    RootPath: verifyRepo.RootPath,
                    RepositoryName: verifyRepo.Name,
                    RepositoryKind: verifyRepo.RepositoryKind,
                    WorkspaceKind: verifyRepo.WorkspaceKind,
                    QueueOrder: 2,
                    Stage: ReleaseQueueStage.Verify,
                    Status: ReleaseQueueItemStatus.ReadyToRun,
                    Summary: "Ready to verify.",
                    CheckpointKey: "verify.ready",
                    CheckpointStateJson: null,
                    UpdatedAtUtc: DateTimeOffset.UtcNow),
                new ReleaseQueueItem(
                    RootPath: failedRepo.RootPath,
                    RepositoryName: failedRepo.Name,
                    RepositoryKind: failedRepo.RepositoryKind,
                    WorkspaceKind: failedRepo.WorkspaceKind,
                    QueueOrder: 3,
                    Stage: ReleaseQueueStage.Publish,
                    Status: ReleaseQueueItemStatus.Failed,
                    Summary: "Publish failed.",
                    CheckpointKey: "publish.failed",
                    CheckpointStateJson: null,
                    UpdatedAtUtc: DateTimeOffset.UtcNow)
            ]);

        var publishReady = Assert.Single(_service.Filter([publishRepo, verifyRepo, failedRepo], queueSession, RepositoryPortfolioFocusMode.PublishReady, searchText: null));
        var verifyReady = Assert.Single(_service.Filter([publishRepo, verifyRepo, failedRepo], queueSession, RepositoryPortfolioFocusMode.VerifyReady, searchText: null));
        var failed = Assert.Single(_service.Filter([publishRepo, verifyRepo, failedRepo], queueSession, RepositoryPortfolioFocusMode.Failed, searchText: null));

        Assert.Equal("PublishRepo", publishReady.Name);
        Assert.Equal("VerifyRepo", verifyReady.Name);
        Assert.Equal("FailedRepo", failed.Name);
    }

    [Fact]
    public void Filter_FamilyScope_ReturnsOnlyRepositoriesFromSelectedFamily()
    {
        var primary = CreateRepository(
            name: "DbaClientX",
            rootPath: @"C:\Support\GitHub\DbaClientX",
            readinessKind: RepositoryReadinessKind.Ready,
            familyKey: "dbaclientx",
            familyName: "DbaClientX");
        var worktree = CreateRepository(
            name: "DbaClientX-release-ops",
            rootPath: @"C:\Support\GitHub\_worktrees\DbaClientX-release-ops",
            readinessKind: RepositoryReadinessKind.Ready,
            familyKey: "dbaclientx",
            familyName: "DbaClientX");
        var other = CreateRepository(
            name: "PSWriteHTML",
            rootPath: @"C:\Support\GitHub\PSWriteHTML",
            readinessKind: RepositoryReadinessKind.Ready,
            familyKey: "pswritehtml",
            familyName: "PSWriteHTML");

        var result = _service.Filter([primary, worktree, other], queueSession: null, RepositoryPortfolioFocusMode.All, searchText: null, familyKey: "dbaclientx");

        Assert.Collection(
            result,
            item => Assert.Equal(primary.RootPath, item.RootPath),
            item => Assert.Equal(worktree.RootPath, item.RootPath));
    }

    private static RepositoryPortfolioItem CreateRepository(
        string name,
        string rootPath,
        RepositoryReadinessKind readinessKind,
        RepositoryGitHubInboxStatus gitHubStatus = RepositoryGitHubInboxStatus.NotProbed,
        RepositoryReleaseDriftStatus driftStatus = RepositoryReleaseDriftStatus.Unknown,
        IReadOnlyList<RepositoryPlanResult>? planResults = null,
        string? familyKey = null,
        string? familyName = null)
    {
        var inbox = gitHubStatus == RepositoryGitHubInboxStatus.NotProbed
            ? null
            : new RepositoryGitHubInbox(
                gitHubStatus,
                RepositorySlug: $"EvotecIT/{name}",
                OpenPullRequestCount: gitHubStatus == RepositoryGitHubInboxStatus.Attention ? 2 : 0,
                LatestWorkflowFailed: gitHubStatus == RepositoryGitHubInboxStatus.Attention,
                LatestReleaseTag: "v0.2.0",
                Summary: "GitHub status sample.",
                Detail: "GitHub detail sample.");

        var drift = driftStatus == RepositoryReleaseDriftStatus.Unknown
            ? null
            : new RepositoryReleaseDrift(
                driftStatus,
                Summary: "Drift status sample.",
                Detail: "Drift detail sample.");

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
            planResults,
            inbox,
            drift,
            familyKey,
            familyName);
    }
}

