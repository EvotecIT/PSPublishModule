using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Verification;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioQueueExecutionResultFactoryTests
{
    [Fact]
    public void CreatePublishResult_UsesPublishedSkippedFailedCounts()
    {
        var queueItem = new ReleaseQueueItem(
            RootPath: @"C:\Support\GitHub\Testimo",
            RepositoryName: "Testimo",
            RepositoryKind: Domain.Catalog.ReleaseRepositoryKind.Module,
            WorkspaceKind: Domain.Catalog.ReleaseWorkspaceKind.PrimaryRepository,
            QueueOrder: 1,
            Stage: ReleaseQueueStage.Publish,
            Status: ReleaseQueueItemStatus.ReadyToRun,
            Summary: "Ready.",
            CheckpointKey: "publish.ready",
            CheckpointStateJson: "{}",
            UpdatedAtUtc: DateTimeOffset.UtcNow);
        var receipts = new[] {
            ReleaseQueueReceiptFactory.CreatePublishReceipt(queueItem.RootPath, queueItem.RepositoryName, "ModuleBuild", "Module publish", "PowerShellRepository", "PSGallery", ReleasePublishReceiptStatus.Published, "Published."),
            ReleaseQueueReceiptFactory.CreatePublishReceipt(queueItem.RootPath, queueItem.RepositoryName, "ModuleBuild", "GitHub release", "GitHub", "EvotecIT/Testimo", ReleasePublishReceiptStatus.Skipped, "Skipped."),
            ReleaseQueueReceiptFactory.CreatePublishReceipt(queueItem.RootPath, queueItem.RepositoryName, "ModuleBuild", "NuGet publish", "NuGet", "nuget.org", ReleasePublishReceiptStatus.Failed, "Failed.")
        };

        var result = ReleaseQueueExecutionResultFactory.CreatePublishResult(queueItem, receipts);

        Assert.False(result.Succeeded);
        Assert.Equal("Publish completed with 1 published, 1 skipped, and 1 failed target(s).", result.Summary);
    }

    [Fact]
    public void CreateVerificationResult_UsesVerifiedSkippedCountsWhenNoFailures()
    {
        var queueItem = new ReleaseQueueItem(
            RootPath: @"C:\Support\GitHub\DbaClientX",
            RepositoryName: "DbaClientX",
            RepositoryKind: Domain.Catalog.ReleaseRepositoryKind.Mixed,
            WorkspaceKind: Domain.Catalog.ReleaseWorkspaceKind.PrimaryRepository,
            QueueOrder: 1,
            Stage: ReleaseQueueStage.Verify,
            Status: ReleaseQueueItemStatus.ReadyToRun,
            Summary: "Ready.",
            CheckpointKey: "verify.ready",
            CheckpointStateJson: "{}",
            UpdatedAtUtc: DateTimeOffset.UtcNow);
        var publishReceipt = ReleaseQueueReceiptFactory.CreatePublishReceipt(
            queueItem.RootPath,
            queueItem.RepositoryName,
            "ProjectBuild",
            "GitHub release",
            "GitHub",
            "EvotecIT/DbaClientX",
            ReleasePublishReceiptStatus.Published,
            "Published.");
        var receipts = new[] {
            ReleaseQueueReceiptFactory.CreateVerificationReceipt(publishReceipt, ReleaseVerificationReceiptStatus.Verified, "Verified."),
            ReleaseQueueReceiptFactory.CreateVerificationReceipt(publishReceipt, ReleaseVerificationReceiptStatus.Skipped, "Skipped.")
        };

        var result = ReleaseQueueExecutionResultFactory.CreateVerificationResult(queueItem, receipts);

        Assert.True(result.Succeeded);
        Assert.Equal("Verification completed with 1 verified and 1 skipped check(s).", result.Summary);
    }
}
