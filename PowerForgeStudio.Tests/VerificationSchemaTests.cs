using DBAClientX;
using PowerForgeStudio.Domain.Verification;
using PowerForgeStudio.Orchestrator.Queue;
using PowerForgeStudio.Orchestrator.Storage;

namespace PowerForgeStudio.Tests;

public sealed class VerificationSchemaTests
{
    private const string LegacyTable = """
        CREATE TABLE release_verification_receipt (
            session_id TEXT NOT NULL, root_path TEXT NOT NULL, repository_name TEXT NOT NULL,
            adapter_kind TEXT NOT NULL, target_name TEXT NOT NULL, target_kind TEXT NOT NULL,
            destination TEXT NULL, status TEXT NOT NULL, summary TEXT NOT NULL,
            verified_at_utc TEXT NOT NULL, PRIMARY KEY(session_id, root_path, target_name, target_kind));
        INSERT INTO release_verification_receipt VALUES
            ('legacy', 'root', 'Fixture', 'ProjectBuild', 'Package', 'NuGet', NULL,
             'Verified', 'Original evidence', '2026-09-20T10:00:00.0000000+00:00');
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
            var original = Assert.Single(await database.LoadVerificationReceiptsAsync("legacy"));
            await database.InitializeAsync();
            Assert.Equal(original, Assert.Single(await database.LoadVerificationReceiptsAsync("legacy")));
            var session = ReleaseQueueSessionFactory.Create(root, [], DateTimeOffset.UtcNow);
            var receipt = original with { RootPath = root, Destination = "feed-a" };
            await database.PersistReleaseCheckpointAsync(session, verificationReceipts: [receipt, receipt with { Destination = "feed-b" }, receipt]);
            await database.InitializeAsync();
            await database.InitializeAsync();
            var recovered = (await database.LoadReleaseCheckpointAsync(session.SessionId))!.VerificationReceipts;
            Assert.Equal(3, recovered.Count);
            Assert.Equal(2, recovered.Count(value => value == receipt));
            Assert.Single(recovered, value => value.Destination == "feed-b");
            Assert.Equal(original, Assert.Single(await database.LoadVerificationReceiptsAsync("legacy")));
            var version = await sqlite.QueryReadOnlyAsListAsync(path, "SELECT value FROM app_schema WHERE key = 'schema_version';", reader => reader.GetString(0));
            Assert.Equal("24", Assert.Single(version));
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
            await sqlite.ExecuteNonQueryAsync(path, "UPDATE release_verification_receipt SET summary = NULL;");
            await Assert.ThrowsAnyAsync<Exception>(() => new ReleaseStateDatabase(path).InitializeAsync());
            var columns = await sqlite.QueryReadOnlyAsListAsync(path, "PRAGMA table_info(release_verification_receipt);", reader => reader.GetString(1));
            Assert.DoesNotContain("receipt_id", columns);
            var rows = await sqlite.QueryReadOnlyAsListAsync(path, "SELECT summary FROM release_verification_receipt;", reader => reader.IsDBNull(0));
            Assert.True(Assert.Single(rows));
            var leftovers = await sqlite.QueryReadOnlyAsListAsync(path, "SELECT name FROM sqlite_master WHERE name = 'release_verification_receipt_legacy_v19';", reader => reader.GetString(0));
            Assert.Empty(leftovers);
            await sqlite.ExecuteNonQueryAsync(path, "UPDATE release_verification_receipt SET summary = 'Repaired';");
            await new ReleaseStateDatabase(path).InitializeAsync();
            Assert.Equal("Repaired", Assert.Single(await new ReleaseStateDatabase(path).LoadVerificationReceiptsAsync("legacy")).Summary);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ConcurrentInitializationMigratesLegacyReceiptsOnce()
    {
        var root = NewRoot();
        try
        {
            var path = Path.Combine(root, "state.db");
            await new SQLite().ExecuteNonQueryAsync(path, LegacyTable);
            await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(() => new ReleaseStateDatabase(path).InitializeAsync())));
            Assert.Equal("Original evidence", Assert.Single(await new ReleaseStateDatabase(path).LoadVerificationReceiptsAsync("legacy")).Summary);
        }
        finally { Directory.Delete(root, true); }
    }

    private static string NewRoot() => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-verification-schema-" + Guid.NewGuid().ToString("N"))).FullName;
}
