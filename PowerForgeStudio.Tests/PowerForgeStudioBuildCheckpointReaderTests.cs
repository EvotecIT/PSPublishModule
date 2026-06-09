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

        Assert.Equal(2, manifest.Count);
        Assert.Contains(manifest, artifact => artifact.RepositoryName == "DbaClientX" && artifact.DisplayName == "DbaClientX.Core.0.2.0.nupkg" && artifact.ArtifactKind == "File");
        Assert.Contains(manifest, artifact => artifact.RepositoryName == "DbaClientX" && artifact.DisplayName == "ProjectBuild" && artifact.ArtifactKind == "Directory");
    }

    [Fact]
    public void BuildSigningManifest_IncludesDirectoriesEvenWhenFilesWereCaptured()
    {
        var buildResult = new ReleaseBuildExecutionResult(
            RootPath: @"C:\Support\GitHub\PSWriteHTML",
            Succeeded: true,
            Summary: "Build completed safely.",
            DurationSeconds: 8.4,
            AdapterResults: [
                new ReleaseBuildAdapterResult(
                    ReleaseBuildAdapterKind.ModuleBuild,
                    true,
                    "Module build completed.",
                    0,
                    8.4,
                    [@"C:\Support\GitHub\PSWriteHTML\Artefacts\Packed"],
                    [@"C:\Support\GitHub\PSWriteHTML\Artefacts\Packed\PSWriteHTML.1.0.0.nupkg"])
            ]);

        var queueItem = new ReleaseQueueItem(
            RootPath: buildResult.RootPath,
            RepositoryName: "PSWriteHTML",
            RepositoryKind: ReleaseRepositoryKind.Module,
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

        Assert.Equal(2, manifest.Count);
        Assert.Contains(manifest, artifact => artifact.ArtifactKind == "File");
        Assert.Contains(manifest, artifact => artifact.ArtifactKind == "Directory");
    }
}

