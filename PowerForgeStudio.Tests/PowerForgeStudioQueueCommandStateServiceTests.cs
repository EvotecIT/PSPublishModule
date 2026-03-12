using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioQueueCommandStateServiceTests
{
    [Fact]
    public async Task LoadResultAsync_LoadsPersistedReceiptsForSession()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "PowerForgeStudio", Guid.NewGuid().ToString("N"), "queue.db");

        try
        {
            var service = new ReleaseQueueCommandStateService();
            var stateDatabase = await service.OpenDatabaseAsync(databasePath);
            var queueItem = new ReleaseQueueItem(
                RootPath: @"C:\Support\GitHub\DbaClientX",
                RepositoryName: "DbaClientX",
                RepositoryKind: ReleaseRepositoryKind.Mixed,
                WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
                QueueOrder: 1,
                Stage: ReleaseQueueStage.Sign,
                Status: ReleaseQueueItemStatus.WaitingApproval,
                Summary: "Waiting.",
                CheckpointKey: "sign.waiting.usb",
                CheckpointStateJson: "{}",
                UpdatedAtUtc: DateTimeOffset.UtcNow);
            var session = ReleaseQueueSessionFactory.Create(@"C:\Support\GitHub", [queueItem], DateTimeOffset.UtcNow);
            await stateDatabase.PersistQueueSessionAsync(session);
            await stateDatabase.PersistSigningReceiptsAsync(session.SessionId, [
                new ReleaseSigningReceipt(
                    RootPath: queueItem.RootPath,
                    RepositoryName: queueItem.RepositoryName,
                    AdapterKind: "ProjectBuild",
                    ArtifactPath: @"C:\Support\GitHub\DbaClientX\Artifact.nupkg",
                    ArtifactKind: "File",
                    Status: ReleaseSigningReceiptStatus.Signed,
                    Summary: "Signed.",
                    SignedAtUtc: DateTimeOffset.UtcNow)
            ]);

            var result = await service.LoadResultAsync(stateDatabase, session, changed: true, message: "Loaded.");

            Assert.True(result.Changed);
            Assert.NotNull(result.QueueSession);
            Assert.Single(result.SigningReceipts);
        }
        finally
        {
            var directory = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
