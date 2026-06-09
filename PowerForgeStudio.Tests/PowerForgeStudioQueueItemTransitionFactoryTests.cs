using System.Text.Json;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioQueueItemTransitionFactoryTests
{
    [Fact]
    public void CreateTransition_WritesTransitionCheckpointAndState()
    {
        var item = new ReleaseQueueItem(
            RootPath: @"C:\Support\GitHub\Testimo",
            RepositoryName: "Testimo",
            RepositoryKind: ReleaseRepositoryKind.Module,
            WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            QueueOrder: 1,
            Stage: ReleaseQueueStage.Build,
            Status: ReleaseQueueItemStatus.ReadyToRun,
            Summary: "Ready.",
            CheckpointKey: "build.ready",
            CheckpointStateJson: null,
            UpdatedAtUtc: DateTimeOffset.UtcNow);
        var factory = new ReleaseQueueItemTransitionFactory();
        var timestamp = DateTimeOffset.UtcNow;

        var updated = factory.CreateTransition(
            item,
            fromStage: "Build",
            targetStage: ReleaseQueueStage.Sign,
            targetStatus: ReleaseQueueItemStatus.WaitingApproval,
            summary: "Moved.",
            checkpointKey: "sign.waiting.usb",
            timestamp: timestamp);

        Assert.Equal(ReleaseQueueStage.Sign, updated.Stage);
        Assert.Equal(ReleaseQueueItemStatus.WaitingApproval, updated.Status);
        Assert.Equal("sign.waiting.usb", updated.CheckpointKey);
        var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(updated.CheckpointStateJson!);
        Assert.Equal("Build", payload!["from"]);
        Assert.Equal("Sign", payload["to"]);
    }

    [Fact]
    public void CreateCheckpointUpdate_SerializesPayload()
    {
        var item = new ReleaseQueueItem(
            RootPath: @"C:\Support\GitHub\DbaClientX",
            RepositoryName: "DbaClientX",
            RepositoryKind: ReleaseRepositoryKind.Mixed,
            WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            QueueOrder: 1,
            Stage: ReleaseQueueStage.Publish,
            Status: ReleaseQueueItemStatus.ReadyToRun,
            Summary: "Ready.",
            CheckpointKey: "publish.ready",
            CheckpointStateJson: null,
            UpdatedAtUtc: DateTimeOffset.UtcNow);
        var factory = new ReleaseQueueItemTransitionFactory();
        var payload = new ReleaseBuildExecutionResult(
            RootPath: item.RootPath,
            Succeeded: true,
            Summary: "Build completed.",
            DurationSeconds: 1.2,
            AdapterResults: []);

        var updated = factory.CreateCheckpointUpdate(
            item,
            targetStage: ReleaseQueueStage.Sign,
            targetStatus: ReleaseQueueItemStatus.WaitingApproval,
            summary: payload.Summary,
            checkpointKey: "sign.waiting.usb",
            checkpoint: payload,
            timestamp: DateTimeOffset.UtcNow);

        var deserialized = JsonSerializer.Deserialize<ReleaseBuildExecutionResult>(updated.CheckpointStateJson!);
        Assert.Equal("Build completed.", deserialized!.Summary);
        Assert.Equal(ReleaseQueueStage.Sign, updated.Stage);
    }
}
