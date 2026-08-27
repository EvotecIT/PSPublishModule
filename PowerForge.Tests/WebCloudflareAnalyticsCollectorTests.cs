using System.Net;
using System.Text;
using System.Text.Json;
using DBAClientX;
using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed partial class WebCloudflareAnalyticsCollectorTests
{
    private static readonly DateTimeOffset CompletionTime = new(2026, 8, 10, 12, 34, 56, TimeSpan.Zero);
    private const string ZoneId = "abcdef0123456789abcdef0123456789";
    private const string TestAccountId = "0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task Probe_DiscoversPlanSpecificDatasetLimitsWithoutExposingTheToken()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(maxPageSize: 25_000),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);
        var collector = CreateCollector(client);

        var result = await collector.ProbeAsync(ZoneId, "https://www.officeimo.com/");

        Assert.True(result.Success);
        Assert.True(result.DatasetEnabled);
        Assert.Equal(10_000, result.MaxPageSize);
        Assert.Equal("officeimo.com", result.ZoneName);
        Assert.Equal(2, result.RequestCount);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal("Bearer", request.AuthorizationScheme);
            Assert.Equal("test-token", request.AuthorizationParameter);
            Assert.DoesNotContain("test-token", request.Body, StringComparison.Ordinal);
        });
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Contains("httpRequestsAdaptiveGroups", handler.Requests[1].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Probe_RejectsAVisibleZoneThatDoesNotOwnTheFleetSite()
    {
        var handler = new ScriptedHandler((_, index) => index == 0
            ? ZoneResponse("tactra.dev")
            : throw new InvalidOperationException("Analytics probe must not run for another site's zone."));
        using var client = new HttpClient(handler);
        var collector = CreateCollector(client);

        var result = await collector.ProbeAsync(ZoneId, "https://officeimo.com/");

        Assert.False(result.Success);
        Assert.Equal("zone-site-mismatch", result.ErrorCode);
        Assert.Equal(1, result.RequestCount);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Collect_MapsDailyHostPathTrafficAndSamplingEvidence()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(),
            2 => TrafficResponse(TrafficRow(new DateOnly(2026, 8, 1), "OFFICEIMO.com.", "/docs/", 100, 25, 5000, 2)),
            3 => TrafficResponse(TrafficRow(new DateOnly(2026, 8, 2), "officeimo.com", "/", 50, 10, 2000, 1)),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);
        var collector = new CloudflareAnalyticsCollector(client, new FakeTokenProvider(), timeProvider: new FixedTimeProvider(CompletionTime));
        var options = CreateOptions();
        options.ThroughDate = new DateOnly(2026, 8, 2);

        var result = await collector.CollectAsync(options);
        var normalized = WebTrafficObservationNormalizer.Normalize(result.Batch);

        Assert.True(result.Success);
        Assert.Equal(4, result.RequestCount);
        Assert.Equal(2, result.CompletedDateCount);
        Assert.Equal(CompletionTime, normalized.CollectedAtUtc);
        Assert.Equal([new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 2)], normalized.CollectionCoverage.CompletedDates);
        var sampled = Assert.Single(normalized.Observations, value => value.Date == new DateOnly(2026, 8, 1));
        Assert.Equal("officeimo.com", sampled.Host);
        Assert.Equal(200, sampled.Requests);
        Assert.Equal(50, sampled.Visits);
        Assert.Equal(10_000, sampled.EdgeResponseBytes);
        Assert.Equal(2d, sampled.SampleInterval);
        Assert.All(handler.Requests.Skip(2), request =>
        {
            Assert.Contains("ZoneHttpRequestsAdaptiveGroupsFilter_InputObject", request.Body, StringComparison.Ordinal);
            Assert.Contains("\"requestSource\":\"eyeball\"", request.Body, StringComparison.Ordinal);
            Assert.Contains("\"clientRequestHTTPHost\":\"officeimo.com\"", request.Body, StringComparison.Ordinal);
            Assert.Contains("\"clientRequestScheme\":\"https\"", request.Body, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Collect_ResolvesOneCredentialForProbeAndPartitions()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(),
            2 => TrafficResponse(),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);
        var tokenProvider = new SingleUseTokenProvider();
        var collector = new CloudflareAnalyticsCollector(client, tokenProvider, timeProvider: new FixedTimeProvider(CompletionTime));

        var result = await collector.CollectAsync(CreateOptions());

        Assert.True(result.Success);
        Assert.Equal(1, tokenProvider.CallCount);
    }

    [Fact]
    public async Task Collect_RejectsRowsFromAnotherUrlScheme()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(),
            2 => TrafficResponse(TrafficRow(
                new DateOnly(2026, 8, 1), "officeimo.com", "/", 10, 2, 100, 1, scheme: "http")),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);

        var result = await CreateCollector(client).CollectAsync(CreateOptions());

        Assert.False(result.Success);
        Assert.Equal("invalid-response", result.ErrorCode);
        Assert.Empty(result.Batch.Observations);
    }

    [Fact]
    public async Task Collect_RejectsDatesOutsideTheProbedRetentionWindowBeforeTrafficRequests()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(notOlderThan: 86_400),
            _ => throw new InvalidOperationException("Traffic collection must not cross the retention boundary.")
        });
        using var client = new HttpClient(handler);
        var collector = new CloudflareAnalyticsCollector(
            client,
            new FakeTokenProvider(),
            timeProvider: new FixedTimeProvider(CompletionTime));

        var result = await collector.CollectAsync(CreateOptions());

        Assert.False(result.Success);
        Assert.Equal("retention-boundary", result.ErrorCode);
        Assert.Equal(2, result.RequestCount);
        Assert.False(result.Batch.ZeroDataConfirmed);
        Assert.Empty(result.Batch.CollectionCoverage.CompletedDates);
        Assert.Equal(new DateOnly(2026, 8, 1), result.Batch.CollectionCoverage.FailedDate);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Collect_RejectsCapabilitiesThatCannotCoverOneCompleteUtcPartition()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(maxDuration: 3600),
            _ => throw new InvalidOperationException("Traffic collection must not exceed the duration boundary.")
        });
        using var client = new HttpClient(handler);
        var collector = new CloudflareAnalyticsCollector(
            client,
            new FakeTokenProvider(),
            timeProvider: new FixedTimeProvider(CompletionTime));

        var result = await collector.CollectAsync(CreateOptions());

        Assert.False(result.Success);
        Assert.Equal("duration-boundary", result.ErrorCode);
        Assert.Equal(2, result.RequestCount);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Collect_ScopesSharedZoneTrafficToTheConfiguredHostAndPath()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(),
            2 => TrafficResponse(TrafficRow(new DateOnly(2026, 8, 1), "docs.officeimo.com", "/products/powerforge/", 10, 2, 1000, 1)),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);
        var collector = CreateCollector(client);
        var options = CreateOptions();
        options.SiteBaseUrl = "https://docs.officeimo.com/products/powerforge/";

        var result = await collector.CollectAsync(options);

        Assert.True(result.Success);
        var body = handler.Requests[2].Body;
        Assert.Contains("\"clientRequestHTTPHost\":\"docs.officeimo.com\"", body, StringComparison.Ordinal);
        Assert.Contains("\"clientRequestPath\":\"/products/powerforge\"", body, StringComparison.Ordinal);
        Assert.Contains("\"clientRequestPath_like\":\"/products/powerforge/%\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Collect_RejectsNullOrOutOfScopeTrafficRowsInsteadOfConfirmingZero()
    {
        var nullHandler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(),
            2 => TrafficNullResponse(),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var nullClient = new HttpClient(nullHandler);
        var nullResult = await CreateCollector(nullClient).CollectAsync(CreateOptions());
        Assert.False(nullResult.Success);
        Assert.Equal("invalid-response", nullResult.ErrorCode);
        Assert.False(nullResult.Batch.ZeroDataConfirmed);

        var foreignHandler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(),
            2 => TrafficResponse(TrafficRow(new DateOnly(2026, 8, 1), "tactra.dev", "/", 10, 2, 1000, 1)),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var foreignClient = new HttpClient(foreignHandler);
        var foreignResult = await CreateCollector(foreignClient).CollectAsync(CreateOptions());
        Assert.False(foreignResult.Success);
        Assert.Equal("invalid-response", foreignResult.ErrorCode);
    }

    [Fact]
    public async Task Collect_ConfirmsZeroOnlyAfterEveryDailyPartitionReturnsNoRows()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(),
            2 or 3 => TrafficResponse(),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);
        var collector = CreateCollector(client);
        var options = CreateOptions();
        options.ThroughDate = new DateOnly(2026, 8, 2);

        var result = await collector.CollectAsync(options);

        Assert.True(result.Success);
        Assert.True(result.Batch.ZeroDataConfirmed);
        Assert.Empty(result.Batch.Observations);
        Assert.Equal(2, result.Batch.CollectionCoverage.CompletedDates.Length);
    }

    [Theory]
    [InlineData("2026-08-10")]
    [InlineData("2026-08-11")]
    [InlineData("9999-12-31")]
    public async Task Collect_RejectsOpenOrFutureUtcDatesBeforeAnyProviderRequest(string throughDate)
    {
        var handler = new ScriptedHandler((_, _) => throw new InvalidOperationException("Provider must not be reached."));
        using var client = new HttpClient(handler);
        var collector = new CloudflareAnalyticsCollector(
            client,
            new FakeTokenProvider(),
            timeProvider: new FixedTimeProvider(CompletionTime));
        var options = CreateOptions();
        options.FromDate = DateOnly.Parse(throughDate);
        options.ThroughDate = options.FromDate;

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => collector.CollectAsync(options));

        Assert.Contains("closed UTC dates", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Collect_PreservesCompletedDatesWhenALaterPartitionFails()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(),
            2 => TrafficResponse(TrafficRow(new DateOnly(2026, 8, 1), "officeimo.com", "/", 10, 2, 1000, 1)),
            3 => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);
        var collector = CreateCollector(client);
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
    public async Task Collect_RejectsMalformedReturnedPathsWithoutDiscardingCompletedPartitions()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(),
            2 => TrafficResponse(TrafficRow(new DateOnly(2026, 8, 1), "officeimo.com", "/", 10, 2, 1000, 1)),
            3 => TrafficResponse(TrafficRow(new DateOnly(2026, 8, 2), "officeimo.com", "relative", 5, 1, 500, 1)),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);
        var collector = CreateCollector(client);
        var options = CreateOptions();
        options.ThroughDate = new DateOnly(2026, 8, 2);

        var result = await collector.CollectAsync(options);

        Assert.False(result.Success);
        Assert.Equal("invalid-response", result.ErrorCode);
        Assert.Equal("partial", result.Batch.Status);
        Assert.Equal([new DateOnly(2026, 8, 1)], result.Batch.CollectionCoverage.CompletedDates);
        Assert.Equal(new DateOnly(2026, 8, 2), result.Batch.CollectionCoverage.FailedDate);
        Assert.Single(result.Batch.Observations);
    }

    [Theory]
    [InlineData("officeimo.com/path")]
    [InlineData("officeimo.com:443")]
    [InlineData("officeimo.com?query")]
    [InlineData(" officeimo.com")]
    public async Task Collect_RejectsMalformedReturnedHostsWithoutDiscardingCompletedPartitions(string host)
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(),
            2 => TrafficResponse(TrafficRow(new DateOnly(2026, 8, 1), "officeimo.com", "/", 10, 2, 1000, 1)),
            3 => TrafficResponse(TrafficRow(new DateOnly(2026, 8, 2), host, "/", 5, 1, 500, 1)),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);
        var collector = CreateCollector(client);
        var options = CreateOptions();
        options.ThroughDate = new DateOnly(2026, 8, 2);

        var result = await collector.CollectAsync(options);

        Assert.False(result.Success);
        Assert.Equal("invalid-response", result.ErrorCode);
        Assert.Equal([new DateOnly(2026, 8, 1)], result.Batch.CollectionCoverage.CompletedDates);
        Assert.Single(result.Batch.Observations);
    }

    [Fact]
    public async Task Collect_ReachingThePlanRowLimitProducesAnHonestPartialPartition()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(maxPageSize: 2),
            2 => TrafficResponse(
                TrafficRow(new DateOnly(2026, 8, 1), "officeimo.com", "/a", 10, 2, 1000, 1),
                TrafficRow(new DateOnly(2026, 8, 1), "officeimo.com", "/b", 5, 1, 500, 1)),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);
        var collector = CreateCollector(client);

        var result = await collector.CollectAsync(CreateOptions());

        Assert.False(result.Success);
        Assert.Equal("row-limit-reached", result.ErrorCode);
        Assert.Empty(result.Batch.CollectionCoverage.CompletedDates);
        Assert.Equal(new DateOnly(2026, 8, 1), result.Batch.CollectionCoverage.FailedDate);
        Assert.Equal(2, result.Batch.Observations.Length);
    }

    [Fact]
    public async Task Collect_ConvertsDuplicateProviderGroupsIntoAPartialResult()
    {
        var duplicate = TrafficRow(new DateOnly(2026, 8, 1), "officeimo.com", "/", 10, 2, 1000, 1);
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(),
            2 => TrafficResponse(duplicate, duplicate),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);
        var collector = CreateCollector(client);

        var result = await collector.CollectAsync(CreateOptions());

        Assert.False(result.Success);
        Assert.Equal("invalid-response", result.ErrorCode);
        Assert.Equal("partial", result.Batch.Status);
        Assert.Empty(result.Batch.Observations);
        WebTrafficObservationNormalizer.Normalize(result.Batch);
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
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => ZoneResponse("officeimo.com"),
            1 => CapabilityResponse(),
            2 => TrafficResponse(invalid),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var client = new HttpClient(handler);
        var collector = CreateCollector(client);

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

            Assert.Equal(7, first.DatabaseSchemaVersion);
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
    public async Task TrafficReports_DistinguishMissingPartialAndExplicitZeroEvidence()
    {
        var root = Path.Combine(Path.GetTempPath(), "powerforge-cloudflare-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var databasePath = Path.Combine(root, "fleet.db");
            var store = new SqliteWebSearchObservationStore(databasePath);
            var query = new WebTrafficObservationQuery
            {
                SiteId = "officeimo",
                Provider = "cloudflare",
                FromDate = new DateOnly(2026, 8, 1),
                ThroughDate = new DateOnly(2026, 8, 1)
            };

            var missing = await store.QueryTrafficEvidenceAsync(query);

            Assert.False(missing.StoreExists);
            Assert.False(missing.HasEvidence);
            Assert.Equal(2, RunTrafficList(databasePath));

            var partial = CreateTrafficBatch();
            partial.Status = "partial";
            partial.CollectionCoverage.CompletedDates = Array.Empty<DateOnly>();
            partial.CollectionCoverage.FailedDate = new DateOnly(2026, 8, 1);
            partial.CollectionCoverage.FailureCategory = "row-limit-reached";
            await store.ImportTrafficAsync(WebTrafficObservationNormalizer.Normalize(partial));

            var partialReport = await store.QueryTrafficEvidenceAsync(query);

            Assert.True(partialReport.StoreExists);
            Assert.True(partialReport.HasEvidence);
            Assert.True(partialReport.HasPartialEvidence);
            Assert.False(partialReport.HasExplicitZeroEvidence);
            Assert.Single(partialReport.Observations);
            Assert.Equal("partial", Assert.Single(partialReport.SelectedRuns).Status);
            Assert.Equal(1, RunTrafficList(databasePath));

            var completeZero = CreateTrafficBatch();
            completeZero.CollectedAtUtc = CompletionTime.AddMinutes(1);
            completeZero.ZeroDataConfirmed = true;
            completeZero.Observations = Array.Empty<WebTrafficObservation>();
            await store.ImportTrafficAsync(WebTrafficObservationNormalizer.Normalize(completeZero));

            var zeroReport = await store.QueryTrafficEvidenceAsync(query);

            Assert.True(zeroReport.HasEvidence);
            Assert.False(zeroReport.HasPartialEvidence);
            Assert.True(zeroReport.HasExplicitZeroEvidence);
            Assert.Empty(zeroReport.Observations);
            Assert.Equal("complete", Assert.Single(zeroReport.SelectedRuns).Status);
            Assert.Equal(0, RunTrafficList(databasePath));

            var boundedGapReport = await store.QueryTrafficEvidenceAsync(new WebTrafficObservationQuery
            {
                SiteId = "officeimo",
                Provider = "cloudflare",
                FromDate = new DateOnly(2026, 8, 1),
                ThroughDate = new DateOnly(2026, 8, 7)
            });

            Assert.True(boundedGapReport.HasEvidence);
            Assert.False(boundedGapReport.HasPartialEvidence);
            Assert.True(boundedGapReport.HasCoverageGaps);
            Assert.Equal(
                Enumerable.Range(2, 6).Select(day => new DateOnly(2026, 8, day)),
                boundedGapReport.MissingDates);
            Assert.Equal(1, RunTrafficList(databasePath, "2026-08-07"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task TrafficReports_TreatCompletedPartitionsOfPartialRunsAsComplete()
    {
        var root = Path.Combine(Path.GetTempPath(), "powerforge-cloudflare-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new SqliteWebSearchObservationStore(Path.Combine(root, "fleet.db"));
            await store.ImportTrafficAsync(WebTrafficObservationNormalizer.Normalize(CreateTrafficBatch()));
            var partial = CreateTrafficBatch();
            partial.CollectedAtUtc = CompletionTime.AddMinutes(1);
            partial.Status = "partial";
            partial.CollectionCoverage.ThroughDate = new DateOnly(2026, 8, 2);
            partial.CollectionCoverage.FailedDate = new DateOnly(2026, 8, 2);
            partial.CollectionCoverage.FailureCategory = "provider-unavailable";
            partial.Observations[0].Requests = 200;
            await store.ImportTrafficAsync(WebTrafficObservationNormalizer.Normalize(partial));

            var completed = await store.QueryTrafficEvidenceAsync(new WebTrafficObservationQuery
            {
                SiteId = "officeimo", Provider = "cloudflare",
                FromDate = new DateOnly(2026, 8, 1), ThroughDate = new DateOnly(2026, 8, 1)
            });
            Assert.False(completed.HasPartialEvidence);
            Assert.Equal("complete", Assert.Single(completed.SelectedRuns).Status);
            Assert.Equal(200, Assert.Single(completed.Observations).Requests);

            var failed = await store.QueryTrafficEvidenceAsync(new WebTrafficObservationQuery
            {
                SiteId = "officeimo", Provider = "cloudflare",
                FromDate = new DateOnly(2026, 8, 2), ThroughDate = new DateOnly(2026, 8, 2)
            });
            Assert.True(failed.HasPartialEvidence);
            Assert.Equal("partial", Assert.Single(failed.SelectedRuns).Status);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task TrafficReports_PreferInformativePartialEvidenceOverNewerEmptyFailures()
    {
        var root = Path.Combine(Path.GetTempPath(), "powerforge-cloudflare-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new SqliteWebSearchObservationStore(Path.Combine(root, "fleet.db"));
            var informative = CreateTrafficBatch();
            informative.Status = "partial";
            informative.CollectionCoverage.CompletedDates = Array.Empty<DateOnly>();
            informative.CollectionCoverage.FailedDate = new DateOnly(2026, 8, 1);
            informative.CollectionCoverage.FailureCategory = "row-limit-reached";
            var normalizedInformative = WebTrafficObservationNormalizer.Normalize(informative);
            await store.ImportTrafficAsync(normalizedInformative);

            var emptyFailure = CreateTrafficBatch();
            emptyFailure.CollectedAtUtc = CompletionTime.AddMinutes(1);
            emptyFailure.Status = "partial";
            emptyFailure.CollectionCoverage.CompletedDates = Array.Empty<DateOnly>();
            emptyFailure.CollectionCoverage.FailedDate = new DateOnly(2026, 8, 1);
            emptyFailure.CollectionCoverage.FailureCategory = "provider-unavailable";
            emptyFailure.Observations = Array.Empty<WebTrafficObservation>();
            await store.ImportTrafficAsync(WebTrafficObservationNormalizer.Normalize(emptyFailure));

            var result = await store.QueryTrafficEvidenceAsync(new WebTrafficObservationQuery
            {
                SiteId = "officeimo",
                Provider = "cloudflare",
                FromDate = new DateOnly(2026, 8, 1),
                ThroughDate = new DateOnly(2026, 8, 1)
            });

            Assert.Equal(normalizedInformative.RunId, Assert.Single(result.SelectedRuns).RunId);
            Assert.Equal(100, Assert.Single(result.Observations).Requests);
            Assert.True(result.HasPartialEvidence);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task TrafficReports_PreserveZeroEvidenceForACompletedPartitionInsideAPartialRun()
    {
        var root = Path.Combine(Path.GetTempPath(), "powerforge-cloudflare-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new SqliteWebSearchObservationStore(Path.Combine(root, "fleet.db"));
            var partial = CreateTrafficBatch();
            partial.Status = "partial";
            partial.CollectionCoverage.ThroughDate = new DateOnly(2026, 8, 2);
            partial.CollectionCoverage.FailedDate = new DateOnly(2026, 8, 2);
            partial.CollectionCoverage.FailureCategory = "provider-unavailable";
            partial.Observations = Array.Empty<WebTrafficObservation>();
            await store.ImportTrafficAsync(WebTrafficObservationNormalizer.Normalize(partial));

            var completedZero = await store.QueryTrafficEvidenceAsync(new WebTrafficObservationQuery
            {
                SiteId = "officeimo", Provider = "cloudflare",
                FromDate = new DateOnly(2026, 8, 1), ThroughDate = new DateOnly(2026, 8, 1)
            });

            Assert.True(completedZero.HasEvidence);
            Assert.False(completedZero.HasPartialEvidence);
            Assert.True(completedZero.HasExplicitZeroEvidence);
            Assert.Empty(completedZero.Observations);
            var selectedRun = Assert.Single(completedZero.SelectedRuns);
            Assert.Equal("complete", selectedRun.Status);
            Assert.True(selectedRun.ZeroDataConfirmed);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task TrafficReports_RequireProviderForAllAggregateTotals()
    {
        var root = Path.Combine(Path.GetTempPath(), "powerforge-cloudflare-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var databasePath = Path.Combine(root, "fleet.db");
            var store = new SqliteWebSearchObservationStore(databasePath);
            await store.ImportTrafficAsync(WebTrafficObservationNormalizer.Normalize(CreateTrafficBatch()));

            var exception = await Assert.ThrowsAsync<ArgumentException>(() => store.QueryTrafficEvidenceAsync(new WebTrafficObservationQuery
            {
                SiteId = "officeimo", FromDate = new DateOnly(2026, 8, 1), ThroughDate = new DateOnly(2026, 8, 1)
            }));

            Assert.Contains("requires a provider", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(2, WebCliCommandHandlers.HandleSubCommand(
                "traffic",
                ["list", "--database", databasePath, "--site", "officeimo", "--from", "2026-08-01", "--to", "2026-08-01", "--output", "json"],
                outputJson: true, logger: new WebConsoleLogger(), outputSchemaVersion: 1));
            Assert.Equal(2, WebCliCommandHandlers.HandleSubCommand(
                "traffic",
                ["list", "--database", databasePath, "--site", "officeimo", "--output", "json"],
                outputJson: true, logger: new WebConsoleLogger(), outputSchemaVersion: 1));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
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
    public async Task Collect_RejectsANonDefaultSitePortBeforeSendingRequests()
    {
        var options = CreateOptions();
        options.SiteBaseUrl = "https://officeimo.com:8443/";
        using var httpClient = new HttpClient(
            new ScriptedHandler((_, _) => throw new InvalidOperationException("HTTP must not be reached.")));

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => CreateCollector(httpClient).CollectAsync(options));

        Assert.Contains("default port", exception.Message, StringComparison.OrdinalIgnoreCase);
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

}
