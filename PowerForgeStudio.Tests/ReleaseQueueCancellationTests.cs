using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Verification;
using PowerForgeStudio.Orchestrator.Queue;
using PowerForgeStudio.Orchestrator.Storage;

namespace PowerForgeStudio.Tests;

public sealed class ReleaseQueueCancellationTests
{
    [Theory]
    [InlineData(ReleaseQueueStage.Build, ReleaseQueueStage.Build, ReleaseQueueItemStatus.Failed)]
    [InlineData(ReleaseQueueStage.Publish, ReleaseQueueStage.Verify, ReleaseQueueItemStatus.ReadyToRun)]
    [InlineData(ReleaseQueueStage.Verify, ReleaseQueueStage.Completed, ReleaseQueueItemStatus.Succeeded)]
    public async Task ReturnedEvidenceIsSavedEvenWhenExecutionCancelsItsToken(ReleaseQueueStage stage, ReleaseQueueStage expectedStage, ReleaseQueueItemStatus expectedStatus)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-stage-finalization-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var path = Path.Combine(root, "state.db");
            var database = new ReleaseStateDatabase(path); await database.InitializeAsync();
            var item = new ReleaseQueueItem(root, "Fixture", ReleaseRepositoryKind.Library, ReleaseWorkspaceKind.PrimaryRepository, 1,
                stage, ReleaseQueueItemStatus.ReadyToRun, "Ready", "fixture", null, DateTimeOffset.UtcNow);
            var session = ReleaseQueueSessionFactory.Create(root, [item], DateTimeOffset.UtcNow);
            await database.PersistQueueSessionAsync(session);
            using var cancellation = new CancellationTokenSource();
            var executor = new CancellingExecutor(cancellation);
            var service = new ReleaseQueueCommandService(new(), new(), executor, executor, executor, executor);
            await service.RunNextReadyItemAsync(path, cancellation.Token);
            Assert.True(cancellation.IsCancellationRequested);
            var persisted = Assert.Single((await database.LoadLatestQueueSessionAsync())!.Items);
            Assert.Equal(expectedStage, persisted.Stage); Assert.Equal(expectedStatus, persisted.Status);
            if (stage == ReleaseQueueStage.Publish) Assert.Equal(ReleasePublishReceiptStatus.Published, Assert.Single(await database.LoadPublishReceiptsAsync(session.SessionId)).Status);
            if (stage == ReleaseQueueStage.Verify) Assert.Equal(ReleaseVerificationReceiptStatus.Verified, Assert.Single(await database.LoadVerificationReceiptsAsync(session.SessionId)).Status);
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class CancellingExecutor(CancellationTokenSource cancellation) : IReleaseBuildExecutionService, IReleaseSigningExecutionService, IReleasePublishExecutionService, IReleaseVerificationExecutionService
    {
        public Task<ReleaseBuildExecutionResult> ExecuteAsync(string root, CancellationToken token = default, IProgress<ReleaseBuildProgress>? progress = null)
        { cancellation.Cancel(); return Task.FromResult(new ReleaseBuildExecutionResult(root, true, "Built", 1, [])); }
        Task<ReleaseSigningExecutionResult> IReleaseSigningExecutionService.ExecuteAsync(ReleaseQueueItem item, CancellationToken token)
            => throw new InvalidOperationException("Signing not part of this fixture");
        Task<ReleasePublishExecutionResult> IReleasePublishExecutionService.ExecuteAsync(ReleaseQueueItem item, CancellationToken token)
        {
            cancellation.Cancel();
            return Task.FromResult(new ReleasePublishExecutionResult(item.RootPath, true, "Published", null,
                [new(item.RootPath, item.RepositoryName, "Fixture", "Package", "NuGet", "local fixture", null, ReleasePublishReceiptStatus.Published, "Published", DateTimeOffset.UtcNow)]));
        }
        Task<ReleaseVerificationExecutionResult> IReleaseVerificationExecutionService.ExecuteAsync(ReleaseQueueItem item, CancellationToken token)
        {
            cancellation.Cancel();
            return Task.FromResult(new ReleaseVerificationExecutionResult(item.RootPath, true, "Verified", null,
                [new(item.RootPath, item.RepositoryName, "Fixture", "Package", "NuGet", "local fixture", ReleaseVerificationReceiptStatus.Verified, "Verified", DateTimeOffset.UtcNow)]));
        }
    }
}
