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
}
