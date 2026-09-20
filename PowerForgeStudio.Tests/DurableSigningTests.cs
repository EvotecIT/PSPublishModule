using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Orchestrator.Queue;
using PowerForgeStudio.Orchestrator.Storage;

namespace PowerForgeStudio.Tests;

public sealed class DurableSigningTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OneCallerClaimsSessionAndRestartRetainsCompletionOrInterruption(bool interrupt)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-durable-sign-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var path = Path.Combine(root, "state.db");
            var database = new ReleaseStateDatabase(path); await database.InitializeAsync();
            var item = new ReleaseQueueItem(root, "Fixture", default, default, 1, ReleaseQueueStage.Sign,
                ReleaseQueueItemStatus.WaitingApproval, "Prepared", "sign.waiting.usb", "{}", DateTimeOffset.UtcNow);
            var handoff = new ReleaseBuildHandoff(ReleaseQueueSessionFactory.Create(root, [item], DateTimeOffset.UtcNow), []);
            var executor = new WaitingSigning(interrupt);
            var workflow = new DurableReleaseSigningWorkflow(path, new ReleaseSigningWorkflow(executor));
            var running = Task.Run(() => workflow.SignAsync(handoff));
            await executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var competing = new DurableReleaseSigningWorkflow(path, new ReleaseSigningWorkflow(executor));
            await Assert.ThrowsAsync<InvalidOperationException>(() => competing.SignAsync(handoff));
            var saved = (await new ReleaseStateDatabase(path).LoadReleaseCheckpointAsync(handoff.Session.SessionId))!;
            Assert.Equal(ReleaseQueueItemStatus.Failed, Assert.Single(saved.Session.Items).Status);
            Assert.Contains("RequiresRebuild", saved.Session.Items[0].CheckpointStateJson);
            executor.Finish.TrySetResult();
            if (interrupt) await Assert.ThrowsAsync<OperationCanceledException>(() => running);
            else
            {
                var result = await running; Assert.Null(result.PersistenceError); Assert.True(result.Execution.Succeeded);
            }
            var reopened = (await new ReleaseStateDatabase(path).LoadReleaseCheckpointAsync(handoff.Session.SessionId))!;
            Assert.Equal(interrupt ? ReleaseQueueStage.Sign : ReleaseQueueStage.Publish, reopened.Session.Items.Single().Stage);
            Assert.Equal(interrupt ? 0 : 1, reopened.SigningReceipts.Count);
            Assert.Equal(1, executor.Calls);
            await Assert.ThrowsAsync<InvalidOperationException>(() => competing.SignAsync(handoff));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CompetingCheckpointWritersHaveExactlyOneWinner()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-claim-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var path = Path.Combine(root, "state.db"); var database = new ReleaseStateDatabase(path); await database.InitializeAsync();
            var initial = ReleaseQueueSessionFactory.Create(root, [], DateTimeOffset.UtcNow);
            Assert.True(await database.TryAdvanceReleaseCheckpointAsync(null, initial));
            var attempts = new[] { "one", "two" }.Select(name => Task.Run(() => new ReleaseStateDatabase(path)
                .TryAdvanceReleaseCheckpointAsync(initial, initial with { ScopeDisplayName = name }))).ToArray();
            var outcomes = await Task.WhenAll(attempts); Assert.Single(outcomes, outcome => outcome);
            Assert.False(await database.TryAdvanceReleaseCheckpointAsync(initial, initial));
            Assert.NotNull((await database.LoadReleaseCheckpointAsync(initial.SessionId))!.Session.ScopeDisplayName);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task FailedFinalizationReturnsReceiptsAndLeavesInterruptionMarker()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-finalize-failure-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var path = Path.Combine(root, "state.db"); var database = new ReleaseStateDatabase(path); await database.InitializeAsync();
            await new DBAClientX.SQLite().ExecuteNonQueryAsync(path,
                "CREATE TRIGGER reject_receipt BEFORE INSERT ON release_signing_receipt BEGIN SELECT RAISE(ABORT, 'fixture write failure'); END;");
            var item = new ReleaseQueueItem(root, "Fixture", default, default, 1, ReleaseQueueStage.Sign,
                ReleaseQueueItemStatus.WaitingApproval, "Prepared", "sign.waiting.usb", "{}", DateTimeOffset.UtcNow);
            var handoff = new ReleaseBuildHandoff(ReleaseQueueSessionFactory.Create(root, [item], DateTimeOffset.UtcNow), []);
            var executor = new WaitingSigning(false); executor.Finish.TrySetResult();
            var result = await new DurableReleaseSigningWorkflow(path, new ReleaseSigningWorkflow(executor)).SignAsync(handoff);
            Assert.True(result.Execution.Succeeded); Assert.Single(result.Execution.Receipts); Assert.NotNull(result.PersistenceError);
            var saved = (await database.LoadReleaseCheckpointAsync(handoff.Session.SessionId))!;
            Assert.Equal(ReleaseQueueItemStatus.Failed, saved.Session.Items.Single().Status);
            Assert.Empty(saved.SigningReceipts);
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class WaitingSigning(bool interrupt) : IReleaseSigningExecutionService
    {
        public int Calls;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ReleaseSigningExecutionResult> ExecuteAsync(ReleaseQueueItem item, CancellationToken token = default)
        {
            Interlocked.Increment(ref Calls); Started.TrySetResult(); await Finish.Task;
            if (interrupt) throw new OperationCanceledException();
            return new(item.RootPath, true, "Signed", item.CheckpointStateJson,
                [new(item.RootPath, item.RepositoryName, "Project", "fixture.dll", "File", ReleaseSigningReceiptStatus.Signed, "Signed", DateTimeOffset.UtcNow)]);
        }
    }
}
