using System.Text.Json;
using DBAClientX;
using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed partial class WebSearchFleetOperationsTests
{
    [Fact]
    public async Task Snapshot_PreservesRowlessCompletedDatesForInferredWebCoverage()
    {
        var root = CreateTempRoot();
        try
        {
            var store = new SqliteWebSearchObservationStore(Path.Combine(root, "fleet.db"));
            var batch = SearchBatch("rowless-web", new DateOnly(2026, 8, 1), AsOf.AddMinutes(-1));
            batch.CollectionCoverage!.SearchType = null;
            batch.CollectionCoverage.ThroughDate = new DateOnly(2026, 8, 2);
            batch.CollectionCoverage.CompletedDates = [new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 2)];
            await store.ImportAsync(WebSearchObservationNormalizer.Normalize(batch));

            var stream = Assert.Single((await store.ReadFleetSnapshotAsync()).Streams);

            var range = Assert.Single(stream.CompletedRanges);
            Assert.Equal(new DateOnly(2026, 8, 1), range.FromDate);
            Assert.Equal(new DateOnly(2026, 8, 2), range.ThroughDate);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Snapshot_DoesNotCreditPartialLegacyObservationDatesAsComplete()
    {
        var root = CreateTempRoot();
        try
        {
            var store = new SqliteWebSearchObservationStore(Path.Combine(root, "fleet.db"));
            await store.ImportAsync(WebSearchObservationNormalizer.Normalize(new WebSearchObservationBatch
            {
                SchemaVersion = 1,
                RunId = "legacy-partial-row",
                Provider = "gsc",
                SiteId = "officeimo",
                CollectedAtUtc = AsOf.AddMinutes(-1),
                SourceKind = "fixture",
                Status = "partial",
                Observations =
                [
                    new WebSearchObservation { Date = new DateOnly(2026, 8, 1), Page = "https://officeimo.com/", Clicks = 1, Impressions = 2 }
                ]
            }));

            var stream = Assert.Single((await store.ReadFleetSnapshotAsync()).Streams);

            Assert.True(stream.HasPartialEvidence);
            Assert.Empty(stream.CompletedRanges);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Snapshot_DoesNotCreditFailedDateRowsAsCompletedCoverage()
    {
        var root = CreateTempRoot();
        try
        {
            var store = new SqliteWebSearchObservationStore(Path.Combine(root, "fleet.db"));
            var completedDate = new DateOnly(2026, 8, 1);
            var failedDate = completedDate.AddDays(1);
            await store.ImportAsync(WebSearchObservationNormalizer.Normalize(new WebSearchObservationBatch
            {
                SchemaVersion = 2,
                RunId = "partial-failed-date-row",
                Provider = "gsc",
                SiteId = "officeimo",
                CollectedAtUtc = AsOf.AddMinutes(-1),
                SourceKind = "fixture",
                Status = "partial",
                CollectionCoverage = new WebSearchObservationCollectionCoverage
                {
                    FromDate = completedDate,
                    ThroughDate = failedDate,
                    CompletedDates = [completedDate],
                    FailedDate = failedDate,
                    FailureCategory = "provider-unavailable"
                },
                Observations =
                [
                    new WebSearchObservation
                    {
                        Date = failedDate,
                        Page = "https://officeimo.com/incomplete",
                        Clicks = 1,
                        Impressions = 2
                    }
                ]
            }));

            var stream = Assert.Single((await store.ReadFleetSnapshotAsync()).Streams);

            Assert.True(stream.HasPartialEvidence);
            var range = Assert.Single(stream.CompletedRanges);
            Assert.Equal(completedDate, range.FromDate);
            Assert.Equal(completedDate, range.ThroughDate);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Snapshot_ExcludesRunsCollectedAfterTheRequestedAsOfTime()
    {
        var root = CreateTempRoot();
        try
        {
            var store = new SqliteWebSearchObservationStore(Path.Combine(root, "fleet.db"));
            await store.ImportAsync(WebSearchObservationNormalizer.Normalize(
                SearchBatch("before-as-of", new DateOnly(2026, 8, 1), AsOf.AddMinutes(-1))));
            await store.ImportAsync(WebSearchObservationNormalizer.Normalize(
                SearchBatch("after-as-of", new DateOnly(2026, 8, 2), AsOf.AddMinutes(1))));

            var stream = Assert.Single((await store.ReadFleetSnapshotAsync(AsOf)).Streams);

            Assert.Equal(new DateOnly(2026, 8, 1), stream.LatestCompleteDate);
            Assert.Equal(1, stream.RunCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Retention_PreservesLatestPermanentFailureAlongsideCoverage()
    {
        var root = CreateTempRoot();
        try
        {
            var store = new SqliteWebSearchObservationStore(Path.Combine(root, "fleet.db"));
            await store.ImportTrafficAsync(WebTrafficObservationNormalizer.Normalize(
                TrafficBatch("covered-old", new DateOnly(2026, 1, 1), AsOf.AddDays(-60))));
            var failure = TrafficBatch("permanent-failure-old", new DateOnly(2026, 1, 2), AsOf.AddDays(-50));
            failure.Status = "partial";
            failure.Observations = Array.Empty<WebTrafficObservation>();
            failure.CollectionCoverage!.CompletedDates = Array.Empty<DateOnly>();
            failure.CollectionCoverage.FailedDate = new DateOnly(2026, 1, 2);
            failure.CollectionCoverage.FailureCategory = "row-limit-reached";
            await store.ImportTrafficAsync(WebTrafficObservationNormalizer.Normalize(failure));

            var result = await store.ApplyFleetRetentionAsync(new WebSearchFleetOperationsConfiguration
            {
                SearchRunRetentionDays = 30,
                TrafficRunRetentionDays = 30,
                PerformanceRunRetentionDays = 30
            }, AsOf, apply: true);

            Assert.Equal(0, Assert.Single(result.Kinds, value => value.Kind == "traffic").DeletedRunCount);
            var stream = Assert.Single((await store.ReadFleetSnapshotAsync()).Streams);
            Assert.Equal("row-limit-reached", stream.LatestFailureCategory);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Snapshot_PreservesPermanentFailuresForEveryDailyPartition()
    {
        var root = CreateTempRoot();
        try
        {
            var store = new SqliteWebSearchObservationStore(Path.Combine(root, "fleet.db"));
            foreach (var (runId, date, collectedAt) in new[]
                     {
                         ("failure-one", new DateOnly(2026, 1, 1), AsOf.AddDays(-60)),
                         ("failure-two", new DateOnly(2026, 1, 2), AsOf.AddDays(-50))
                     })
            {
                var failure = TrafficBatch(runId, date, collectedAt);
                failure.Status = "partial";
                failure.Observations = Array.Empty<WebTrafficObservation>();
                failure.CollectionCoverage!.CompletedDates = Array.Empty<DateOnly>();
                failure.CollectionCoverage.FailedDate = date;
                failure.CollectionCoverage.FailureCategory = "row-limit-reached";
                await store.ImportTrafficAsync(WebTrafficObservationNormalizer.Normalize(failure));
            }
            var newerAttempt = TrafficBatch("newer-attempt", new DateOnly(2026, 1, 3), AsOf.AddDays(-40));
            await store.ImportTrafficAsync(WebTrafficObservationNormalizer.Normalize(newerAttempt));
            await store.ApplyFleetRetentionAsync(new WebSearchFleetOperationsConfiguration
            {
                SearchRunRetentionDays = 30,
                TrafficRunRetentionDays = 30,
                PerformanceRunRetentionDays = 30
            }, AsOf, apply: true);

            var stream = Assert.Single((await store.ReadFleetSnapshotAsync(AsOf)).Streams);

            Assert.Equal([new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2)],
                stream.PermanentFailures.Select(value => value.Date).ToArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RetentionSummariesRemainVisibleBeforeTheirMaintenanceTimestamp()
    {
        var root = CreateTempRoot();
        try
        {
            var store = new SqliteWebSearchObservationStore(Path.Combine(root, "fleet.db"));
            await store.ImportTrafficAsync(WebTrafficObservationNormalizer.Normalize(
                TrafficBatch("older-coverage", new DateOnly(2026, 1, 1), AsOf.AddDays(-60))));
            await store.ImportTrafficAsync(WebTrafficObservationNormalizer.Normalize(
                TrafficBatch("newer-coverage", new DateOnly(2026, 1, 2), AsOf.AddDays(-50))));

            await store.ApplyFleetRetentionAsync(new WebSearchFleetOperationsConfiguration
            {
                SearchRunRetentionDays = 30,
                TrafficRunRetentionDays = 30,
                PerformanceRunRetentionDays = 30
            }, AsOf.AddYears(1), apply: true);
            var stream = Assert.Single((await store.ReadFleetSnapshotAsync(AsOf)).Streams);

            Assert.True(stream.HasRetainedCoverage);
            var range = Assert.Single(stream.CompletedRanges);
            Assert.Equal(new DateOnly(2026, 1, 1), range.FromDate);
            Assert.Equal(new DateOnly(2026, 1, 2), range.ThroughDate);
            Assert.Empty((await store.ReadFleetSnapshotAsync(AsOf.AddDays(-61))).Streams);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Import_MigratesVersionSixRetainedCoverageWithAConservativeSourceTimestamp()
    {
        var root = CreateTempRoot();
        try
        {
            var databasePath = Path.Combine(root, "fleet.db");
            var store = new SqliteWebSearchObservationStore(databasePath);
            await store.ImportTrafficAsync(WebTrafficObservationNormalizer.Normalize(
                TrafficBatch("schema-seed", new DateOnly(2026, 8, 1), AsOf)));
            await using var client = new SQLite();
            await client.ExecuteNonQueryAsync(databasePath,
                """
                DROP TABLE fleet_retained_coverage;
                CREATE TABLE fleet_retained_coverage (
                    kind TEXT NOT NULL,
                    provider TEXT NOT NULL,
                    site_id TEXT NOT NULL,
                    stream_key TEXT NOT NULL,
                    configuration_hash TEXT NOT NULL,
                    from_date TEXT NOT NULL,
                    through_date TEXT NOT NULL,
                    retained_at_utc TEXT NOT NULL,
                    PRIMARY KEY (kind, provider, site_id, stream_key, configuration_hash, from_date, through_date)
                );
                INSERT INTO fleet_retained_coverage (
                    kind, provider, site_id, stream_key, configuration_hash,
                    from_date, through_date, retained_at_utc
                ) VALUES (
                    'traffic', 'cloudflare', 'officeimo', 'daily', '',
                    '2026-01-01', '2026-01-01', '2026-08-10T12:00:00.0000000+00:00'
                );
                PRAGMA user_version = 6;
                """);

            var migrationTrigger = TrafficBatch("migration-trigger", new DateOnly(2026, 8, 2), AsOf.AddMinutes(1));
            await store.ImportTrafficAsync(WebTrafficObservationNormalizer.Normalize(migrationTrigger));

            var version = await client.ExecuteScalarAsync(databasePath, "PRAGMA user_version;");
            var sourceTimestamp = await client.ExecuteScalarAsync(
                databasePath,
                "SELECT source_collected_at_utc FROM fleet_retained_coverage LIMIT 1;");
            Assert.Equal(7, Convert.ToInt32(version));
            Assert.Equal("2026-08-10T12:00:00.0000000+00:00", Convert.ToString(sourceTimestamp));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FleetCli_SucceedsWhenHealthyWorkSurvivesAnUnrelatedConfigurationError()
    {
        var root = CreateTempRoot();
        try
        {
            var configPath = Path.Combine(root, "providers.json");
            var configuration = CreateConfiguration();
            configuration.Sites[0].Providers = [configuration.Sites[0].Providers.Single(value => value.Id == "lighthouse")];
            configuration.Sites =
            [
                .. configuration.Sites,
                new WebSearchSiteProviderConfiguration
                {
                    Id = "broken", BaseUrl = "not-a-url",
                    Providers = [Provider("broken", "unknown", "unknown")]
                }
            ];
            File.WriteAllText(configPath, JsonSerializer.Serialize(configuration, WebCliJson.Options));

            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "fleet",
                ["schedule", "--config", configPath, "--database", Path.Combine(root, "missing.db"), "--as-of", "2026-08-10T12:00:00Z", "--output", "json"],
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: 1);

            Assert.Equal(0, exitCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
