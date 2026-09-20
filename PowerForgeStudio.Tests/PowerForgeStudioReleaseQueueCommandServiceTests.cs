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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApproveUsbAsync_PersistsSigningReceiptsAndMovesQueueForward(bool cancelAfterExecution)
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

            using var cancellation = new CancellationTokenSource();
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
                new StubSigningExecutionService(receipts: [receipt], beforeReturn: () => { if (cancelAfterExecution) cancellation.Cancel(); }),
                new StubPublishExecutionService(),
                new StubVerificationExecutionService());

            var result = await service.ApproveUsbAsync(databasePath, cancellation.Token);

            Assert.True(result.Changed);
            Assert.NotNull(result.QueueSession);
            Assert.Equal(cancelAfterExecution ? ReleaseQueueStage.Sign : ReleaseQueueStage.Publish, result.QueueSession!.Items[0].Stage);
            Assert.Equal(cancelAfterExecution ? ReleaseQueueItemStatus.Failed : ReleaseQueueItemStatus.ReadyToRun, result.QueueSession.Items[0].Status);
            var persisted = await stateDatabase.LoadLatestQueueSessionAsync();
            Assert.Equal(result.QueueSession.Items[0].Status, persisted!.Items[0].Status);
            Assert.Single(await stateDatabase.LoadSigningReceiptsAsync(session.SessionId));
            Assert.Single(result.SigningReceipts);
            Assert.Equal(ReleaseSigningReceiptStatus.Signed, result.SigningReceipts[0].Status);
            if (cancelAfterExecution)
            {
                var retry = await service.RetryFailedAsync(databasePath);
                Assert.Equal(ReleaseQueueStage.Build, Assert.Single(retry.QueueSession!.Items).Stage);
                Assert.Null(retry.QueueSession.Items[0].CheckpointStateJson);
            }
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
            Summary: ReleaseQueueSummaryFactory.Create([item]),
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
        public Task<ReleaseBuildExecutionResult> ExecuteAsync(string rootPath, CancellationToken cancellationToken = default, IProgress<ReleaseBuildProgress>? progress = null)
            => Task.FromResult(new ReleaseBuildExecutionResult(
                RootPath: rootPath,
                Succeeded: succeeded,
                Summary: succeeded ? "Build completed safely." : "Build failed.",
                DurationSeconds: 1.2,
                AdapterResults: []));
    }

    private sealed class StubSigningExecutionService(IReadOnlyList<ReleaseSigningReceipt>? receipts = null, Action? beforeReturn = null) : IReleaseSigningExecutionService
    {
        public Task<ReleaseSigningExecutionResult> ExecuteAsync(ReleaseQueueItem queueItem, CancellationToken cancellationToken = default)
        {
            beforeReturn?.Invoke();
            return Task.FromResult(new ReleaseSigningExecutionResult(
                RootPath: queueItem.RootPath,
                Succeeded: true,
                Summary: "Signing completed safely.",
                SourceCheckpointStateJson: queueItem.CheckpointStateJson,
                Receipts: receipts ?? []));
        }
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
