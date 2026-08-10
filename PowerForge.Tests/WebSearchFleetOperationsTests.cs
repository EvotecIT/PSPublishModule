using System.Text.Json.Nodes;
using System.Text.Json;
using DBAClientX;
using Json.Schema;
using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class WebSearchFleetOperationsTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Planner_BoundsBackfillAndDistinguishesApiFromInputDependentWork()
    {
        var configuration = CreateConfiguration();
        var snapshot = new WebSearchFleetEvidenceSnapshot
        {
            StoreExists = true,
            DatabaseSchemaVersion = 5,
            Streams =
            [
                Stream("gsc", WebSearchProviderCapabilities.SearchAnalytics, new DateOnly(2026, 8, 1), AsOf.AddDays(-1)),
                Stream("cloudflare", WebSearchProviderCapabilities.TrafficAnalytics, new DateOnly(2026, 7, 1), AsOf.AddDays(-1)),
                Stream("crux", WebSearchProviderCapabilities.PerformanceCrux, null, AsOf.AddDays(-2))
            ]
        };

        var plan = WebSearchFleetPlanner.CreateSchedule(configuration, Doctor(configuration), snapshot, AsOf);

        var search = Assert.Single(plan.WorkItems, value => value.ProviderId == "gsc");
        Assert.Equal("collect-search", search.Action);
        Assert.Equal("ready", search.Readiness);
        Assert.Equal(new DateOnly(2026, 8, 2), search.FromDate);
        Assert.Equal(new DateOnly(2026, 8, 7), search.ThroughDate);
        Assert.False(search.HasMoreBackfill);

        var traffic = Assert.Single(plan.WorkItems, value => value.ProviderId == "cloudflare");
        Assert.Equal(new DateOnly(2026, 7, 2), traffic.FromDate);
        Assert.Equal(new DateOnly(2026, 7, 8), traffic.ThroughDate);
        Assert.True(traffic.HasMoreBackfill);

        var export = Assert.Single(plan.WorkItems, value => value.ProviderId == "bing-export");
        Assert.Equal("import-bing-export", export.Action);
        Assert.Equal("input-required", export.Readiness);
        Assert.Equal(new DateOnly(2026, 1, 1), export.FromDate);
        Assert.Equal(new DateOnly(2026, 1, 7), export.ThroughDate);
        Assert.True(export.HasMoreBackfill);

        var lighthouse = Assert.Single(plan.WorkItems, value => value.ProviderId == "lighthouse");
        Assert.Equal("import-lighthouse", lighthouse.Action);
        Assert.Equal("input-required", lighthouse.Readiness);
        Assert.DoesNotContain(plan.WorkItems, value => value.ProviderId == "crux");
    }

    [Fact]
    public void Planner_BackfillsTheEarliestInternalGapWithoutRecollectingCompletedRanges()
    {
        var configuration = CreateConfiguration();
        configuration.Operations!.BackfillStartDate = null;
        configuration.Sites[0].Providers =
            [configuration.Sites[0].Providers.Single(value => value.Id == "gsc")];
        var snapshot = new WebSearchFleetEvidenceSnapshot
        {
            StoreExists = true,
            DatabaseSchemaVersion = 5,
            Streams =
            [
                new WebSearchFleetEvidenceStream
                {
                    SiteId = "officeimo", ProviderId = "gsc", Capability = WebSearchProviderCapabilities.SearchAnalytics, ScopeKey = "web",
                    LatestCompleteDate = new DateOnly(2026, 8, 7), LastCompleteAtUtc = AsOf.AddHours(-1), LastAttemptAtUtc = AsOf.AddHours(-1),
                    CompletedRanges =
                    [
                        new WebSearchFleetCompletedRange { FromDate = new DateOnly(2026, 8, 1), ThroughDate = new DateOnly(2026, 8, 3) },
                        new WebSearchFleetCompletedRange { FromDate = new DateOnly(2026, 8, 5), ThroughDate = new DateOnly(2026, 8, 7) }
                    ]
                }
            ]
        };

        var item = Assert.Single(WebSearchFleetPlanner.CreateSchedule(configuration, Doctor(configuration), snapshot, AsOf).WorkItems);

        Assert.Equal(new DateOnly(2026, 8, 4), item.FromDate);
        Assert.Equal(new DateOnly(2026, 8, 4), item.ThroughDate);
        Assert.False(item.HasMoreBackfill);
    }

    [Fact]
    public void Report_SeparatesDisabledMissingPartialDueAndCurrentStates()
    {
        var configuration = CreateConfiguration();
        configuration.Sites[0].Providers.Single(value => value.Id == "bing-export").Enabled = false;
        configuration.Sites[0].Providers.Single(value => value.Id == "gsc").Capabilities =
            [WebSearchProviderCapabilities.SearchAnalytics, WebSearchProviderCapabilities.SearchSitemaps];
        var doctor = WebSearchProviderDoctor.InspectWithCapabilities(
            configuration, WebSearchCollectorCatalog.AvailableCapabilities, _ => "test-credential");
        var snapshot = new WebSearchFleetEvidenceSnapshot
        {
            StoreExists = true,
            DatabaseSchemaVersion = 5,
            Streams =
            [
                Stream("gsc", WebSearchProviderCapabilities.SearchAnalytics, new DateOnly(2026, 8, 7), AsOf.AddHours(-1)),
                Stream("cloudflare", WebSearchProviderCapabilities.TrafficAnalytics, new DateOnly(2026, 8, 9), AsOf.AddHours(-1)),
                Stream("crux", WebSearchProviderCapabilities.PerformanceCrux, null, AsOf.AddDays(-8)),
                Stream("lighthouse", WebSearchProviderCapabilities.PerformanceLighthouse, null, AsOf.AddDays(-1), partial: true)
            ]
        };

        var report = WebSearchFleetPlanner.CreateReport(configuration, doctor, snapshot, AsOf);

        Assert.Equal("disabled", Row(report, "bing-export").EvidenceState);
        Assert.Equal("current", Row(report, "gsc", WebSearchProviderCapabilities.SearchAnalytics).EvidenceState);
        Assert.Equal("collector-unavailable", Row(report, "gsc", WebSearchProviderCapabilities.SearchSitemaps).EvidenceState);
        Assert.Equal("current", Row(report, "cloudflare").EvidenceState);
        Assert.Equal("due", Row(report, "crux").EvidenceState);
        Assert.Equal("partial", Row(report, "lighthouse").EvidenceState);
        Assert.True(report.NeedsAttention);
    }

    [Fact]
    public void Report_ReturnsConfigurationErrorsForDoctorRecognizedInvalidShapes()
    {
        var invalidUrl = new WebSearchProviderConfiguration
        {
            Sites =
            [
                new WebSearchSiteProviderConfiguration
                {
                    Id = "officeimo",
                    BaseUrl = "not-a-url",
                    Providers =
                    [
                        Provider(
                            "crux", "google-crux", WebSearchProviderCapabilities.PerformanceCrux,
                            new WebSearchCredentialReference { Kind = "google-api-key", EnvironmentVariable = "TEST_CRUX" })
                    ]
                }
            ]
        };
        var invalidUrlDoctor = Doctor(invalidUrl);

        var invalidUrlReport = WebSearchFleetPlanner.CreateReport(
            invalidUrl,
            invalidUrlDoctor,
            new WebSearchFleetEvidenceSnapshot(),
            AsOf);

        Assert.False(invalidUrlReport.ConfigurationValid);
        Assert.Equal("configuration-error", Assert.Single(invalidUrlReport.Rows).EvidenceState);
        Assert.Equal("configuration-error", Assert.Single(WebSearchFleetPlanner.CreateSchedule(
            invalidUrl, invalidUrlDoctor, new WebSearchFleetEvidenceSnapshot(), AsOf).WorkItems).Readiness);

        var nullSites = new WebSearchProviderConfiguration { Sites = null! };
        var nullSitesReport = WebSearchFleetPlanner.CreateReport(
            nullSites, Doctor(nullSites), new WebSearchFleetEvidenceSnapshot(), AsOf);
        Assert.False(nullSitesReport.ConfigurationValid);
        Assert.Equal(0, nullSitesReport.SiteCount);
        Assert.Empty(nullSitesReport.Rows);

        var nullCapabilities = CreateConfiguration();
        nullCapabilities.Sites[0].Providers = [nullCapabilities.Sites[0].Providers[0]];
        nullCapabilities.Sites[0].Providers[0].Capabilities = null!;
        var nullCapabilitiesReport = WebSearchFleetPlanner.CreateReport(
            nullCapabilities, Doctor(nullCapabilities), new WebSearchFleetEvidenceSnapshot(), AsOf);
        Assert.False(nullCapabilitiesReport.ConfigurationValid);
        Assert.Equal(1, nullCapabilitiesReport.ProviderCount);
        Assert.Empty(nullCapabilitiesReport.Rows);
    }

    [Fact]
    public async Task Store_BuildsSnapshotAcrossSearchTrafficAndPerformanceContracts()
    {
        var root = CreateTempRoot();
        try
        {
            var store = new SqliteWebSearchObservationStore(Path.Combine(root, "fleet.db"));
            await store.ImportAsync(WebSearchObservationNormalizer.Normalize(SearchBatch("search-run", new DateOnly(2026, 8, 7), AsOf.AddMinutes(-3))));
            await store.ImportTrafficAsync(WebTrafficObservationNormalizer.Normalize(TrafficBatch("traffic-run", new DateOnly(2026, 8, 9), AsOf.AddMinutes(-2))));
            await store.ImportPerformanceAsync(WebPerformanceObservationNormalizer.Normalize(PerformanceBatch("performance-run", AsOf.AddMinutes(-1))));

            var snapshot = await store.ReadFleetSnapshotAsync();

            Assert.True(snapshot.StoreExists);
            Assert.Equal(5, snapshot.DatabaseSchemaVersion);
            Assert.Equal(3, snapshot.Streams.Length);
            Assert.Equal(new DateOnly(2026, 8, 7), Assert.Single(snapshot.Streams, value => value.Capability == WebSearchProviderCapabilities.SearchAnalytics).LatestCompleteDate);
            Assert.Equal(new DateOnly(2026, 8, 9), Assert.Single(snapshot.Streams, value => value.Capability == WebSearchProviderCapabilities.TrafficAnalytics).LatestCompleteDate);
            Assert.Equal(AsOf.AddMinutes(-1), Assert.Single(snapshot.Streams, value => value.Capability == WebSearchProviderCapabilities.PerformanceCrux).LastCompleteAtUtc);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task StoreAndPlanner_KeepSearchTypeAndPerformanceTargetScopesDistinct()
    {
        var root = CreateTempRoot();
        try
        {
            var store = new SqliteWebSearchObservationStore(Path.Combine(root, "fleet.db"));
            var imageSearch = SearchBatch("image-search", new DateOnly(2026, 8, 7), AsOf.AddMinutes(-2), "image");
            var phoneUrl = PerformanceBatch("phone-url", AsOf.AddMinutes(-1));
            phoneUrl.TargetKind = "url";
            phoneUrl.TargetUrl = "https://officeimo.com/docs/";
            await store.ImportAsync(WebSearchObservationNormalizer.Normalize(imageSearch));
            await store.ImportPerformanceAsync(WebPerformanceObservationNormalizer.Normalize(phoneUrl));

            var snapshot = await store.ReadFleetSnapshotAsync();
            Assert.Contains(snapshot.Streams, value =>
                value.Capability == WebSearchProviderCapabilities.SearchAnalytics && value.ScopeKey == "image");
            Assert.Contains(snapshot.Streams, value =>
                value.Capability == WebSearchProviderCapabilities.PerformanceCrux &&
                value.ScopeKey == string.Join("\u001f", "field", "url", "https://officeimo.com/docs/", "phone"));

            var configuration = CreateConfiguration();
            configuration.Sites[0].Providers = configuration.Sites[0].Providers
                .Where(value => value.Id is "gsc" or "crux")
                .ToArray();
            var plan = WebSearchFleetPlanner.CreateSchedule(configuration, Doctor(configuration), snapshot, AsOf);

            Assert.Contains(plan.WorkItems, value => value.ProviderId == "gsc" && value.Capability == WebSearchProviderCapabilities.SearchAnalytics);
            Assert.Contains(plan.WorkItems, value => value.ProviderId == "crux" && value.Capability == WebSearchProviderCapabilities.PerformanceCrux);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Retention_PreservesNewestReportingEvidenceIncludingCompletedPartialPartitions()
    {
        var root = CreateTempRoot();
        try
        {
            var path = Path.Combine(root, "fleet.db");
            var store = new SqliteWebSearchObservationStore(path);
            await store.ImportAsync(WebSearchObservationNormalizer.Normalize(
                SearchBatch("complete-current", new DateOnly(2025, 6, 1), new DateTimeOffset(2025, 6, 2, 0, 0, 0, TimeSpan.Zero))));
            await store.ImportAsync(WebSearchObservationNormalizer.Normalize(
                SearchBatch("later-collected-backfill", new DateOnly(2020, 1, 1), new DateTimeOffset(2025, 7, 2, 0, 0, 0, TimeSpan.Zero))));
            await store.ImportAsync(WebSearchObservationNormalizer.Normalize(
                PartialSearchBatch("partial-current", new DateOnly(2025, 7, 1), new DateTimeOffset(2025, 7, 3, 0, 0, 0, TimeSpan.Zero))));
            var policy = new WebSearchFleetOperationsConfiguration
            {
                SearchRunRetentionDays = 30,
                TrafficRunRetentionDays = 30,
                PerformanceRunRetentionDays = 30
            };

            var dryRun = await store.ApplyFleetRetentionAsync(policy, AsOf, apply: false);
            var drySearch = Assert.Single(dryRun.Kinds, value => value.Kind == "search");
            Assert.Equal(2, drySearch.CandidateRunCount);
            Assert.Equal(2, drySearch.CandidateObservationCount);
            Assert.Equal(0, drySearch.DeletedRunCount);
            Assert.Single(await store.QueryAsync(new WebSearchObservationQuery
            {
                SiteId = "officeimo", FromDate = new DateOnly(2020, 1, 1), ThroughDate = new DateOnly(2020, 1, 1)
            }));

            var applied = await store.ApplyFleetRetentionAsync(policy, AsOf, apply: true);
            var appliedSearch = Assert.Single(applied.Kinds, value => value.Kind == "search");
            Assert.Equal(2, appliedSearch.DeletedRunCount);
            Assert.Equal(2, appliedSearch.DeletedObservationCount);
            Assert.Empty(await store.QueryAsync(new WebSearchObservationQuery
            {
                SiteId = "officeimo", FromDate = new DateOnly(2020, 1, 1), ThroughDate = new DateOnly(2020, 1, 1)
            }));
            Assert.Empty(await store.QueryAsync(new WebSearchObservationQuery
            {
                SiteId = "officeimo", FromDate = new DateOnly(2025, 6, 1), ThroughDate = new DateOnly(2025, 6, 1)
            }));
            Assert.Single(await store.QueryAsync(new WebSearchObservationQuery
            {
                SiteId = "officeimo", FromDate = new DateOnly(2025, 7, 1), ThroughDate = new DateOnly(2025, 7, 1)
            }));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FleetReads_RejectLegacySchemaWithoutMigratingIt()
    {
        var root = CreateTempRoot();
        try
        {
            var path = Path.Combine(root, "fleet.db");
            var store = new SqliteWebSearchObservationStore(path);
            await store.ImportTrafficAsync(WebTrafficObservationNormalizer.Normalize(TrafficBatch("traffic-run", new DateOnly(2026, 8, 1), AsOf)));
            await using var sqlite = new SQLite();
            await sqlite.ExecuteNonQueryAsync(path, "DROP TABLE performance_observations; DROP TABLE performance_observation_runs; PRAGMA user_version = 4;");

            await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReadFleetSnapshotAsync());
            Assert.Equal(4, Convert.ToInt32(await sqlite.ExecuteScalarAsync(path, "PRAGMA user_version;")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ProviderSchemaAndDoctorValidateOperationsPolicy()
    {
        var schema = JsonSchema.FromText(File.ReadAllText(RepositoryPath("Schemas", "powerforge.web.search-providers.schema.json")));
        var example = JsonNode.Parse(File.ReadAllText(RepositoryPath("Examples", "PowerForge.Web", "Search", "providers.json")))!;
        Assert.True(schema.Evaluate(example, new EvaluationOptions()).IsValid);

        var configuration = CreateConfiguration();
        configuration.Operations!.MaxBackfillDaysPerRun = 0;
        var result = WebSearchProviderDoctor.InspectWithCapabilities(
            configuration, WebSearchCollectorCatalog.AvailableCapabilities, _ => "test-credential");
        Assert.False(result.Success);
        Assert.Contains(result.Checks, value => value.Code == "configuration.operations-invalid");
    }

    [Fact]
    public void FleetCli_PlansMissingStorageButRejectsInvalidTimeBeforeCreatingIt()
    {
        var root = CreateTempRoot();
        try
        {
            var configPath = Path.Combine(root, "providers.json");
            var databasePath = Path.Combine(root, "missing.db");
            var configuration = new WebSearchProviderConfiguration
            {
                Operations = new WebSearchFleetOperationsConfiguration { BackfillStartDate = new DateOnly(2026, 1, 1) },
                Sites =
                [
                    new WebSearchSiteProviderConfiguration
                    {
                        Id = "officeimo", BaseUrl = "https://officeimo.com/",
                        Providers = [Provider("lighthouse", "lighthouse", WebSearchProviderCapabilities.PerformanceLighthouse)]
                    }
                ]
            };
            File.WriteAllText(configPath, JsonSerializer.Serialize(configuration, WebCliJson.Options));

            Assert.Equal(0, WebCliCommandHandlers.HandleSubCommand(
                "fleet",
                ["schedule", "--config", configPath, "--database", databasePath, "--as-of", "2026-08-10T12:00:00Z", "--output", "json"],
                outputJson: true, logger: new WebConsoleLogger(), outputSchemaVersion: 1));
            Assert.False(File.Exists(databasePath));

            Assert.Equal(2, WebCliCommandHandlers.HandleSubCommand(
                "fleet",
                ["schedule", "--config", configPath, "--database", databasePath, "--as-of", "2026-08-10T12:00:00", "--output", "json"],
                outputJson: true, logger: new WebConsoleLogger(), outputSchemaVersion: 1));
            Assert.False(File.Exists(databasePath));

            Assert.Equal(2, WebCliCommandHandlers.HandleSubCommand(
                "fleet",
                ["prune", "--config", configPath, "--database", databasePath, "--output", "json"],
                outputJson: true, logger: new WebConsoleLogger(), outputSchemaVersion: 1));
            Assert.False(File.Exists(databasePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FleetCli_ReportsMissingCredentialsButPrunesWithoutSecretsAndRequiresExactApplyFlag()
    {
        var root = CreateTempRoot();
        try
        {
            var configPath = Path.Combine(root, "providers.json");
            var databasePath = Path.Combine(root, "fleet.db");
            var configuration = new WebSearchProviderConfiguration
            {
                Operations = new WebSearchFleetOperationsConfiguration
                {
                    SearchRunRetentionDays = 30,
                    TrafficRunRetentionDays = 30,
                    PerformanceRunRetentionDays = 30
                },
                Sites =
                [
                    new WebSearchSiteProviderConfiguration
                    {
                        Id = "officeimo", BaseUrl = "https://officeimo.com/",
                        Providers =
                        [
                            Provider(
                                "gsc", "google-search-console", WebSearchProviderCapabilities.SearchAnalytics,
                                new WebSearchCredentialReference
                                {
                                    Kind = "google-service-account-json",
                                    EnvironmentVariable = "POWERFORGE_FLEET_TEST_UNAVAILABLE_CREDENTIAL"
                                },
                                new Dictionary<string, string?> { ["property"] = "sc-domain:officeimo.com" })
                        ]
                    }
                ]
            };
            File.WriteAllText(configPath, JsonSerializer.Serialize(configuration, WebCliJson.Options));
            var store = new SqliteWebSearchObservationStore(databasePath);
            await store.ImportAsync(WebSearchObservationNormalizer.Normalize(
                SearchBatch("older", new DateOnly(2025, 1, 1), new DateTimeOffset(2025, 1, 2, 0, 0, 0, TimeSpan.Zero))));
            await store.ImportAsync(WebSearchObservationNormalizer.Normalize(
                SearchBatch("newer", new DateOnly(2025, 2, 1), new DateTimeOffset(2025, 2, 2, 0, 0, 0, TimeSpan.Zero))));

            Assert.Equal(2, WebCliCommandHandlers.HandleSubCommand(
                "fleet",
                ["report", "--config", configPath, "--database", databasePath, "--as-of", "2026-08-10T12:00:00Z", "--output", "json"],
                outputJson: true, logger: new WebConsoleLogger(), outputSchemaVersion: 1));
            var report = WebSearchFleetPlanner.CreateReport(
                configuration,
                WebSearchProviderDoctor.InspectWithCapabilities(configuration, WebSearchCollectorCatalog.AvailableCapabilities),
                await store.ReadFleetSnapshotAsync(),
                AsOf);
            Assert.Equal("configuration-error", Assert.Single(report.Rows).EvidenceState);

            Assert.Equal(0, WebCliCommandHandlers.HandleSubCommand(
                "fleet",
                ["prune", "--config", configPath, "--database", databasePath, "--as-of", "2026-08-10T12:00:00Z", "--output", "json"],
                outputJson: true, logger: new WebConsoleLogger(), outputSchemaVersion: 1));
            Assert.Equal(2, WebCliCommandHandlers.HandleSubCommand(
                "fleet",
                ["prune", "--config", configPath, "--database", databasePath, "--as-of", "2026-08-10T12:00:00Z", "--apply", "false", "--output", "json"],
                outputJson: true, logger: new WebConsoleLogger(), outputSchemaVersion: 1));
            await using var sqlite = new SQLite();
            Assert.Equal(2, Convert.ToInt32(await sqlite.ExecuteScalarAsync(databasePath, "SELECT COUNT(*) FROM search_observation_runs;")));

            Assert.Equal(0, WebCliCommandHandlers.HandleSubCommand(
                "fleet",
                ["prune", "--config", configPath, "--database", databasePath, "--as-of", "2026-08-10T12:00:00Z", "--apply", "--output", "json"],
                outputJson: true, logger: new WebConsoleLogger(), outputSchemaVersion: 1));
            Assert.Equal(1, Convert.ToInt32(await sqlite.ExecuteScalarAsync(databasePath, "SELECT COUNT(*) FROM search_observation_runs;")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static WebSearchFleetReportRow Row(WebSearchFleetReport report, string provider, string? capability = null) =>
        Assert.Single(report.Rows, value => value.ProviderId == provider &&
                                           (capability is null || value.Capability == capability));

    private static WebSearchFleetEvidenceStream Stream(
        string provider,
        string capability,
        DateOnly? latestDate,
        DateTimeOffset completeAt,
        bool partial = false) => new()
    {
        SiteId = "officeimo",
        ProviderId = provider,
        Capability = capability,
        ScopeKey = capability switch
        {
            WebSearchProviderCapabilities.SearchAnalytics => "web",
            WebSearchProviderCapabilities.TrafficAnalytics => "traffic",
            WebSearchProviderCapabilities.PerformanceCrux => string.Join("\u001f", "field", "origin", "https://officeimo.com/", "all"),
            _ => string.Join("\u001f", "lab", "url", "https://officeimo.com/", "phone")
        },
        LatestCompleteDate = latestDate,
        CompletedRanges = latestDate is DateOnly through
            ? [new WebSearchFleetCompletedRange { FromDate = new DateOnly(2026, 1, 1), ThroughDate = through }]
            : Array.Empty<WebSearchFleetCompletedRange>(),
        LastCompleteAtUtc = completeAt,
        LastAttemptAtUtc = partial ? completeAt.AddMinutes(1) : completeAt,
        HasPartialEvidence = partial,
        RunCount = partial ? 2 : 1
    };

    private static WebSearchProviderDoctorResult Doctor(WebSearchProviderConfiguration configuration) =>
        WebSearchProviderDoctor.InspectWithCapabilities(
            configuration,
            WebSearchCollectorCatalog.AvailableCapabilities,
            _ => "test-credential");

    private static WebSearchProviderConfiguration CreateConfiguration() => new()
    {
        Operations = new WebSearchFleetOperationsConfiguration
        {
            BackfillStartDate = new DateOnly(2026, 1, 1),
            MaxBackfillDaysPerRun = 7,
            SearchDataLagDays = 3,
            TrafficDataLagDays = 1,
            CruxIntervalDays = 7,
            LighthouseIntervalDays = 7
        },
        Sites =
        [
            new WebSearchSiteProviderConfiguration
            {
                Id = "officeimo",
                BaseUrl = "https://officeimo.com/",
                Providers =
                [
                    Provider("gsc", "google-search-console", WebSearchProviderCapabilities.SearchAnalytics,
                        new WebSearchCredentialReference { Kind = "google-service-account-json", EnvironmentVariable = "TEST_GSC" },
                        new Dictionary<string, string?> { ["property"] = "sc-domain:officeimo.com" }),
                    Provider("bing-export", "bing-webmaster-export", WebSearchProviderCapabilities.SearchAnalytics),
                    Provider("cloudflare", "cloudflare-analytics", WebSearchProviderCapabilities.TrafficAnalytics,
                        new WebSearchCredentialReference { Kind = "cloudflare-api-token", EnvironmentVariable = "TEST_CF" },
                        new Dictionary<string, string?> { ["zoneId"] = "00000000000000000000000000000000" }),
                    Provider("lighthouse", "lighthouse", WebSearchProviderCapabilities.PerformanceLighthouse),
                    Provider("crux", "google-crux", WebSearchProviderCapabilities.PerformanceCrux,
                        new WebSearchCredentialReference { Kind = "google-api-key", EnvironmentVariable = "TEST_CRUX" })
                ]
            }
        ]
    };

    private static WebSearchProviderRegistration Provider(
        string id,
        string kind,
        string capability,
        WebSearchCredentialReference? credential = null,
        Dictionary<string, string?>? settings = null) => new()
    {
        Id = id,
        Kind = kind,
        Capabilities = [capability],
        Credential = credential,
        Settings = settings ?? new Dictionary<string, string?>()
    };

    private static WebSearchObservationBatch SearchBatch(
        string runId,
        DateOnly date,
        DateTimeOffset collectedAt,
        string searchType = "web") => new()
    {
        RunId = runId,
        Provider = "gsc",
        SiteId = "officeimo",
        CollectedAtUtc = collectedAt,
        SourceKind = "fixture",
        Status = "complete",
        CollectionCoverage = new WebSearchObservationCollectionCoverage
        {
            Mode = "daily", FromDate = date, ThroughDate = date, SearchType = searchType, CompletedDates = [date]
        },
        Observations =
        [
            new WebSearchObservation { Date = date, Page = "https://officeimo.com/", SearchType = searchType, Clicks = 1, Impressions = 2 }
        ]
    };

    private static WebSearchObservationBatch PartialSearchBatch(string runId, DateOnly completedDate, DateTimeOffset collectedAt) => new()
    {
        RunId = runId,
        Provider = "gsc",
        SiteId = "officeimo",
        CollectedAtUtc = collectedAt,
        SourceKind = "fixture",
        Status = "partial",
        CollectionCoverage = new WebSearchObservationCollectionCoverage
        {
            Mode = "daily",
            FromDate = completedDate,
            ThroughDate = completedDate.AddDays(1),
            SearchType = "web",
            CompletedDates = [completedDate],
            FailedDate = completedDate.AddDays(1),
            FailureCategory = "provider-unavailable"
        },
        Observations =
        [
            new WebSearchObservation { Date = completedDate, Page = "https://officeimo.com/", SearchType = "web", Clicks = 1, Impressions = 2 }
        ]
    };

    private static WebTrafficObservationBatch TrafficBatch(string runId, DateOnly date, DateTimeOffset collectedAt) => new()
    {
        RunId = runId,
        Provider = "cloudflare",
        SiteId = "officeimo",
        CollectedAtUtc = collectedAt,
        SourceKind = "fixture",
        Status = "complete",
        CollectionCoverage = new WebTrafficObservationCollectionCoverage
        {
            FromDate = date, ThroughDate = date, CompletedDates = [date]
        },
        Observations =
        [
            new WebTrafficObservation { Date = date, Host = "officeimo.com", Path = "/", Requests = 2, Visits = 1, EdgeResponseBytes = 100 }
        ]
    };

    private static WebPerformanceObservationBatch PerformanceBatch(string runId, DateTimeOffset collectedAt) => new()
    {
        RunId = runId,
        Provider = "crux",
        SiteId = "officeimo",
        CollectedAtUtc = collectedAt,
        SourceKind = "fixture",
        Status = "complete",
        MeasurementKind = "field",
        TargetKind = "origin",
        TargetUrl = "https://officeimo.com/",
        FormFactor = "phone",
        Observations =
        [
            new WebPerformanceObservation
            {
                Metric = "largest-contentful-paint", Value = 2000, Unit = "milliseconds", Percentile = 75,
                PeriodStartDate = new DateOnly(2026, 7, 12), PeriodEndDate = new DateOnly(2026, 8, 8),
                Histogram =
                [
                    new WebPerformanceHistogramBin { Start = 0, End = 2500, Density = 0.8 },
                    new WebPerformanceHistogramBin { Start = 2500, Density = 0.2 }
                ]
            }
        ]
    };

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "powerforge-fleet-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string RepositoryPath(params string[] parts)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        return Path.Combine([root, .. parts]);
    }
}
