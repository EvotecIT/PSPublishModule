using System.Net;
using System.Text;
using System.Text.Json;
using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class WebBingWebmasterCollectorTests
{
    private static readonly DateTimeOffset CompletionTime = new(2026, 8, 10, 12, 34, 56, TimeSpan.Zero);

    [Fact]
    public async Task Probe_RequiresTheExactVerifiedSiteAndNormalizesHostIdentity()
    {
        var handler = new ScriptedHandler((_, _) => SitesResponse("https://xn--bcher-kva.de./", true));
        using var httpClient = new HttpClient(handler);
        var collector = new BingWebmasterCollector(httpClient, new FakeApiKeyProvider());

        var result = await collector.ProbeAsync("HTTPS://BÜCHER.de:443/");

        Assert.True(result.Success);
        Assert.True(result.Verified);
        Assert.Equal([WebSearchProviderCapabilities.SearchAnalytics], result.AvailableCapabilities);
        var request = Assert.Single(handler.Requests);
        Assert.EndsWith("/GetUserSites?apikey=test-api-key", request.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Probe_RejectsAnUnverifiedOrDifferentSite()
    {
        var handler = new ScriptedHandler((_, _) => SitesResponse("https://example.net/", false));
        using var httpClient = new HttpClient(handler);
        var collector = new BingWebmasterCollector(httpClient, new FakeApiKeyProvider());

        var result = await collector.ProbeAsync("https://officeimo.com/");

        Assert.False(result.Success);
        Assert.Equal("site-not-visible", result.ErrorCode);
    }

    [Fact]
    public async Task Probe_TreatsCustomApiBaseAsDirectoryWithoutTrailingSlash()
    {
        var handler = new ScriptedHandler((_, _) => SitesResponse("https://officeimo.com/", true));
        using var httpClient = new HttpClient(handler);
        var collector = new BingWebmasterCollector(
            httpClient,
            new FakeApiKeyProvider(),
            new Uri("https://proxy.example/api.svc/json"));

        var result = await collector.ProbeAsync("https://officeimo.com/");

        Assert.True(result.Success);
        Assert.Equal("/api.svc/json/GetUserSites", new Uri(Assert.Single(handler.Requests).AbsoluteUri).AbsolutePath);
    }

    [Fact]
    public async Task Probe_AndCollect_ClassifyCredentialFailureWithoutClaimingAnHttpRequest()
    {
        var handler = new ScriptedHandler((_, _) => throw new InvalidOperationException("Transport must not be reached."));
        using var httpClient = new HttpClient(handler);
        var collector = new BingWebmasterCollector(httpClient, new FailingApiKeyProvider());

        var probe = await collector.ProbeAsync("https://officeimo.com/");
        var collection = await collector.CollectAsync(CreateOptions());

        Assert.False(probe.Success);
        Assert.Equal("credential-unavailable", probe.ErrorCode);
        Assert.False(collection.Success);
        Assert.Equal("credential-unavailable", collection.ErrorCode);
        Assert.Equal(0, collection.RequestCount);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Collect_MapsDatedQueryAndPageRowsIntoTheNeutralContract()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => SitesResponse("https://officeimo.com/", true),
            1 => StatsResponse(Stat("powerforge", 3, 30, 4.5)),
            2 => StatsResponse(LegacyPageStat("https://officeimo.com/docs/powerforge", 2, 20, 3.25)),
            3 => TrafficResponse(5, 50),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var httpClient = new HttpClient(handler);
        var collector = new BingWebmasterCollector(
            httpClient,
            new FakeApiKeyProvider(),
            timeProvider: new FixedTimeProvider(CompletionTime));

        var result = await collector.CollectAsync(CreateOptions());
        var normalized = WebSearchObservationNormalizer.Normalize(result.Batch);

        Assert.True(result.Success);
        Assert.Equal(4, result.RequestCount);
        Assert.Equal(1, result.CompletedDateCount);
        Assert.Equal(CompletionTime, normalized.CollectedAtUtc);
        Assert.Equal("complete", normalized.Status);
        Assert.False(normalized.ZeroDataConfirmed);
        Assert.Equal(2, normalized.Observations.Length);
        var query = Assert.Single(normalized.Observations, observation => observation.Query == "powerforge");
        Assert.Null(query.Page);
        Assert.Equal(3, query.Clicks);
        Assert.Equal(30, query.Impressions);
        Assert.Equal(4.5, query.AveragePosition);
        var page = Assert.Single(normalized.Observations, observation => observation.Page is not null);
        Assert.Equal("https://officeimo.com/docs/powerforge", page.Page);
        Assert.Null(page.Query);
        Assert.Equal("web", page.SearchType);
        Assert.Equal(
            ["GetUserSites", "GetQueryStats", "GetPageStats", "GetRankAndTrafficStats"],
            handler.Requests.Select(request => new Uri(request.AbsoluteUri).AbsolutePath.Split('/').Last()));
    }

    [Fact]
    public async Task Collect_AppliesTheProviderOffsetBeforeDerivingTheReportingDate()
    {
        var offsetDate = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(14));
        var encodedDate = $"/Date({offsetDate.ToUnixTimeMilliseconds()}+1400)/";
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => SitesResponse("https://officeimo.com/", true),
            1 => JsonResponse(new { d = new[] { new { Query = "powerforge", Date = encodedDate, Clicks = 1, Impressions = 10, AvgImpressionPosition = 2d } } }),
            2 => StatsResponse(),
            3 => JsonResponse(new { d = new[] { new { Date = encodedDate, Clicks = 1, Impressions = 10 } } }),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var httpClient = new HttpClient(handler);
        var collector = new BingWebmasterCollector(httpClient, new FakeApiKeyProvider());

        var result = await collector.CollectAsync(CreateOptions());

        Assert.True(result.Success);
        Assert.Equal(new DateOnly(2026, 8, 1), Assert.Single(result.Batch.Observations).Date);
    }

    [Fact]
    public async Task Collect_ConfirmsZeroOnlyWhenTrafficTotalsAlsoConfirmZero()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => SitesResponse("https://officeimo.com/", true),
            1 or 2 => StatsResponse(),
            3 => TrafficResponse(0, 0),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var httpClient = new HttpClient(handler);
        var collector = new BingWebmasterCollector(httpClient, new FakeApiKeyProvider());

        var result = await collector.CollectAsync(CreateOptions());
        var normalized = WebSearchObservationNormalizer.Normalize(result.Batch);

        Assert.True(result.Success);
        Assert.True(normalized.ZeroDataConfirmed);
        Assert.Empty(normalized.Observations);
    }

    [Fact]
    public async Task Collect_IsPartialWhenTotalsExistButDimensionRowsAreUnavailable()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => SitesResponse("https://officeimo.com/", true),
            1 or 2 => StatsResponse(),
            3 => TrafficResponse(1, 10),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var httpClient = new HttpClient(handler);
        var collector = new BingWebmasterCollector(httpClient, new FakeApiKeyProvider());

        var result = await collector.CollectAsync(CreateOptions());
        var normalized = WebSearchObservationNormalizer.Normalize(result.Batch);

        Assert.False(result.Success);
        Assert.Equal("dimension-data-unavailable", result.ErrorCode);
        Assert.Equal("partial", normalized.Status);
        Assert.Empty(normalized.Observations);
        Assert.Equal("snapshot", normalized.CollectionCoverage!.Mode);
        Assert.Null(normalized.CollectionCoverage.FailedDate);
    }

    [Fact]
    public async Task Collect_SnapshotCoverageClaimsOnlyDatesPresentInProviderResponses()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => SitesResponse("https://officeimo.com/", true),
            1 => StatsResponse(Stat("powerforge", 1, 10, 2)),
            2 => StatsResponse(),
            3 => TrafficResponse(1, 10),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var httpClient = new HttpClient(handler);
        var collector = new BingWebmasterCollector(httpClient, new FakeApiKeyProvider());
        var options = CreateOptions();
        options.ThroughDate = new DateOnly(2026, 8, 7);

        var result = await collector.CollectAsync(options);
        var normalized = WebSearchObservationNormalizer.Normalize(result.Batch);

        Assert.True(result.Success);
        Assert.Equal("snapshot", normalized.CollectionCoverage!.Mode);
        Assert.Equal([new DateOnly(2026, 8, 1)], normalized.CollectionCoverage.CompletedDates);
        Assert.Equal(1, result.CompletedDateCount);
    }

    [Fact]
    public async Task Collect_PreservesQueryRowsWhenThePageSnapshotFails()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => SitesResponse("https://officeimo.com/", true),
            1 => StatsResponse(Stat("powerforge", 1, 10, 2)),
            2 => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var httpClient = new HttpClient(handler);
        var collector = new BingWebmasterCollector(httpClient, new FakeApiKeyProvider());

        var result = await collector.CollectAsync(CreateOptions());
        var normalized = WebSearchObservationNormalizer.Normalize(result.Batch);

        Assert.False(result.Success);
        Assert.Equal("provider-unavailable", result.ErrorCode);
        Assert.Equal("partial", normalized.Status);
        Assert.Equal("powerforge", Assert.Single(normalized.Observations).Query);
        Assert.Equal([new DateOnly(2026, 8, 1)], normalized.CollectionCoverage!.CompletedDates);
    }

    [Fact]
    public async Task Collect_RejectsDuplicateProviderRowsBeforeBuildingAPartialBatch()
    {
        var duplicate = Stat("powerforge", 1, 10, 2);
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => SitesResponse("https://officeimo.com/", true),
            1 => StatsResponse(duplicate, duplicate),
            _ => throw new InvalidOperationException("A duplicate response must fail before the next provider request.")
        });
        using var httpClient = new HttpClient(handler);
        var collector = new BingWebmasterCollector(httpClient, new FakeApiKeyProvider());

        var result = await collector.CollectAsync(CreateOptions());

        Assert.False(result.Success);
        Assert.Equal("invalid-response", result.ErrorCode);
        Assert.Equal(2, result.RequestCount);
        Assert.Empty(result.Batch.Observations);
        WebSearchObservationNormalizer.Normalize(result.Batch);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"other\":[]}")]
    [InlineData("{\"d\":null}")]
    public async Task Collect_RejectsResponsesWithoutANonNullBingEnvelope(string json)
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => SitesResponse("https://officeimo.com/", true),
            1 => RawJsonResponse(json),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var httpClient = new HttpClient(handler);
        var collector = new BingWebmasterCollector(httpClient, new FakeApiKeyProvider());

        var result = await collector.CollectAsync(CreateOptions());

        Assert.False(result.Success);
        Assert.Equal("invalid-response", result.ErrorCode);
        Assert.Equal("partial", result.Batch.Status);
        Assert.Empty(result.Batch.Observations);
        WebSearchObservationNormalizer.Normalize(result.Batch);
    }

    [Fact]
    public async Task Collect_RejectsMissingRequiredMetricsInsteadOfTreatingThemAsZero()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => SitesResponse("https://officeimo.com/", true),
            1 => JsonResponse(new { d = new[] { new { Query = "powerforge", Date = ProviderDate(new DateOnly(2026, 8, 1)) } } }),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var httpClient = new HttpClient(handler);
        var collector = new BingWebmasterCollector(httpClient, new FakeApiKeyProvider());

        var result = await collector.CollectAsync(CreateOptions());

        Assert.False(result.Success);
        Assert.Equal("invalid-response", result.ErrorCode);
        Assert.False(result.Batch.ZeroDataConfirmed);
        Assert.Empty(result.Batch.Observations);
    }

    [Fact]
    public async Task Collect_MissingTrafficCountsCannotConfirmZeroData()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => SitesResponse("https://officeimo.com/", true),
            1 or 2 => StatsResponse(),
            3 => JsonResponse(new
            {
                d = new[]
                {
                    new { Date = ProviderDate(new DateOnly(2026, 8, 1)) }
                }
            }),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var httpClient = new HttpClient(handler);
        var collector = new BingWebmasterCollector(httpClient, new FakeApiKeyProvider());

        var result = await collector.CollectAsync(CreateOptions());

        Assert.False(result.Success);
        Assert.Equal("invalid-response", result.ErrorCode);
        Assert.False(result.Batch.ZeroDataConfirmed);
        Assert.Empty(result.Batch.Observations);
    }

    [Fact]
    public async Task Collect_NeverReturnsTheApiKeyInProviderErrors()
    {
        var handler = new ScriptedHandler((_, _) => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                "{\"ErrorCode\":3,\"Message\":\"InvalidApiKey test-api-key\"}",
                Encoding.UTF8,
                "application/json")
        });
        using var httpClient = new HttpClient(handler);
        var collector = new BingWebmasterCollector(httpClient, new FakeApiKeyProvider());

        var result = await collector.CollectAsync(CreateOptions());

        Assert.False(result.Success);
        Assert.Equal("authentication-or-request-rejected", result.ErrorCode);
        Assert.DoesNotContain("test-api-key", result.ErrorMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("test-api-key", result.Probe.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnvironmentApiKeyProvider_ResolvesOnlyTheReferencedSecret()
    {
        var provider = BingWebmasterEnvironmentApiKeyProvider.Create(
            new WebSearchCredentialReference
            {
                Kind = "bing-api-key",
                EnvironmentVariable = "POWERFORGE_TEST_BING_API_KEY"
            },
            name => name == "POWERFORGE_TEST_BING_API_KEY" ? "  key-value  " : null);

        Assert.Equal("key-value", await provider.GetApiKeyAsync());
    }

    [Fact]
    public void CollectorCatalog_ClaimsAnalyticsButNotUnimplementedSitemaps()
    {
        var capabilities = WebSearchCollectorCatalog.AvailableCapabilities[BingWebmasterCollector.ProviderKind];

        Assert.Contains(WebSearchProviderCapabilities.SearchAnalytics, capabilities);
        Assert.DoesNotContain(WebSearchProviderCapabilities.SearchSitemaps, capabilities);
    }

    [Fact]
    public async Task Collect_RejectsABroaderPropertyThanTheOwningFleetSite()
    {
        var options = CreateOptions();
        options.SiteBaseUrl = "https://officeimo.com/docs/";
        using var httpClient = new HttpClient(
            new ScriptedHandler((_, _) => throw new InvalidOperationException("HTTP must not be reached.")));
        var collector = new BingWebmasterCollector(httpClient, new FakeApiKeyProvider());

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => collector.CollectAsync(options));

        Assert.Contains("match", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CsvExport_RejectsPagesOutsideTheOwningFleetSite()
    {
        const string csv = "Date,Page,Clicks,Impressions\n2026-08-01,https://officeimo.com/other/,1,10";
        var options = CreateCsvOptions();
        options.SiteBaseUrl = "https://officeimo.com/docs/";
        options.PropertySiteUrl = "https://officeimo.com/docs/";

        var exception = Assert.Throws<FormatException>(() => BingWebmasterCsvExportParser.Parse(csv, options));

        Assert.Contains("outside", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CsvExport_RejectsQueryOnlyRowsWithoutAnExactVerifiedPropertyBoundary()
    {
        const string csv = "Date,Query,Clicks,Impressions\n2026-08-01,officeimo,1,10";
        var options = CreateCsvOptions();
        options.PropertySiteUrl = "https://officeimo.com/";
        options.SiteBaseUrl = "https://officeimo.com/docs/";

        var exception = Assert.Throws<FormatException>(() => BingWebmasterCsvExportParser.Parse(csv, options));

        Assert.Contains("cannot prove", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CsvExport_ParsesQuotedPageAndQueryRowsWithInvariantMetrics()
    {
        const string csv = "\uFEFFDate,Page,Keyword,Clicks,Impressions,Avg. CTR,Avg. position\r\n" +
                           "2026-08-01,\"https://officeimo.com/docs/a,b\",\"powerforge \"\"web\"\"\",3,30,10%,4.5\r\n";

        var batch = BingWebmasterCsvExportParser.Parse(csv, CreateCsvOptions());

        var observation = Assert.Single(batch.Observations);
        Assert.Equal("https://officeimo.com/docs/a,b", observation.Page);
        Assert.Equal("powerforge \"web\"", observation.Query);
        Assert.Equal(0.1, observation.ClickThroughRate);
        Assert.Equal(4.5, observation.AveragePosition);
        Assert.Equal("csv-import", batch.SourceKind);
        Assert.Equal(CompletionTime, batch.CollectedAtUtc);
    }

    [Fact]
    public void CsvExport_SupportsSemicolonDelimitedPortalFiles()
    {
        const string csv = "Day;URL;Clicks;Impressions;CTR;Position\n" +
                           "2026-08-01;https://officeimo.com/;1;10;0.1;2\n";

        var batch = BingWebmasterCsvExportParser.Parse(csv, CreateCsvOptions());

        Assert.Single(batch.Observations);
        Assert.Equal(1, batch.Observations[0].Clicks);
    }

    [Fact]
    public void CsvExport_RejectsAggregateOnlyRowsInsteadOfInventingDimensions()
    {
        const string csv = "Date,Clicks,Impressions,CTR\n2026-08-01,1,10,10%\n";

        var exception = Assert.Throws<FormatException>(() =>
            BingWebmasterCsvExportParser.Parse(csv, CreateCsvOptions()));

        Assert.Contains("aggregate-only", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CsvExport_RejectsRowsOutsideDeclaredCoverage()
    {
        const string csv = "Date,Page,Clicks,Impressions\n2026-07-31,https://officeimo.com/,1,10\n";

        Assert.Throws<FormatException>(() => BingWebmasterCsvExportParser.Parse(csv, CreateCsvOptions()));
    }

    [Theory]
    [InlineData("discover")]
    [InlineData("webb")]
    [InlineData("WEB")]
    public void CsvExport_RejectsUnsupportedSearchTypes(string searchType)
    {
        const string csv = "Date,Page,Clicks,Impressions\n2026-08-01,https://officeimo.com/,1,10\n";
        var options = CreateCsvOptions();
        options.SearchType = searchType;

        var exception = Assert.Throws<ArgumentException>(() => BingWebmasterCsvExportParser.Parse(csv, options));

        Assert.Contains("only the 'web'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CsvExport_HeaderOnlyFileCannotFabricateZeroData()
    {
        const string csv = "Date,Page,Clicks,Impressions\n";

        var exception = Assert.Throws<FormatException>(() =>
            BingWebmasterCsvExportParser.Parse(csv, CreateCsvOptions()));

        Assert.Contains("cannot prove zero", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CsvExport_RejectsDuplicateSemanticHeaders()
    {
        const string csv = "Date,Page,URL,Clicks,Impressions\n2026-08-01,https://officeimo.com/,,1,10\n";

        Assert.Throws<FormatException>(() => BingWebmasterCsvExportParser.Parse(csv, CreateCsvOptions()));
    }

    [Fact]
    public void CsvExport_SnapshotCoverageClaimsOnlyDatesPresentInRows()
    {
        const string csv = "Date,Page,Clicks,Impressions\n2026-08-01,https://officeimo.com/,1,10\n";
        var options = CreateCsvOptions();
        options.ThroughDate = new DateOnly(2026, 8, 7);

        var batch = BingWebmasterCsvExportParser.Parse(csv, options);

        Assert.Equal("snapshot", batch.CollectionCoverage!.Mode);
        Assert.Equal([new DateOnly(2026, 8, 1)], batch.CollectionCoverage.CompletedDates);
    }

    [Fact]
    public async Task Cli_ImportBing_ValidatesConfigurationAndPersistsTheExport()
    {
        var root = Path.Combine(Path.GetTempPath(), "powerforge-bing-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var configPath = Path.Combine(root, "providers.json");
            var inputPath = Path.Combine(root, "bing.csv");
            var databasePath = Path.Combine(root, "search.db");
            var configuration = CreateConfiguration(BingWebmasterCollector.ProviderKind);
            configuration.Sites[0].Providers =
            [
                .. configuration.Sites[0].Providers,
                new WebSearchProviderRegistration
                {
                    Id = "google-search-console",
                    Kind = GoogleSearchConsoleCollector.ProviderKind,
                    Enabled = true,
                    Capabilities = [WebSearchProviderCapabilities.SearchAnalytics],
                    Credential = new WebSearchCredentialReference
                    {
                        Kind = "google-service-account-json",
                        EnvironmentVariable = "POWERFORGE_TEST_GSC_UNRELATED_UNAVAILABLE"
                    },
                    Settings = new Dictionary<string, string?> { ["property"] = "sc-domain:officeimo.com" }
                }
            ];
            File.WriteAllText(configPath, JsonSerializer.Serialize(configuration));
            File.WriteAllText(inputPath, "Date,Page,Clicks,Impressions,CTR,Position\n2026-08-01,https://officeimo.com/,1,10,10%,2\n");

            var commandArgs = new[]
            {
                "import-bing", "--config", configPath, "--input", inputPath, "--database", databasePath,
                "--site", "officeimo", "--provider", "bing-webmaster",
                "--from", "2026-08-01", "--to", "2026-08-01",
                "--collected-at", "2026-08-10T12:34:56Z", "--output", "json"
            };
            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "observe",
                commandArgs,
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: 1);
            File.SetLastWriteTimeUtc(inputPath, DateTime.UtcNow.AddHours(1));
            var repeatExitCode = WebCliCommandHandlers.HandleSubCommand(
                "observe",
                commandArgs,
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: 1);

            Assert.Equal(0, exitCode);
            Assert.Equal(0, repeatExitCode);
            Assert.True(File.Exists(databasePath));
            var stored = await new SqliteWebSearchObservationStore(databasePath).QueryAsync(new WebSearchObservationQuery
            {
                SiteId = "officeimo",
                Provider = "bing-webmaster"
            });
            Assert.Single(stored);
            Assert.Equal("https://officeimo.com/", stored[0].Page);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Cli_ImportBing_RequiresExplicitCollectedAtBeforeCreatingStorage()
    {
        var root = Path.Combine(Path.GetTempPath(), "powerforge-bing-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var configPath = Path.Combine(root, "providers.json");
            var inputPath = Path.Combine(root, "bing.csv");
            var databasePath = Path.Combine(root, "search.db");
            File.WriteAllText(configPath, JsonSerializer.Serialize(CreateConfiguration(BingWebmasterCsvExportParser.ProviderKind)));
            File.WriteAllText(inputPath, "Date,Page,Clicks,Impressions\n2026-08-01,https://officeimo.com/,1,10\n");

            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "observe",
                [
                    "import-bing", "--config", configPath, "--input", inputPath, "--database", databasePath,
                    "--site", "officeimo", "--provider", "bing-webmaster",
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
    public void Cli_ImportBing_RejectsMalformedExportBeforeCreatingStorage()
    {
        var root = Path.Combine(Path.GetTempPath(), "powerforge-bing-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var configPath = Path.Combine(root, "providers.json");
            var inputPath = Path.Combine(root, "bing.csv");
            var databasePath = Path.Combine(root, "search.db");
            File.WriteAllText(configPath, JsonSerializer.Serialize(CreateConfiguration(BingWebmasterCsvExportParser.ProviderKind)));
            File.WriteAllText(inputPath, "Date,Clicks,Impressions\n2026-08-01,1,10\n");

            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "observe",
                [
                    "import-bing", "--config", configPath, "--input", inputPath, "--database", databasePath,
                    "--site", "officeimo", "--provider", "bing-webmaster",
                    "--from", "2026-08-01", "--to", "2026-08-01",
                    "--collected-at", "2026-08-10T12:34:56Z", "--output", "json"
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
    public void Cli_CollectBing_FailsClosedWhenCredentialIsUnavailable()
    {
        var root = Path.Combine(Path.GetTempPath(), "powerforge-bing-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var configPath = Path.Combine(root, "providers.json");
            var databasePath = Path.Combine(root, "search.db");
            File.WriteAllText(configPath, JsonSerializer.Serialize(CreateConfiguration(BingWebmasterCollector.ProviderKind)));

            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "observe",
                [
                    "collect", "--config", configPath, "--database", databasePath,
                    "--site", "officeimo", "--provider", "bing-webmaster",
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

    private static BingWebmasterCollectionOptions CreateOptions() => new()
    {
        ProviderId = "bing-webmaster",
        SiteId = "officeimo",
        SiteUrl = "https://officeimo.com/",
        SiteBaseUrl = "https://officeimo.com/",
        FromDate = new DateOnly(2026, 8, 1),
        ThroughDate = new DateOnly(2026, 8, 1),
        SearchType = "web",
        ConfigurationHash = "sha256:configuration"
    };

    private static BingWebmasterCsvExportOptions CreateCsvOptions() => new()
    {
        ProviderId = "bing-webmaster",
        SiteId = "officeimo",
        SiteBaseUrl = "https://officeimo.com/",
        PropertySiteUrl = "https://officeimo.com/",
        FromDate = new DateOnly(2026, 8, 1),
        ThroughDate = new DateOnly(2026, 8, 1),
        SearchType = "web",
        CollectedAtUtc = CompletionTime,
        ConfigurationHash = "sha256:configuration",
        EvidenceReference = "bing-export.csv"
    };

    private static WebSearchProviderConfiguration CreateConfiguration(string kind) => new()
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
                        Id = "bing-webmaster",
                        Kind = kind,
                        Enabled = true,
                        Capabilities = [WebSearchProviderCapabilities.SearchAnalytics],
                        Credential = kind == BingWebmasterCollector.ProviderKind
                            ? new WebSearchCredentialReference
                            {
                                Kind = "bing-api-key",
                                EnvironmentVariable = "POWERFORGE_TEST_BING_API_KEY_UNAVAILABLE"
                            }
                            : null,
                        Settings = kind == BingWebmasterCollector.ProviderKind
                            ? new Dictionary<string, string?> { ["siteUrl"] = "https://officeimo.com/" }
                            : new Dictionary<string, string?>()
                    }
                ]
            }
        ]
    };

    private static HttpResponseMessage SitesResponse(string url, bool verified) => JsonResponse(new
    {
        d = new[] { new { Url = url, IsVerified = verified } }
    });

    private static HttpResponseMessage StatsResponse(params object[] rows) => JsonResponse(new { d = rows });

    private static object Stat(string dimension, long clicks, long impressions, double position) => new
    {
        Query = dimension,
        Date = ProviderDate(new DateOnly(2026, 8, 1)),
        Clicks = clicks,
        Impressions = impressions,
        AvgImpressionPosition = position,
        AvgClickPosition = position
    };

    private static object PageStat(string page, long clicks, long impressions, double position) => new
    {
        Page = page,
        Date = ProviderDate(new DateOnly(2026, 8, 1)),
        Clicks = clicks,
        Impressions = impressions,
        AvgImpressionPosition = position,
        AvgClickPosition = position
    };

    private static object LegacyPageStat(string page, long clicks, long impressions, double position) => new
    {
        Query = page,
        Date = ProviderDate(new DateOnly(2026, 8, 1)),
        Clicks = clicks,
        Impressions = impressions,
        AvgImpressionPosition = position,
        AvgClickPosition = position
    };

    private static HttpResponseMessage TrafficResponse(long clicks, long impressions) => JsonResponse(new
    {
        d = new[]
        {
            new
            {
                Date = ProviderDate(new DateOnly(2026, 8, 1)),
                Clicks = clicks,
                Impressions = impressions
            }
        }
    });

    private static string ProviderDate(DateOnly date)
    {
        var timestamp = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeMilliseconds();
        return $"/Date({timestamp}+0000)/";
    }

    private static HttpResponseMessage JsonResponse(object value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage RawJsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class FakeApiKeyProvider : IBingWebmasterApiKeyProvider
    {
        public Task<string> GetApiKeyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult("test-api-key");
    }

    private sealed class FailingApiKeyProvider : IBingWebmasterApiKeyProvider
    {
        public Task<string> GetApiKeyAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Credential is unavailable.");
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class ScriptedHandler(Func<HttpRequestMessage, int, HttpResponseMessage> responder) : HttpMessageHandler
    {
        internal List<RequestSnapshot> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RequestSnapshot(request.Method, request.RequestUri!.AbsoluteUri));
            return Task.FromResult(responder(request, Requests.Count - 1));
        }
    }

    private sealed record RequestSnapshot(HttpMethod Method, string AbsoluteUri);
}
