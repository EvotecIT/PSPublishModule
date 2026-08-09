using System.Text.Json;
using System.Text.Json.Nodes;
using DBAClientX;
using Json.Schema;
using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class WebSearchIntelligenceTests
{
    [Fact]
    public void Normalize_AssignsStableIdentitiesAndCanonicalDimensions()
    {
        var batch = CreateBatch();

        var first = WebSearchObservationNormalizer.Normalize(batch);
        var second = WebSearchObservationNormalizer.Normalize(batch);

        Assert.Equal(first.RunId, second.RunId);
        Assert.Equal("google-search-console", first.Provider);
        Assert.Equal("officeimo", first.SiteId);
        Assert.Equal("desktop", first.Observations[0].Device);
        Assert.Equal("https://officeimo.com/convert/?mode=fast", first.Observations[0].Page);
        Assert.Equal(0.01d, first.Observations[0].ClickThroughRate!.Value, precision: 6);
        Assert.Equal(first.Observations[0].ObservationKey, second.Observations[0].ObservationKey);
        Assert.Equal(64, first.Observations[0].ObservationKey.Length);
    }

    [Fact]
    public void Normalize_RejectsObservationOutsideBatchIdentity()
    {
        var batch = CreateBatch();
        batch.Observations[0].SiteId = "another-site";

        var exception = Assert.Throws<ArgumentException>(() => WebSearchObservationNormalizer.Normalize(batch));

        Assert.Contains("does not match its batch", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Normalize_RejectsAmbiguousDuplicateDimensionsWithinRun()
    {
        var batch = CreateBatch();
        batch.Observations =
        [
            batch.Observations[0],
            new WebSearchObservation
            {
                Date = new DateOnly(2026, 8, 1),
                Page = "https://officeimo.com/convert/?mode=fast#overview",
                Query = "convert office files",
                Country = "pl",
                Device = "desktop",
                SearchType = "web",
                Clicks = 2,
                Impressions = 110,
                AveragePosition = 8.5d
            }
        ];

        var exception = Assert.Throws<ArgumentException>(() => WebSearchObservationNormalizer.Normalize(batch));

        Assert.Contains("multiple rows", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(double.NaN, 9d)]
    [InlineData(double.PositiveInfinity, 9d)]
    [InlineData(0.01d, double.NaN)]
    [InlineData(0.01d, double.NegativeInfinity)]
    public void Normalize_RejectsNonFiniteMetrics(double clickThroughRate, double averagePosition)
    {
        var batch = CreateBatch();
        batch.Observations[0].ClickThroughRate = clickThroughRate;
        batch.Observations[0].AveragePosition = averagePosition;

        var exception = Assert.Throws<ArgumentException>(() => WebSearchObservationNormalizer.Normalize(batch));

        Assert.Contains("finite", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ObservationSchema_AcceptsDocumentedContractAndRejectsMissingDimensions()
    {
        var schemaPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "Schemas",
            "powerforge.web.search-observations.schema.json"));
        var schema = JsonSchema.FromText(File.ReadAllText(schemaPath));
        var serialized = JsonSerializer.Serialize(CreateBatch());
        var valid = JsonNode.Parse(serialized)!;
        var invalid = JsonNode.Parse(serialized)!;
        invalid["observations"]![0]!["page"] = null;
        invalid["observations"]![0]!["query"] = null;
        var unknown = JsonNode.Parse(serialized)!;
        unknown["observations"]![0]!["impression"] = 240;

        Assert.True(schema.Evaluate(valid, new EvaluationOptions()).IsValid);
        Assert.False(schema.Evaluate(invalid, new EvaluationOptions()).IsValid);
        Assert.False(schema.Evaluate(unknown, new EvaluationOptions()).IsValid);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<WebSearchObservationBatch>(unknown.ToJsonString(), WebCliJson.Options));
    }

    [Fact]
    public void ObservationJson_RequiresExplicitCollectionOffset()
    {
        var offsetless = JsonNode.Parse(JsonSerializer.Serialize(CreateBatch()))!.AsObject();
        offsetless["collectedAtUtc"] = "2026-08-02T08:00:00";

        var exception = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<WebSearchObservationBatch>(offsetless.ToJsonString(), WebCliJson.Options));

        Assert.Contains("explicit", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(JsonSerializer.Deserialize<WebSearchObservationBatch>(
            JsonSerializer.Serialize(CreateBatch()),
            WebCliJson.Options));
    }

    [Fact]
    public void IdentityFraming_DistinguishesControlCharactersAcrossDimensions()
    {
        var firstBatch = CreateBatch();
        firstBatch.Observations[0].Query = "a\u001fb";
        firstBatch.Observations[0].Country = "c";
        var secondBatch = CreateBatch();
        secondBatch.Observations[0].Query = "a";
        secondBatch.Observations[0].Country = "b\u001fc";

        var first = WebSearchObservationNormalizer.Normalize(firstBatch);
        var second = WebSearchObservationNormalizer.Normalize(secondBatch);
        var report = WebSearchOpportunityAnalyzer.Analyze(
            first.Observations.Concat(second.Observations),
            new WebSearchOpportunityOptions { SiteId = "officeimo" },
            new DateTimeOffset(2026, 8, 4, 0, 0, 0, TimeSpan.Zero));

        Assert.NotEqual(first.RunId, second.RunId);
        Assert.NotEqual(first.Observations[0].ObservationKey, second.Observations[0].ObservationKey);
        Assert.Equal(4, report.Opportunities.Length);
        Assert.Equal(4, report.Opportunities.Select(opportunity => opportunity.OpportunityId).Distinct().Count());
    }

    [Fact]
    public void Analyze_EmitsExplainableStableWeakPageAndCtrOpportunities()
    {
        var firstBatch = CreateBatch();
        var secondBatch = CreateBatch();
        secondBatch.CollectedAtUtc = new DateTimeOffset(2026, 8, 3, 8, 0, 0, TimeSpan.Zero);
        secondBatch.Observations[0].Date = new DateOnly(2026, 8, 2);
        secondBatch.Observations[0].Clicks = 2;
        secondBatch.Observations[0].Impressions = 200;
        secondBatch.Observations[0].ClickThroughRate = null;

        var observations = WebSearchObservationNormalizer.Normalize(firstBatch).Observations
            .Concat(WebSearchObservationNormalizer.Normalize(secondBatch).Observations)
            .ToArray();
        var options = new WebSearchOpportunityOptions { SiteId = "officeimo" };
        var generatedAt = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

        var report = WebSearchOpportunityAnalyzer.Analyze(observations, options, generatedAt);
        var repeated = WebSearchOpportunityAnalyzer.Analyze(observations.Reverse(), options, generatedAt);

        Assert.Equal(2, report.ObservationCount);
        Assert.Equal(2, report.Opportunities.Length);
        Assert.Contains(report.Opportunities, opportunity => opportunity.RuleId == "search.weak-page");
        Assert.Contains(report.Opportunities, opportunity => opportunity.RuleId == "search.ctr-underperformance");
        Assert.All(report.Opportunities, opportunity =>
        {
            Assert.Equal(300, opportunity.Impressions);
            Assert.Equal(3, opportunity.Clicks);
            Assert.Equal(2, opportunity.EvidenceObservationKeys.Length);
            Assert.NotEmpty(opportunity.Explanation);
            Assert.NotEmpty(opportunity.Recommendation);
            Assert.InRange(opportunity.Score, 0d, 100d);
            Assert.InRange(opportunity.Confidence, 0d, 1d);
        });
        Assert.Equal(
            report.Opportunities.Select(opportunity => opportunity.OpportunityId),
            repeated.Opportunities.Select(opportunity => opportunity.OpportunityId));
    }

    [Fact]
    public void Analyze_RejectsNonFiniteThresholds()
    {
        var observations = WebSearchObservationNormalizer.Normalize(CreateBatch()).Observations;
        var generatedAt = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

        Assert.Throws<ArgumentOutOfRangeException>(() => WebSearchOpportunityAnalyzer.Analyze(
            observations,
            new WebSearchOpportunityOptions { SiteId = "officeimo", MinimumClickThroughRate = double.NaN },
            generatedAt));
        Assert.Throws<ArgumentOutOfRangeException>(() => WebSearchOpportunityAnalyzer.Analyze(
            observations,
            new WebSearchOpportunityOptions { SiteId = "officeimo", WeakPageMinimumPosition = double.NaN },
            generatedAt));
        Assert.Throws<ArgumentOutOfRangeException>(() => WebSearchOpportunityAnalyzer.Analyze(
            observations,
            new WebSearchOpportunityOptions { SiteId = "officeimo", WeakPageMaximumPosition = double.PositiveInfinity },
            generatedAt));
        Assert.Throws<ArgumentOutOfRangeException>(() => WebSearchOpportunityAnalyzer.Analyze(
            observations,
            new WebSearchOpportunityOptions { SiteId = "officeimo", CtrMaximumPosition = double.NegativeInfinity },
            generatedAt));
    }

    [Fact]
    public void Analyze_ExcludesZeroImpressionRowsFromPositionEvidence()
    {
        var unpositionedBatch = CreateBatch();
        unpositionedBatch.Observations[0].AveragePosition = null;
        var zeroImpressionBatch = CreateBatch();
        zeroImpressionBatch.CollectedAtUtc = zeroImpressionBatch.CollectedAtUtc.AddDays(1);
        zeroImpressionBatch.Observations[0].Date = zeroImpressionBatch.Observations[0].Date.AddDays(1);
        zeroImpressionBatch.Observations[0].Clicks = 0;
        zeroImpressionBatch.Observations[0].Impressions = 0;
        zeroImpressionBatch.Observations[0].ClickThroughRate = null;
        zeroImpressionBatch.Observations[0].AveragePosition = 9d;
        var observations = WebSearchObservationNormalizer.Normalize(unpositionedBatch).Observations
            .Concat(WebSearchObservationNormalizer.Normalize(zeroImpressionBatch).Observations);

        var report = WebSearchOpportunityAnalyzer.Analyze(
            observations,
            new WebSearchOpportunityOptions { SiteId = "officeimo" },
            new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(2, report.ObservationCount);
        Assert.Empty(report.Opportunities);
    }

    [Fact]
    public void Analyze_UsesOnlyPositionBackedRowsForOpportunityEvidenceAndMetrics()
    {
        var positionedBatch = CreateBatch();
        positionedBatch.Observations[0].Clicks = 0;
        positionedBatch.Observations[0].Impressions = 1;
        var unpositionedBatch = CreateBatch();
        unpositionedBatch.CollectedAtUtc = unpositionedBatch.CollectedAtUtc.AddDays(1);
        unpositionedBatch.Observations[0].Date = unpositionedBatch.Observations[0].Date.AddDays(1);
        unpositionedBatch.Observations[0].Clicks = 50;
        unpositionedBatch.Observations[0].Impressions = 1000;
        unpositionedBatch.Observations[0].AveragePosition = null;
        var positioned = WebSearchObservationNormalizer.Normalize(positionedBatch);
        var unpositioned = WebSearchObservationNormalizer.Normalize(unpositionedBatch);
        var observations = positioned.Observations.Concat(unpositioned.Observations).ToArray();
        var generatedAt = new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);

        var defaultReport = WebSearchOpportunityAnalyzer.Analyze(
            observations,
            new WebSearchOpportunityOptions { SiteId = "officeimo" },
            generatedAt);
        var lowVolumeReport = WebSearchOpportunityAnalyzer.Analyze(
            observations,
            new WebSearchOpportunityOptions { SiteId = "officeimo", MinimumImpressions = 1 },
            generatedAt);

        Assert.Empty(defaultReport.Opportunities);
        Assert.Equal(2, lowVolumeReport.ObservationCount);
        Assert.Equal(2, lowVolumeReport.Opportunities.Length);
        Assert.All(lowVolumeReport.Opportunities, opportunity =>
        {
            Assert.Equal(1, opportunity.Impressions);
            Assert.Equal(0, opportunity.Clicks);
            Assert.Equal(0d, opportunity.ClickThroughRate);
            Assert.Equal(new DateOnly(2026, 8, 1), opportunity.FromDate);
            Assert.Equal(new DateOnly(2026, 8, 1), opportunity.ThroughDate);
            Assert.Equal(new[] { positioned.Observations[0].ObservationKey }, opportunity.EvidenceObservationKeys);
        });
    }

    [Fact]
    public async Task SqliteStore_ImportsIdempotentlyAndQueriesNormalizedHistory()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var databasePath = Path.Combine(root, "history", "search.db");
            var normalized = WebSearchObservationNormalizer.Normalize(CreateBatch());
            var store = new SqliteWebSearchObservationStore(databasePath);

            var first = await store.ImportAsync(normalized);
            var second = await store.ImportAsync(normalized);
            var observations = await store.QueryAsync(new WebSearchObservationQuery
            {
                SiteId = "officeimo",
                Provider = "google-search-console",
                FromDate = new DateOnly(2026, 8, 1),
                ThroughDate = new DateOnly(2026, 8, 1)
            });

            Assert.Equal(1, first.InsertedCount);
            Assert.Equal(0, first.DuplicateCount);
            Assert.Equal(2, first.DatabaseSchemaVersion);
            Assert.Equal(0, second.InsertedCount);
            Assert.Equal(1, second.DuplicateCount);
            var observation = Assert.Single(observations);
            Assert.Equal(normalized.Observations[0].ObservationKey, observation.ObservationKey);
            Assert.Equal(100, observation.Impressions);
            Assert.True(File.Exists(databasePath));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SqliteStore_PreservesRevisionsButQueriesLatestDailySnapshot()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var databasePath = Path.Combine(root, "search.db");
            var firstBatch = CreateBatch();
            var revisedBatch = CreateBatch();
            revisedBatch.CollectedAtUtc = revisedBatch.CollectedAtUtc.AddDays(1);
            revisedBatch.Observations[0].Clicks = 4;
            revisedBatch.Observations[0].Impressions = 120;
            var first = WebSearchObservationNormalizer.Normalize(firstBatch);
            var revised = WebSearchObservationNormalizer.Normalize(revisedBatch);
            var store = new SqliteWebSearchObservationStore(databasePath);

            var firstResult = await store.ImportAsync(first);
            var revisedResult = await store.ImportAsync(revised);
            var observations = await store.QueryAsync(new WebSearchObservationQuery { SiteId = "officeimo" });

            Assert.Equal(1, firstResult.InsertedCount);
            Assert.Equal(1, revisedResult.InsertedCount);
            Assert.NotEqual(first.Observations[0].ObservationKey, revised.Observations[0].ObservationKey);
            var latest = Assert.Single(observations);
            Assert.Equal(4, latest.Clicks);
            Assert.Equal(120, latest.Impressions);
            Assert.Equal(revised.Observations[0].ObservationKey, latest.ObservationKey);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SqliteStore_RejectsRunIdentifierCollisionWithDifferentEvidence()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var databasePath = Path.Combine(root, "search.db");
            var first = WebSearchObservationNormalizer.Normalize(CreateBatch());
            var collisionInput = CreateBatch();
            collisionInput.RunId = first.RunId;
            collisionInput.Observations[0].Impressions = 101;
            var collision = WebSearchObservationNormalizer.Normalize(collisionInput);
            var store = new SqliteWebSearchObservationStore(databasePath);
            await store.ImportAsync(first);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => store.ImportAsync(collision));

            Assert.Contains("different normalized evidence", exception.ToString(), StringComparison.Ordinal);
            var observations = await store.QueryAsync(new WebSearchObservationQuery { SiteId = "officeimo" });
            Assert.Single(observations);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SqliteStore_RejectsCompetingRevisionsAtTheSameCollectionTime()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var databasePath = Path.Combine(root, "search.db");
            var first = WebSearchObservationNormalizer.Normalize(CreateBatch());
            var competingInput = CreateBatch();
            competingInput.Observations[0].Clicks = 4;
            competingInput.Observations[0].Impressions = 120;
            var competing = WebSearchObservationNormalizer.Normalize(competingInput);
            var store = new SqliteWebSearchObservationStore(databasePath);
            await store.ImportAsync(first);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => store.ImportAsync(competing));

            Assert.Contains("collection time", exception.Message, StringComparison.Ordinal);
            var current = Assert.Single(await store.QueryAsync(new WebSearchObservationQuery { SiteId = "officeimo" }));
            Assert.Equal(first.Observations[0].ObservationKey, current.ObservationKey);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SqliteStore_UpgradesVersionOneBeforeEnforcingCollectionTimeIdentity()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var databasePath = Path.Combine(root, "search.db");
            var first = WebSearchObservationNormalizer.Normalize(CreateBatch());
            var competingInput = CreateBatch();
            competingInput.Observations[0].Impressions = 110;
            var competing = WebSearchObservationNormalizer.Normalize(competingInput);
            var store = new SqliteWebSearchObservationStore(databasePath);
            await store.ImportAsync(first);
            await DowngradeDatabaseToVersionOneAsync(databasePath);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => store.ImportAsync(competing));

            Assert.Contains("collection time", exception.Message, StringComparison.Ordinal);
            await using var client = new SQLite();
            var version = await client.ExecuteScalarAsync(databasePath, "PRAGMA user_version;");
            Assert.Equal(2, Convert.ToInt32(version));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SqliteStore_BlocksVersionOneUpgradeWhenCollectionTimesAlreadyConflict()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var databasePath = Path.Combine(root, "search.db");
            var first = WebSearchObservationNormalizer.Normalize(CreateBatch());
            var store = new SqliteWebSearchObservationStore(databasePath);
            await store.ImportAsync(first);
            await DowngradeDatabaseToVersionOneAsync(databasePath);
            await using (var client = new SQLite())
            {
                await client.ExecuteNonQueryAsync(
                    databasePath,
                    """
                    INSERT INTO search_observation_runs (
                        run_id, provider, site_id, collected_at_utc, source_kind, status,
                        configuration_hash, evidence_reference, normalized_manifest_json
                    ) VALUES (
                        'legacy-conflict', 'google-search-console', 'officeimo',
                        '2026-08-02T08:00:00.0000000+00:00', 'fixture', 'complete',
                        NULL, NULL, '{}'
                    );
                    """);
            }

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.QueryAsync(new WebSearchObservationQuery { SiteId = "officeimo" }));

            Assert.Contains("schema v1 contains competing runs", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Cli_ImportsAndReportsThroughStableCommandSurface()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var inputPath = Path.Combine(root, "observations.json");
            var databasePath = Path.Combine(root, "search.db");
            File.WriteAllText(inputPath, JsonSerializer.Serialize(CreateBatch()));

            var importExitCode = WebCliCommandHandlers.HandleSubCommand(
                "observe",
                new[] { "import", "--input", inputPath, "--database", databasePath },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: 1);
            var reportExitCode = WebCliCommandHandlers.HandleSubCommand(
                "opportunity",
                new[] { "list", "--database", databasePath, "--site", "officeimo" },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: 1);

            Assert.Equal(0, importExitCode);
            Assert.Equal(0, reportExitCode);
            Assert.True(File.Exists(databasePath));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Cli_OpportunityList_FailsForMissingDatabase()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "opportunity",
                new[] { "list", "--database", Path.Combine(root, "missing.db"), "--site", "officeimo" },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: 1);

            Assert.Equal(2, exitCode);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Cli_OpportunityList_RejectsNonFiniteRate()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var inputPath = Path.Combine(root, "observations.json");
            var databasePath = Path.Combine(root, "search.db");
            File.WriteAllText(inputPath, JsonSerializer.Serialize(CreateBatch()));
            var importExitCode = WebCliCommandHandlers.HandleSubCommand(
                "observe",
                new[] { "import", "--input", inputPath, "--database", databasePath },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: 1);

            var reportExitCode = WebCliCommandHandlers.HandleSubCommand(
                "opportunity",
                new[] { "list", "--database", databasePath, "--site", "officeimo", "--min-ctr", "NaN" },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: 1);

            Assert.Equal(0, importExitCode);
            Assert.Equal(2, reportExitCode);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("schemaVersion", false)]
    [InlineData("provider", false)]
    [InlineData("siteId", false)]
    [InlineData("clicks", true)]
    [InlineData("impressions", true)]
    public void Cli_ObserveImport_RejectsMissingRequiredContractMembers(string propertyName, bool observationProperty)
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var inputPath = Path.Combine(root, "observations.json");
            var databasePath = Path.Combine(root, "search.db");
            var document = JsonNode.Parse(JsonSerializer.Serialize(CreateBatch()))!.AsObject();
            var target = observationProperty
                ? document["observations"]![0]!.AsObject()
                : document;
            Assert.True(target.Remove(propertyName));
            File.WriteAllText(inputPath, document.ToJsonString());

            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "observe",
                new[] { "import", "--input", inputPath, "--database", databasePath },
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
    public async Task Cli_ObserveImport_AppliesIdentityOverridesBeforeRequiredMemberValidation()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var inputPath = Path.Combine(root, "observations.json");
            var databasePath = Path.Combine(root, "search.db");
            var document = JsonNode.Parse(JsonSerializer.Serialize(CreateBatch()))!.AsObject();
            var provider = document["provider"]!.DeepClone();
            Assert.True(document.Remove("provider"));
            document["Provider"] = provider;
            Assert.True(document.Remove("siteId"));
            var json = document.ToJsonString();
            File.WriteAllText(inputPath, "/* provider export */" + json[..^1] + ",}");

            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "observe",
                new[]
                {
                    "import", "--input", inputPath, "--database", databasePath,
                    "--provider", " Bing-Webmaster ", "--site", " Tactra "
                },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: 1);

            Assert.Equal(0, exitCode);
            var observations = await new SqliteWebSearchObservationStore(databasePath).QueryAsync(
                new WebSearchObservationQuery { SiteId = "tactra" });
            var observation = Assert.Single(observations);
            Assert.Equal("bing-webmaster", observation.Provider);
            Assert.Equal("tactra", observation.SiteId);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static WebSearchObservationBatch CreateBatch() => new()
    {
        Provider = " Google-Search-Console ",
        SiteId = " OfficeIMO ",
        CollectedAtUtc = new DateTimeOffset(2026, 8, 2, 8, 0, 0, TimeSpan.Zero),
        SourceKind = " Fixture ",
        ConfigurationHash = "sha256:fixture",
        EvidenceReference = "fixtures/gsc-officeimo-2026-08-01.json",
        Observations = new[]
        {
            new WebSearchObservation
            {
                Date = new DateOnly(2026, 8, 1),
                Page = "https://officeimo.com/convert/?mode=fast#overview",
                Query = " convert office files ",
                Country = " PL ",
                Device = " Desktop ",
                SearchType = " Web ",
                Clicks = 1,
                Impressions = 100,
                AveragePosition = 9d
            }
        }
    };

    private static async Task DowngradeDatabaseToVersionOneAsync(string databasePath)
    {
        await using var client = new SQLite();
        await client.ExecuteNonQueryAsync(
            databasePath,
            """
            DROP INDEX ux_search_observation_runs_provider_site_collected;
            PRAGMA user_version = 1;
            """);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "pf-web-search-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for transient SQLite file locks on Windows.
        }
    }
}
