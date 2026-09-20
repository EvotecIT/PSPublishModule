using DBAClientX;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Orchestrator.Queue;
using PowerForgeStudio.Orchestrator.Storage;

namespace PowerForgeStudio.Tests;

public sealed class PublicationSchemaTests
{
    private const string LegacyTable = """
        CREATE TABLE release_publish_receipt (
            session_id TEXT NOT NULL, root_path TEXT NOT NULL, repository_name TEXT NOT NULL,
            adapter_kind TEXT NOT NULL, target_name TEXT NOT NULL, target_kind TEXT NOT NULL,
            destination TEXT NULL, source_path TEXT NULL, status TEXT NOT NULL, summary TEXT NOT NULL,
            published_at_utc TEXT NOT NULL, PRIMARY KEY(session_id, root_path, target_name, target_kind));
        INSERT INTO release_publish_receipt VALUES
            ('legacy', 'root', 'Fixture', 'ProjectBuild', 'Package', 'NuGet', NULL, NULL,
             'Published', 'Original evidence', '2026-09-20T10:00:00.0000000+00:00');
        """;

    [Fact]
    public async Task MigrationPreservesLegacyEvidenceAndSupportsMultipleDestinationsAndDuplicateEvidence()
    {
        var root = NewRoot();
        try
        {
            var path = Path.Combine(root, "state.db"); var sqlite = new SQLite();
            await sqlite.ExecuteNonQueryAsync(path, LegacyTable);
            var database = new ReleaseStateDatabase(path);
            var original = Assert.Single(await database.LoadPublishReceiptsAsync("legacy"));
            await database.InitializeAsync();
            Assert.Equal(original, Assert.Single(await database.LoadPublishReceiptsAsync("legacy")));
            var session = ReleaseQueueSessionFactory.Create(root, [], DateTimeOffset.UtcNow);
            var receipt = original with { RootPath = root, Destination = "feed-a" };
            await database.PersistReleaseCheckpointAsync(session, publishReceipts: [receipt, receipt with { Destination = "feed-b" }, receipt]);
            await database.InitializeAsync();
            await database.InitializeAsync();
            var recovered = (await database.LoadReleaseCheckpointAsync(session.SessionId))!.PublishReceipts;
            Assert.Equal(3, recovered.Count);
            Assert.Equal(2, recovered.Count(value => value == receipt));
            Assert.Single(recovered, value => value.Destination == "feed-b");
            Assert.Equal(original, Assert.Single(await database.LoadPublishReceiptsAsync("legacy")));
            var version = await sqlite.QueryReadOnlyAsListAsync(path, "SELECT value FROM app_schema WHERE key = 'schema_version';", reader => reader.GetString(0));
            Assert.Equal("19", Assert.Single(version));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task InvalidLegacyEvidenceRollsBackTableReplacement()
    {
        var root = NewRoot();
        try
        {
            var path = Path.Combine(root, "state.db"); var sqlite = new SQLite();
            await sqlite.ExecuteNonQueryAsync(path, LegacyTable.Replace("summary TEXT NOT NULL", "summary TEXT NULL"));
            await sqlite.ExecuteNonQueryAsync(path, "UPDATE release_publish_receipt SET summary = NULL;");
            await Assert.ThrowsAnyAsync<Exception>(() => new ReleaseStateDatabase(path).InitializeAsync());
            var columns = await sqlite.QueryReadOnlyAsListAsync(path, "PRAGMA table_info(release_publish_receipt);", reader => reader.GetString(1));
            Assert.DoesNotContain("receipt_id", columns);
            var rows = await sqlite.QueryReadOnlyAsListAsync(path, "SELECT summary FROM release_publish_receipt;", reader => reader.IsDBNull(0));
            Assert.True(Assert.Single(rows));
            var leftovers = await sqlite.QueryReadOnlyAsListAsync(path, "SELECT name FROM sqlite_master WHERE name = 'release_publish_receipt_legacy_v18';", reader => reader.GetString(0));
            Assert.Empty(leftovers);
            await sqlite.ExecuteNonQueryAsync(path, "UPDATE release_publish_receipt SET summary = 'Repaired';");
            await new ReleaseStateDatabase(path).InitializeAsync();
            Assert.Equal("Repaired", Assert.Single(await new ReleaseStateDatabase(path).LoadPublishReceiptsAsync("legacy")).Summary);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentInitializationMigratesLegacyReceiptsOnce(bool missingSourceColumn)
    {
        var root = NewRoot();
        try
        {
            var path = Path.Combine(root, "state.db");
            await new SQLite().ExecuteNonQueryAsync(path, LegacyTable);
            if (missingSourceColumn)
                await new SQLite().ExecuteNonQueryAsync(path, "ALTER TABLE release_publish_receipt DROP COLUMN source_path;");
            await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(() => new ReleaseStateDatabase(path).InitializeAsync())));
            Assert.Equal("Original evidence", Assert.Single(await new ReleaseStateDatabase(path).LoadPublishReceiptsAsync("legacy")).Summary);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task OlderReceiptWithoutSourcePathRetainsItsEvidence()
    {
        var root = NewRoot();
        try
        {
            var path = Path.Combine(root, "state.db"); var sqlite = new SQLite();
            await sqlite.ExecuteNonQueryAsync(path, LegacyTable);
            await sqlite.ExecuteNonQueryAsync(path, "ALTER TABLE release_publish_receipt DROP COLUMN source_path;");
            var database = new ReleaseStateDatabase(path); await database.InitializeAsync();
            var receipt = Assert.Single(await database.LoadPublishReceiptsAsync("legacy"));
            Assert.Null(receipt.SourcePath); Assert.Equal("Original evidence", receipt.Summary);
        }
        finally { Directory.Delete(root, true); }
    }

    private static string NewRoot() => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-publication-schema-" + Guid.NewGuid().ToString("N"))).FullName;
}
