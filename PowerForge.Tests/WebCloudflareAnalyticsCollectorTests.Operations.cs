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
            2 => FirewallCapabilityResponse(),
            3 => OperationalHttpResponse(
                HttpOperationRow("2026-08-10T09:00:00Z", "hit", 200, 10, 1000, 2),
                HttpOperationRow("2026-08-10T09:00:00Z", "stale", 200, 2, 200, 1),
                HttpOperationRow("2026-08-10T09:00:00Z", "miss", 404, 3, 300, 1),
                HttpOperationRow("2026-08-10T10:00:00Z", "dynamic", 503, 2, 200, 1)),
            4 => OperationalFirewallResponse(
                FirewallOperationRow("2026-08-10T09:00:00Z", "block", 4, 2),
                FirewallOperationRow("2026-08-10T09:00:00Z", "managedChallenge", 3, 1),
                FirewallOperationRow("2026-08-10T09:00:00Z", "log", 1, 1)),
            5 => RumSitesResponse(enabled: true, autoInstall: true),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);

        var result = await CreateCollector(client).CollectOperationsAsync(CreateOperationalOptions(includeAccount: true));

        Assert.True(result.Success);
        Assert.Equal("officeimo", result.SiteId);
        Assert.True(result.Http.Success);
        Assert.True(result.Firewall.Success);
        Assert.Equal(6, result.RequestCount);
        Assert.Equal(new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.Zero), result.FromUtc);
        Assert.Equal(new DateTimeOffset(2026, 8, 10, 11, 0, 0, TimeSpan.Zero), result.ThroughUtc);
        Assert.Equal(CompletionTime, result.CollectedAtUtc);
        Assert.True(result.Rum.Requested);
        Assert.True(result.Rum.Configured);
        Assert.True(result.Rum.Enabled);
        Assert.True(result.Rum.AutoInstall);
        Assert.Equal(2, result.Hours.Length);
        var first = result.Hours[0];
        Assert.Equal(25, first.Requests);
        Assert.Equal(22, first.CachedRequests);
        Assert.Equal(3, first.ClientErrors);
        Assert.Equal(12, first.FirewallEvents);
        Assert.Equal(11, first.FirewallMitigated);
        Assert.Equal(2, first.MaximumSampleInterval);
        Assert.Equal(22d * 100d / 25d, first.CacheHitPercent, precision: 6);
        Assert.DoesNotContain("firewallEventsAdaptiveGroups", handler.Requests[1].Body, StringComparison.Ordinal);
        Assert.Contains("firewallEventsAdaptiveGroups", handler.Requests[2].Body, StringComparison.Ordinal);
        Assert.Contains("clientRequestHTTPHost", handler.Requests[3].Body, StringComparison.Ordinal);
        Assert.Contains("firewallEventsAdaptiveGroups", handler.Requests[4].Body, StringComparison.Ordinal);
        Assert.Contains("clientRequestScheme", handler.Requests[4].Body, StringComparison.Ordinal);
        Assert.Equal(HttpMethod.Get, handler.Requests[5].Method);
        Assert.Contains("/rum/site_info/list", handler.Requests[5].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CollectOperations_PreservesHttpEvidenceWhenFirewallDatasetIsUnavailable()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(),
            2 => new HttpResponseMessage(HttpStatusCode.Forbidden),
            3 => OperationalHttpResponse(HttpOperationRow("2026-08-10T09:00:00Z", "hit", 200, 10, 1000, 1)),
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
            2 => FirewallCapabilityResponse(maxDuration: 3600),
            3 or 4 => OperationalHttpResponse(),
            5 or 6 => OperationalFirewallResponse(),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);
        var options = CreateOperationalOptions(includeAccount: false);
        options.SiteBaseUrl = "https://officeimo.com/products/powerforge/";

        var result = await CreateCollector(client).CollectOperationsAsync(options);

        Assert.True(result.Success);
        Assert.Equal(7, result.RequestCount);
        Assert.Contains("\"limit\":7", handler.Requests[3].Body, StringComparison.Ordinal);
        Assert.Contains("2026-08-10T09:00:00Z", handler.Requests[3].Body, StringComparison.Ordinal);
        Assert.Contains("2026-08-10T10:00:00Z", handler.Requests[4].Body, StringComparison.Ordinal);
        Assert.Contains("clientRequestPath", handler.Requests[5].Body, StringComparison.Ordinal);
        Assert.Contains("/products/powerforge", handler.Requests[5].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CollectOperations_AlignsNonIntegralProviderDurationsToCompleteUtcHours()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(maxDuration: 5_400),
            2 => FirewallCapabilityResponse(maxDuration: 5_400),
            3 => OperationalHttpResponse(HttpOperationRow("2026-08-10T09:00:00Z", "hit", 200, 1, 100, 1)),
            4 => OperationalHttpResponse(HttpOperationRow("2026-08-10T10:00:00Z", "hit", 200, 1, 100, 1)),
            5 => OperationalFirewallResponse(FirewallOperationRow("2026-08-10T09:00:00Z", "block", 1, 1)),
            6 => OperationalFirewallResponse(FirewallOperationRow("2026-08-10T10:00:00Z", "block", 1, 1)),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);

        var result = await CreateCollector(client).CollectOperationsAsync(CreateOperationalOptions(includeAccount: false));

        Assert.True(result.Success);
        Assert.True(result.Firewall.Success);
        Assert.Equal(2, result.Hours.Length);
        Assert.Contains("2026-08-10T10:00:00Z", handler.Requests[4].Body, StringComparison.Ordinal);
        Assert.Contains("2026-08-10T10:00:00Z", handler.Requests[6].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CollectOperations_FailsClosedWhenProviderDurationCannotCoverAnHour()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(maxDuration: 1_800),
            2 => FirewallCapabilityResponse(maxDuration: 1_800),
            _ => throw new InvalidOperationException("Operational queries must not run for sub-hour capability limits.")
        });
        using var client = new HttpClient(handler);

        var result = await CreateCollector(client).CollectOperationsAsync(CreateOperationalOptions(includeAccount: false));

        Assert.False(result.Success);
        Assert.Equal("duration-boundary", result.Http.ErrorCode);
        Assert.Equal("duration-boundary", result.Firewall.ErrorCode);
        Assert.Equal(3, result.RequestCount);
    }

    [Fact]
    public async Task CollectOperations_RejectsRangesOutsideProviderRetention()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(notOlderThan: 3600),
            2 => FirewallCapabilityResponse(notOlderThan: 3600),
            _ => throw new InvalidOperationException("Operational queries must not run outside provider retention.")
        });
        using var client = new HttpClient(handler);

        var result = await CreateCollector(client).CollectOperationsAsync(CreateOperationalOptions(includeAccount: true));

        Assert.False(result.Success);
        Assert.Equal("retention-boundary", result.Http.ErrorCode);
        Assert.Equal("retention-boundary", result.Firewall.ErrorCode);
        Assert.Equal("not-attempted", result.Rum.ErrorCode);
        Assert.Equal(3, result.RequestCount);
    }

    [Fact]
    public async Task CollectOperations_RequiresAnHourAlignedUtcWindow()
    {
        using var client = new HttpClient(new ScriptedHandler((_, _) => throw new InvalidOperationException("No request expected.")));
        var options = CreateOperationalOptions(includeAccount: false);
        options.FromUtc = options.FromUtc.AddMinutes(15);

        await Assert.ThrowsAsync<ArgumentException>(() => CreateCollector(client).CollectOperationsAsync(options));
    }

    [Fact]
    public async Task CollectOperations_UsesBoundedFallbackWhenProbeOmitsMaximumDuration()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(maxDuration: null),
            2 => FirewallCapabilityResponse(maxDuration: null),
            3 => OperationalHttpResponse(),
            4 => OperationalFirewallResponse(),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);

        var result = await CreateCollector(client).CollectOperationsAsync(CreateOperationalOptions(includeAccount: false));

        Assert.True(result.Success);
        Assert.Equal(5, result.RequestCount);
    }

    [Fact]
    public async Task CollectOperations_FailsClosedWhenProviderPageLimitIsReached()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(maxPageSize: 1),
            2 => FirewallCapabilityResponse(),
            3 => OperationalHttpResponse(HttpOperationRow("2026-08-10T09:00:00Z", "hit", 200, 10, 1000, 1)),
            4 => OperationalFirewallResponse(),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);

        var result = await CreateCollector(client).CollectOperationsAsync(CreateOperationalOptions(includeAccount: false));

        Assert.False(result.Success);
        Assert.Equal("row-limit-reached", result.Http.ErrorCode);
        Assert.Empty(result.Hours);
        Assert.Contains("\"limit\":1", handler.Requests[3].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CollectOperations_TraversesRumPagesUntilZoneIsFound()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(),
            2 => FirewallCapabilityResponse(),
            3 => OperationalHttpResponse(),
            4 => OperationalFirewallResponse(),
            5 => RumSitesPageResponse(includeMatch: false, totalPages: 2, itemCount: 50),
            6 => RumSitesPageResponse(includeMatch: true, totalPages: 2, itemCount: 1),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);

        var result = await CreateCollector(client).CollectOperationsAsync(CreateOperationalOptions(includeAccount: true));

        Assert.True(result.Rum.Configured);
        Assert.Equal(7, result.RequestCount);
        Assert.Contains("page=1", handler.Requests[5].Uri.Query, StringComparison.Ordinal);
        Assert.Contains("page=2", handler.Requests[6].Uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CollectOperations_MatchesRumSiteByZoneAndHost()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(),
            2 => FirewallCapabilityResponse(),
            3 => OperationalHttpResponse(),
            4 => OperationalFirewallResponse(),
            5 => RumSitesForHostsResponse(),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);

        var result = await CreateCollector(client).CollectOperationsAsync(CreateOperationalOptions(includeAccount: true));

        Assert.True(result.Rum.Configured);
        Assert.True(result.Rum.Enabled);
        Assert.True(result.Rum.AutoInstall);
    }

    [Fact]
    public async Task CollectOperations_CountsFailedRumTransportAttempt()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(),
            2 => FirewallCapabilityResponse(),
            3 => OperationalHttpResponse(),
            4 => OperationalFirewallResponse(),
            5 => throw new HttpRequestException("Synthetic RUM transport failure."),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);

        var result = await CreateCollector(client).CollectOperationsAsync(CreateOperationalOptions(includeAccount: true));

        Assert.Equal("request-failed", result.Rum.ErrorCode);
        Assert.Equal(6, result.RequestCount);
    }

    [Fact]
    public async Task CollectOperations_UsesFirewallSpecificCapabilityLimits()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(maxPageSize: 1000),
            2 => FirewallCapabilityResponse(maxPageSize: 25),
            3 => OperationalHttpResponse(),
            4 => OperationalFirewallResponse(),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);

        var result = await CreateCollector(client).CollectOperationsAsync(CreateOperationalOptions(includeAccount: false));

        Assert.True(result.Firewall.Success);
        Assert.Contains("\"limit\":25", handler.Requests[4].Body, StringComparison.Ordinal);
        Assert.Contains("\"limit\":1000", handler.Requests[3].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CollectOperations_RejectsIncompleteRumConfiguration()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(),
            2 => FirewallCapabilityResponse(),
            3 => OperationalHttpResponse(),
            4 => OperationalFirewallResponse(),
            5 => JsonResponse(new
            {
                success = true,
                errors = Array.Empty<object>(),
                result = new[] { new { host = "officeimo.com", auto_install = (bool?)null, ruleset = new { enabled = (bool?)null, zone_tag = ZoneId } } }
            }),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);

        var result = await CreateCollector(client).CollectOperationsAsync(CreateOperationalOptions(includeAccount: true));

        Assert.Equal("invalid-response", result.Rum.ErrorCode);
        Assert.False(result.Rum.Configured);
    }

    [Fact]
    public async Task CollectOperations_RejectsRumAccountThatDoesNotOwnTheZone()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com", "ffffffffffffffffffffffffffffffff"),
            1 => CapabilityResponse(),
            2 => FirewallCapabilityResponse(),
            3 => OperationalHttpResponse(),
            4 => OperationalFirewallResponse(),
            _ => throw new InvalidOperationException("RUM lookup must not run for a mismatched account.")
        });
        using var client = new HttpClient(handler);

        var result = await CreateCollector(client).CollectOperationsAsync(CreateOperationalOptions(includeAccount: true));

        Assert.Equal("account-zone-mismatch", result.Rum.ErrorCode);
        Assert.Equal(5, result.RequestCount);
    }

    [Theory]
    [InlineData("2026-08-10T08:00:00Z")]
    [InlineData("2026-08-10T09:15:00Z")]
    public async Task CollectOperations_RejectsInvalidHttpHourDimensions(string hour)
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(),
            2 => FirewallCapabilityResponse(),
            3 => OperationalHttpResponse(HttpOperationRow(hour, "hit", 200, 1, 100, 1)),
            4 => OperationalFirewallResponse(),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);

        var result = await CreateCollector(client).CollectOperationsAsync(CreateOperationalOptions(includeAccount: false));

        Assert.False(result.Http.Success);
        Assert.Equal("invalid-response", result.Http.ErrorCode);
        Assert.Empty(result.Hours);
    }

    [Theory]
    [InlineData("2026-08-10T11:00:00Z")]
    [InlineData("2026-08-10T10:00:30Z")]
    public async Task CollectOperations_RejectsInvalidFirewallHourDimensions(string hour)
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(),
            2 => FirewallCapabilityResponse(),
            3 => OperationalHttpResponse(),
            4 => OperationalFirewallResponse(FirewallOperationRow(hour, "block", 1, 1)),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);

        var result = await CreateCollector(client).CollectOperationsAsync(CreateOperationalOptions(includeAccount: false));

        Assert.False(result.Firewall.Success);
        Assert.Equal("invalid-response", result.Firewall.ErrorCode);
        Assert.Empty(result.Hours);
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
            2 => FirewallCapabilityResponse(),
            3 => OperationalHttpResponse(HttpOperationRow("2026-08-10T09:00:00Z", "hit", 200, 10, 1000, 1)),
            4 => OperationalFirewallResponse(FirewallOperationRow("2026-08-10T09:00:00Z", "block", 4, 1), invalidRow),
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
            2 => FirewallCapabilityResponse(),
            3 => OperationalHttpResponse(missingCacheStatus),
            4 => OperationalFirewallResponse(),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);

        var result = await CreateCollector(client).CollectOperationsAsync(CreateOperationalOptions(includeAccount: false));

        Assert.False(result.Success);
        Assert.Equal("invalid-response", result.Http.ErrorCode);
        Assert.Empty(result.Hours);
    }

    [Fact]
    public async Task CollectOperations_RejectsDuplicateHttpDimensionsWithoutPartialEvidence()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(),
            2 => FirewallCapabilityResponse(),
            3 => OperationalHttpResponse(
                HttpOperationRow("2026-08-10T09:00:00Z", "hit", 200, 1, 100, 1),
                HttpOperationRow("2026-08-10T09:00:00Z", " HIT ", 200, 2, 200, 1)),
            4 => OperationalFirewallResponse(),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);

        var result = await CreateCollector(client).CollectOperationsAsync(CreateOperationalOptions(includeAccount: false));

        Assert.False(result.Http.Success);
        Assert.Equal("invalid-response", result.Http.ErrorCode);
        Assert.Empty(result.Hours);
    }

    [Fact]
    public async Task CollectOperations_RejectsDuplicateFirewallDimensionsWithoutPartialEvidence()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(),
            2 => FirewallCapabilityResponse(),
            3 => OperationalHttpResponse(),
            4 => OperationalFirewallResponse(
                FirewallOperationRow("2026-08-10T09:00:00Z", "block", 1, 1),
                FirewallOperationRow("2026-08-10T09:00:00Z", " BLOCK ", 2, 1)),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);

        var result = await CreateCollector(client).CollectOperationsAsync(CreateOperationalOptions(includeAccount: false));

        Assert.False(result.Firewall.Success);
        Assert.Equal("invalid-response", result.Firewall.ErrorCode);
        Assert.Empty(result.Hours);
    }

    [Fact]
    public async Task CollectOperations_RecordsCompletionTimeAfterProviderWork()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(),
            2 => FirewallCapabilityResponse(),
            3 => OperationalHttpResponse(),
            4 => OperationalFirewallResponse(),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);
        var clock = new AdvancingTimeProvider(CompletionTime, TimeSpan.FromMinutes(1));

        var result = await CreateCollector(client, clock).CollectOperationsAsync(CreateOperationalOptions(includeAccount: false));

        Assert.Equal(CompletionTime.AddMinutes(2), result.CollectedAtUtc);
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
        AccountId = includeAccount ? TestAccountId : null
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
        result = new[] { new { host = "officeimo.com", auto_install = autoInstall, ruleset = new { enabled, zone_tag = ZoneId } } }
    });

    private static HttpResponseMessage RumSitesForHostsResponse() => JsonResponse(new
    {
        success = true,
        errors = Array.Empty<object>(),
        result = new[]
        {
            new { host = "other.officeimo.com", auto_install = false, ruleset = new { enabled = false, zone_tag = ZoneId } },
            new { host = "officeimo.com", auto_install = true, ruleset = new { enabled = true, zone_tag = ZoneId } }
        }
    });

    private static HttpResponseMessage RumSitesPageResponse(bool includeMatch, int totalPages, int itemCount)
    {
        var rows = Enumerable.Range(0, itemCount)
            .Select(index => new
            {
                auto_install = includeMatch && index == 0,
                host = includeMatch && index == 0 ? "officeimo.com" : $"other-{index}.officeimo.com",
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
