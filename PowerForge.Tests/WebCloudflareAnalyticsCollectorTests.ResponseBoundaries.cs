using System.Text.Json;
using DBAClientX;
using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed partial class WebCloudflareAnalyticsCollectorTests
{
    [Theory]
    [InlineData(-1, 2_678_400)]
    [InlineData(0, 2_678_400)]
    [InlineData(86_400, -1)]
    [InlineData(86_400, 0)]
    public async Task Probe_RejectsNonPositiveCapabilityBoundaries(int maxDuration, int notOlderThan)
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(maxDuration: maxDuration, notOlderThan: notOlderThan),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);

        var result = await CreateCollector(client).ProbeAsync(ZoneId, "https://officeimo.com/");

        Assert.False(result.Success);
        Assert.Equal("invalid-response", result.ErrorCode);
        Assert.Equal(2, result.RequestCount);
    }

    [Fact]
    public async Task Probe_AllowsOmittedOptionalCapabilityBoundaries()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(maxDuration: null, notOlderThan: null),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);

        var result = await CreateCollector(client).ProbeAsync(ZoneId, "https://officeimo.com/");

        Assert.True(result.Success);
        Assert.Null(result.MaxDurationSeconds);
        Assert.Null(result.NotOlderThanSeconds);
    }

    [Fact]
    public async Task Probe_RejectsNullCapabilityZoneElementsAsInvalidResponses()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityNullZoneResponse(),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);

        var result = await CreateCollector(client).ProbeAsync(ZoneId, "https://officeimo.com/");

        Assert.False(result.Success);
        Assert.Equal("invalid-response", result.ErrorCode);
        Assert.Equal(2, result.RequestCount);
    }

    [Fact]
    public async Task Collect_ScalesSampledRequestCountsBeforePersistence()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(),
            2 => TrafficResponse(TrafficRow(new DateOnly(2026, 8, 1), "officeimo.com", "/", 100, 25, 5000, 2.5)),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);

        var result = await CreateCollector(client).CollectAsync(CreateOptions());

        Assert.True(result.Success);
        Assert.Equal(250, Assert.Single(result.Batch.Observations).Requests);
    }

    [Fact]
    public async Task Collect_RejectsNullTrafficGroupElements()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(),
            2 => TrafficResponse((object?)null!),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);

        var result = await CreateCollector(client).CollectAsync(CreateOptions());

        Assert.False(result.Success);
        Assert.Equal("invalid-response", result.ErrorCode);
        Assert.Empty(result.Batch.Observations);
    }

    [Fact]
    public async Task Collect_RejectsNullZoneElementsAsInvalidResponses()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(),
            2 => TrafficNullZoneResponse(),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);

        var result = await CreateCollector(client).CollectAsync(CreateOptions());

        Assert.False(result.Success);
        Assert.Equal("invalid-response", result.ErrorCode);
        Assert.Empty(result.Batch.Observations);
    }

    [Fact]
    public async Task Collect_RejectsPathsThatWouldChangeDuringNormalization()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(),
            2 => TrafficResponse(TrafficRow(new DateOnly(2026, 8, 1), "officeimo.com", "/docs ", 10, 2, 1000, 1)),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);

        var result = await CreateCollector(client).CollectAsync(CreateOptions());

        Assert.False(result.Success);
        Assert.Equal("invalid-response", result.ErrorCode);
        Assert.Empty(result.Batch.Observations);
    }

    [Theory]
    [InlineData("https://officeimo.com//admin/")]
    [InlineData("https://officeimo.com/admin//")]
    public async Task Collect_RejectsRepeatedSeparatorsInSiteBasePaths(string siteBaseUrl)
    {
        using var client = new HttpClient(new ScriptedHandler((_, _) =>
            throw new InvalidOperationException("Invalid site paths must fail before HTTP.")));
        var options = CreateOptions();
        options.SiteBaseUrl = siteBaseUrl;

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => CreateCollector(client).CollectAsync(options));

        Assert.Contains("repeated path separators", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("https://officeimo.com/docs%20archive/")]
    [InlineData("https://officeimo.com/docs_archive/")]
    public async Task Collect_RejectsPathsWithAnalyticsWildcardMetacharacters(string siteBaseUrl)
    {
        using var client = new HttpClient(new ScriptedHandler((_, _) =>
            throw new InvalidOperationException("Invalid site paths must fail before HTTP.")));
        var options = CreateOptions();
        options.SiteBaseUrl = siteBaseUrl;

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => CreateCollector(client).CollectAsync(options));

        Assert.Contains("wildcard metacharacters", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TrafficStorage_BoundedQueryDoesNotMaterializeOutOfRangeRunManifests()
    {
        var root = Path.Combine(Path.GetTempPath(), "powerforge-cloudflare-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var databasePath = Path.Combine(root, "fleet.db");
            var store = new SqliteWebSearchObservationStore(databasePath);
            await store.ImportTrafficAsync(WebTrafficObservationNormalizer.Normalize(CreateTrafficBatch()));
            var invalidOldManifest = JsonSerializer.Serialize(new WebTrafficObservationBatch
            {
                RunId = "invalid-old-run",
                Provider = "cloudflare",
                SiteId = "officeimo",
                CollectedAtUtc = CompletionTime.AddYears(-1),
                SourceKind = "fixture",
                Status = "complete",
                CollectionCoverage = new WebTrafficObservationCollectionCoverage
                {
                    FromDate = new DateOnly(2025, 8, 1),
                    ThroughDate = new DateOnly(2025, 8, 1),
                    CompletedDates = [new DateOnly(2025, 8, 1)]
                },
                Observations =
                [
                    new WebTrafficObservation
                    {
                        Date = new DateOnly(2025, 8, 1), Host = "officeimo.com", Path = "/",
                        Requests = -1, Visits = 0, EdgeResponseBytes = 0, SampleInterval = 1
                    }
                ]
            }, WebCliJson.Options);
            await using (var sqlite = new SQLite())
            {
                await sqlite.ExecuteNonQueryAsync(
                    databasePath,
                    """
                    INSERT INTO traffic_observation_runs (
                        run_id, provider, site_id, collected_at_utc, source_kind, status,
                        configuration_hash, evidence_reference, normalized_manifest_json
                    ) VALUES (
                        @run_id, @provider, @site_id, @collected_at_utc, @source_kind, @status,
                        NULL, NULL, @manifest
                    );
                    """,
                    new Dictionary<string, object?>
                    {
                        ["@run_id"] = "invalid-old-run",
                        ["@provider"] = "cloudflare",
                        ["@site_id"] = "officeimo",
                        ["@collected_at_utc"] = CompletionTime.AddYears(-1).ToString("O"),
                        ["@source_kind"] = "fixture",
                        ["@status"] = "complete",
                        ["@manifest"] = invalidOldManifest
                    });
            }

            var result = await store.QueryTrafficEvidenceAsync(new WebTrafficObservationQuery
            {
                SiteId = "officeimo",
                Provider = "cloudflare",
                FromDate = new DateOnly(2026, 8, 1),
                ThroughDate = new DateOnly(2026, 8, 1)
            });

            Assert.Single(result.Observations);
            Assert.Single(result.SelectedRuns);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("evil.com@officeimo.com")]
    [InlineData("officeimo.com:443")]
    [InlineData("officeimo.com/path")]
    public async Task Probe_RejectsZoneNamesContainingUriComponents(string zoneName)
    {
        var handler = new ScriptedHandler((_, index) => index == 0
            ? ZoneResponse(zoneName)
            : throw new InvalidOperationException("Capability probing must not run for malformed zone names."));
        using var client = new HttpClient(handler);

        var result = await CreateCollector(client).ProbeAsync(ZoneId, "https://officeimo.com/");

        Assert.False(result.Success);
        Assert.Equal("invalid-response", result.ErrorCode);
        Assert.Single(handler.Requests);
    }
}
