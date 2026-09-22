using DBAClientX;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Orchestrator.Queue;
using PowerForgeStudio.Orchestrator.Storage;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Verification;
using System.Text.Json;

namespace PowerForgeStudio.Tests;

public sealed class PublicationSchemaTests
{
    [Fact]
    public async Task GitHubAssetInventorySurvivesPublicationReceiptStorage()
    {
        var root = NewRoot();
        try
        {
            var database = new ReleaseStateDatabase(Path.Combine(root, "state.db"));
            await database.InitializeAsync();
            var receipt = ReleaseQueueReceiptFactory.CreatePublishReceipt(root, "Fixture", "ProjectBuild",
                "GitHub release", "GitHub", "https://github.com/Contoso/Fixture/releases/tag/v1.2.3",
                ReleasePublishReceiptStatus.Published, "Published.") with {
                    GitHubAssets = new Dictionary<string, long> { ["app.zip"] = 123, ["symbols.zip"] = 45 }
                };

            await database.PersistPublishReceiptsAsync("release", [receipt]);
            var recovered = Assert.Single(await database.LoadPublishReceiptsAsync("release"));

            Assert.NotNull(recovered.GitHubAssets);
            Assert.Equal(receipt.GitHubAssets, recovered.GitHubAssets);
        }
        finally { Directory.Delete(root, true); }
    }

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
    public async Task ExistingReceiptTableGainsIdentityColumnsWithoutLosingPublicationEvidence()
    {
        var root = NewRoot();
        try
        {
            var path = Path.Combine(root, "state.db");
            var database = new ReleaseStateDatabase(path);
            await database.InitializeAsync();
            var original = ReleaseQueueReceiptFactory.CreatePublishReceipt(root, "Fixture", "ProjectBuild",
                "Fixture.1.2.3.nupkg", "NuGet", "https://api.nuget.org/v3/index.json",
                ReleasePublishReceiptStatus.Published, "Published.", "package.nupkg");
            await database.PersistPublishReceiptsAsync("existing", [original]);
            var sqlite = new SQLite();
            await sqlite.ExecuteNonQueryAsync(path,
                "ALTER TABLE release_publish_receipt DROP COLUMN package_id; ALTER TABLE release_publish_receipt DROP COLUMN package_version;");

            await new ReleaseStateDatabase(path).InitializeAsync();

            var recovered = Assert.Single(await new ReleaseStateDatabase(path).LoadPublishReceiptsAsync("existing"));
            Assert.Equal(original, recovered);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task MigrationPreservesLegacyEvidenceAndSupportsMultipleDestinationsAndDuplicateEvidence()
    {
        var root = NewRoot();
        try
        {
            var path = Path.Combine(root, "state.db"); var sqlite = new SQLite();
            await sqlite.ExecuteNonQueryAsync(path, LegacyTable);
            var database = new ReleaseStateDatabase(path);
            await database.InitializeAsync();
            var original = Assert.Single(await database.LoadPublishReceiptsAsync("legacy"));
            Assert.Equal("Original evidence", original.Summary);
            Assert.Null(original.PackageId);
            Assert.Null(original.PackageVersion);
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

    [Fact]
    public async Task UpgradeScrubsOlderReceiptsProgressAndNestedQueueCheckpoints()
    {
        var root = NewRoot();
        try
        {
            var path = Path.Combine(root, "state.db");
            var database = new ReleaseStateDatabase(path);
            await database.InitializeAsync();
            const string address = "https://user:password@packages.example.test/v3/index.json?token=query-secret";
            var receipt = new ReleasePublishReceipt(root, "Fixture", "ProjectBuild", "Package", "NuGet", address,
                null, ReleasePublishReceiptStatus.Published, $"Published to {address}", DateTimeOffset.UtcNow);
            var publish = new ReleasePublishExecutionResult(root, true, "Published", JsonSerializer.Serialize(new { Destination = address }), [receipt]);
            var item = new ReleaseQueueItem(root, "Fixture", ReleaseRepositoryKind.Library, ReleaseWorkspaceKind.PrimaryRepository,
                1, ReleaseQueueStage.Verify, ReleaseQueueItemStatus.ReadyToRun, "Ready", "verify.ready",
                JsonSerializer.Serialize(publish), DateTimeOffset.UtcNow);
            var session = ReleaseQueueSessionFactory.Create(root, [item], DateTimeOffset.UtcNow);
            await database.PersistReleaseCheckpointAsync(session, publishReceipts: [receipt]);
            await database.PersistVerificationReceiptsAsync(session.SessionId, [
                new ReleaseVerificationReceipt(root, "Fixture", "ProjectBuild", "Package", "NuGet", address,
                    ReleaseVerificationReceiptStatus.Verified, $"Verified at {address}", DateTimeOffset.UtcNow)
            ]);
            await database.AppendReleaseProgressAsync(session.SessionId,
                new ReleaseArtifactProgress(ReleaseQueueStage.Publish, "Package", null, "Planned", 0, 1, "Safe", DateTimeOffset.UtcNow));

            var sqlite = new SQLite();
            await sqlite.ExecuteNonQueryAsync(path,
                "UPDATE release_publish_receipt SET destination = @Address, summary = @Summary, destination_credentials_omitted = 0; " +
                "UPDATE release_verification_receipt SET destination = @Address, summary = @Summary; " +
                "UPDATE release_queue_item SET checkpoint_state_json = @Checkpoint; " +
                "UPDATE release_execution_progress SET detail = @Summary; " +
                "UPDATE app_schema SET value = '22' WHERE key = 'schema_version';",
                new Dictionary<string, object?> {
                    ["@Address"] = address,
                    ["@Summary"] = $"Published to {address}",
                    ["@Checkpoint"] = JsonSerializer.Serialize(publish)
                });

            await new ReleaseStateDatabase(path).InitializeAsync();

            var reopened = new ReleaseStateDatabase(path);
            var savedReceipt = Assert.Single(await reopened.LoadPublishReceiptsAsync(session.SessionId));
            Assert.True(savedReceipt.DestinationCredentialsOmitted);
            Assert.Equal("https://packages.example.test/v3/index.json", savedReceipt.Destination);
            var savedVerification = Assert.Single(await reopened.LoadVerificationReceiptsAsync(session.SessionId));
            Assert.Equal(savedReceipt.Destination, savedVerification.Destination);
            Assert.DoesNotContain("query-secret", savedVerification.Summary);
            var checkpoint = (await reopened.LoadReleaseCheckpointAsync(session.SessionId))!;
            var savedItem = Assert.Single(checkpoint.Session.Items);
            Assert.DoesNotContain("password", savedItem.CheckpointStateJson);
            Assert.DoesNotContain("query-secret", savedItem.CheckpointStateJson);
            var savedPublish = new ReleaseQueueCheckpointSerializer().TryDeserialize<ReleasePublishExecutionResult>(savedItem.CheckpointStateJson)!;
            Assert.True(Assert.Single(savedPublish.Receipts).DestinationCredentialsOmitted);
            Assert.DoesNotContain("query-secret", savedPublish.SourceCheckpointStateJson);
            Assert.DoesNotContain("password", Assert.Single(checkpoint.Progress).Detail);
        }
        finally { Directory.Delete(root, true); }
    }

    private static string NewRoot() => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-publication-schema-" + Guid.NewGuid().ToString("N"))).FullName;
}
