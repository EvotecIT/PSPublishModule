using System.Net;
using System.Text;
using System.Text.Json;
using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed partial class WebCloudflareAnalyticsCollectorTests
{
    private static CloudflareAnalyticsCollector CreateCollector(HttpClient client) =>
        new(client, new FakeTokenProvider(), timeProvider: new FixedTimeProvider(CompletionTime));

    private static CloudflareAnalyticsCollectionOptions CreateOptions() => new()
    {
        ProviderId = "cloudflare",
        SiteId = "officeimo",
        ZoneId = ZoneId,
        SiteBaseUrl = "https://officeimo.com/",
        FromDate = new DateOnly(2026, 8, 1),
        ThroughDate = new DateOnly(2026, 8, 1),
        ConfigurationHash = "sha256:configuration"
    };

    private static int RunTrafficList(string databasePath, string throughDate = "2026-08-01") => WebCliCommandHandlers.HandleSubCommand(
        "traffic",
        [
            "list", "--database", databasePath, "--site", "officeimo", "--provider", "cloudflare",
            "--from", "2026-08-01", "--to", throughDate, "--output", "json"
        ],
        outputJson: true,
        logger: new WebConsoleLogger(),
        outputSchemaVersion: 1);

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

    private static HttpResponseMessage CapabilityResponse(
        int maxPageSize = 1000,
        int? maxDuration = 86_400,
        int? notOlderThan = 2_678_400,
        bool firewallEnabled = true,
        int? firewallMaxPageSize = null,
        int? firewallMaxDuration = null,
        int? firewallNotOlderThan = null) => JsonResponse(new
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
                            httpRequestsAdaptiveGroups = new { enabled = true, maxPageSize, maxDuration, notOlderThan },
                            firewallEventsAdaptiveGroups = new
                            {
                                enabled = firewallEnabled,
                                maxPageSize = firewallMaxPageSize ?? maxPageSize,
                                maxDuration = firewallMaxDuration ?? maxDuration,
                                notOlderThan = firewallNotOlderThan ?? notOlderThan
                            }
                        }
                    }
                }
            }
        }
    });

    private static HttpResponseMessage CapabilityNullZoneResponse() => JsonResponse(new
    {
        errors = (object?)null,
        data = new { viewer = new { zones = new object?[] { null } } }
    });

    private static HttpResponseMessage ZoneResponse(string name, string? accountId = TestAccountId) => JsonResponse(new
    {
        success = true,
        errors = Array.Empty<object>(),
        result = new { id = ZoneId, name, account = accountId is null ? null : new { id = accountId } }
    });

    private static object TrafficRow(
        DateOnly date,
        string host,
        string path,
        ulong requests,
        ulong visits,
        ulong bytes,
        double sampleInterval,
        string scheme = "https") => new
    {
        count = requests,
        avg = new { sampleInterval },
        sum = new { visits, edgeResponseBytes = bytes },
        dimensions = new
        {
            date = date.ToString("yyyy-MM-dd"),
            clientRequestHTTPHost = host,
            clientRequestPath = path,
            clientRequestScheme = scheme
        }
    };

    private static HttpResponseMessage TrafficResponse(params object[] rows) => JsonResponse(new
    {
        errors = (object?)null,
        data = new { viewer = new { zones = new[] { new { traffic = rows } } } }
    });

    private static HttpResponseMessage TrafficNullResponse() => JsonResponse(new
    {
        errors = (object?)null,
        data = new { viewer = new { zones = new[] { new { traffic = (object?)null } } } }
    });

    private static HttpResponseMessage TrafficNullZoneResponse() => JsonResponse(new
    {
        errors = (object?)null,
        data = new { viewer = new { zones = new object?[] { null } } }
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
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter,
                body));
            return responder(request, Requests.Count - 1);
        }
    }

    private sealed record RequestSnapshot(
        HttpMethod Method,
        Uri Uri,
        string? AuthorizationScheme,
        string? AuthorizationParameter,
        string Body);
}
