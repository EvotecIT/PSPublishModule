using DBAClientX;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Verification;
using PowerForgeStudio.Orchestrator.Queue;
using PowerForgeStudio.Orchestrator.Storage;

namespace PowerForgeStudio.Tests;

public sealed class DurableVerificationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClaimsSavedCheckpointBeforeExecutionAndRetainsCompletedOrInterruptedState(bool interrupt)
    {
        var root = NewRoot();
        var executor = new ControlledVerifier(interrupt);
        Task<ReleaseVerificationWorkflowResult>? running = null;
        try
        {
            var path = Path.Combine(root, "state.db");
            var database = new ReleaseStateDatabase(path);
            await database.InitializeAsync();
            var session = Session(root);
            await database.PersistQueueSessionAsync(session);
            using var workflow = new DurableReleaseVerificationWorkflow(path, executor);
            using var cancellation = new CancellationTokenSource();
            running = workflow.VerifyAsync(session, cancellation.Token);
            await executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

            var marker = (await database.LoadReleaseCheckpointAsync(session.SessionId))!;
            var state = new ReleaseQueueCheckpointSerializer().TryDeserialize<ReleaseVerificationExecutionResult>(marker.Session.Items[0].CheckpointStateJson)!;
            Assert.False(state.Succeeded);
            Assert.Equal(ReleaseQueueStage.Verify, marker.Session.Items[0].Stage);
            Assert.Equal(ReleaseQueueItemStatus.Failed, marker.Session.Items[0].Status);
            await Assert.ThrowsAsync<InvalidOperationException>(() => new DurableReleaseVerificationWorkflow(path, executor).VerifyAsync(session));

            var signingItem = session.Items[0] with { Stage = ReleaseQueueStage.Sign, Status = ReleaseQueueItemStatus.WaitingApproval };
            var handoff = new ReleaseBuildHandoff(ReleaseQueueSessionFactory.Create(root, [signingItem], DateTimeOffset.UtcNow), []);
            await Assert.ThrowsAsync<InvalidOperationException>(() => new DurableReleaseSigningWorkflow(path).SignAsync(handoff));
            if (interrupt) cancellation.Cancel();
            executor.Finish.TrySetResult();

            var result = await running;
            Assert.Null(result.PersistenceError);
            Assert.Null(result.PendingCheckpoint);
            Assert.Equal(interrupt, result.Execution.WasCancelled);
            var reopened = (await database.LoadReleaseCheckpointAsync(session.SessionId))!;
            Assert.Equal(interrupt ? ReleaseQueueStage.Verify : ReleaseQueueStage.Completed, reopened.Session.Items[0].Stage);
            Assert.Equal(interrupt ? 0 : 2, reopened.VerificationReceipts.Count);
            await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.VerifyAsync(session));
            Assert.Equal(1, executor.Calls);
        }
        finally
        {
            executor.Finish.TrySetResult();
            if (running is not null) await running;
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task FailedReceiptSaveRollsBackAndRetryNeverRepeatsRemoteChecks()
    {
        var root = NewRoot();
        try
        {
            var path = Path.Combine(root, "state.db");
            var database = new ReleaseStateDatabase(path);
            await database.InitializeAsync();
            var session = Session(root);
            await database.PersistQueueSessionAsync(session);
            await new SQLite().ExecuteNonQueryAsync(path,
                "CREATE TRIGGER reject_verify BEFORE INSERT ON release_verification_receipt BEGIN SELECT RAISE(ABORT, 'fixture write failure'); END;");
            var executor = new ControlledVerifier(false);
            executor.Finish.TrySetResult();
            using var workflow = new DurableReleaseVerificationWorkflow(path, executor);

            var result = await workflow.VerifyAsync(session);
            Assert.NotNull(result.PersistenceError);
            Assert.NotNull(result.PendingCheckpoint);
            Assert.Equal(2, result.Execution.Receipts.Count);
            var marker = (await database.LoadReleaseCheckpointAsync(session.SessionId))!;
            Assert.Equal(ReleaseQueueItemStatus.Failed, marker.Session.Items[0].Status);
            Assert.Empty(marker.VerificationReceipts);

            await new SQLite().ExecuteNonQueryAsync(path, "DROP TRIGGER reject_verify;");
            var recovered = await workflow.RetrySaveAsync(result);
            Assert.Null(recovered.PersistenceError);
            Assert.Null(recovered.PendingCheckpoint);
            Assert.Null((await workflow.RetrySaveAsync(result)).PersistenceError);
            Assert.Equal(1, executor.Calls);
            var reopened = (await database.LoadReleaseCheckpointAsync(session.SessionId))!;
            Assert.Equal(ReleaseQueueStage.Completed, reopened.Session.Items[0].Stage);
            Assert.Equal(2, reopened.VerificationReceipts.Count);

            await new SQLite().ExecuteNonQueryAsync(path,
                "UPDATE release_verification_receipt SET summary = 'changed elsewhere' WHERE destination = 'fixture release';");
            Assert.NotNull((await workflow.RetrySaveAsync(result)).PersistenceError);
            Assert.Contains((await database.LoadReleaseCheckpointAsync(session.SessionId))!.VerificationReceipts,
                receipt => receipt.Summary == "changed elsewhere");
            Assert.Equal(1, executor.Calls);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task MissingSavedCheckpointCannotStartVerification()
    {
        var root = NewRoot();
        try
        {
            var executor = new ControlledVerifier(false);
            executor.Finish.TrySetResult();
            using var workflow = new DurableReleaseVerificationWorkflow(Path.Combine(root, "state.db"), executor);
            await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.VerifyAsync(Session(root)));
            Assert.Equal(0, executor.Calls);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task EvidenceForAnotherWorkingCopyIsRejectedBeforePersistence()
    {
        var root = NewRoot();
        try
        {
            var path = Path.Combine(root, "state.db");
            var database = new ReleaseStateDatabase(path);
            await database.InitializeAsync();
            var session = Session(root);
            await database.PersistQueueSessionAsync(session);
            var executor = new ControlledVerifier(false, wrongRoot: true);
            executor.Finish.TrySetResult();
            using var workflow = new DurableReleaseVerificationWorkflow(path, executor);

            var result = await workflow.VerifyAsync(session);

            Assert.False(result.Execution.Succeeded);
            Assert.Empty(result.Execution.Receipts);
            Assert.Equal(1, executor.Calls);
            var reopened = (await database.LoadReleaseCheckpointAsync(session.SessionId))!;
            Assert.Equal(ReleaseQueueItemStatus.Failed, reopened.Session.Items[0].Status);
            Assert.Empty(reopened.VerificationReceipts);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string NewRoot()
        => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-durable-verify-" + Guid.NewGuid().ToString("N"))).FullName;

    private static ReleaseQueueSession Session(string root)
        => ReleaseQueueSessionFactory.Create(root,
            [new(root, "Fixture", default, default, 1, ReleaseQueueStage.Verify, ReleaseQueueItemStatus.ReadyToRun,
                "Published", "verify.ready", "{}", DateTimeOffset.UtcNow)], DateTimeOffset.UtcNow);

    private sealed class ControlledVerifier(bool interrupt, bool wrongRoot = false) : IReleaseVerificationExecutionService
    {
        public int Calls;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ReleaseVerificationExecutionResult> ExecuteAsync(ReleaseQueueItem item, CancellationToken cancellationToken = default)
        {
            Calls++;
            Started.TrySetResult();
            await Finish.Task;
            if (interrupt) throw new OperationCanceledException("fixture secret", cancellationToken);
            var evidenceRoot = wrongRoot ? item.RootPath + "-other" : item.RootPath;
            var receipt = new ReleaseVerificationReceipt(evidenceRoot, item.RepositoryName, "ProjectBuild", "Fixture", "NuGet",
                "fixture feed", ReleaseVerificationReceiptStatus.Verified, "Verified", DateTimeOffset.UtcNow);
            return new(evidenceRoot, true, "Verified", item.CheckpointStateJson,
                [receipt, receipt with { Destination = "fixture release", VerifiedAtUtc = receipt.VerifiedAtUtc.AddSeconds(1) }]);
        }
    }
}
