using System.Text.Json;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioBuildCheckpointReaderTests
{
    [Fact]
    public void BuildSigningManifest_WaitingSigningCheckpoint_ReturnsArtifacts()
    {
        var buildResult = new ReleaseBuildExecutionResult(
            RootPath: @"C:\Support\GitHub\DbaClientX",
            Succeeded: true,
            Summary: "Build completed safely.",
            DurationSeconds: 11.2,
            AdapterResults: [
                new ReleaseBuildAdapterResult(
                    ReleaseBuildAdapterKind.ProjectBuild,
                    true,
                    "Project build completed.",
                    0,
                    11.2,
                    [@"C:\Support\GitHub\DbaClientX\Artefacts\ProjectBuild"],
                    [@"C:\Support\GitHub\DbaClientX\Artefacts\ProjectBuild\DbaClientX.Core.0.2.0.nupkg"])
            ]);

        var queueItem = new ReleaseQueueItem(
            RootPath: buildResult.RootPath,
            RepositoryName: "DbaClientX",
            RepositoryKind: ReleaseRepositoryKind.Mixed,
            WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            QueueOrder: 1,
            Stage: ReleaseQueueStage.Sign,
            Status: ReleaseQueueItemStatus.WaitingApproval,
            Summary: "USB approval required.",
            CheckpointKey: "sign.waiting.usb",
            CheckpointStateJson: JsonSerializer.Serialize(buildResult),
            UpdatedAtUtc: DateTimeOffset.UtcNow);

        var reader = new ReleaseBuildCheckpointReader();
        var manifest = reader.BuildSigningManifest([queueItem]);

        Assert.Single(manifest);
        Assert.Equal("DbaClientX", manifest[0].RepositoryName);
        Assert.Equal("DbaClientX.Core.0.2.0.nupkg", manifest[0].DisplayName);
        Assert.Equal("File", manifest[0].ArtifactKind);
    }
}

