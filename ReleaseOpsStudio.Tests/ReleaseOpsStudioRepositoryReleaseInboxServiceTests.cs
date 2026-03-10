using ReleaseOpsStudio.Domain.Catalog;
using ReleaseOpsStudio.Domain.Portfolio;
using ReleaseOpsStudio.Domain.Queue;
using ReleaseOpsStudio.Orchestrator.Portfolio;

namespace ReleaseOpsStudio.Tests;

public sealed class ReleaseOpsStudioRepositoryReleaseInboxServiceTests
{
    [Fact]
    public void BuildInbox_PrioritizesQueueActionsAndDeduplicatesRepositories()
    {
        var waitingUsb = CreateRepository(
            name: "UsbRepo",
            rootPath: @"C:\Support\GitHub\UsbRepo",
            readinessKind: RepositoryReadinessKind.Ready,
            gitHubStatus: RepositoryGitHubInboxStatus.Attention);
        var failed = CreateRepository(
            name: "FailedRepo",
            rootPath: @"C:\Support\GitHub\FailedRepo",
            readinessKind: RepositoryReadinessKind.Attention);
        var readyToday = CreateRepository(
            name: "ReadyRepo",
            rootPath: @"C:\Support\GitHub\ReadyRepo",
            readinessKind: RepositoryReadinessKind.Ready,
            planResults: [
                new RepositoryPlanResult(
                    RepositoryPlanAdapterKind.ProjectPlan,
                    RepositoryPlanStatus.Succeeded,
                    "Plan generated.",
                    PlanPath: @"C:\Temp\ready.plan.json",
                    ExitCode: 0,
                    DurationSeconds: 1.2)
            ]);

        var queueSession = new ReleaseQueueSession(
            SessionId: Guid.NewGuid().ToString("N"),
            WorkspaceRoot: @"C:\Support\GitHub",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            Summary: new ReleaseQueueSummary(3, 0, 0, 1, 1, 0),
            Items: [
                new ReleaseQueueItem(
                    RootPath: failed.RootPath,
                    RepositoryName: failed.Name,
                    RepositoryKind: failed.RepositoryKind,
                    WorkspaceKind: failed.WorkspaceKind,
                    QueueOrder: 1,
                    Stage: ReleaseQueueStage.Publish,
                    Status: ReleaseQueueItemStatus.Failed,
                    Summary: "Publish failed.",
                    CheckpointKey: "publish.failed",
                    CheckpointStateJson: null,
                    UpdatedAtUtc: DateTimeOffset.UtcNow),
                new ReleaseQueueItem(
                    RootPath: waitingUsb.RootPath,
                    RepositoryName: waitingUsb.Name,
                    RepositoryKind: waitingUsb.RepositoryKind,
                    WorkspaceKind: waitingUsb.WorkspaceKind,
                    QueueOrder: 2,
                    Stage: ReleaseQueueStage.Sign,
                    Status: ReleaseQueueItemStatus.WaitingApproval,
                    Summary: "Waiting for USB token.",
                    CheckpointKey: "sign.waiting.usb",
                    CheckpointStateJson: null,
                    UpdatedAtUtc: DateTimeOffset.UtcNow)
            ]);

        var service = new RepositoryReleaseInboxService();
        var inbox = service.BuildInbox([waitingUsb, failed, readyToday], queueSession);

        Assert.Collection(
            inbox,
            item => {
                Assert.Equal("FailedRepo", item.RepositoryName);
                Assert.Equal("Failed", item.Badge);
            },
            item => {
                Assert.Equal("UsbRepo", item.RepositoryName);
                Assert.Equal("USB Waiting", item.Badge);
            },
            item => {
                Assert.Equal("ReadyRepo", item.RepositoryName);
                Assert.Equal("Ready Today", item.Badge);
            });
    }

    private static RepositoryPortfolioItem CreateRepository(
        string name,
        string rootPath,
        RepositoryReadinessKind readinessKind,
        RepositoryGitHubInboxStatus gitHubStatus = RepositoryGitHubInboxStatus.NotProbed,
        IReadOnlyList<RepositoryPlanResult>? planResults = null)
    {
        var inbox = gitHubStatus == RepositoryGitHubInboxStatus.NotProbed
            ? null
            : new RepositoryGitHubInbox(
                gitHubStatus,
                RepositorySlug: $"EvotecIT/{name}",
                OpenPullRequestCount: 2,
                LatestWorkflowFailed: true,
                LatestReleaseTag: "v0.2.0",
                Summary: "GitHub attention.",
                Detail: "GitHub detail.");

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
            null);
    }
}

