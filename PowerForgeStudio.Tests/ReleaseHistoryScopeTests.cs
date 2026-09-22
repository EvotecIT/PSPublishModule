using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Orchestrator.Queue;
using PowerForgeStudio.Orchestrator.Storage;

namespace PowerForgeStudio.Tests;

public sealed class ReleaseHistoryScopeTests
{
    [Fact]
    public async Task SelectedWorkingCopyRemainsDiscoverableBeyondGlobalLimitAndBatchEvidenceIsScoped()
    {
        var workspace = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
            "studio-release-scope-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var first = Directory.CreateDirectory(Path.Combine(workspace, "First")).FullName;
            var second = Directory.CreateDirectory(Path.Combine(workspace, "Second")).FullName;
            var databasePath = Path.Combine(workspace, "history.db");
            var database = new ReleaseStateDatabase(databasePath);
            await database.InitializeAsync();
            var old = DateTimeOffset.UtcNow.AddDays(-3);
            var batch = ReleaseQueueSessionFactory.Create(workspace,
                [Item(first + Path.DirectorySeparatorChar, "First", 1), Item(second, "Second", 2)], old);
            await database.PersistReleaseCheckpointAsync(batch,
                publishReceipts: [Receipt(first, "First"), Receipt(second, "Second")]);
            await database.AppendReleaseProgressAsync(batch.SessionId,
                new ReleaseArtifactProgress(ReleaseQueueStage.Publish, "batch", null,
                    "Published", 2, 2, "Batch published.", old));

            for (var index = 0; index < 101; index++)
                await database.PersistQueueSessionAsync(ReleaseQueueSessionFactory.Create(workspace,
                    [Item(second, "Second", 1)], old.AddMinutes(index + 1)));

            var history = new ReleaseHistoryService(databasePath);
            Assert.DoesNotContain(await history.ListAsync(), entry => entry.SessionId == batch.SessionId);
            var scoped = await history.ListForWorkingCopyAsync(first);
            Assert.Equal(batch.SessionId, Assert.Single(scoped).SessionId);
            Assert.Equal(first, scoped[0].WorkingCopy);

            var project = await history.LoadForWorkingCopyAsync(batch.SessionId, first);
            Assert.NotNull(project);
            Assert.True(project.IsScopedBatch);
            Assert.Equal(first + Path.DirectorySeparatorChar, Assert.Single(project.Session.Items).RootPath);
            Assert.Equal(first, Assert.Single(project.PublishReceipts).RootPath);
            Assert.Empty(project.Progress);
            Assert.Equal(2, (await history.LoadAsync(batch.SessionId))!.PublishReceipts.Count);
            Assert.Null(await history.LoadForWorkingCopyAsync(batch.SessionId, workspace));
        }
        finally { Directory.Delete(workspace, recursive: true); }
    }

    private static ReleaseQueueItem Item(string root, string name, int order)
        => new(root, name, default, default, order, ReleaseQueueStage.Completed,
            ReleaseQueueItemStatus.Succeeded, "Published", "release.completed", "{}", DateTimeOffset.UtcNow);

    private static ReleasePublishReceipt Receipt(string root, string name)
        => new(root, name, "ProjectBuild", name + ".1.0.0.nupkg", "NuGet", "https://www.nuget.org",
            null, ReleasePublishReceiptStatus.Published, "Published", DateTimeOffset.UtcNow);
}
