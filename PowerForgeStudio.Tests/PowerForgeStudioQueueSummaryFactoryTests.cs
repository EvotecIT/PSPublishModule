using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioQueueSummaryFactoryTests
{
    [Fact]
    public void Create_ComputesStageAndStatusCounters()
    {
        var items = new[] {
            CreateItem("a", ReleaseQueueStage.Build, ReleaseQueueItemStatus.ReadyToRun),
            CreateItem("b", ReleaseQueueStage.Prepare, ReleaseQueueItemStatus.Pending),
            CreateItem("c", ReleaseQueueStage.Sign, ReleaseQueueItemStatus.WaitingApproval),
            CreateItem("d", ReleaseQueueStage.Build, ReleaseQueueItemStatus.Failed),
            CreateItem("e", ReleaseQueueStage.Verify, ReleaseQueueItemStatus.ReadyToRun)
        };

        var summary = ReleaseQueueSummaryFactory.Create(items);

        Assert.Equal(5, summary.TotalItems);
        Assert.Equal(1, summary.BuildReadyItems);
        Assert.Equal(1, summary.PreparePendingItems);
        Assert.Equal(1, summary.WaitingApprovalItems);
        Assert.Equal(1, summary.BlockedItems);
        Assert.Equal(1, summary.VerificationReadyItems);
    }

    private static ReleaseQueueItem CreateItem(string name, ReleaseQueueStage stage, ReleaseQueueItemStatus status)
        => new(
            RootPath: $@"C:\Support\GitHub\{name}",
            RepositoryName: name,
            RepositoryKind: ReleaseRepositoryKind.Mixed,
            WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            QueueOrder: 1,
            Stage: stage,
            Status: status,
            Summary: "test",
            CheckpointKey: "test",
            CheckpointStateJson: null,
            UpdatedAtUtc: DateTimeOffset.UtcNow);
}
