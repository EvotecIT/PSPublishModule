using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Orchestrator.Queue;
using PowerForgeStudio.Orchestrator.Storage;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioReleaseQueueCommandServiceTests
{
    [Fact]
    public async Task RunNextReadyItemAsync_BuildReadyItem_ExecutesThroughReusableCommandService()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "PowerForgeStudio", Guid.NewGuid().ToString("N"), "queue.db");
        try
        {
            var stateDatabase = new ReleaseStateDatabase(databasePath);
            await stateDatabase.InitializeAsync();

            var session = CreateSession(new ReleaseQueueItem(
                RootPath: @"C:\Support\GitHub\DbaClientX",
                RepositoryName: "DbaClientX",
                RepositoryKind: ReleaseRepositoryKind.Library,
                WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
                QueueOrder: 1,
                Stage: ReleaseQueueStage.Build,
                Status: ReleaseQueueItemStatus.ReadyToRun,
                Summary: "Ready for build.",
                CheckpointKey: "build.ready",
                CheckpointStateJson: null,
                UpdatedAtUtc: DateTimeOffset.UtcNow));
            await stateDatabase.PersistQueueSessionAsync(session);

            var service = new ReleaseQueueCommandService(
                new ReleaseQueuePlanner(),
                new ReleaseQueueRunner(),
                new StubBuildExecutionService(succeeded: true),
                new StubSigningExecutionService(),
                new StubPublishExecutionService(),
                new StubVerificationExecutionService());

            var result = await service.RunNextReadyItemAsync(databasePath);

            Assert.True(result.Changed);
            Assert.NotNull(result.QueueSession);
            Assert.Equal(ReleaseQueueStage.Sign, result.QueueSession!.Items[0].Stage);
            Assert.Equal(ReleaseQueueItemStatus.WaitingApproval, result.QueueSession.Items[0].Status);
            Assert.Contains("USB signing approval", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteParentDirectory(databasePath);
        }
    }

    [Fact]
    public async Task ApproveUsbAsync_PersistsSigningReceiptsAndMovesQueueForward()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "PowerForgeStudio", Guid.NewGuid().ToString("N"), "queue.db");
        try
        {
            var stateDatabase = new ReleaseStateDatabase(databasePath);
            await stateDatabase.InitializeAsync();

            var session = CreateSession(new ReleaseQueueItem(
                RootPath: @"C:\Support\GitHub\PSWriteHTML",
                RepositoryName: "PSWriteHTML",
                RepositoryKind: ReleaseRepositoryKind.Module,
                WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
                QueueOrder: 1,
                Stage: ReleaseQueueStage.Sign,
                Status: ReleaseQueueItemStatus.WaitingApproval,
                Summary: "Waiting for USB token.",
                CheckpointKey: "sign.waiting.usb",
                CheckpointStateJson: "{}",
                UpdatedAtUtc: DateTimeOffset.UtcNow));
            await stateDatabase.PersistQueueSessionAsync(session);

            var receipt = new ReleaseSigningReceipt(
                RootPath: session.Items[0].RootPath,
                RepositoryName: session.Items[0].RepositoryName,
                AdapterKind: "ModuleBuild",
                ArtifactPath: @"C:\Support\GitHub\PSWriteHTML\Output\PSWriteHTML.nupkg",
                ArtifactKind: "NuGetPackage",
                Status: ReleaseSigningReceiptStatus.Signed,
                Summary: "Package signed.",
                SignedAtUtc: DateTimeOffset.UtcNow);

            var service = new ReleaseQueueCommandService(
                new ReleaseQueuePlanner(),
                new ReleaseQueueRunner(),
                new StubBuildExecutionService(succeeded: true),
                new StubSigningExecutionService(receipts: [receipt]),
                new StubPublishExecutionService(),
                new StubVerificationExecutionService());

            var result = await service.ApproveUsbAsync(databasePath);

            Assert.True(result.Changed);
            Assert.NotNull(result.QueueSession);
            Assert.Equal(ReleaseQueueStage.Publish, result.QueueSession!.Items[0].Stage);
            Assert.Equal(ReleaseQueueItemStatus.ReadyToRun, result.QueueSession.Items[0].Status);
            Assert.Single(result.SigningReceipts);
            Assert.Equal(ReleaseSigningReceiptStatus.Signed, result.SigningReceipts[0].Status);
        }
        finally
        {
            DeleteParentDirectory(databasePath);
        }
    }

    private static ReleaseQueueSession CreateSession(ReleaseQueueItem item)
        => new(
            SessionId: Guid.NewGuid().ToString("N"),
            WorkspaceRoot: @"C:\Support\GitHub",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            Summary: new ReleaseQueueSummary(
                TotalItems: 1,
                BuildReadyItems: item.Stage == ReleaseQueueStage.Build && item.Status == ReleaseQueueItemStatus.ReadyToRun ? 1 : 0,
                PreparePendingItems: item.Stage == ReleaseQueueStage.Prepare && item.Status == ReleaseQueueItemStatus.Pending ? 1 : 0,
                WaitingApprovalItems: item.Status == ReleaseQueueItemStatus.WaitingApproval ? 1 : 0,
                BlockedItems: item.Status is ReleaseQueueItemStatus.Blocked or ReleaseQueueItemStatus.Failed ? 1 : 0,
                VerificationReadyItems: item.Stage == ReleaseQueueStage.Verify && item.Status == ReleaseQueueItemStatus.ReadyToRun ? 1 : 0),
            Items: [item]);

    private static void DeleteParentDirectory(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class StubBuildExecutionService(bool succeeded) : IReleaseBuildExecutionService
    {
        public Task<ReleaseBuildExecutionResult> ExecuteAsync(string rootPath, CancellationToken cancellationToken = default)
            => Task.FromResult(new ReleaseBuildExecutionResult(
                RootPath: rootPath,
                Succeeded: succeeded,
                Summary: succeeded ? "Build completed safely." : "Build failed.",
                DurationSeconds: 1.2,
                AdapterResults: []));
    }

    private sealed class StubSigningExecutionService(IReadOnlyList<ReleaseSigningReceipt>? receipts = null) : IReleaseSigningExecutionService
    {
        public Task<ReleaseSigningExecutionResult> ExecuteAsync(ReleaseQueueItem queueItem, CancellationToken cancellationToken = default)
            => Task.FromResult(new ReleaseSigningExecutionResult(
                RootPath: queueItem.RootPath,
                Succeeded: true,
                Summary: "Signing completed safely.",
                SourceCheckpointStateJson: queueItem.CheckpointStateJson,
                Receipts: receipts ?? []));
    }

    private sealed class StubPublishExecutionService : IReleasePublishExecutionService
    {
        public Task<ReleasePublishExecutionResult> ExecuteAsync(ReleaseQueueItem queueItem, CancellationToken cancellationToken = default)
            => Task.FromResult(new ReleasePublishExecutionResult(
                RootPath: queueItem.RootPath,
                Succeeded: true,
                Summary: "Publish completed safely.",
                SourceCheckpointStateJson: queueItem.CheckpointStateJson,
                Receipts: []));
    }

    private sealed class StubVerificationExecutionService : IReleaseVerificationExecutionService
    {
        public Task<ReleaseVerificationExecutionResult> ExecuteAsync(ReleaseQueueItem queueItem, CancellationToken cancellationToken = default)
            => Task.FromResult(new ReleaseVerificationExecutionResult(
                RootPath: queueItem.RootPath,
                Succeeded: true,
                Summary: "Verification completed safely.",
                SourceCheckpointStateJson: queueItem.CheckpointStateJson,
                Receipts: []));
    }
}
