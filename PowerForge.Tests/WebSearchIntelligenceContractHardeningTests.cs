using System.Text.Json;
using System.Text.Json.Nodes;
using DBAClientX;
using Json.Schema;
using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed partial class WebSearchIntelligenceTests
{
    [Fact]
    public void Normalize_CanonicalizesSignedZeroMetricsAndIdentity()
    {
        var positiveZeroBatch = CreateBatch();
        positiveZeroBatch.Observations[0].ClickThroughRate = 0d;
        positiveZeroBatch.Observations[0].AveragePosition = 0d;
        var negativeZeroBatch = CreateBatch();
        negativeZeroBatch.Observations[0].ClickThroughRate = -0d;
        negativeZeroBatch.Observations[0].AveragePosition = -0d;

        var positive = WebSearchObservationNormalizer.Normalize(positiveZeroBatch);
        var negative = WebSearchObservationNormalizer.Normalize(negativeZeroBatch);

        Assert.Equal(0L, BitConverter.DoubleToInt64Bits(negative.Observations[0].ClickThroughRate!.Value));
        Assert.Equal(0L, BitConverter.DoubleToInt64Bits(negative.Observations[0].AveragePosition!.Value));
        Assert.Equal(positive.RunId, negative.RunId);
        Assert.Equal(positive.Observations[0].ObservationKey, negative.Observations[0].ObservationKey);
    }

    [Fact]
    public void Cli_ObserveImport_RejectsAnotherOptionAsProviderValue()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var inputPath = Path.Combine(root, "observations.json");
            var databasePath = Path.Combine(root, "search.db");
            File.WriteAllText(inputPath, JsonSerializer.Serialize(CreateBatch()));

            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "observe",
                new[]
                {
                    "import", "--input", inputPath, "--database", databasePath,
                    "--provider", "--site", "officeimo"
                },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: 1);

            Assert.Equal(2, exitCode);
            Assert.False(File.Exists(databasePath));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Cli_OpportunityList_RejectsAnotherOptionAsFilterValue()
    {
        var exitCode = WebCliCommandHandlers.HandleSubCommand(
            "opportunity",
            new[]
            {
                "list", "--database", "search.db", "--site", "officeimo",
                "--provider", "--from", "2026-08-01"
            },
            outputJson: true,
            logger: new WebConsoleLogger(),
            outputSchemaVersion: 1);

        Assert.Equal(2, exitCode);
    }

    [Theory]
    [InlineData("--provider")]
    [InlineData("--output")]
    public void Cli_ObserveImport_RejectsWhitespaceOptionValues(string optionName)
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var inputPath = Path.Combine(root, "observations.json");
            var databasePath = Path.Combine(root, "search.db");
            File.WriteAllText(inputPath, JsonSerializer.Serialize(CreateBatch()));

            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "observe",
                new[]
                {
                    "import", "--input", inputPath, "--database", databasePath,
                    optionName, " "
                },
                outputJson: false,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: 1);

            Assert.Equal(2, exitCode);
            Assert.False(File.Exists(databasePath));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("--provider")]
    [InlineData("--from")]
    [InlineData("--to")]
    [InlineData("--min-impressions")]
    [InlineData("--min-ctr")]
    [InlineData("--output")]
    public void Cli_OpportunityList_RejectsWhitespaceOptionValues(string optionName)
    {
        var exitCode = WebCliCommandHandlers.HandleSubCommand(
            "opportunity",
            new[]
            {
                "list", "--database", "search.db", "--site", "officeimo",
                optionName, " "
            },
            outputJson: false,
            logger: new WebConsoleLogger(),
            outputSchemaVersion: 1);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public void Cli_HumanOutputEscapesProviderControlCharacters()
    {
        var escaped = WebCliCommandHandlers.EscapeSearchConsoleText(
            "line one\nline two\u001b[31m\u2028end",
            "fallback");

        Assert.Equal("line one\\u000Aline two\\u001B[31m\\u2028end", escaped);
        Assert.Equal("fallback", WebCliCommandHandlers.EscapeSearchConsoleText(null, "fallback"));
    }

    [Theory]
    [InlineData("provider")]
    [InlineData("siteId")]
    [InlineData("sourceKind")]
    public void ObservationSchema_RejectsWhitespaceRequiredBatchValues(string propertyName)
    {
        var document = JsonNode.Parse(JsonSerializer.Serialize(CreateBatch()))!;
        document[propertyName] = " \t ";

        Assert.False(LoadObservationSchema().Evaluate(document, new EvaluationOptions()).IsValid);
    }

    [Fact]
    public void ObservationSchema_RejectsWhitespaceOnlyQueryWithoutPage()
    {
        var document = JsonNode.Parse(JsonSerializer.Serialize(CreateBatch()))!;
        document["observations"]![0]!["page"] = null;
        document["observations"]![0]!["query"] = " \t ";

        Assert.False(LoadObservationSchema().Evaluate(document, new EvaluationOptions()).IsValid);
    }

    [Fact]
    public void ObservationSchema_AllowsBlankOptionalPageOnlyWhenQueryIsPresent()
    {
        var withQuery = JsonNode.Parse(JsonSerializer.Serialize(CreateBatch()))!;
        withQuery["observations"]![0]!["page"] = " \t ";
        var withoutQuery = withQuery.DeepClone();
        withoutQuery["observations"]![0]!["query"] = null;
        var schema = LoadObservationSchema();

        Assert.True(schema.Evaluate(withQuery, new EvaluationOptions()).IsValid);
        Assert.False(schema.Evaluate(withoutQuery, new EvaluationOptions()).IsValid);
    }

    [Fact]
    public void ObservationSchema_IsStructuralWhileNormalizerEnforcesCrossFieldMetrics()
    {
        var batch = CreateBatch();
        batch.Observations[0].Clicks = 101;
        batch.Observations[0].Impressions = 100;
        var document = JsonNode.Parse(JsonSerializer.Serialize(batch))!;

        Assert.True(LoadObservationSchema().Evaluate(document, new EvaluationOptions()).IsValid);
        var exception = Assert.Throws<ArgumentException>(() => WebSearchObservationNormalizer.Normalize(batch));
        Assert.Contains("more clicks than impressions", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ObservationSchema_AcceptsVersionTwoCoverageAndRejectsItWhenMissing()
    {
        var batch = CreateBatch();
        batch.SchemaVersion = 2;
        batch.CollectionCoverage = new WebSearchObservationCollectionCoverage
        {
            FromDate = new DateOnly(2026, 8, 1),
            ThroughDate = new DateOnly(2026, 8, 1),
            SearchType = "web",
            CompletedDates = [new DateOnly(2026, 8, 1)]
        };
        var documented = JsonNode.Parse(JsonSerializer.Serialize(batch))!;
        var missingCoverage = documented.DeepClone();
        missingCoverage.AsObject().Remove("collectionCoverage");
        var modeFromVersionThree = documented.DeepClone();
        modeFromVersionThree["collectionCoverage"]!["mode"] = "daily";
        var schema = LoadObservationSchema();

        Assert.True(schema.Evaluate(documented, new EvaluationOptions()).IsValid);
        Assert.Null(documented["collectionCoverage"]!["dimensionScopes"]);
        var normalizedDocument = JsonNode.Parse(JsonSerializer.Serialize(WebSearchObservationNormalizer.Normalize(batch)))!;
        var reparsed = JsonSerializer.Deserialize<WebSearchObservationBatch>(normalizedDocument.ToJsonString(), WebCliJson.Options)!;
        var renormalizedDocument = JsonNode.Parse(JsonSerializer.Serialize(WebSearchObservationNormalizer.Normalize(reparsed)))!;
        Assert.Null(normalizedDocument["collectionCoverage"]!["dimensionScopes"]);
        Assert.True(JsonNode.DeepEquals(normalizedDocument, renormalizedDocument));
        Assert.False(schema.Evaluate(missingCoverage, new EvaluationOptions()).IsValid);
        Assert.False(schema.Evaluate(modeFromVersionThree, new EvaluationOptions()).IsValid);
    }

    [Fact]
    public void Normalizer_RejectsDimensionScopesFromVersionTwoJson()
    {
        var batch = CreateBatch();
        batch.SchemaVersion = 2;
        batch.CollectionCoverage = new WebSearchObservationCollectionCoverage
        {
            FromDate = new DateOnly(2026, 8, 1),
            ThroughDate = new DateOnly(2026, 8, 1),
            SearchType = "web",
            CompletedDates = [new DateOnly(2026, 8, 1)]
        };
        var document = JsonNode.Parse(JsonSerializer.Serialize(batch))!;
        document["collectionCoverage"]!["dimensionScopes"] = new JsonArray();
        var schema = LoadObservationSchema();
        var parsed = JsonSerializer.Deserialize<WebSearchObservationBatch>(document.ToJsonString(), WebCliJson.Options)!;

        Assert.False(schema.Evaluate(document, new EvaluationOptions()).IsValid);
        var exception = Assert.Throws<ArgumentException>(() => WebSearchObservationNormalizer.Normalize(parsed));
        Assert.Contains("version 2", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Normalizer_RejectsDimensionScopesOutsideSnapshotMode()
    {
        var batch = CreateBatch();
        batch.SchemaVersion = 3;
        batch.CollectionCoverage = new WebSearchObservationCollectionCoverage
        {
            Mode = "daily",
            FromDate = batch.Observations[0].Date,
            ThroughDate = batch.Observations[0].Date,
            SearchType = "web",
            DimensionScopes = ["page"],
            CompletedDates = [batch.Observations[0].Date]
        };

        var exception = Assert.Throws<ArgumentException>(() => WebSearchObservationNormalizer.Normalize(batch));

        Assert.Contains("snapshot", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Normalizer_RequiresCoverageSearchTypeForNonWebObservations()
    {
        var batch = CreateBatch();
        batch.SchemaVersion = 2;
        batch.Observations[0].SearchType = "image";
        batch.CollectionCoverage = new WebSearchObservationCollectionCoverage
        {
            FromDate = batch.Observations[0].Date,
            ThroughDate = batch.Observations[0].Date,
            CompletedDates = [batch.Observations[0].Date]
        };

        var exception = Assert.Throws<ArgumentException>(() => WebSearchObservationNormalizer.Normalize(batch));

        Assert.Contains("coverage searchType", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Normalizer_RejectsAnExplicitNullCoverageModeFromVersionTwoJson()
    {
        var batch = CreateBatch();
        batch.SchemaVersion = 2;
        batch.CollectionCoverage = new WebSearchObservationCollectionCoverage
        {
            FromDate = new DateOnly(2026, 8, 1),
            ThroughDate = new DateOnly(2026, 8, 1),
            SearchType = "web",
            CompletedDates = [new DateOnly(2026, 8, 1)]
        };
        var document = JsonNode.Parse(JsonSerializer.Serialize(batch))!;
        document["collectionCoverage"]!["mode"] = null;
        var parsed = JsonSerializer.Deserialize<WebSearchObservationBatch>(document.ToJsonString(), WebCliJson.Options)!;

        var exception = Assert.Throws<ArgumentException>(() => WebSearchObservationNormalizer.Normalize(parsed));

        Assert.Contains("version 2", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ObservationSchema_VersionThreeRequiresADeclaredCoverageMode()
    {
        var batch = CreateBatch();
        batch.SchemaVersion = 3;
        batch.CollectionCoverage = new WebSearchObservationCollectionCoverage
        {
            Mode = "snapshot",
            FromDate = new DateOnly(2026, 8, 1),
            ThroughDate = new DateOnly(2026, 8, 7),
            SearchType = "web",
            CompletedDates = [new DateOnly(2026, 8, 1)]
        };
        var documented = JsonNode.Parse(JsonSerializer.Serialize(batch))!;
        var missingMode = documented.DeepClone();
        missingMode["collectionCoverage"]!.AsObject().Remove("mode");
        var schema = LoadObservationSchema();

        Assert.True(schema.Evaluate(documented, new EvaluationOptions()).IsValid);
        Assert.False(schema.Evaluate(missingMode, new EvaluationOptions()).IsValid);
        Assert.Single(WebSearchObservationNormalizer.Normalize(batch).CollectionCoverage!.CompletedDates);
    }

    [Fact]
    public void TrafficObservationSchema_AcceptsTheNormalizedContractAndRejectsMissingSamplingEvidence()
    {
        var batch = WebTrafficObservationNormalizer.Normalize(new WebTrafficObservationBatch
        {
            Provider = "cloudflare",
            SiteId = "officeimo",
            CollectedAtUtc = new DateTimeOffset(2026, 8, 10, 12, 34, 56, TimeSpan.Zero),
            SourceKind = "fixture",
            Status = "complete",
            CollectionCoverage = new WebTrafficObservationCollectionCoverage
            {
                FromDate = new DateOnly(2026, 8, 1),
                ThroughDate = new DateOnly(2026, 8, 1),
                CompletedDates = [new DateOnly(2026, 8, 1)]
            },
            Observations =
            [
                new WebTrafficObservation
                {
                    Date = new DateOnly(2026, 8, 1),
                    Host = "officeimo.com",
                    Path = "/",
                    Requests = 10,
                    Visits = 2,
                    EdgeResponseBytes = 1000,
                    SampleInterval = 1
                }
            ]
        });
        var documented = JsonNode.Parse(JsonSerializer.Serialize(batch))!;
        var missingSampling = documented.DeepClone();
        missingSampling["observations"]![0]!.AsObject().Remove("sampleInterval");
        var schema = LoadTrafficObservationSchema();

        Assert.True(schema.Evaluate(documented, new EvaluationOptions()).IsValid);
        Assert.False(schema.Evaluate(missingSampling, new EvaluationOptions()).IsValid);
    }

    [Fact]
    public async Task SqliteStore_ScopesExternalRunIdentifiersByProviderAndSite()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var databasePath = Path.Combine(root, "fleet.db");
            await CreateLegacyVersionTwoDatabaseAsync(databasePath);
            var officeGoogle = CreateBatch();
            officeGoogle.RunId = "42";
            var tactraGoogle = CreateBatch();
            tactraGoogle.RunId = "42";
            tactraGoogle.SiteId = "tactra";
            var officeBing = CreateBatch();
            officeBing.RunId = "42";
            officeBing.Provider = "bing-webmaster";
            var store = new SqliteWebSearchObservationStore(databasePath);

            var imports = new[]
            {
                await store.ImportAsync(WebSearchObservationNormalizer.Normalize(officeGoogle)),
                await store.ImportAsync(WebSearchObservationNormalizer.Normalize(tactraGoogle)),
                await store.ImportAsync(WebSearchObservationNormalizer.Normalize(officeBing))
            };

            Assert.All(imports, result => Assert.Equal(1, result.InsertedCount));
            Assert.All(imports, result => Assert.Equal(4, result.DatabaseSchemaVersion));
            Assert.Single(await store.QueryAsync(new WebSearchObservationQuery
            {
                SiteId = "officeimo",
                Provider = "google-search-console"
            }));
            Assert.Single(await store.QueryAsync(new WebSearchObservationQuery
            {
                SiteId = "officeimo",
                Provider = "bing-webmaster"
            }));
            Assert.Single(await store.QueryAsync(new WebSearchObservationQuery
            {
                SiteId = "tactra",
                Provider = "google-search-console"
            }));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SqliteStore_PrefersCompleteEvidenceOverNewerPartialRevision()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var databasePath = Path.Combine(root, "revisions.db");
            var complete = WebSearchObservationNormalizer.Normalize(CreateBatch());
            var partialInput = CreateBatch();
            partialInput.Status = "partial";
            partialInput.CollectedAtUtc = complete.CollectedAtUtc.AddHours(1);
            partialInput.Observations[0].Clicks = 0;
            partialInput.Observations[0].Impressions = 1;
            partialInput.Observations[0].AveragePosition = null;
            partialInput.Observations = partialInput.Observations.Append(new WebSearchObservation
            {
                Date = partialInput.Observations[0].Date,
                Page = "https://officeimo.com/partial-only/",
                Query = "partial-only query",
                Clicks = 0,
                Impressions = 5,
                AveragePosition = 15d
            }).ToArray();
            var partial = WebSearchObservationNormalizer.Normalize(partialInput);
            var store = new SqliteWebSearchObservationStore(databasePath);

            await store.ImportAsync(complete);
            await store.ImportAsync(partial);
            var current = await store.QueryAsync(new WebSearchObservationQuery
            {
                SiteId = "officeimo",
                Provider = "google-search-console"
            });
            var completeDimension = Assert.Single(current, observation =>
                observation.Page == complete.Observations[0].Page);
            var partialOnlyDimension = Assert.Single(current, observation =>
                observation.Page == "https://officeimo.com/partial-only/");

            Assert.Equal(complete.Observations[0].ObservationKey, completeDimension.ObservationKey);
            Assert.Equal(100, completeDimension.Impressions);
            Assert.Equal(5, partialOnlyDimension.Impressions);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SqliteStore_PreservesSnapshotCoverageByDateAndDimensionShape()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var firstDate = new DateOnly(2026, 8, 1);
            var secondDate = firstDate.AddDays(1);
            var older = WebSearchObservationNormalizer.Normalize(new WebSearchObservationBatch
            {
                Provider = "bing-webmaster",
                SiteId = "officeimo",
                CollectedAtUtc = new DateTimeOffset(2026, 8, 3, 8, 0, 0, TimeSpan.Zero),
                SourceKind = "fixture",
                Status = "complete",
                CollectionCoverage = new WebSearchObservationCollectionCoverage
                {
                    Mode = "snapshot",
                    FromDate = firstDate,
                    ThroughDate = secondDate,
                    SearchType = "web",
                    DimensionScopes = ["page", "query"],
                    CompletedDates = [firstDate, secondDate]
                },
                Observations =
                [
                    new WebSearchObservation { Date = firstDate, Page = "https://officeimo.com/old-page", SearchType = "web", Clicks = 1, Impressions = 10 },
                    new WebSearchObservation { Date = secondDate, Query = "old query", SearchType = "web", Clicks = 2, Impressions = 20 }
                ]
            });
            var newer = WebSearchObservationNormalizer.Normalize(new WebSearchObservationBatch
            {
                Provider = "bing-webmaster",
                SiteId = "officeimo",
                CollectedAtUtc = older.CollectedAtUtc.AddHours(1),
                SourceKind = "fixture",
                Status = "complete",
                CollectionCoverage = new WebSearchObservationCollectionCoverage
                {
                    Mode = "snapshot",
                    FromDate = firstDate,
                    ThroughDate = secondDate,
                    SearchType = "web",
                    DimensionScopes = ["page", "query"],
                    CompletedDates = [firstDate, secondDate]
                },
                Observations =
                [
                    new WebSearchObservation { Date = firstDate, Query = "new query", SearchType = "web", Clicks = 3, Impressions = 30 },
                    new WebSearchObservation { Date = secondDate, Page = "https://officeimo.com/new-page", SearchType = "web", Clicks = 4, Impressions = 40 }
                ]
            });
            var store = new SqliteWebSearchObservationStore(Path.Combine(root, "snapshot-revisions.db"));

            await store.ImportAsync(older);
            await store.ImportAsync(newer);
            var current = await store.QueryAsync(new WebSearchObservationQuery
            {
                SiteId = "officeimo",
                Provider = "bing-webmaster"
            });

            Assert.Equal(4, current.Count);
            Assert.Contains(current, value => value.Date == firstDate && value.Page == "https://officeimo.com/old-page");
            Assert.Contains(current, value => value.Date == firstDate && value.Query == "new query");
            Assert.Contains(current, value => value.Date == secondDate && value.Query == "old query");
            Assert.Contains(current, value => value.Date == secondDate && value.Page == "https://officeimo.com/new-page");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SqliteStore_RefusesToClaimUnrelatedVersionZeroDatabase()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var databasePath = Path.Combine(root, "unrelated.db");
            await using var client = new SQLite();
            await client.ExecuteNonQueryAsync(databasePath, "CREATE TABLE unrelated_data (id INTEGER PRIMARY KEY);");
            var store = new SqliteWebSearchObservationStore(databasePath);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.ImportAsync(WebSearchObservationNormalizer.Normalize(CreateBatch())));

            Assert.Contains("nonempty schema-version-zero", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, Convert.ToInt32(await client.ExecuteScalarAsync(databasePath, "PRAGMA user_version;")));
            var searchObjects = await client.QueryAsListAsync(
                databasePath,
                "SELECT name FROM sqlite_master WHERE name LIKE 'search_%';",
                static record => record.GetString(0));
            Assert.Empty(searchObjects);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static JsonSchema LoadObservationSchema()
    {
        var schemaPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "Schemas",
            "powerforge.web.search-observations.schema.json"));
        return JsonSchema.FromText(File.ReadAllText(schemaPath));
    }

    private static JsonSchema LoadTrafficObservationSchema()
    {
        var schemaPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "Schemas",
            "powerforge.web.traffic-observations.schema.json"));
        return JsonSchema.FromText(File.ReadAllText(schemaPath));
    }

    private static async Task CreateLegacyVersionTwoDatabaseAsync(string databasePath)
    {
        await using var client = new SQLite();
        await client.ExecuteNonQueryAsync(
            databasePath,
            """
            CREATE TABLE search_observation_runs (
                run_id TEXT NOT NULL PRIMARY KEY,
                provider TEXT NOT NULL,
                site_id TEXT NOT NULL,
                collected_at_utc TEXT NOT NULL,
                source_kind TEXT NOT NULL,
                status TEXT NOT NULL,
                configuration_hash TEXT NULL,
                evidence_reference TEXT NULL,
                normalized_manifest_json TEXT NOT NULL
            );
            CREATE TABLE search_observations (
                observation_key TEXT NOT NULL PRIMARY KEY,
                run_id TEXT NOT NULL,
                provider TEXT NOT NULL,
                site_id TEXT NOT NULL,
                observation_date TEXT NOT NULL,
                page TEXT NULL,
                query TEXT NULL,
                country TEXT NULL,
                device TEXT NULL,
                search_type TEXT NULL,
                clicks INTEGER NOT NULL,
                impressions INTEGER NOT NULL,
                click_through_rate REAL NULL,
                average_position REAL NULL,
                evidence_reference TEXT NULL,
                FOREIGN KEY (run_id) REFERENCES search_observation_runs(run_id)
            );
            CREATE INDEX ix_search_observations_site_date
                ON search_observations(site_id, observation_date);
            CREATE INDEX ix_search_observations_provider_site_date
                ON search_observations(provider, site_id, observation_date);
            CREATE UNIQUE INDEX ux_search_observation_runs_provider_site_collected
                ON search_observation_runs(provider, site_id, collected_at_utc);
            PRAGMA user_version = 2;
            """);
    }
}
