using System.Text.Json;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioQueueCheckpointSerializerTests
{
    [Fact]
    public void TryRead_WithMatchingCheckpointKey_DeserializesTypedPayload()
    {
        var serializer = new ReleaseQueueCheckpointSerializer();
        var payload = new ReleaseBuildExecutionResult(
            RootPath: @"C:\Support\GitHub\Testimo",
            Succeeded: true,
            Summary: "Build completed.",
            DurationSeconds: 1.5,
            AdapterResults: []);
        var queueItem = new ReleaseQueueItem(
            RootPath: payload.RootPath,
            RepositoryName: "Testimo",
            RepositoryKind: ReleaseRepositoryKind.Module,
            WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            QueueOrder: 1,
            Stage: ReleaseQueueStage.Sign,
            Status: ReleaseQueueItemStatus.WaitingApproval,
            Summary: "USB approval required.",
            CheckpointKey: "sign.waiting.usb",
            CheckpointStateJson: JsonSerializer.Serialize(payload),
            UpdatedAtUtc: DateTimeOffset.UtcNow);

        var result = serializer.TryRead<ReleaseBuildExecutionResult>(queueItem, "sign.waiting.usb");

        Assert.NotNull(result);
        Assert.Equal(payload.RootPath, result!.RootPath);
        Assert.Equal(payload.Summary, result.Summary);
    }

    [Fact]
    public void SerializeTransition_WritesExpectedEnvelope()
    {
        var serializer = new ReleaseQueueCheckpointSerializer();
        var timestamp = DateTimeOffset.UtcNow;

        var json = serializer.SerializeTransition("Build", "Sign", timestamp);
        var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

        Assert.NotNull(payload);
        Assert.Equal("Build", payload!["from"]);
        Assert.Equal("Sign", payload["to"]);
        Assert.Equal(timestamp.ToString("O"), payload["updatedAtUtc"]);
    }
}
