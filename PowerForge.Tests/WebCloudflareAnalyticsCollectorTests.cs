using System.Net;
using System.Text;
using System.Text.Json;
using DBAClientX;
using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class WebCloudflareAnalyticsCollectorTests
{
    private static readonly DateTimeOffset CompletionTime = new(2026, 8, 10, 12, 34, 56, TimeSpan.Zero);
    private const string ZoneId = "abcdef0123456789abcdef0123456789";

    [Fact]
    public async Task Probe_DiscoversPlanSpecificDatasetLimitsWithoutExposingTheToken()
    {
        var handler = new ScriptedHandler((_, _) => CapabilityResponse(maxPageSize: 25_000));
        using var client = new HttpClient(handler);
        var collector = new CloudflareAnalyticsCollector(client, new FakeTokenProvider());

        var result = await collector.ProbeAsync(ZoneId);

        Assert.True(result.Success);
        Assert.True(result.DatasetEnabled);
        Assert.Equal(10_000, result.MaxPageSize);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer", request.AuthorizationScheme);
        Assert.Equal("test-token", request.AuthorizationParameter);
        Assert.Contains("httpRequestsAdaptiveGroups", request.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("test-token", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Collect_MapsDailyHostPathTrafficAndSamplingEvidence()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => CapabilityResponse(),
            1 => TrafficResponse(TrafficRow(new DateOnly(2026, 8, 1), "BÜCHER.de.", "/docs/", 100, 25, 5000, 2)),
            2 => TrafficResponse(TrafficRow(new DateOnly(2026, 8, 2), "www.example.com", "/", 50, 10, 2000, 1)),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);
        var collector = new CloudflareAnalyticsCollector(client, new FakeTokenProvider(), timeProvider: new FixedTimeProvider(CompletionTime));
        var options = CreateOptions();
        options.ThroughDate = new DateOnly(2026, 8, 2);

        var result = await collector.CollectAsync(options);
        var normalized = WebTrafficObservationNormalizer.Normalize(result.Batch);

        Assert.True(result.Success);
        Assert.Equal(3, result.RequestCount);
        Assert.Equal(2, result.CompletedDateCount);
        Assert.Equal(CompletionTime, normalized.CollectedAtUtc);
        Assert.Equal([new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 2)], normalized.CollectionCoverage.CompletedDates);
        var sampled = Assert.Single(normalized.Observations, value => value.Date == new DateOnly(2026, 8, 1));
        Assert.Equal("xn--bcher-kva.de", sampled.Host);
        Assert.Equal(2d, sampled.SampleInterval);
        Assert.All(handler.Requests.Skip(1), request => Assert.Contains("\"requestSource\":\"eyeball\"", request.Body, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Collect_ConfirmsZeroOnlyAfterEveryDailyPartitionReturnsNoRows()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => CapabilityResponse(),
            1 or 2 => TrafficResponse(),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);
        var collector = new CloudflareAnalyticsCollector(client, new FakeTokenProvider());
        var options = CreateOptions();
        options.ThroughDate = new DateOnly(2026, 8, 2);

        var result = await collector.CollectAsync(options);

        Assert.True(result.Success);
        Assert.True(result.Batch.ZeroDataConfirmed);
        Assert.Empty(result.Batch.Observations);
        Assert.Equal(2, result.Batch.CollectionCoverage.CompletedDates.Length);
    }

    [Fact]
    public async Task Collect_PreservesCompletedDatesWhenALaterPartitionFails()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => CapabilityResponse(),
            1 => TrafficResponse(TrafficRow(new DateOnly(2026, 8, 1), "officeimo.com", "/", 10, 2, 1000, 1)),
            2 => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);
        var collector = new CloudflareAnalyticsCollector(client, new FakeTokenProvider());
        var options = CreateOptions();
        options.ThroughDate = new DateOnly(2026, 8, 2);

        var result = await collector.CollectAsync(options);

        Assert.False(result.Success);
        Assert.Equal("provider-unavailable", result.ErrorCode);
        Assert.Equal("partial", result.Batch.Status);
        Assert.Single(result.Batch.Observations);
        Assert.Equal([new DateOnly(2026, 8, 1)], result.Batch.CollectionCoverage.CompletedDates);
        Assert.Equal(new DateOnly(2026, 8, 2), result.Batch.CollectionCoverage.FailedDate);
    }

    [Fact]
    public async Task Collect_ReachingThePlanRowLimitProducesAnHonestPartialPartition()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => CapabilityResponse(maxPageSize: 2),
            1 => TrafficResponse(
                TrafficRow(new DateOnly(2026, 8, 1), "officeimo.com", "/a", 10, 2, 1000, 1),
                TrafficRow(new DateOnly(2026, 8, 1), "officeimo.com", "/b", 5, 1, 500, 1)),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);
        var collector = new CloudflareAnalyticsCollector(client, new FakeTokenProvider());

        var result = await collector.CollectAsync(CreateOptions());

        Assert.False(result.Success);
        Assert.Equal("row-limit-reached", result.ErrorCode);
        Assert.Empty(result.Batch.CollectionCoverage.CompletedDates);
        Assert.Equal(new DateOnly(2026, 8, 1), result.Batch.CollectionCoverage.FailedDate);
        Assert.Equal(2, result.Batch.Observations.Length);
    }

    [Fact]
    public async Task Collect_RejectsMissingMetricsRatherThanDeserializingThemAsZero()
    {
        var invalid = new
        {
            dimensions = new { date = "2026-08-01", clientRequestHTTPHost = "officeimo.com", clientRequestPath = "/" },
            avg = new { sampleInterval = 1d },
            sum = new { visits = 1UL, edgeResponseBytes = 10UL }
        };
        var handler = new ScriptedHandler((_, index) => index == 0 ? CapabilityResponse() : TrafficResponse(invalid));
        using var client = new HttpClient(handler);
        var collector = new CloudflareAnalyticsCollector(client, new FakeTokenProvider());

        var result = await collector.CollectAsync(CreateOptions());

        Assert.False(result.Success);
        Assert.Equal("invalid-response", result.ErrorCode);
        Assert.False(result.Batch.ZeroDataConfirmed);
    }

    [Fact]
    public async Task TrafficStorage_MigratesSchemaThreeAndKeepsTrafficSeparateFromSearch()
    {
        var root = Path.Combine(Path.GetTempPath(), "powerforge-cloudflare-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var databasePath = Path.Combine(root, "fleet.db");
            var searchStore = new SqliteWebSearchObservationStore(databasePath);
            await searchStore.ImportAsync(WebSearchObservationNormalizer.Normalize(CreateSearchBatch()));
            await using (var sqlite = new SQLite())
            {
                await sqlite.ExecuteNonQueryAsync(
                    databasePath,
                    """
                    DROP TABLE traffic_observations;
                    DROP TABLE traffic_observation_runs;
                    PRAGMA user_version = 3;
                    """);
            }
            var traffic = WebTrafficObservationNormalizer.Normalize(CreateTrafficBatch());

            var first = await searchStore.ImportTrafficAsync(traffic);
            var second = await searchStore.ImportTrafficAsync(traffic);
            var stored = await searchStore.QueryTrafficAsync(new WebTrafficObservationQuery { SiteId = "officeimo" });

            Assert.Equal(4, first.DatabaseSchemaVersion);
            Assert.Equal(1, first.InsertedCount);
            Assert.Equal(1, second.DuplicateCount);
            Assert.Single(stored);
            Assert.Equal(100, stored[0].Requests);
            Assert.Single(await searchStore.QueryAsync(new WebSearchObservationQuery { SiteId = "officeimo" }));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task EnvironmentTokenProvider_ResolvesOnlyTheReferencedSecret()
    {
        var provider = CloudflareEnvironmentApiTokenProvider.Create(
            new WebSearchCredentialReference { Kind = "cloudflare-api-token", EnvironmentVariable = "POWERFORGE_TEST_CLOUDFLARE_TOKEN" },
            name => name == "POWERFORGE_TEST_CLOUDFLARE_TOKEN" ? " token-value " : null);

        Assert.Equal("token-value", await provider.GetTokenAsync());
    }

    [Fact]
    public void Cli_TrafficCollect_FailsClosedBeforeStorageWhenCredentialIsUnavailable()
    {
        var root = Path.Combine(Path.GetTempPath(), "powerforge-cloudflare-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var configPath = Path.Combine(root, "providers.json");
            var databasePath = Path.Combine(root, "fleet.db");
            File.WriteAllText(configPath, JsonSerializer.Serialize(CreateConfiguration()));

            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "traffic",
                [
                    "collect", "--config", configPath, "--database", databasePath,
                    "--site", "officeimo", "--provider", "cloudflare",
                    "--from", "2026-08-01", "--to", "2026-08-01", "--output", "json"
                ],
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: 1);

            Assert.Equal(2, exitCode);
            Assert.False(File.Exists(databasePath));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static CloudflareAnalyticsCollectionOptions CreateOptions() => new()
    {
        ProviderId = "cloudflare",
        SiteId = "officeimo",
        ZoneId = ZoneId,
        FromDate = new DateOnly(2026, 8, 1),
        ThroughDate = new DateOnly(2026, 8, 1),
        ConfigurationHash = "sha256:configuration"
    };

    private static WebTrafficObservationBatch CreateTrafficBatch() => new()
    {
        Provider = "cloudflare",
        SiteId = "officeimo",
        CollectedAtUtc = CompletionTime,
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
                Date = new DateOnly(2026, 8, 1), Host = "officeimo.com", Path = "/",
                Requests = 100, Visits = 25, EdgeResponseBytes = 5000, SampleInterval = 1
            }
        ]
    };

    private static WebSearchObservationBatch CreateSearchBatch() => new()
    {
        Provider = "google-search-console",
        SiteId = "officeimo",
        CollectedAtUtc = CompletionTime.AddMinutes(-1),
        SourceKind = "fixture",
        Status = "complete",
        CollectionCoverage = new WebSearchObservationCollectionCoverage
        {
            Mode = "daily",
            FromDate = new DateOnly(2026, 8, 1),
            ThroughDate = new DateOnly(2026, 8, 1),
            CompletedDates = [new DateOnly(2026, 8, 1)]
        },
        Observations =
        [
            new WebSearchObservation { Date = new DateOnly(2026, 8, 1), Page = "https://officeimo.com/", Clicks = 1, Impressions = 10 }
        ]
    };

    private static WebSearchProviderConfiguration CreateConfiguration() => new()
    {
        Sites =
        [
            new WebSearchSiteProviderConfiguration
            {
                Id = "officeimo",
                BaseUrl = "https://officeimo.com/",
                Providers =
                [
                    new WebSearchProviderRegistration
                    {
                        Id = "cloudflare",
                        Kind = CloudflareAnalyticsCollector.ProviderKind,
                        Enabled = true,
                        Capabilities = [WebSearchProviderCapabilities.TrafficAnalytics],
                        Credential = new WebSearchCredentialReference
                        {
                            Kind = "cloudflare-api-token",
                            EnvironmentVariable = "POWERFORGE_TEST_CLOUDFLARE_TOKEN_UNAVAILABLE"
                        },
                        Settings = new Dictionary<string, string?> { ["zoneId"] = ZoneId }
                    }
                ]
            }
        ]
    };

    private static HttpResponseMessage CapabilityResponse(int maxPageSize = 1000) => JsonResponse(new
    {
        errors = (object?)null,
        data = new
        {
            viewer = new
            {
                zones = new[]
                {
                    new
                    {
                        settings = new
                        {
                            httpRequestsAdaptiveGroups = new { enabled = true, maxPageSize, maxDuration = 86400, notOlderThan = 2678400 }
                        }
                    }
                }
            }
        }
    });

    private static object TrafficRow(DateOnly date, string host, string path, ulong requests, ulong visits, ulong bytes, double sampleInterval) => new
    {
        count = requests,
        avg = new { sampleInterval },
        sum = new { visits, edgeResponseBytes = bytes },
        dimensions = new { date = date.ToString("yyyy-MM-dd"), clientRequestHTTPHost = host, clientRequestPath = path }
    };

    private static HttpResponseMessage TrafficResponse(params object[] rows) => JsonResponse(new
    {
        errors = (object?)null,
        data = new { viewer = new { zones = new[] { new { traffic = rows } } } }
    });

    private static HttpResponseMessage JsonResponse(object value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
    };

    private sealed class FakeTokenProvider : ICloudflareAnalyticsTokenProvider
    {
        public Task<string> GetTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult("test-token");
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class ScriptedHandler(Func<HttpRequestMessage, int, HttpResponseMessage> responder) : HttpMessageHandler
    {
        internal List<RequestSnapshot> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RequestSnapshot(
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter,
                body));
            return responder(request, Requests.Count - 1);
        }
    }

    private sealed record RequestSnapshot(string? AuthorizationScheme, string? AuthorizationParameter, string Body);
}
