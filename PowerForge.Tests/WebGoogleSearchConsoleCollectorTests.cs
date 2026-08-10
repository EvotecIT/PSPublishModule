using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Google.Apis.Auth.OAuth2.Responses;
using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed partial class WebGoogleSearchConsoleCollectorTests
{
    private static readonly DateTimeOffset CompletionTime = new(2026, 8, 10, 12, 34, 56, TimeSpan.Zero);

    [Fact]
    public async Task Probe_NormalizesDomainIdentityAndRequiresExactVisibleProperty()
    {
        var handler = new ScriptedHandler((request, _) => JsonResponse(
            HttpStatusCode.OK,
            """{"siteEntry":[{"siteUrl":"sc-domain:xn--bcher-kva.de","permissionLevel":"siteOwner"}]}"""));
        using var httpClient = new HttpClient(handler);
        var tokenProvider = new FakeTokenProvider();
        var collector = new GoogleSearchConsoleCollector(httpClient, tokenProvider);

        var result = await collector.ProbeAsync("sc-domain:BÜCHER.de.");

        Assert.True(result.Success);
        Assert.Equal("sc-domain:xn--bcher-kva.de", result.Property);
        Assert.Equal("siteOwner", result.PermissionLevel);
        Assert.Equal([WebSearchProviderCapabilities.SearchAnalytics], result.AvailableCapabilities);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("Bearer", request.AuthorizationScheme);
        Assert.Equal("test-access-token", request.AuthorizationParameter);
        Assert.Equal(request.Uri, Assert.Single(tokenProvider.RequestUris));
    }

    [Fact]
    public async Task Probe_RejectsAnUnverifiedPropertyEntry()
    {
        var handler = new ScriptedHandler((request, _) => JsonResponse(
            HttpStatusCode.OK,
            """{"siteEntry":[{"siteUrl":"sc-domain:officeimo.com","permissionLevel":"siteUnverifiedUser"}]}"""));
        using var httpClient = new HttpClient(handler);
        var collector = new GoogleSearchConsoleCollector(httpClient, new FakeTokenProvider());

        var result = await collector.ProbeAsync("sc-domain:officeimo.com");

        Assert.False(result.Success);
        Assert.Equal("property-unverified", result.ErrorCode);
        Assert.Equal("siteUnverifiedUser", result.PermissionLevel);
    }

    [Fact]
    public async Task Probe_ClassifiesOAuthTokenRejectionWithoutSendingARequest()
    {
        var handler = new ScriptedHandler((_, _) => throw new InvalidOperationException("HTTP transport must not be reached."));
        using var httpClient = new HttpClient(handler);
        var tokenError = new TokenErrorResponse { Error = "invalid_grant" };
        var collector = new GoogleSearchConsoleCollector(
            httpClient,
            new ThrowingTokenProvider(new TokenResponseException(tokenError)));

        var result = await collector.ProbeAsync("sc-domain:officeimo.com");

        Assert.False(result.Success);
        Assert.Equal("authentication-failed", result.ErrorCode);
        Assert.Empty(handler.Requests);
        Assert.DoesNotContain("invalid_grant", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"siteEntry\":[null,{\"siteUrl\":\"sc-domain:officeimo.com\",\"permissionLevel\":\"siteOwner\"}]}")]
    public async Task Probe_RejectsNullPropertyPayloads(string json)
    {
        var handler = new ScriptedHandler((_, _) => JsonResponse(HttpStatusCode.OK, json));
        using var httpClient = new HttpClient(handler);
        var collector = new GoogleSearchConsoleCollector(httpClient, new FakeTokenProvider());

        var result = await collector.ProbeAsync("sc-domain:officeimo.com");

        Assert.False(result.Success);
        Assert.Equal("provider-response-invalid", result.ErrorCode);
    }

    [Fact]
    public async Task Collect_PagesDailyAnalyticsAndMapsProviderNeutralObservations()
    {
        var handler = new ScriptedHandler((request, index) => index switch
        {
            0 => SitesResponse("sc-domain:officeimo.com"),
            1 => QueryResponse(),
            2 => QueryResponse(
                Row("2026-08-01", "https://officeimo.com/docs/a", "alpha", "usa", "DESKTOP", 4, 40, 0.1, 2.5),
                Row("2026-08-01", "https://officeimo.com/docs/b", "beta", "pol", "MOBILE", 2, 20, 0.1, 3.5)),
            3 => QueryResponse(
                Row("2026-08-01", "https://officeimo.com/docs/c", "gamma", "gbr", "TABLET", 0, 0, 0, 0)),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var httpClient = new HttpClient(handler);
        var collector = new GoogleSearchConsoleCollector(
            httpClient,
            new FakeTokenProvider(),
            new FixedTimeProvider(CompletionTime));

        var result = await collector.CollectAsync(CreateOptions(rowLimit: 2));

        Assert.True(result.Success);
        Assert.Equal(1, result.CompletedDateCount);
        Assert.Equal(3, result.RequestCount);
        Assert.Equal("complete", result.Batch.Status);
        Assert.False(result.Batch.ZeroDataConfirmed);
        Assert.Equal(CompletionTime, result.Batch.CollectedAtUtc);
        Assert.Equal(3, result.Batch.Observations.Length);
        var first = result.Batch.Observations[0];
        Assert.Equal(new DateOnly(2026, 8, 1), first.Date);
        Assert.Equal("https://officeimo.com/docs/a", first.Page);
        Assert.Equal("alpha", first.Query);
        Assert.Equal("usa", first.Country);
        Assert.Equal("DESKTOP", first.Device);
        Assert.Equal("web", first.SearchType);
        Assert.Equal(4, first.Clicks);
        Assert.Equal(40, first.Impressions);
        Assert.Equal(0.1, first.ClickThroughRate);
        Assert.Equal(2.5, first.AveragePosition);
        Assert.Null(result.Batch.Observations[2].AveragePosition);
        Assert.All(result.Batch.Observations, observation =>
            Assert.StartsWith("gsc:sc-domain:officeimo.com:2026-08-01:web", observation.EvidenceReference, StringComparison.Ordinal));

        var finalityRequest = handler.Requests[1];
        using (var finalityDocument = JsonDocument.Parse(finalityRequest.Body!))
        {
            Assert.Equal("all", finalityDocument.RootElement.GetProperty("dataState").GetString());
            Assert.Equal(["date"], finalityDocument.RootElement.GetProperty("dimensions").EnumerateArray().Select(value => value.GetString()));
            AssertPageScopeFilter(finalityDocument.RootElement, "^https://officeimo\\.com/");
        }

        var analyticsRequests = handler.Requests.Skip(2).ToArray();
        Assert.Equal([0, 2], analyticsRequests.Select(RequestStartRow));
        Assert.All(analyticsRequests, request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Contains("sc-domain%3Aofficeimo.com", request.Uri.AbsoluteUri, StringComparison.Ordinal);
            using var document = JsonDocument.Parse(request.Body!);
            Assert.Equal("final", document.RootElement.GetProperty("dataState").GetString());
            Assert.Equal("web", document.RootElement.GetProperty("type").GetString());
            Assert.Equal(2, document.RootElement.GetProperty("rowLimit").GetInt32());
            Assert.Equal(
                ["date", "page", "query", "country", "device"],
                document.RootElement.GetProperty("dimensions").EnumerateArray().Select(value => value.GetString()));
            AssertPageScopeFilter(document.RootElement, "^https://officeimo\\.com/");
        });

        var normalized = WebSearchObservationNormalizer.Normalize(result.Batch);
        Assert.Equal(3, normalized.Observations.Length);
    }

    [Theory]
    [InlineData("discover")]
    [InlineData("googleNews")]
    public async Task Collect_OmitsUnsupportedQueryDimensionForQuerylessSearchTypes(string searchType)
    {
        var handler = new ScriptedHandler((request, index) => index switch
        {
            0 => SitesResponse("sc-domain:officeimo.com"),
            1 => QueryResponse(),
            2 => QueryResponse(RowWithoutQuery(
                "2026-08-01", "https://officeimo.com/news/", "pol", "MOBILE", 3, 30, 0.1, 4.5)),
            3 => QueryResponse(),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var httpClient = new HttpClient(handler);
        var collector = new GoogleSearchConsoleCollector(httpClient, new FakeTokenProvider());
        var options = CreateOptions();
        options.SearchType = searchType;

        var result = await collector.CollectAsync(options);

        Assert.True(result.Success);
        var observation = Assert.Single(result.Batch.Observations);
        Assert.Equal("https://officeimo.com/news/", observation.Page);
        Assert.Null(observation.Query);
        Assert.Equal("pol", observation.Country);
        Assert.Equal("MOBILE", observation.Device);
        Assert.Equal(searchType, observation.SearchType);
        using var requestBody = JsonDocument.Parse(handler.Requests[2].Body!);
        Assert.Equal(
            ["date", "page", "country", "device"],
            requestBody.RootElement.GetProperty("dimensions").EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public async Task Collect_PreservesRowsAsPartialWhenALaterPageIsQuotaLimited()
    {
        var handler = new ScriptedHandler((request, index) => index switch
        {
            0 => SitesResponse("sc-domain:officeimo.com"),
            1 => QueryResponse(),
            2 => QueryResponse(Row(
                "2026-08-01", "https://officeimo.com/", "officeimo", "pol", "DESKTOP", 1, 10, 0.1, 1)),
            3 => JsonResponse(HttpStatusCode.TooManyRequests, """{"error":{"message":"Daily quota exceeded."}}"""),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var httpClient = new HttpClient(handler);
        var collector = new GoogleSearchConsoleCollector(
            httpClient,
            new FakeTokenProvider(),
            new FixedTimeProvider(CompletionTime));

        var result = await collector.CollectAsync(CreateOptions(rowLimit: 1));

        Assert.False(result.Success);
        Assert.Equal("partial", result.Batch.Status);
        Assert.False(result.Batch.ZeroDataConfirmed);
        Assert.Equal("quota-exceeded", result.ErrorCode);
        Assert.DoesNotContain("test-access-token", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(3, result.RequestCount);
        Assert.Equal(0, result.CompletedDateCount);
        Assert.Single(result.Batch.Observations);
        var normalized = WebSearchObservationNormalizer.Normalize(result.Batch);
        Assert.Single(normalized.Observations);
        Assert.Equal(new DateOnly(2026, 8, 1), normalized.CollectionCoverage!.FailedDate);
        Assert.Equal("quota-exceeded", normalized.CollectionCoverage.FailureCategory);
    }

    [Fact]
    public async Task Collect_PreservesRowsAsPartialWhenALaterResponseBodyCannotBeRead()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => SitesResponse("sc-domain:officeimo.com"),
            1 => QueryResponse(),
            2 => QueryResponse(Row(
                "2026-08-01", "https://officeimo.com/", "officeimo", "pol", "DESKTOP", 1, 10, 0.1, 1)),
            3 => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ThrowingReadContent() },
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var httpClient = new HttpClient(handler);
        var collector = new GoogleSearchConsoleCollector(
            httpClient,
            new FakeTokenProvider(),
            new FixedTimeProvider(CompletionTime));

        var result = await collector.CollectAsync(CreateOptions(rowLimit: 1));

        Assert.False(result.Success);
        Assert.Equal("partial", result.Batch.Status);
        Assert.Equal("provider-unavailable", result.ErrorCode);
        Assert.Single(result.Batch.Observations);
        WebSearchObservationNormalizer.Normalize(result.Batch);
    }

    [Fact]
    public async Task Collect_RecordsAnExplicitCompleteZeroDataRun()
    {
        var handler = new ScriptedHandler((request, index) => index switch
        {
            0 => SitesResponse("sc-domain:officeimo.com"),
            1 => QueryResponse(),
            2 => QueryResponse(),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var httpClient = new HttpClient(handler);
        var collector = new GoogleSearchConsoleCollector(
            httpClient,
            new FakeTokenProvider(),
            new FixedTimeProvider(CompletionTime));

        var result = await collector.CollectAsync(CreateOptions());
        var normalized = WebSearchObservationNormalizer.Normalize(result.Batch);

        Assert.True(result.Success);
        Assert.True(normalized.ZeroDataConfirmed);
        Assert.Empty(normalized.Observations);
        Assert.Equal("complete", normalized.Status);
        Assert.Equal(new DateOnly(2026, 8, 1), normalized.CollectionCoverage!.FromDate);
        Assert.Equal([new DateOnly(2026, 8, 1)], normalized.CollectionCoverage.CompletedDates);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"rows\":null}")]
    public async Task Collect_DoesNotTreatNullAnalyticsPayloadsAsConfirmedZero(string json)
    {
        var handler = new ScriptedHandler((request, index) => index switch
        {
            0 => SitesResponse("sc-domain:officeimo.com"),
            1 => QueryResponse(),
            2 => JsonResponse(HttpStatusCode.OK, json),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var httpClient = new HttpClient(handler);
        var collector = new GoogleSearchConsoleCollector(httpClient, new FakeTokenProvider());

        var result = await collector.CollectAsync(CreateOptions());

        Assert.False(result.Success);
        Assert.Equal("provider-response-invalid", result.ErrorCode);
        Assert.False(result.Batch.ZeroDataConfirmed);
        Assert.Equal("partial", result.Batch.Status);
        WebSearchObservationNormalizer.Normalize(result.Batch);
    }

    [Fact]
    public async Task Collect_UsesIndependentDailyPartitionsAcrossTheRequestedRange()
    {
        var handler = new ScriptedHandler((request, index) => index switch
        {
            0 => SitesResponse("sc-domain:officeimo.com"),
            1 => QueryResponse(),
            2 => QueryResponse(),
            3 => QueryResponse(),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var httpClient = new HttpClient(handler);
        var collector = new GoogleSearchConsoleCollector(httpClient, new FakeTokenProvider());
        var options = CreateOptions();
        options.ThroughDate = new DateOnly(2026, 8, 2);

        var result = await collector.CollectAsync(options);

        Assert.True(result.Success);
        Assert.Equal(2, result.CompletedDateCount);
        Assert.Equal(3, result.RequestCount);
        Assert.Equal(
            ["2026-08-01", "2026-08-02"],
            handler.Requests.Skip(2).Select(request =>
            {
                using var document = JsonDocument.Parse(request.Body!);
                return document.RootElement.GetProperty("startDate").GetString();
            }));
    }

    [Fact]
    public async Task Collect_DoesNotConfirmZeroDataForAnIncompleteReportingDate()
    {
        var handler = new ScriptedHandler((request, index) => index switch
        {
            0 => SitesResponse("sc-domain:officeimo.com"),
            1 => JsonResponse(HttpStatusCode.OK, """{"rows":[],"metadata":{"first_incomplete_date":"2026-08-01"}}"""),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var httpClient = new HttpClient(handler);
        var collector = new GoogleSearchConsoleCollector(httpClient, new FakeTokenProvider());

        var result = await collector.CollectAsync(CreateOptions());
        var normalized = WebSearchObservationNormalizer.Normalize(result.Batch);

        Assert.False(result.Success);
        Assert.Equal("data-not-final", result.ErrorCode);
        Assert.False(normalized.ZeroDataConfirmed);
        Assert.Empty(normalized.Observations);
        Assert.Equal("partial", normalized.Status);
        Assert.Equal(new DateOnly(2026, 8, 1), normalized.CollectionCoverage!.FailedDate);
        Assert.Equal("data-not-final", normalized.CollectionCoverage.FailureCategory);
        Assert.Empty(normalized.CollectionCoverage.CompletedDates);
        Assert.Equal(1, result.RequestCount);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public void Normalize_RejectsUnconfirmedOrContradictoryZeroDataState()
    {
        var unconfirmed = CreateEmptyBatch(zeroDataConfirmed: false, status: "complete");
        var partialConfirmed = CreateEmptyBatch(zeroDataConfirmed: true, status: "partial");
        var nonEmptyConfirmed = CreateEmptyBatch(zeroDataConfirmed: true, status: "complete");
        nonEmptyConfirmed.Observations =
        [
            new WebSearchObservation
            {
                Date = new DateOnly(2026, 8, 1),
                Page = "https://officeimo.com/",
                Clicks = 0,
                Impressions = 0
            }
        ];

        Assert.Throws<ArgumentException>(() => WebSearchObservationNormalizer.Normalize(unconfirmed));
        Assert.Throws<ArgumentException>(() => WebSearchObservationNormalizer.Normalize(partialConfirmed));
        Assert.Throws<ArgumentException>(() => WebSearchObservationNormalizer.Normalize(nonEmptyConfirmed));
    }

    [Fact]
    public void Normalize_RejectsExplicitNullObservations()
    {
        var document = JsonNode.Parse(JsonSerializer.Serialize(CreateEmptyBatch(zeroDataConfirmed: true, status: "complete")))!.AsObject();
        document["observations"] = null;
        var batch = JsonSerializer.Deserialize<WebSearchObservationBatch>(document.ToJsonString(), WebCliJson.Options)!;

        var exception = Assert.Throws<ArgumentException>(() => WebSearchObservationNormalizer.Normalize(batch));

        Assert.Contains("must be an array", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("collectionCoverage")]
    [InlineData("zeroDataConfirmed")]
    public void Normalize_RejectsExplicitVersionTwoMembersInVersionOneJson(string propertyName)
    {
        var legacy = new WebSearchObservationBatch
        {
            SchemaVersion = 1,
            Provider = "google-search-console",
            SiteId = "officeimo",
            CollectedAtUtc = CompletionTime,
            SourceKind = "fixture",
            Status = "complete",
            Observations =
            [
                new WebSearchObservation
                {
                    Date = new DateOnly(2026, 8, 1),
                    Page = "https://officeimo.com/",
                    Clicks = 1,
                    Impressions = 10
                }
            ]
        };
        var document = JsonNode.Parse(JsonSerializer.Serialize(legacy))!.AsObject();
        if (propertyName == "zeroDataConfirmed")
            document[propertyName] = false;
        else
            document[propertyName] = null;
        var deserialized = JsonSerializer.Deserialize<WebSearchObservationBatch>(document.ToJsonString())!;

        var exception = Assert.Throws<ArgumentException>(() => WebSearchObservationNormalizer.Normalize(deserialized));

        Assert.Contains("version 1", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Normalize_RejectsImpossiblePartialCollectionCoverage()
    {
        var missingCoverage = CreateEmptyBatch(zeroDataConfirmed: false, status: "partial");
        missingCoverage.CollectionCoverage = null;
        var missingFailure = CreateEmptyBatch(zeroDataConfirmed: false, status: "partial");
        var observationAfterFailure = new WebSearchObservationBatch
        {
            Provider = "google-search-console",
            SiteId = "officeimo",
            CollectedAtUtc = CompletionTime,
            SourceKind = "api",
            Status = "partial",
            CollectionCoverage = new WebSearchObservationCollectionCoverage
            {
                FromDate = new DateOnly(2026, 8, 1),
                ThroughDate = new DateOnly(2026, 8, 3),
                SearchType = "web",
                CompletedDates = [new DateOnly(2026, 8, 1)],
                FailedDate = new DateOnly(2026, 8, 2),
                FailureCategory = "quota-exceeded"
            },
            Observations =
            [
                new WebSearchObservation
                {
                    Date = new DateOnly(2026, 8, 3),
                    Page = "https://officeimo.com/unrequested-partition/",
                    SearchType = "web",
                    Clicks = 0,
                    Impressions = 1
                }
            ]
        };

        Assert.Throws<ArgumentException>(() => WebSearchObservationNormalizer.Normalize(missingCoverage));
        Assert.Throws<ArgumentException>(() => WebSearchObservationNormalizer.Normalize(missingFailure));
        Assert.Throws<ArgumentException>(() => WebSearchObservationNormalizer.Normalize(observationAfterFailure));
    }

    [Fact]
    public void Normalize_RejectsExplicitNullCompletedDates()
    {
        var document = JsonNode.Parse(JsonSerializer.Serialize(CreateEmptyBatch(zeroDataConfirmed: true, status: "complete")))!.AsObject();
        document["collectionCoverage"]!.AsObject()["completedDates"] = null;
        var deserialized = JsonSerializer.Deserialize<WebSearchObservationBatch>(document.ToJsonString())!;

        var exception = Assert.Throws<ArgumentException>(() => WebSearchObservationNormalizer.Normalize(deserialized));

        Assert.Contains("completedDates", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Collect_PreservesEarlierPageWhenLaterResponseContainsNullRow()
    {
        var handler = new ScriptedHandler((request, index) => index switch
        {
            0 => SitesResponse("sc-domain:officeimo.com"),
            1 => QueryResponse(),
            2 => QueryResponse(Row("2026-08-01", "https://officeimo.com/", "officeimo", "pol", "DESKTOP", 1, 10, 0.1, 1)),
            3 => QueryResponse("null"),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var httpClient = new HttpClient(handler);
        var collector = new GoogleSearchConsoleCollector(httpClient, new FakeTokenProvider());

        var result = await collector.CollectAsync(CreateOptions(rowLimit: 1));

        Assert.False(result.Success);
        Assert.Equal("provider-response-invalid", result.ErrorCode);
        Assert.Single(result.Batch.Observations);
        Assert.Single(WebSearchObservationNormalizer.Normalize(result.Batch).Observations);
    }

    [Fact]
    public async Task Collect_RejectsPagesOutsideConfiguredSiteBoundary()
    {
        var handler = new ScriptedHandler((request, index) => index switch
        {
            0 => SitesResponse("sc-domain:officeimo.com"),
            1 => QueryResponse(),
            2 => QueryResponse(Row("2026-08-01", "https://officeimo.com/other/", "officeimo", "pol", "DESKTOP", 1, 10, 0.1, 1)),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var httpClient = new HttpClient(handler);
        var collector = new GoogleSearchConsoleCollector(httpClient, new FakeTokenProvider());
        var options = CreateOptions();
        options.SiteBaseUrl = "https://officeimo.com/docs/";

        var result = await collector.CollectAsync(options);

        Assert.False(result.Success);
        Assert.Equal("provider-response-invalid", result.ErrorCode);
        Assert.Empty(result.Batch.Observations);
        WebSearchObservationNormalizer.Normalize(result.Batch);
    }

    [Fact]
    public void SelectedProviderReadiness_DoesNotRequireUnrelatedCredentials()
    {
        const string selectedVariable = "POWERFORGE_TEST_GSC_SELECTED";
        var previous = Environment.GetEnvironmentVariable(selectedVariable);
        Environment.SetEnvironmentVariable(selectedVariable, "credential-present");
        try
        {
            var configuration = CreateConfiguration(WebSearchProviderCapabilities.SearchAnalytics);
            var site = Assert.Single(configuration.Sites);
            var selected = Assert.Single(site.Providers);
            selected.Credential!.EnvironmentVariable = selectedVariable;
            site.Providers =
            [
                selected,
                new WebSearchProviderRegistration
                {
                    Id = "unrelated-cloudflare",
                    Kind = "cloudflare-analytics",
                    Enabled = true,
                    Capabilities = [WebSearchProviderCapabilities.TrafficAnalytics],
                    Credential = new WebSearchCredentialReference
                    {
                        Kind = "cloudflare-api-token",
                        EnvironmentVariable = "POWERFORGE_TEST_UNRELATED_MISSING"
                    },
                    Settings = new Dictionary<string, string?> { ["zoneId"] = new string('a', 32) }
                }
            ];

            var result = WebCliCommandHandlers.InspectProviderAction(
                configuration,
                site,
                selected,
                WebSearchProviderCapabilities.SearchAnalytics,
                useSelectedCredential: true);

            Assert.True(result.Success);
            Assert.NotNull(result.ConfigurationHash);
        }
        finally
        {
            Environment.SetEnvironmentVariable(selectedVariable, previous);
        }
    }

    [Fact]
    public async Task Collect_RejectsMalformedProviderMetricsWithoutLosingRunEvidence()
    {
        var handler = new ScriptedHandler((request, index) => index switch
        {
            0 => SitesResponse("sc-domain:officeimo.com"),
            1 => QueryResponse(),
            2 => JsonResponse(HttpStatusCode.OK, """
                {"rows":[{"keys":["2026-08-01","https://officeimo.com/","query","pol","DESKTOP"],"clicks":1.5,"impressions":10,"ctr":0.15,"position":2}]}
                """),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var httpClient = new HttpClient(handler);
        var collector = new GoogleSearchConsoleCollector(httpClient, new FakeTokenProvider());

        var result = await collector.CollectAsync(CreateOptions());

        Assert.False(result.Success);
        Assert.Equal("provider-response-invalid", result.ErrorCode);
        Assert.Equal("partial", result.Batch.Status);
        Assert.Empty(result.Batch.Observations);
        Assert.False(result.Batch.ZeroDataConfirmed);
        WebSearchObservationNormalizer.Normalize(result.Batch);
    }

    [Fact]
    public async Task Collect_RejectsClicksGreaterThanImpressionsAsPartialEvidence()
    {
        var handler = new ScriptedHandler((request, index) => index switch
        {
            0 => SitesResponse("sc-domain:officeimo.com"),
            1 => QueryResponse(),
            2 => QueryResponse(Row("2026-08-01", "https://officeimo.com/", "officeimo", "pol", "DESKTOP", 11, 10, 1, 2)),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var httpClient = new HttpClient(handler);
        var collector = new GoogleSearchConsoleCollector(httpClient, new FakeTokenProvider());

        var result = await collector.CollectAsync(CreateOptions());

        Assert.False(result.Success);
        Assert.Equal("provider-response-invalid", result.ErrorCode);
        Assert.Equal("partial", result.Batch.Status);
        Assert.Empty(result.Batch.Observations);
        WebSearchObservationNormalizer.Normalize(result.Batch);
    }

    [Fact]
    public void Doctor_ReportsAnalyticsAvailableWithoutClaimingUnimplementedGoogleCapabilities()
    {
        var configuration = CreateConfiguration(
            WebSearchProviderCapabilities.SearchAnalytics,
            WebSearchProviderCapabilities.SearchSitemaps,
            WebSearchProviderCapabilities.SearchUrlInspection);

        var result = WebSearchProviderDoctor.InspectWithCapabilities(
            configuration,
            WebSearchCollectorCatalog.AvailableCapabilities,
            _ => "credential-present");

        Assert.True(result.Success);
        var provider = Assert.Single(result.Providers);
        Assert.False(provider.CollectorAvailable);
        Assert.False(provider.CollectionReady);
        Assert.Equal([WebSearchProviderCapabilities.SearchAnalytics], provider.AvailableCollectorCapabilities);
        Assert.Equal(
            [WebSearchProviderCapabilities.SearchSitemaps, WebSearchProviderCapabilities.SearchUrlInspection],
            provider.MissingCollectorCapabilities);
        Assert.Contains(result.Checks, check =>
            check.Code == "provider.collector-unavailable" &&
            check.Message.Contains(WebSearchProviderCapabilities.SearchSitemaps, StringComparison.Ordinal));
    }

    [Fact]
    public void ServiceAccountFactory_RejectsAnotherGoogleCredentialTypeWithoutEchoingTheCredential()
    {
        const string secretMarker = "do-not-echo-this-refresh-token";
        var reference = new WebSearchCredentialReference
        {
            Kind = "google-service-account-json",
            EnvironmentVariable = "POWERFORGE_TEST_GSC_JSON"
        };
        var json = $$"""{"type":"authorized_user","client_id":"client","client_secret":"secret","refresh_token":"{{secretMarker}}"}""";

        var exception = Assert.ThrowsAny<Exception>(() =>
            GoogleSearchConsoleServiceAccountAccessTokenProvider.Create(reference, _ => json));

        Assert.DoesNotContain(secretMarker, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ServiceAccountFactory_DoesNotEchoAMissingCredentialFilePath()
    {
        var secretPath = Path.Combine(Path.GetTempPath(), "powerforge-secret", Guid.NewGuid().ToString("N"), "credential.json");
        var reference = new WebSearchCredentialReference
        {
            Kind = "google-service-account-file",
            EnvironmentVariable = "POWERFORGE_TEST_GSC_FILE"
        };

        var exception = Assert.Throws<FileNotFoundException>(() =>
            GoogleSearchConsoleServiceAccountAccessTokenProvider.Create(reference, _ => secretPath));

        Assert.DoesNotContain(secretPath, exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(reference.EnvironmentVariable, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ServiceAccountFactory_DoesNotEchoAPathWhenAnExistingCredentialFileCannotBeRead()
    {
        var root = Path.Combine(Path.GetTempPath(), "powerforge-gsc-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var secretPath = Path.Combine(root, "credential.json");
        File.WriteAllText(secretPath, "{}");
        try
        {
            using var lockStream = new FileStream(secretPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var reference = new WebSearchCredentialReference
            {
                Kind = "google-service-account-file",
                EnvironmentVariable = "POWERFORGE_TEST_GSC_LOCKED_FILE"
            };

            var exception = Assert.Throws<InvalidOperationException>(() =>
                GoogleSearchConsoleServiceAccountAccessTokenProvider.Create(reference, _ => secretPath));

            Assert.DoesNotContain(secretPath, exception.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal("Google Search Console service-account credential is invalid.", exception.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Cli_ObserveCollect_FailsClosedBeforeCreatingStorageWhenCredentialIsUnavailable()
    {
        var root = Path.Combine(Path.GetTempPath(), "powerforge-gsc-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var configPath = Path.Combine(root, "providers.json");
            var databasePath = Path.Combine(root, "search.db");
            File.WriteAllText(configPath, JsonSerializer.Serialize(CreateConfiguration(WebSearchProviderCapabilities.SearchAnalytics)));

            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "observe",
                [
                    "collect", "--config", configPath, "--database", databasePath,
                    "--site", "officeimo", "--provider", "google-search-console",
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

    [Fact]
    public void Cli_ObserveCollect_RejectsUnsupportedOutputBeforeReadingConfiguration()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"powerforge-gsc-{Guid.NewGuid():N}.db");

        var exitCode = WebCliCommandHandlers.HandleSubCommand(
            "observe",
            [
                "collect", "--config", "missing.json", "--database", databasePath,
                "--site", "officeimo", "--provider", "google-search-console",
                "--from", "2026-08-01", "--to", "2026-08-01", "--output", "yaml"
            ],
            outputJson: false,
            logger: new WebConsoleLogger(),
            outputSchemaVersion: 1);

        Assert.Equal(2, exitCode);
        Assert.False(File.Exists(databasePath));
    }

    private static GoogleSearchConsoleCollectionOptions CreateOptions(int rowLimit = 25_000) => new()
    {
        ProviderId = "google-search-console",
        SiteId = "officeimo",
        SiteBaseUrl = "https://officeimo.com/",
        Property = "sc-domain:officeimo.com",
        FromDate = new DateOnly(2026, 8, 1),
        ThroughDate = new DateOnly(2026, 8, 1),
        SearchType = "web",
        RowLimit = rowLimit,
        ConfigurationHash = "sha256:configuration"
    };

    private static WebSearchObservationBatch CreateEmptyBatch(bool zeroDataConfirmed, string status) => new()
    {
        Provider = "google-search-console",
        SiteId = "officeimo",
        CollectedAtUtc = CompletionTime,
        SourceKind = "api",
        Status = status,
        CollectionCoverage = new WebSearchObservationCollectionCoverage
        {
            FromDate = new DateOnly(2026, 8, 1),
            ThroughDate = new DateOnly(2026, 8, 1),
            SearchType = "web",
            CompletedDates = [new DateOnly(2026, 8, 1)]
        },
        ZeroDataConfirmed = zeroDataConfirmed,
        Observations = Array.Empty<WebSearchObservation>()
    };

    private static WebSearchProviderConfiguration CreateConfiguration(params string[] capabilities) => new()
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
                        Id = "google-search-console",
                        Kind = "google-search-console",
                        Enabled = true,
                        Capabilities = capabilities,
                        Credential = new WebSearchCredentialReference
                        {
                            Kind = "google-service-account-json",
                            EnvironmentVariable = "POWERFORGE_TEST_GSC_UNAVAILABLE"
                        },
                        Settings = new Dictionary<string, string?>
                        {
                            ["property"] = "sc-domain:officeimo.com"
                        }
                    }
                ]
            }
        ]
    };

    private static int RequestStartRow(RequestSnapshot request)
    {
        using var document = JsonDocument.Parse(request.Body!);
        return document.RootElement.GetProperty("startRow").GetInt32();
    }

    private static string Row(
        string date,
        string page,
        string query,
        string country,
        string device,
        double clicks,
        double impressions,
        double ctr,
        double position) => JsonSerializer.Serialize(new
        {
            keys = new[] { date, page, query, country, device },
            clicks,
            impressions,
            ctr,
            position
        });

    private static string RowWithoutQuery(
        string date,
        string page,
        string country,
        string device,
        double clicks,
        double impressions,
        double ctr,
        double position) => JsonSerializer.Serialize(new
        {
            keys = new[] { date, page, country, device },
            clicks,
            impressions,
            ctr,
            position
        });

    private static HttpResponseMessage SitesResponse(string property) => JsonResponse(
        HttpStatusCode.OK,
        JsonSerializer.Serialize(new
        {
            siteEntry = new[] { new { siteUrl = property, permissionLevel = "siteOwner" } }
        }));

    private static HttpResponseMessage QueryResponse(params string[] rows) => JsonResponse(
        HttpStatusCode.OK,
        $"{{\"rows\":[{string.Join(",", rows)}]}}");

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class FakeTokenProvider : IGoogleSearchConsoleAccessTokenProvider
    {
        internal List<Uri> RequestUris { get; } = new();

        public Task<string> GetAccessTokenAsync(Uri requestUri, CancellationToken cancellationToken = default)
        {
            RequestUris.Add(requestUri);
            return Task.FromResult("test-access-token");
        }
    }

    private sealed class ThrowingTokenProvider(Exception exception) : IGoogleSearchConsoleAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(Uri requestUri, CancellationToken cancellationToken = default) =>
            Task.FromException<string>(exception);
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class ThrowingReadContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            Task.FromException(new IOException("simulated response-body failure"));

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class ScriptedHandler(Func<HttpRequestMessage, int, HttpResponseMessage> responder) : HttpMessageHandler
    {
        internal List<RequestSnapshot> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var snapshot = new RequestSnapshot(
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter,
                body);
            Requests.Add(snapshot);
            return responder(request, Requests.Count - 1);
        }
    }

    private sealed record RequestSnapshot(
        HttpMethod Method,
        Uri Uri,
        string? AuthorizationScheme,
        string? AuthorizationParameter,
        string? Body);
}
