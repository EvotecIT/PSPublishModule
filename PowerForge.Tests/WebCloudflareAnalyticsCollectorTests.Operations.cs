using System.Net;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed partial class WebCloudflareAnalyticsCollectorTests
{
    [Fact]
    public async Task CollectOperations_MapsHourlyCacheErrorsFirewallAndRumState()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(),
            2 => OperationalHttpResponse(
                HttpOperationRow("2026-08-10T09:00:00Z", "hit", 200, 10, 1000, 2),
                HttpOperationRow("2026-08-10T09:00:00Z", "miss", 404, 3, 300, 1),
                HttpOperationRow("2026-08-10T10:00:00Z", "dynamic", 503, 2, 200, 1)),
            3 => OperationalFirewallResponse(
                FirewallOperationRow("2026-08-10T09:00:00Z", "block", 4, 2),
                FirewallOperationRow("2026-08-10T09:00:00Z", "log", 1, 1)),
            4 => RumSitesResponse(enabled: true, autoInstall: true),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);

        var result = await CreateCollector(client).CollectOperationsAsync(CreateOperationalOptions(includeAccount: true));

        Assert.True(result.Success);
        Assert.True(result.Http.Success);
        Assert.True(result.Firewall.Success);
        Assert.Equal(5, result.RequestCount);
        Assert.True(result.Rum.Requested);
        Assert.True(result.Rum.Configured);
        Assert.True(result.Rum.Enabled);
        Assert.True(result.Rum.AutoInstall);
        Assert.Equal(2, result.Hours.Length);
        var first = result.Hours[0];
        Assert.Equal(23, first.Requests);
        Assert.Equal(20, first.CachedRequests);
        Assert.Equal(3, first.ClientErrors);
        Assert.Equal(9, first.FirewallEvents);
        Assert.Equal(8, first.FirewallMitigated);
        Assert.Equal(2, first.MaximumSampleInterval);
        Assert.Equal(20d * 100d / 23d, first.CacheHitPercent, precision: 6);
        Assert.Contains("clientRequestHTTPHost", handler.Requests[2].Body, StringComparison.Ordinal);
        Assert.Contains("firewallEventsAdaptiveGroups", handler.Requests[3].Body, StringComparison.Ordinal);
        Assert.Equal(HttpMethod.Get, handler.Requests[4].Method);
        Assert.Contains("/rum/site_info/list", handler.Requests[4].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CollectOperations_PreservesHttpEvidenceWhenFirewallDatasetIsUnavailable()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(),
            2 => OperationalHttpResponse(HttpOperationRow("2026-08-10T09:00:00Z", "hit", 200, 10, 1000, 1)),
            3 => new HttpResponseMessage(HttpStatusCode.Forbidden),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);

        var result = await CreateCollector(client).CollectOperationsAsync(CreateOperationalOptions(includeAccount: false));

        Assert.True(result.Success);
        Assert.True(result.Http.Success);
        Assert.False(result.Firewall.Success);
        Assert.Equal("authentication-rejected", result.Firewall.ErrorCode);
        Assert.Single(result.Hours);
        Assert.False(result.Rum.Requested);
    }

    [Fact]
    public async Task CollectOperations_PartitionsByProbeDurationAndUsesPageLimit()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(maxPageSize: 7, maxDuration: 3600),
            2 or 3 => OperationalHttpResponse(),
            4 or 5 => OperationalFirewallResponse(),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);
        var options = CreateOperationalOptions(includeAccount: false);
        options.SiteBaseUrl = "https://officeimo.com/products/powerforge/";

        var result = await CreateCollector(client).CollectOperationsAsync(options);

        Assert.True(result.Success);
        Assert.Equal(6, result.RequestCount);
        Assert.Contains("\"limit\":7", handler.Requests[2].Body, StringComparison.Ordinal);
        Assert.Contains("2026-08-10T09:00:00Z", handler.Requests[2].Body, StringComparison.Ordinal);
        Assert.Contains("2026-08-10T10:00:00Z", handler.Requests[3].Body, StringComparison.Ordinal);
        Assert.Contains("clientRequestPath", handler.Requests[4].Body, StringComparison.Ordinal);
        Assert.Contains("/products/powerforge", handler.Requests[4].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CollectOperations_FailsClosedWhenProviderPageLimitIsReached()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(maxPageSize: 1),
            2 => OperationalHttpResponse(HttpOperationRow("2026-08-10T09:00:00Z", "hit", 200, 10, 1000, 1)),
            3 => OperationalFirewallResponse(),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);

        var result = await CreateCollector(client).CollectOperationsAsync(CreateOperationalOptions(includeAccount: false));

        Assert.False(result.Success);
        Assert.Equal("row-limit-reached", result.Http.ErrorCode);
        Assert.Empty(result.Hours);
        Assert.Contains("\"limit\":1", handler.Requests[2].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CollectOperations_TraversesRumPagesUntilZoneIsFound()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(),
            2 => OperationalHttpResponse(),
            3 => OperationalFirewallResponse(),
            4 => RumSitesPageResponse(includeMatch: false, totalPages: 2, itemCount: 50),
            5 => RumSitesPageResponse(includeMatch: true, totalPages: 2, itemCount: 1),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);

        var result = await CreateCollector(client).CollectOperationsAsync(CreateOperationalOptions(includeAccount: true));

        Assert.True(result.Rum.Configured);
        Assert.Equal(6, result.RequestCount);
        Assert.Contains("page=1", handler.Requests[4].Uri.Query, StringComparison.Ordinal);
        Assert.Contains("page=2", handler.Requests[5].Uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CollectOperations_DiscardsPartialFirewallRowsWhenDatasetIsInvalid()
    {
        var invalidRow = new
        {
            count = 1UL,
            avg = new { sampleInterval = 1d },
            dimensions = new { datetimeHour = "2026-08-10T09:00:00Z", action = (string?)null }
        };
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(),
            2 => OperationalHttpResponse(HttpOperationRow("2026-08-10T09:00:00Z", "hit", 200, 10, 1000, 1)),
            3 => OperationalFirewallResponse(FirewallOperationRow("2026-08-10T09:00:00Z", "block", 4, 1), invalidRow),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);

        var result = await CreateCollector(client).CollectOperationsAsync(CreateOperationalOptions(includeAccount: false));

        Assert.True(result.Success);
        Assert.False(result.Firewall.Success);
        Assert.Single(result.Hours);
        Assert.Equal(0, result.Hours[0].FirewallEvents);
        Assert.Equal(0, result.Hours[0].FirewallMitigated);
    }

    [Fact]
    public async Task CollectOperations_RejectsRowsMissingRequestedCategoryDimensions()
    {
        var missingCacheStatus = new
        {
            count = 1UL,
            avg = new { sampleInterval = 1d },
            sum = new { edgeResponseBytes = 100UL },
            dimensions = new { datetimeHour = "2026-08-10T09:00:00Z", cacheStatus = (string?)null, edgeResponseStatus = 200 }
        };
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(),
            2 => OperationalHttpResponse(missingCacheStatus),
            3 => OperationalFirewallResponse(),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);

        var result = await CreateCollector(client).CollectOperationsAsync(CreateOperationalOptions(includeAccount: false));

        Assert.False(result.Success);
        Assert.Equal("invalid-response", result.Http.ErrorCode);
        Assert.Empty(result.Hours);
    }

    [Fact]
    public async Task CollectOperations_MarksRumNotAttemptedWhenProbeFails()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => new HttpResponseMessage(HttpStatusCode.Forbidden),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);

        var result = await CreateCollector(client).CollectOperationsAsync(CreateOperationalOptions(includeAccount: true));

        Assert.False(result.Success);
        Assert.True(result.Rum.Requested);
        Assert.Equal("not-attempted", result.Rum.ErrorCode);
    }

    private static CloudflareOperationalCollectionOptions CreateOperationalOptions(bool includeAccount) => new()
    {
        SiteId = "officeimo",
        ZoneId = ZoneId,
        SiteBaseUrl = "https://officeimo.com/",
        FromUtc = new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.Zero),
        ThroughUtc = new DateTimeOffset(2026, 8, 10, 11, 0, 0, TimeSpan.Zero),
        AccountId = includeAccount ? "0123456789abcdef0123456789abcdef" : null
    };

    private static object HttpOperationRow(string hour, string cacheStatus, int status, ulong count, ulong bytes, double sampleInterval) => new
    {
        count,
        avg = new { sampleInterval },
        sum = new { edgeResponseBytes = bytes },
        dimensions = new { datetimeHour = hour, cacheStatus, edgeResponseStatus = status }
    };

    private static object FirewallOperationRow(string hour, string action, ulong count, double sampleInterval) => new
    {
        count,
        avg = new { sampleInterval },
        dimensions = new { datetimeHour = hour, action }
    };

    private static HttpResponseMessage OperationalHttpResponse(params object[] rows) => JsonResponse(new
    {
        errors = (object?)null,
        data = new { viewer = new { zones = new[] { new { http = rows } } } }
    });

    private static HttpResponseMessage OperationalFirewallResponse(params object[] rows) => JsonResponse(new
    {
        errors = (object?)null,
        data = new { viewer = new { zones = new[] { new { firewall = rows } } } }
    });

    private static HttpResponseMessage RumSitesResponse(bool enabled, bool autoInstall) => JsonResponse(new
    {
        success = true,
        errors = Array.Empty<object>(),
        result = new[] { new { auto_install = autoInstall, ruleset = new { enabled, zone_tag = ZoneId } } }
    });

    private static HttpResponseMessage RumSitesPageResponse(bool includeMatch, int totalPages, int itemCount)
    {
        var rows = Enumerable.Range(0, itemCount)
            .Select(index => new
            {
                auto_install = includeMatch && index == 0,
                ruleset = new
                {
                    enabled = includeMatch && index == 0,
                    zone_tag = includeMatch && index == 0 ? ZoneId : index.ToString("x32")
                }
            })
            .ToArray();
        return JsonResponse(new
        {
            success = true,
            errors = Array.Empty<object>(),
            result = rows,
            result_info = new { total_pages = totalPages }
        });
    }
}
