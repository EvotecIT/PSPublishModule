using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioQueueTargetProjectionServiceTests
{
    [Fact]
    public void BuildTargets_FiltersByStageAndStatusBeforeProjecting()
    {
        var service = new ReleaseQueueTargetProjectionService();
        var matching = new ReleaseQueueItem(
            RootPath: @"C:\Support\GitHub\Testimo",
            RepositoryName: "Testimo",
            RepositoryKind: ReleaseRepositoryKind.Module,
            WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            QueueOrder: 1,
            Stage: ReleaseQueueStage.Publish,
            Status: ReleaseQueueItemStatus.ReadyToRun,
            Summary: "Ready.",
            CheckpointKey: "publish.ready",
            CheckpointStateJson: "{}",
            UpdatedAtUtc: DateTimeOffset.UtcNow);
        var ignored = matching with {
            RootPath = @"C:\Support\GitHub\Other",
            RepositoryName = "Other",
            Stage = ReleaseQueueStage.Verify
        };

        var targets = service.BuildTargets<string, string>(
            [matching, ignored],
            ReleaseQueueStage.Publish,
            item => item.RootPath == matching.RootPath ? "checkpoint" : null,
            static (item, checkpoint) => [ $"{item.RepositoryName}:{checkpoint}" ],
            static target => target);

        var target = Assert.Single(targets);
        Assert.Equal("Testimo:checkpoint", target);
    }
}
