using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DBAClientX;
using Json.Schema;
using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class WebPerformanceObservationTests
{
    private static readonly DateTimeOffset CollectionTime = new(2026, 8, 10, 12, 34, 56, TimeSpan.Zero);

    [Fact]
    public void LighthouseImport_PreservesLabSemanticsAndNeverInventsInp()
    {
        var batch = LighthouseReportImporter.Import(LighthouseReport(), LighthouseOptions());

        Assert.Equal("lab", batch.MeasurementKind);
        Assert.Equal("phone", batch.FormFactor);
        Assert.Equal("https://officeimo.com/docs/", batch.TargetUrl);
        Assert.Equal(CollectionTime, batch.CollectedAtUtc);
        Assert.Equal("12.3.0", batch.ToolVersion);
        Assert.Equal(6, batch.Observations.Length);
        Assert.Contains(batch.Observations, value => value.Metric == "total-blocking-time" && value.Value == 120);
        Assert.DoesNotContain(batch.Observations, value => value.Metric == "interaction-to-next-paint");
        Assert.All(batch.Observations, value =>
        {
            Assert.Null(value.Percentile);
            Assert.Null(value.PeriodStartDate);
            Assert.Empty(value.Histogram);
        });
    }

    [Fact]
    public void LighthouseImport_RejectsAnotherFleetSitesFinalUrl()
    {
        var json = LighthouseReport().Replace("https://officeimo.com/docs/", "https://tactra.dev/", StringComparison.Ordinal);

        var exception = Assert.Throws<ArgumentException>(() => LighthouseReportImporter.Import(json, LighthouseOptions()));

        Assert.Contains("does not belong", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LighthouseImport_RejectsDuplicateMembersAndOffsetlessFetchTime()
    {
        var duplicate = LighthouseReport().Replace(
            "\"finalUrl\": \"https://officeimo.com/docs/\",",
            "\"finalUrl\": \"https://officeimo.com/docs/\", \"finalUrl\": \"https://officeimo.com/other/\",",
            StringComparison.Ordinal);
        Assert.Contains("duplicate JSON member", Assert.Throws<ArgumentException>(() =>
            LighthouseReportImporter.Import(duplicate, LighthouseOptions())).Message, StringComparison.OrdinalIgnoreCase);

        var offsetless = LighthouseReport().Replace("2026-08-10T12:34:56Z", "2026-08-10T12:34:56", StringComparison.Ordinal);
        Assert.Contains("explicit UTC offset", Assert.Throws<ArgumentException>(() =>
            LighthouseReportImporter.Import(offsetless, LighthouseOptions())).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Normalizer_RejectsLabInpAndNonFiniteMetrics()
    {
        var batch = CreateLabBatch();
        batch.Observations = [new WebPerformanceObservation { Metric = "interaction-to-next-paint", Value = 100, Unit = "milliseconds" }];
        Assert.Throws<ArgumentException>(() => WebPerformanceObservationNormalizer.Normalize(batch));

        batch.Observations = [new WebPerformanceObservation { Metric = "largest-contentful-paint", Value = double.NaN, Unit = "milliseconds" }];
        Assert.Throws<ArgumentException>(() => WebPerformanceObservationNormalizer.Normalize(batch));
    }

    [Fact]
    public void Normalizer_RejectsFieldMetricsWithoutP75PeriodAndCompleteHistogram()
    {
        var batch = CreateFieldBatch();
        batch.Observations[0].Histogram[0].Density = 0.1;

        var exception = Assert.Throws<ArgumentException>(() => WebPerformanceObservationNormalizer.Normalize(batch));

        Assert.Contains("sum to one", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Normalizer_RequiresTheCruxTwentyEightDayPeriod()
    {
        var batch = CreateFieldBatch();
        batch.Observations[0].PeriodEndDate = new DateOnly(2026, 8, 7);

        var exception = Assert.Throws<ArgumentException>(() => WebPerformanceObservationNormalizer.Normalize(batch));

        Assert.Contains("28-day", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CruxCollector_MapsP75PeriodAndHistogramWithoutSendingAllAsNullDimension()
    {
        var handler = new ScriptedHandler(_ => JsonResponse(CruxResponse()));
        using var client = new HttpClient(handler);
        var collector = new CruxCollector(client, new FakeApiKeyProvider(), new FixedTimeProvider(CollectionTime));
        var options = CruxOptions();

        var result = await collector.CollectAsync(options);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(1, result.RequestCount);
        Assert.Equal("field", result.Batch.MeasurementKind);
        Assert.Equal("all", result.Batch.FormFactor);
        Assert.Equal(5, result.Batch.Observations.Length);
        var lcp = Assert.Single(result.Batch.Observations, value => value.Metric == "largest-contentful-paint");
        Assert.Equal(2300, lcp.Value);
        Assert.Equal(75, lcp.Percentile);
        Assert.Equal(new DateOnly(2026, 7, 12), lcp.PeriodStartDate);
        Assert.Equal(new DateOnly(2026, 8, 8), lcp.PeriodEndDate);
        Assert.Equal(3, lcp.Histogram.Length);
        var request = Assert.Single(handler.Requests);
        Assert.DoesNotContain("formFactor", request.Body, StringComparison.Ordinal);
        Assert.Empty(request.Uri.Query);
        Assert.Equal("test-api-key", request.ApiKey);
        Assert.DoesNotContain("test-api-key", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CruxCollector_StoresA404AsExactCompleteZeroEvidence()
    {
        var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new HttpClient(handler);
        var collector = new CruxCollector(client, new FakeApiKeyProvider(), new FixedTimeProvider(CollectionTime));

        var result = await collector.CollectAsync(CruxOptions());

        Assert.True(result.Success);
        Assert.True(result.Batch.ZeroDataConfirmed);
        Assert.Equal("complete", result.Batch.Status);
        Assert.Empty(result.Batch.Observations);
    }

    [Fact]
    public async Task CruxCollector_AcceptsProviderNumericStringsAndRejectsAMismatchedFormFactor()
    {
        var json = JsonSerializer.Serialize(CruxResponse()).Replace("2300", "\"2300\"", StringComparison.Ordinal);
        var stringHandler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        using var stringClient = new HttpClient(stringHandler);
        var stringResult = await new CruxCollector(stringClient, new FakeApiKeyProvider()).CollectAsync(CruxOptions());
        Assert.True(stringResult.Success, stringResult.ErrorMessage);
        Assert.All(stringResult.Batch.Observations, value => Assert.Equal(2300, value.Value));

        var mismatchHandler = new ScriptedHandler(_ => JsonResponse(CruxResponse()));
        using var mismatchClient = new HttpClient(mismatchHandler);
        var phone = CruxOptions();
        phone.FormFactor = "phone";
        var mismatch = await new CruxCollector(mismatchClient, new FakeApiKeyProvider()).CollectAsync(phone);
        Assert.False(mismatch.Success);
        Assert.Equal("invalid-response", mismatch.ErrorCode);
        Assert.Contains("form factor", mismatch.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CruxCollector_MissingCredentialDoesNotIssueAProviderRequest()
    {
        var handler = new ScriptedHandler(_ => throw new InvalidOperationException("Provider must not be reached."));
        using var client = new HttpClient(handler);
        var collector = new CruxCollector(client, new MissingApiKeyProvider(), new FixedTimeProvider(CollectionTime));

        var result = await collector.CollectAsync(CruxOptions());

        Assert.False(result.Success);
        Assert.Equal("credential-unavailable", result.ErrorCode);
        Assert.Equal(0, result.RequestCount);
        Assert.DoesNotContain("API key unavailable", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CruxCollector_NetworkFailuresDoNotEchoTheApiKey()
    {
        var handler = new ScriptedHandler(_ => throw new HttpRequestException("failed https://provider/?key=test-api-key"));
        using var client = new HttpClient(handler);

        var result = await new CruxCollector(client, new FakeApiKeyProvider()).CollectAsync(CruxOptions());

        Assert.False(result.Success);
        Assert.Equal("provider-unavailable", result.ErrorCode);
        Assert.DoesNotContain("test-api-key", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PerformanceStorage_MigratesSchemaFourAndSelectsCompleteBeforeRecency()
    {
        var root = Path.Combine(Path.GetTempPath(), "powerforge-performance-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "fleet.db");
            var store = new SqliteWebSearchObservationStore(path);
            await store.ImportTrafficAsync(WebTrafficObservationNormalizer.Normalize(CreateTrafficBatch()));
            await using (var sqlite = new SQLite())
            {
                await sqlite.ExecuteNonQueryAsync(path, "DROP TABLE performance_observations; DROP TABLE performance_observation_runs; PRAGMA user_version = 4;");
            }

            var complete = WebPerformanceObservationNormalizer.Normalize(CreateFieldBatch());
            var first = await store.ImportPerformanceAsync(complete);
            var duplicate = await store.ImportPerformanceAsync(complete);
            var partial = CreateFieldBatch();
            partial.CollectedAtUtc = CollectionTime.AddMinutes(1);
            partial.Status = "partial";
            partial.Observations[0].Value = 5000;
            await store.ImportPerformanceAsync(WebPerformanceObservationNormalizer.Normalize(partial));

            var evidence = await store.QueryPerformanceEvidenceAsync(new WebPerformanceObservationQuery { SiteId = "officeimo", MeasurementKind = "field" });

            Assert.Equal(5, first.DatabaseSchemaVersion);
            Assert.Equal(1, first.InsertedCount);
            Assert.Equal(1, duplicate.DuplicateCount);
            Assert.False(evidence.HasPartialEvidence);
            Assert.Equal(2300, Assert.Single(evidence.Observations).Value);
            Assert.Equal(complete.RunId, Assert.Single(evidence.SelectedRuns).RunId);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PerformanceStorage_AllowsSameTimestampForDistinctFormFactors()
    {
        var root = Path.Combine(Path.GetTempPath(), "powerforge-performance-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new SqliteWebSearchObservationStore(Path.Combine(root, "fleet.db"));
            var phone = WebPerformanceObservationNormalizer.Normalize(CreateFieldBatch());
            var desktopBatch = CreateFieldBatch();
            desktopBatch.FormFactor = "desktop";
            desktopBatch.Observations[0].Value = 1800;
            var desktop = WebPerformanceObservationNormalizer.Normalize(desktopBatch);

            await store.ImportPerformanceAsync(phone);
            await store.ImportPerformanceAsync(desktop);
            var evidence = await store.QueryPerformanceEvidenceAsync(new WebPerformanceObservationQuery { SiteId = "officeimo" });

            Assert.Equal(2, evidence.SelectedRuns.Length);
            Assert.Equal(2, evidence.Observations.Length);
            Assert.Contains(evidence.SelectedRuns, value => value.FormFactor == "phone");
            Assert.Contains(evidence.SelectedRuns, value => value.FormFactor == "desktop");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PerformanceStorage_ScopesExternalRunAndObservationIdentitiesByFleetSite()
    {
        var root = Path.Combine(Path.GetTempPath(), "powerforge-performance-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new SqliteWebSearchObservationStore(Path.Combine(root, "fleet.db"));
            var office = CreateFieldBatch();
            office.RunId = "external-run";
            var tactra = CreateFieldBatch();
            tactra.RunId = "external-run";
            tactra.SiteId = "tactra";
            tactra.TargetUrl = "https://tactra.dev/";

            var first = await store.ImportPerformanceAsync(WebPerformanceObservationNormalizer.Normalize(office));
            var second = await store.ImportPerformanceAsync(WebPerformanceObservationNormalizer.Normalize(tactra));
            var officeEvidence = await store.QueryPerformanceEvidenceAsync(new WebPerformanceObservationQuery { SiteId = "officeimo" });
            var tactraEvidence = await store.QueryPerformanceEvidenceAsync(new WebPerformanceObservationQuery { SiteId = "tactra" });

            Assert.Equal(1, first.InsertedCount);
            Assert.Equal(1, second.InsertedCount);
            Assert.Single(officeEvidence.Observations);
            Assert.Single(tactraEvidence.Observations);
            Assert.NotEqual(officeEvidence.Observations[0].ObservationKey, tactraEvidence.Observations[0].ObservationKey);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PerformanceList_RejectsInvalidFiltersBeforeOpeningOrMigratingStorage()
    {
        var root = Path.Combine(Path.GetTempPath(), "powerforge-performance-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var databasePath = Path.Combine(root, "fleet.db");
            var missingPath = Path.Combine(root, "missing.db");
            var store = new SqliteWebSearchObservationStore(databasePath);
            await store.ImportTrafficAsync(WebTrafficObservationNormalizer.Normalize(CreateTrafficBatch()));
            await using var sqlite = new SQLite();
            await sqlite.ExecuteNonQueryAsync(databasePath,
                "DROP TABLE performance_observations; DROP TABLE performance_observation_runs; PRAGMA user_version = 4;");

            var invalidFilters = new[]
            {
                new[] { "--kind", "nonsense" },
                new[] { "--form-factor", "television" },
                new[] { "--target", "not-a-url" }
            };
            foreach (var invalid in invalidFilters)
            {
                var args = new[] { "list", "--database", databasePath, "--site", "officeimo", "--output", "json" }
                    .Concat(invalid).ToArray();
                Assert.Equal(2, WebCliCommandHandlers.HandleSubCommand(
                    "performance", args, outputJson: true, logger: new WebConsoleLogger(), outputSchemaVersion: 1));
                Assert.Equal(4, Convert.ToInt32(await sqlite.ExecuteScalarAsync(databasePath, "PRAGMA user_version;")));
                var tables = await sqlite.QueryAsListAsync(databasePath,
                    "SELECT name FROM sqlite_master WHERE name LIKE 'performance_%';", static record => record.GetString(0));
                Assert.Empty(tables);
            }

            Assert.Equal(2, WebCliCommandHandlers.HandleSubCommand(
                "performance",
                ["list", "--database", missingPath, "--site", "officeimo", "--kind", "nonsense", "--output", "json"],
                outputJson: true, logger: new WebConsoleLogger(), outputSchemaVersion: 1));
            Assert.False(File.Exists(missingPath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TargetOwnership_DoesNotLetASubdomainFleetSiteClaimItsParentApex()
    {
        Assert.False(WebPerformanceObservationNormalizer.TargetBelongsToSite(
            "https://example.com/", "https://docs.example.com/"));
        Assert.True(WebPerformanceObservationNormalizer.TargetBelongsToSite(
            "https://api.docs.example.com/", "https://docs.example.com/"));
    }

    [Fact]
    public void PerformanceSchema_AcceptsPublishedExample()
    {
        var schemaPath = RepositoryPath("Schemas", "powerforge.web.performance-observations.schema.json");
        var examplePath = RepositoryPath("Examples", "PowerForge.Web", "Search", "performance-observations.json");
        var schema = JsonSchema.FromText(File.ReadAllText(schemaPath));
        var document = JsonNode.Parse(File.ReadAllText(examplePath))!;

        Assert.True(schema.Evaluate(document, new EvaluationOptions()).IsValid);
        var batch = JsonSerializer.Deserialize<WebPerformanceObservationBatch>(File.ReadAllText(examplePath), WebCliJson.Options);
        Assert.NotNull(batch);
        Assert.NotNull(WebPerformanceObservationNormalizer.Normalize(batch));
    }

    [Fact]
    public void CruxCli_FailsClosedBeforeStorageWhenCredentialIsUnavailable()
    {
        var root = Path.Combine(Path.GetTempPath(), "powerforge-performance-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var configPath = Path.Combine(root, "providers.json");
            var databasePath = Path.Combine(root, "fleet.db");
            File.WriteAllText(configPath, JsonSerializer.Serialize(CreateConfiguration()));

            var exitCode = WebCliCommandHandlers.HandleSubCommand("performance",
                ["collect-crux", "--config", configPath, "--database", databasePath, "--site", "officeimo", "--provider", "crux", "--output", "json"],
                outputJson: true, logger: new WebConsoleLogger(), outputSchemaVersion: 1);

            Assert.Equal(2, exitCode);
            Assert.False(File.Exists(databasePath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static LighthouseReportImportOptions LighthouseOptions() => new()
    {
        ProviderId = "lighthouse", SiteId = "officeimo", SiteBaseUrl = "https://officeimo.com/"
    };

    private static string LighthouseReport() => """
        {
          "finalUrl": "https://officeimo.com/docs/",
          "fetchTime": "2026-08-10T12:34:56Z",
          "lighthouseVersion": "12.3.0",
          "configSettings": { "formFactor": "mobile" },
          "categories": { "performance": { "score": 0.91 } },
          "audits": {
            "first-contentful-paint": { "numericValue": 900 },
            "largest-contentful-paint": { "numericValue": 1800 },
            "cumulative-layout-shift": { "numericValue": 0.04 },
            "total-blocking-time": { "numericValue": 120 },
            "speed-index": { "numericValue": 1300 }
          }
        }
        """;

    private static CruxCollectionOptions CruxOptions() => new()
    {
        ProviderId = "crux", SiteId = "officeimo", SiteBaseUrl = "https://officeimo.com/",
        TargetKind = "origin", TargetUrl = "https://officeimo.com/", FormFactor = "all"
    };

    private static object CruxResponse()
    {
        var metric = new
        {
            histogram = new[]
            {
                new { start = (double?)0, end = (double?)2500, density = 0.8 },
                new { start = (double?)2500, end = (double?)4000, density = 0.15 },
                new { start = (double?)4000, end = (double?)null, density = 0.05 }
            },
            percentiles = new { p75 = 2300 }
        };
        return new
        {
            record = new
            {
                key = new { origin = "https://officeimo.com/" },
                collectionPeriod = new
                {
                    firstDate = new { year = 2026, month = 7, day = 12 },
                    lastDate = new { year = 2026, month = 8, day = 8 }
                },
                metrics = new Dictionary<string, object>
                {
                    ["largest_contentful_paint"] = metric,
                    ["interaction_to_next_paint"] = metric,
                    ["cumulative_layout_shift"] = metric,
                    ["first_contentful_paint"] = metric,
                    ["experimental_time_to_first_byte"] = metric
                }
            }
        };
    }

    private static WebPerformanceObservationBatch CreateLabBatch() => new()
    {
        Provider = "lighthouse", SiteId = "officeimo", CollectedAtUtc = CollectionTime,
        SourceKind = "fixture", Status = "complete", MeasurementKind = "lab", TargetKind = "url",
        TargetUrl = "https://officeimo.com/", FormFactor = "phone",
        Observations = [new WebPerformanceObservation { Metric = "performance-score", Value = 0.9, Unit = "score" }]
    };

    private static WebPerformanceObservationBatch CreateFieldBatch() => new()
    {
        Provider = "crux", SiteId = "officeimo", CollectedAtUtc = CollectionTime,
        SourceKind = "fixture", Status = "complete", MeasurementKind = "field", TargetKind = "origin",
        TargetUrl = "https://officeimo.com/", FormFactor = "phone", ToolVersion = "crux-api-v1",
        Observations =
        [
            new WebPerformanceObservation
            {
                Metric = "largest-contentful-paint", Value = 2300, Unit = "milliseconds", Percentile = 75,
                PeriodStartDate = new DateOnly(2026, 7, 12), PeriodEndDate = new DateOnly(2026, 8, 8),
                Histogram =
                [
                    new WebPerformanceHistogramBin { Start = 0, End = 2500, Density = 0.8 },
                    new WebPerformanceHistogramBin { Start = 2500, End = 4000, Density = 0.15 },
                    new WebPerformanceHistogramBin { Start = 4000, Density = 0.05 }
                ]
            }
        ]
    };

    private static WebTrafficObservationBatch CreateTrafficBatch() => new()
    {
        Provider = "cloudflare", SiteId = "officeimo", CollectedAtUtc = CollectionTime.AddMinutes(-1),
        SourceKind = "fixture", Status = "complete",
        CollectionCoverage = new WebTrafficObservationCollectionCoverage
        {
            FromDate = new DateOnly(2026, 8, 1), ThroughDate = new DateOnly(2026, 8, 1), CompletedDates = [new DateOnly(2026, 8, 1)]
        },
        Observations =
        [
            new WebTrafficObservation { Date = new DateOnly(2026, 8, 1), Host = "officeimo.com", Path = "/", Requests = 1, Visits = 1, EdgeResponseBytes = 1 }
        ]
    };

    private static WebSearchProviderConfiguration CreateConfiguration() => new()
    {
        Sites =
        [
            new WebSearchSiteProviderConfiguration
            {
                Id = "officeimo", BaseUrl = "https://officeimo.com/",
                Providers =
                [
                    new WebSearchProviderRegistration
                    {
                        Id = "crux", Kind = CruxCollector.ProviderKind, Enabled = true,
                        Capabilities = [WebSearchProviderCapabilities.PerformanceCrux],
                        Credential = new WebSearchCredentialReference
                        {
                            Kind = "google-api-key", EnvironmentVariable = "POWERFORGE_TEST_CRUX_UNAVAILABLE"
                        }
                    }
                ]
            }
        ]
    };

    private static HttpResponseMessage JsonResponse(object value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
    };

    private static string RepositoryPath(params string[] parts)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        return Path.Combine([root, .. parts]);
    }

    private sealed class FakeApiKeyProvider : ICruxApiKeyProvider
    {
        public ValueTask<string> GetApiKeyAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult("test-api-key");
    }

    private sealed class MissingApiKeyProvider : ICruxApiKeyProvider
    {
        public ValueTask<string> GetApiKeyAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("API key unavailable.");
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        internal List<RequestSnapshot> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            request.Headers.TryGetValues("X-Goog-Api-Key", out var apiKeys);
            Requests.Add(new RequestSnapshot(request.RequestUri!, body, apiKeys?.SingleOrDefault()));
            return responder(request);
        }
    }

    private sealed record RequestSnapshot(Uri Uri, string Body, string? ApiKey);
}
