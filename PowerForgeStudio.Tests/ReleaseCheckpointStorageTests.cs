using System.Collections;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Orchestrator.Queue;
using PowerForgeStudio.Orchestrator.Storage;

namespace PowerForgeStudio.Tests;

public sealed class ReleaseCheckpointStorageTests
{
    [Theory]
    [InlineData("receipt")]
    [InlineData("item")]
    [InlineData("cancel")]
    public async Task FailedCheckpointWriteRetainsPriorHeaderItemsAndReceipts(string failure)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-atomic-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var database = new ReleaseStateDatabase(Path.Combine(root, "state.db")); await database.InitializeAsync();
            var item = new ReleaseQueueItem(root, "Fixture", default, default, 1, ReleaseQueueStage.Sign,
                ReleaseQueueItemStatus.WaitingApproval, "Original checkpoint", "sign.waiting.usb", "original", DateTimeOffset.UtcNow);
            var session = ReleaseQueueSessionFactory.Create(root, [item], DateTimeOffset.UtcNow);
            var receipt = new ReleaseSigningReceipt(root, "Fixture", "Project", Path.Combine(root, "first.dll"), "File",
                ReleaseSigningReceiptStatus.Signed, "Original evidence", DateTimeOffset.UtcNow);
            await database.PersistReleaseCheckpointAsync(session, [receipt]);
            using var cancellation = new CancellationTokenSource();
            var updated = session with { WorkspaceRoot = Path.Combine(root, "changed"), Items = [item with {
                Summary = failure == "item" ? null! : "Changed checkpoint", Stage = ReleaseQueueStage.Publish }] };
            IReadOnlyList<ReleaseSigningReceipt> receipts = failure == "cancel"
                ? new CancellingReceipts(receipt, cancellation)
                : [receipt with { Summary = "New first receipt" }, receipt with { ArtifactPath = "second.dll", RootPath = null! }];
            await Assert.ThrowsAnyAsync<Exception>(() => database.PersistReleaseCheckpointAsync(updated, receipts, cancellationToken: cancellation.Token));
            var reopened = new ReleaseStateDatabase(database.DatabasePath);
            var snapshot = await reopened.LoadReleaseCheckpointAsync(session.SessionId);
            Assert.NotNull(snapshot); Assert.Equal(root, snapshot.Session.WorkspaceRoot);
            Assert.Equal("Original checkpoint", Assert.Single(snapshot.Session.Items).Summary);
            Assert.Equal("Original evidence", Assert.Single(snapshot.SigningReceipts).Summary);
            if (failure == "cancel") Assert.True(cancellation.IsCancellationRequested);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CapturedSessionReadDoesNotFollowANewerQueueAndNullReceiptSetsPreserveEvidence()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-session-id-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var database = new ReleaseStateDatabase(Path.Combine(root, "state.db")); await database.InitializeAsync();
            var first = ReleaseQueueSessionFactory.Create(root, [], DateTimeOffset.UtcNow);
            var receipt = new ReleaseSigningReceipt(root, "Fixture", "Project", "artifact.dll", "File", ReleaseSigningReceiptStatus.Signed, "Signed", DateTimeOffset.UtcNow);
            await database.PersistReleaseCheckpointAsync(first, [receipt]);
            await database.PersistQueueSessionAsync(first);
            var second = ReleaseQueueSessionFactory.Create(Path.Combine(root, "other"), [], first.CreatedAtUtc.AddMinutes(1));
            await database.PersistQueueSessionAsync(second);
            Assert.Equal(second.SessionId, (await database.LoadLatestQueueSessionAsync())!.SessionId);
            var result = await new ReleaseQueueCommandStateService().LoadResultAsync(database, first, false, "Captured");
            Assert.Equal(first.SessionId, result.QueueSession!.SessionId); Assert.Single(result.SigningReceipts);
            Assert.Null(await database.LoadReleaseCheckpointAsync("missing"));
            await database.PersistReleaseCheckpointAsync(first, signingReceipts: []);
            Assert.Empty((await database.LoadReleaseCheckpointAsync(first.SessionId))!.SigningReceipts);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ReceiptReplacementCanBeScopedToOneWorkingCopy()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-receipt-scope-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var database = new ReleaseStateDatabase(Path.Combine(root, "state.db")); await database.InitializeAsync();
            var session = ReleaseQueueSessionFactory.Create(root, [], DateTimeOffset.UtcNow);
            var first = new ReleaseSigningReceipt(root, "First", "Project", "first.dll", "File", ReleaseSigningReceiptStatus.Signed, "First evidence", DateTimeOffset.UtcNow);
            var otherRoot = Path.Combine(root, "other");
            var second = first with { RootPath = otherRoot, RepositoryName = "Second", ArtifactPath = "second.dll", Summary = "Second evidence" };
            await database.PersistReleaseCheckpointAsync(session, [first, second]);
            await database.PersistReleaseCheckpointAsync(session, [first with { Summary = "Updated first" }], receiptRootPath: root);
            var snapshot = (await database.LoadReleaseCheckpointAsync(session.SessionId))!;
            Assert.Equal(2, snapshot.SigningReceipts.Count);
            Assert.Contains(snapshot.SigningReceipts, receipt => receipt.Summary == "Second evidence");
            Assert.Contains(snapshot.SigningReceipts, receipt => receipt.Summary == "Updated first");
            await Assert.ThrowsAsync<InvalidOperationException>(() => database.PersistReleaseCheckpointAsync(session, [second], receiptRootPath: root));
            Assert.Equal(2, (await database.LoadReleaseCheckpointAsync(session.SessionId))!.SigningReceipts.Count);
            await database.PersistReleaseCheckpointAsync(session, [], receiptRootPath: root);
            Assert.Equal(otherRoot, Assert.Single((await database.LoadReleaseCheckpointAsync(session.SessionId))!.SigningReceipts).RootPath);
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class CancellingReceipts(ReleaseSigningReceipt receipt, CancellationTokenSource cancellation) : IReadOnlyList<ReleaseSigningReceipt>
    {
        public int Count => 2;
        public ReleaseSigningReceipt this[int index] => receipt;
        public IEnumerator<ReleaseSigningReceipt> GetEnumerator()
        {
            yield return receipt with { Summary = "New first receipt" };
            cancellation.Cancel();
            yield return receipt with { ArtifactPath = "second.dll" };
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
