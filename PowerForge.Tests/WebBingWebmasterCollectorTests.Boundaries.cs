using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed partial class WebBingWebmasterCollectorTests
{
    [Fact]
    public async Task Collect_SendsTheVerifiedNormalizedSiteUrlToStatisticsEndpoints()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => SitesResponse("https://officeimo.com/", true),
            1 or 2 => StatsResponse(),
            3 => TrafficResponse(0, 0),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var httpClient = new HttpClient(handler);
        var options = CreateOptions();
        options.SiteUrl = "  HTTPS://OFFICEIMO.COM:443/  ";

        var result = await new BingWebmasterCollector(httpClient, new FakeApiKeyProvider()).CollectAsync(options);

        Assert.True(result.Success);
        Assert.Equal("https://officeimo.com/", result.Probe.SiteUrl);
        Assert.All(handler.Requests.Skip(1), request =>
        {
            var query = Uri.UnescapeDataString(new Uri(request.AbsoluteUri).Query);
            Assert.Contains("siteUrl=https://officeimo.com/", query, StringComparison.Ordinal);
            Assert.DoesNotContain("%20", request.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task Collect_RejectsDuplicateDimensionsAfterCanonicalization()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => SitesResponse("https://officeimo.com/", true),
            1 => StatsResponse(Stat("powerforge", 1, 10, 2), Stat(" powerforge ", 1, 10, 2)),
            _ => throw new InvalidOperationException("Canonical duplicate rows must fail before the page request.")
        });
        using var httpClient = new HttpClient(handler);

        var result = await new BingWebmasterCollector(httpClient, new FakeApiKeyProvider()).CollectAsync(CreateOptions());

        Assert.False(result.Success);
        Assert.Equal("invalid-response", result.ErrorCode);
        Assert.Empty(result.Batch.Observations);
        WebSearchObservationNormalizer.Normalize(result.Batch);
    }

    [Theory]
    [InlineData("1,2")]
    [InlineData("12,34")]
    [InlineData("1,,2")]
    [InlineData(",123")]
    public void CsvExport_RejectsMalformedInvariantThousandsGrouping(string clicks)
    {
        var csv = $"Date,Query,Clicks,Impressions\n2026-08-01,powerforge,\"{clicks}\",2000";

        var exception = Assert.Throws<FormatException>(() => BingWebmasterCsvExportParser.Parse(csv, CreateCsvOptions()));

        Assert.Contains("invalid clicks", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CsvExport_AcceptsCorrectInvariantThousandsGrouping()
    {
        const string csv = "Date,Query,Clicks,Impressions\n2026-08-01,powerforge,\"1,234\",\"2,000\"";

        var batch = BingWebmasterCsvExportParser.Parse(csv, CreateCsvOptions());

        var observation = Assert.Single(batch.Observations);
        Assert.Equal(1234, observation.Clicks);
        Assert.Equal(2000, observation.Impressions);
    }

    [Fact]
    public void CsvExport_RejectsNonzeroCtrWithZeroImpressions()
    {
        const string csv = "Date,Query,Clicks,Impressions,CTR\n2026-08-01,powerforge,0,0,50%";

        var exception = Assert.Throws<FormatException>(() => BingWebmasterCsvExportParser.Parse(csv, CreateCsvOptions()));

        Assert.Contains("nonzero CTR with zero impressions", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CsvExport_DimensionScopedCoverageDoesNotSupersedeAnotherExportShape()
    {
        var root = Path.Combine(Path.GetTempPath(), "powerforge-bing-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new SqliteWebSearchObservationStore(Path.Combine(root, "search.db"));
            var queryOptions = CreateCsvOptions();
            queryOptions.CollectedAtUtc = CompletionTime.AddMinutes(-1);
            var queryBatch = BingWebmasterCsvExportParser.Parse(
                "Date,Query,Clicks,Impressions\n2026-08-01,powerforge,1,10", queryOptions);
            var pageOptions = CreateCsvOptions();
            pageOptions.CollectedAtUtc = CompletionTime;
            var pageBatch = BingWebmasterCsvExportParser.Parse(
                "Date,Page,Clicks,Impressions\n2026-08-01,https://officeimo.com/,2,20", pageOptions);

            await store.ImportAsync(queryBatch);
            await store.ImportAsync(pageBatch);
            var observations = await store.QueryAsync(new WebSearchObservationQuery
            {
                SiteId = "officeimo",
                Provider = "bing-webmaster",
                FromDate = new DateOnly(2026, 8, 1),
                ThroughDate = new DateOnly(2026, 8, 1)
            });

            Assert.Equal(["page"], pageBatch.CollectionCoverage!.DimensionScopes!);
            Assert.Equal(["query"], queryBatch.CollectionCoverage!.DimensionScopes!);
            Assert.Equal(2, observations.Count);
            Assert.Contains(observations, value => value.Page == "https://officeimo.com/");
            Assert.Contains(observations, value => value.Query == "powerforge");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
