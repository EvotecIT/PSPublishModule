using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Orchestrator.Queue;
using PowerForgeStudio.Orchestrator.Storage;

namespace PowerForgeStudio.Tests;

public sealed class DurablePublicationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClaimsSavedCheckpointBeforeExecutionAndRetainsCompletedOrInterruptedState(bool interrupt)
    {
        var root = NewRoot(); var executor = new ControlledPublisher(interrupt);
        Task<ReleasePublicationWorkflowResult>? running = null;
        try
        {
            var path = Path.Combine(root, "state.db"); var database = new ReleaseStateDatabase(path); await database.InitializeAsync();
            var session = Session(root); await database.PersistQueueSessionAsync(session);
            var workflow = new DurableReleasePublicationWorkflow(path, executor);
            using var cancellation = new CancellationTokenSource();
            running = workflow.PublishAsync(session, cancellation.Token);
            await executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var marker = (await new ReleaseStateDatabase(path).LoadReleaseCheckpointAsync(session.SessionId))!;
            var state = new ReleaseQueueCheckpointSerializer().TryDeserialize<ReleasePublishExecutionResult>(marker.Session.Items[0].CheckpointStateJson)!;
            Assert.True(state.RequiresReconciliation); Assert.False(state.Succeeded);
            await Assert.ThrowsAsync<InvalidOperationException>(() => new DurableReleasePublicationWorkflow(path, executor).PublishAsync(session));
            var signingItem = session.Items[0] with { Stage = ReleaseQueueStage.Sign, Status = ReleaseQueueItemStatus.WaitingApproval };
            var handoff = new ReleaseBuildHandoff(ReleaseQueueSessionFactory.Create(root, [signingItem], DateTimeOffset.UtcNow), []);
            await Assert.ThrowsAsync<InvalidOperationException>(() => new DurableReleaseSigningWorkflow(path).SignAsync(handoff));
            if (interrupt) cancellation.Cancel();
            executor.Finish.TrySetResult();
            var result = await running; Assert.Null(result.PersistenceError); Assert.Null(result.PendingCheckpoint);
            Assert.Equal(interrupt, result.Execution.RequiresReconciliation);
            Assert.Equal(interrupt, result.Execution.WasCancelled);
            var reopened = (await database.LoadReleaseCheckpointAsync(session.SessionId))!;
            Assert.Equal(interrupt ? ReleaseQueueStage.Publish : ReleaseQueueStage.Verify, reopened.Session.Items[0].Stage);
            Assert.Equal(interrupt ? 0 : 1, reopened.PublishReceipts.Count);
            await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.PublishAsync(session));
            Assert.Equal(1, executor.Calls);
        }
        finally { executor.Finish.TrySetResult(); if (running is not null) await running; Directory.Delete(root, true); }
    }

    [Fact]
    public async Task FailedReceiptSaveRollsBackAndRetryNeverRepublishes()
    {
        var root = NewRoot();
        try
        {
            var path = Path.Combine(root, "state.db"); var database = new ReleaseStateDatabase(path); await database.InitializeAsync();
            var session = Session(root); await database.PersistQueueSessionAsync(session);
            await new DBAClientX.SQLite().ExecuteNonQueryAsync(path,
                "CREATE TRIGGER reject_publish BEFORE INSERT ON release_publish_receipt BEGIN SELECT RAISE(ABORT, 'fixture write failure'); END;");
            var executor = new ControlledPublisher(false, multiple: true); executor.Finish.TrySetResult();
            var workflow = new DurableReleasePublicationWorkflow(path, executor);
            var result = await workflow.PublishAsync(session);
            Assert.NotNull(result.PersistenceError); Assert.NotNull(result.PendingCheckpoint); Assert.Equal(2, result.Execution.Receipts.Count);
            var interrupted = (await database.LoadReleaseCheckpointAsync(session.SessionId))!;
            Assert.Equal(ReleaseQueueItemStatus.Failed, interrupted.Session.Items[0].Status); Assert.Empty(interrupted.PublishReceipts);
            await new DBAClientX.SQLite().ExecuteNonQueryAsync(path, "DROP TRIGGER reject_publish;");
            var recovered = await workflow.RetrySaveAsync(result);
            Assert.Null(recovered.PersistenceError); Assert.Null(recovered.PendingCheckpoint);
            Assert.Null((await workflow.RetrySaveAsync(result)).PersistenceError);
            Assert.Equal(1, executor.Calls);
            var reopened = (await database.LoadReleaseCheckpointAsync(session.SessionId))!;
            Assert.Equal(ReleaseQueueStage.Verify, reopened.Session.Items[0].Stage); Assert.Equal(2, reopened.PublishReceipts.Count);
            await new DBAClientX.SQLite().ExecuteNonQueryAsync(path,
                "UPDATE release_publish_receipt SET summary = 'changed elsewhere' WHERE target_kind = 'GitHub';");
            Assert.NotNull((await workflow.RetrySaveAsync(result)).PersistenceError);
            Assert.Contains((await database.LoadReleaseCheckpointAsync(session.SessionId))!.PublishReceipts,
                receipt => receipt.Summary == "changed elsewhere");
            Assert.Equal(1, executor.Calls);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task MissingSavedCheckpointCannotStartPublication()
    {
        var root = NewRoot();
        try
        {
            var executor = new ControlledPublisher(false); executor.Finish.TrySetResult();
            await Assert.ThrowsAsync<InvalidOperationException>(() => new DurableReleasePublicationWorkflow(Path.Combine(root, "state.db"), executor).PublishAsync(Session(root)));
            Assert.Equal(0, executor.Calls);
        }
        finally { Directory.Delete(root, true); }
    }

    private static string NewRoot() => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-durable-publish-" + Guid.NewGuid().ToString("N"))).FullName;
    private static ReleaseQueueSession Session(string root) => ReleaseQueueSessionFactory.Create(root,
        [new(root, "Fixture", default, default, 1, ReleaseQueueStage.Publish, ReleaseQueueItemStatus.ReadyToRun, "Signed", "publish.ready", "{}", DateTimeOffset.UtcNow)], DateTimeOffset.UtcNow);

    private sealed class ControlledPublisher(bool interrupt, bool multiple = false) : IReleasePublishExecutionService
    {
        public int Calls;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ReleasePublishExecutionResult> ExecuteAsync(ReleaseQueueItem item, CancellationToken cancellationToken = default)
        {
            Calls++; Started.TrySetResult(); await Finish.Task;
            if (interrupt) throw new IOException("Unknown remote outcome");
            var receipt = new ReleasePublishReceipt(item.RootPath, item.RepositoryName, "ProjectBuild", "Fixture", "NuGet", "fixture feed", "fixture.nupkg", ReleasePublishReceiptStatus.Published, "Published", DateTimeOffset.UtcNow);
            return new(item.RootPath, true, "Published", item.CheckpointStateJson,
                multiple ? [receipt, receipt with { TargetKind = "GitHub", Destination = "fixture release", PublishedAtUtc = receipt.PublishedAtUtc.AddSeconds(1) }] : [receipt]);
        }
    }
}
