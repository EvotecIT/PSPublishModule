using PowerForge.Web;

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
}
