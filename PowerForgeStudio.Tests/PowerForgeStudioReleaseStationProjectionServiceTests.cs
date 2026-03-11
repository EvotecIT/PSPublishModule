using System.Text.Json;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Domain.Verification;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioReleaseStationProjectionServiceTests
{
    [Fact]
    public void BuildSnapshots_ProjectsSigningPublishAndVerificationStations()
    {
        var buildResult = new ReleaseBuildExecutionResult(
            RootPath: @"C:\Support\GitHub\DbaClientX",
            Succeeded: true,
            Summary: "Build completed safely.",
            DurationSeconds: 1.2,
            AdapterResults: [
                new ReleaseBuildAdapterResult(
                    AdapterKind: ReleaseBuildAdapterKind.ProjectBuild,
                    Succeeded: true,
                    Summary: "Project build completed.",
                    ExitCode: 0,
                    DurationSeconds: 1.2,
                    ArtifactDirectories: [@"C:\Support\GitHub\DbaClientX\Artefacts"],
                    ArtifactFiles: [@"C:\Support\GitHub\DbaClientX\Artefacts\DbaClientX.1.0.0.nupkg"])
            ]);

        var signingResult = new ReleaseSigningExecutionResult(
            RootPath: @"C:\Support\GitHub\DbaClientX",
            Succeeded: true,
            Summary: "Signing completed safely.",
            SourceCheckpointStateJson: JsonSerializer.Serialize(buildResult),
            Receipts: [
                new ReleaseSigningReceipt(
                    RootPath: @"C:\Support\GitHub\DbaClientX",
                    RepositoryName: "DbaClientX",
                    AdapterKind: ReleaseBuildAdapterKind.ProjectBuild.ToString(),
                    ArtifactPath: @"C:\Support\GitHub\DbaClientX\Artefacts\DbaClientX.1.0.0.nupkg",
                    ArtifactKind: "NuGetPackage",
                    Status: ReleaseSigningReceiptStatus.Signed,
                    Summary: "Package signed.",
                    SignedAtUtc: DateTimeOffset.UtcNow)
            ]);

        var publishResult = new ReleasePublishExecutionResult(
            RootPath: @"C:\Support\GitHub\DbaClientX",
            Succeeded: true,
            Summary: "Publish completed safely.",
            SourceCheckpointStateJson: JsonSerializer.Serialize(signingResult),
            Receipts: [
                new ReleasePublishReceipt(
                    RootPath: @"C:\Support\GitHub\DbaClientX",
                    RepositoryName: "DbaClientX",
                    AdapterKind: ReleaseBuildAdapterKind.ProjectBuild.ToString(),
                    TargetName: "NuGet package",
                    TargetKind: "NuGet",
                    Destination: "Configured NuGet feed",
                    SourcePath: @"C:\Support\GitHub\DbaClientX\Artefacts\DbaClientX.1.0.0.nupkg",
                    Status: ReleasePublishReceiptStatus.Published,
                    Summary: "Published to NuGet.",
                    PublishedAtUtc: DateTimeOffset.UtcNow)
            ]);

        var queueSession = new ReleaseQueueSession(
            SessionId: Guid.NewGuid().ToString("N"),
            WorkspaceRoot: @"C:\Support\GitHub",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            Summary: new ReleaseQueueSummary(3, 0, 0, 1, 0, 1),
            Items: [
                new ReleaseQueueItem(
                    RootPath: @"C:\Support\GitHub\DbaClientX",
                    RepositoryName: "DbaClientX",
                    RepositoryKind: ReleaseRepositoryKind.Library,
                    WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
                    QueueOrder: 1,
                    Stage: ReleaseQueueStage.Sign,
                    Status: ReleaseQueueItemStatus.WaitingApproval,
                    Summary: "Waiting on USB signing.",
                    CheckpointKey: "sign.waiting.usb",
                    CheckpointStateJson: JsonSerializer.Serialize(buildResult),
                    UpdatedAtUtc: DateTimeOffset.UtcNow),
                new ReleaseQueueItem(
                    RootPath: @"C:\Support\GitHub\DbaClientX",
                    RepositoryName: "DbaClientX",
                    RepositoryKind: ReleaseRepositoryKind.Library,
                    WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
                    QueueOrder: 2,
                    Stage: ReleaseQueueStage.Publish,
                    Status: ReleaseQueueItemStatus.ReadyToRun,
                    Summary: "Ready to publish.",
                    CheckpointKey: "publish.ready",
                    CheckpointStateJson: JsonSerializer.Serialize(signingResult),
                    UpdatedAtUtc: DateTimeOffset.UtcNow),
                new ReleaseQueueItem(
                    RootPath: @"C:\Support\GitHub\DbaClientX",
                    RepositoryName: "DbaClientX",
                    RepositoryKind: ReleaseRepositoryKind.Library,
                    WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
                    QueueOrder: 3,
                    Stage: ReleaseQueueStage.Verify,
                    Status: ReleaseQueueItemStatus.ReadyToRun,
                    Summary: "Ready to verify.",
                    CheckpointKey: "verify.ready",
                    CheckpointStateJson: JsonSerializer.Serialize(publishResult),
                    UpdatedAtUtc: DateTimeOffset.UtcNow)
            ]);

        var service = new ReleaseStationProjectionService();
        var signingStation = service.BuildSigningStation(queueSession);
        var publishStation = service.BuildPublishStation(queueSession);
        var verificationStation = service.BuildVerificationStation(queueSession);
        var signingBatch = service.BuildSigningReceipts(signingResult.Receipts);

        Assert.Equal(2, signingStation.Items.Count);
        Assert.Contains("waiting for USB approval", signingStation.Headline, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(signingStation.Items, item => item.ArtifactKind == "File");
        Assert.Contains(signingStation.Items, item => item.ArtifactKind == "Directory");
        Assert.Single(publishStation.Items);
        Assert.Equal("NuGet", publishStation.Items[0].TargetKind);
        Assert.Single(verificationStation.Items);
        Assert.Equal("NuGet", verificationStation.Items[0].TargetKind);
        Assert.Contains("signed", signingBatch.Headline, StringComparison.OrdinalIgnoreCase);
    }
}
