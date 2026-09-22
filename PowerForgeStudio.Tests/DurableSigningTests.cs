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
            var otherHandoff = handoff with { Session = ReleaseQueueSessionFactory.Create(root, [item], DateTimeOffset.UtcNow) };
            await Assert.ThrowsAsync<InvalidOperationException>(() => competing.SignAsync(otherHandoff));
            var saved = (await new ReleaseStateDatabase(path).LoadReleaseCheckpointAsync(handoff.Session.SessionId))!;
            Assert.Equal(ReleaseQueueItemStatus.Failed, Assert.Single(saved.Session.Items).Status);
            Assert.Contains("RequiresRebuild", saved.Session.Items[0].CheckpointStateJson);
            var runningProgress = Assert.Single(saved.Progress);
            Assert.Equal("Running", runningProgress.State);
            Assert.Equal(0, runningProgress.CompletedItems);
            executor.Finish.TrySetResult();
            if (interrupt) await Assert.ThrowsAsync<OperationCanceledException>(() => running);
            else
            {
                var result = await running; Assert.Null(result.PersistenceError); Assert.True(result.Execution.Succeeded);
            }
            var reopened = (await new ReleaseStateDatabase(path).LoadReleaseCheckpointAsync(handoff.Session.SessionId))!;
            Assert.Equal(interrupt ? ReleaseQueueStage.Sign : ReleaseQueueStage.Publish, reopened.Session.Items.Single().Stage);
            Assert.Equal(interrupt ? 0 : 1, reopened.SigningReceipts.Count);
            Assert.Equal(interrupt ? 1 : 2, reopened.Progress.Count);
            Assert.Equal("Running", reopened.Progress[0].State);
            if (!interrupt) Assert.Equal("Signed", reopened.Progress[1].State);
            Assert.Equal(1, executor.Calls);
            await Assert.ThrowsAsync<InvalidOperationException>(() => competing.SignAsync(handoff));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task SignerDoesNotMutateArtifactWhenProgressJournalCannotCommit()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-progress-failure-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var path = Path.Combine(root, "state.db");
            var database = new ReleaseStateDatabase(path); await database.InitializeAsync();
            await new DBAClientX.SQLite().ExecuteNonQueryAsync(path,
                "CREATE TRIGGER reject_progress BEFORE INSERT ON release_execution_progress BEGIN SELECT RAISE(ABORT, 'fixture journal failure'); END;");
            var item = new ReleaseQueueItem(root, "Fixture", default, default, 1, ReleaseQueueStage.Sign,
                ReleaseQueueItemStatus.WaitingApproval, "Prepared", "sign.waiting.usb", "{}", DateTimeOffset.UtcNow);
            var handoff = new ReleaseBuildHandoff(ReleaseQueueSessionFactory.Create(root, [item], DateTimeOffset.UtcNow), []);
            var executor = new ProgressBeforeMutationSigning();
            var workflow = new DurableReleaseSigningWorkflow(path, new ReleaseSigningWorkflow(executor));

            await Assert.ThrowsAnyAsync<Exception>(() => workflow.SignAsync(handoff));

            Assert.False(executor.ArtifactMutated);
            var saved = (await database.LoadReleaseCheckpointAsync(handoff.Session.SessionId))!;
            Assert.Equal(ReleaseQueueItemStatus.Failed, saved.Session.Items.Single().Status);
            Assert.Empty(saved.Progress);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DirectHistoryOpenMigratesSchemaTwentyBeforeReadingProgress()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-progress-migration-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var path = Path.Combine(root, "state.db");
            var database = new ReleaseStateDatabase(path); await database.InitializeAsync();
            var session = ReleaseQueueSessionFactory.Create(root, [], DateTimeOffset.UtcNow);
            await database.PersistQueueSessionAsync(session);
            var sqlite = new DBAClientX.SQLite();
            await sqlite.ExecuteNonQueryAsync(path,
                "DROP TABLE release_execution_progress; UPDATE app_schema SET value = '20' WHERE key = 'schema_version';");

            var reopened = await new ReleaseHistoryService(path).LoadAsync(session.SessionId);

            Assert.NotNull(reopened);
            Assert.Empty(reopened.Progress);
            var version = await sqlite.QueryReadOnlyAsListAsync(path,
                "SELECT value FROM app_schema WHERE key = 'schema_version';", reader => reader.GetString(0));
            Assert.Equal("23", Assert.Single(version));
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
            var workflow = new DurableReleaseSigningWorkflow(path, new ReleaseSigningWorkflow(executor));
            var result = await workflow.SignAsync(handoff);
            Assert.True(result.Execution.Succeeded); Assert.Single(result.Execution.Receipts); Assert.NotNull(result.PersistenceError);
            var saved = (await database.LoadReleaseCheckpointAsync(handoff.Session.SessionId))!;
            Assert.Equal(ReleaseQueueItemStatus.Failed, saved.Session.Items.Single().Status);
            Assert.Empty(saved.SigningReceipts);
            await new DBAClientX.SQLite().ExecuteNonQueryAsync(path, "DROP TRIGGER reject_receipt;");
            var recovered = await workflow.RetrySaveAsync(result);
            Assert.Null(recovered.PersistenceError); Assert.Null(recovered.PendingCheckpoint); Assert.Equal(1, executor.Calls);
            var repeated = await workflow.RetrySaveAsync(result);
            Assert.Null(repeated.PersistenceError); Assert.Equal(1, executor.Calls);
            Assert.Single((await database.LoadReleaseCheckpointAsync(handoff.Session.SessionId))!.SigningReceipts);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task WindowsCaseVariantsShareTheWorkingCopyLease()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-case-lease-" + Guid.NewGuid().ToString("N"))).FullName;
        var signer = new WaitingSigning(false); Task<ReleaseSigningWorkflowResult>? running = null;
        try
        {
            var path = Path.Combine(root, "state.db");
            var item = new ReleaseQueueItem(root, "Fixture", default, default, 1, ReleaseQueueStage.Sign,
                ReleaseQueueItemStatus.WaitingApproval, "Prepared", "sign.waiting.usb", "{}", DateTimeOffset.UtcNow);
            var handoff = new ReleaseBuildHandoff(ReleaseQueueSessionFactory.Create(root, [item], DateTimeOffset.UtcNow), []);
            running = Task.Run(() => new DurableReleaseSigningWorkflow(path, new ReleaseSigningWorkflow(signer)).SignAsync(handoff));
            await signer.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var alias = root.ToUpperInvariant(); var secondSigner = new WaitingSigning(false); secondSigner.Finish.TrySetResult();
            var second = new ReleaseBuildHandoff(ReleaseQueueSessionFactory.Create(alias, [item with { RootPath = alias }], DateTimeOffset.UtcNow), []);
            await Assert.ThrowsAsync<InvalidOperationException>(() => new DurableReleaseSigningWorkflow(path, new ReleaseSigningWorkflow(secondSigner)).SignAsync(second));
            Assert.Equal(0, secondSigner.Calls);
        }
        finally
        {
            signer.Finish.TrySetResult(); if (running is not null) await running;
            Directory.Delete(root, true);
        }
    }

    private sealed class WaitingSigning(bool interrupt) : IReleaseSigningExecutionService
    {
        public int Calls;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ReleaseSigningExecutionResult> ExecuteAsync(ReleaseQueueItem item, CancellationToken token = default)
            => ExecuteAsync(item, token, null);

        public async Task<ReleaseSigningExecutionResult> ExecuteAsync(ReleaseQueueItem item, CancellationToken token,
            IReleaseArtifactProgressSink? progress)
        {
            Interlocked.Increment(ref Calls);
            await progress.ReportAsync(ReleaseQueueStage.Sign, "fixture.dll", "fixture.dll", "Running", 0, 1,
                "Signing fixture.", CancellationToken.None);
            Started.TrySetResult(); await Finish.Task;
            if (interrupt) throw new OperationCanceledException();
            await progress.ReportAsync(ReleaseQueueStage.Sign, "fixture.dll", "fixture.dll", "Signed", 1, 1,
                "Signed fixture.", CancellationToken.None);
            return new(item.RootPath, true, "Signed", item.CheckpointStateJson,
                [new(item.RootPath, item.RepositoryName, "Project", "fixture.dll", "File", ReleaseSigningReceiptStatus.Signed, "Signed", DateTimeOffset.UtcNow)]);
        }
    }

    private sealed class ProgressBeforeMutationSigning : IReleaseSigningExecutionService
    {
        public bool ArtifactMutated { get; private set; }

        public Task<ReleaseSigningExecutionResult> ExecuteAsync(ReleaseQueueItem item, CancellationToken token = default)
            => ExecuteAsync(item, token, null);

        public async Task<ReleaseSigningExecutionResult> ExecuteAsync(ReleaseQueueItem item, CancellationToken token,
            IReleaseArtifactProgressSink? progress)
        {
            await progress.ReportAsync(ReleaseQueueStage.Sign, "fixture.dll", "fixture.dll", "Running", 0, 1,
                "Signing fixture.", CancellationToken.None);
            ArtifactMutated = true;
            return new(item.RootPath, true, "Signed", item.CheckpointStateJson, []);
        }
    }
}
