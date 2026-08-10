using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed partial class WebGoogleSearchConsoleCollectorTests
{
    [Fact]
    public async Task Collect_ScopesABroadPropertyToTheConfiguredFleetSubpath()
    {
        var handler = new ScriptedHandler((request, index) => index switch
        {
            0 => SitesResponse("sc-domain:officeimo.com"),
            1 => QueryResponse(),
            2 => QueryResponse(Row("2026-08-01", "https://officeimo.com/docs/a", "alpha", "usa", "DESKTOP", 1, 10, 0.1, 2)),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var httpClient = new HttpClient(handler);
        var options = CreateOptions();
        options.SiteBaseUrl = "https://officeimo.com/docs/";

        var result = await new GoogleSearchConsoleCollector(httpClient, new FakeTokenProvider()).CollectAsync(options);

        Assert.True(result.Success);
        Assert.All(handler.Requests.Skip(1), request =>
        {
            using var document = JsonDocument.Parse(request.Body!);
            AssertPageScopeFilter(document.RootElement, "^https://officeimo\\.com/docs(?:/|\\?|$)");
        });
    }

    [Fact]
    public async Task Collect_IncludesTheExactFleetPathWithAQueryString()
    {
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => SitesResponse("sc-domain:officeimo.com"),
            1 => QueryResponse(),
            2 => QueryResponse(Row("2026-08-01", "https://officeimo.com/docs?version=2", "docs", "usa", "DESKTOP", 1, 2, 0.5, 1)),
            _ => throw new InvalidOperationException("Unexpected request.")
        });
        using var httpClient = new HttpClient(handler);
        var options = CreateOptions();
        options.SiteBaseUrl = "https://officeimo.com/docs/";

        var result = await new GoogleSearchConsoleCollector(httpClient, new FakeTokenProvider()).CollectAsync(options);

        Assert.True(result.Success);
        Assert.Equal("https://officeimo.com/docs?version=2", Assert.Single(result.Batch.Observations).Page);
    }

    [Fact]
    public async Task Collect_StopsAtTheDocumentedDailyRowCap()
    {
        var firstPage = Enumerable.Range(0, GoogleSearchConsoleCollector.MaximumRowLimit)
            .Select(index => Row("2026-08-01", "https://officeimo.com/", $"query-{index}", "usa", "DESKTOP", 1, 2, 0.5, 1))
            .ToArray();
        var secondPage = Enumerable.Range(GoogleSearchConsoleCollector.MaximumRowLimit, GoogleSearchConsoleCollector.MaximumRowLimit)
            .Select(index => Row("2026-08-01", "https://officeimo.com/", $"query-{index}", "usa", "DESKTOP", 1, 2, 0.5, 1))
            .ToArray();
        var handler = new ScriptedHandler((_, index) => index switch
        {
            0 => SitesResponse("sc-domain:officeimo.com"),
            1 => QueryResponse(),
            2 => QueryResponse(firstPage),
            3 => QueryResponse(secondPage),
            _ => throw new InvalidOperationException("The collector requested rows beyond Google's daily cap.")
        });
        using var httpClient = new HttpClient(handler);

        var result = await new GoogleSearchConsoleCollector(httpClient, new FakeTokenProvider()).CollectAsync(CreateOptions());

        Assert.True(result.Success);
        Assert.Equal(GoogleSearchConsoleCollector.MaximumRowsPerDate, result.Batch.Observations.Length);
        Assert.Equal(3, result.RequestCount);
    }

    [Fact]
    public async Task Collect_RejectsAnUnboundedMultiDayBatchBeforeProviderAccess()
    {
        var handler = new ScriptedHandler((_, _) => throw new InvalidOperationException("Provider access was not expected."));
        using var httpClient = new HttpClient(handler);
        var options = CreateOptions();
        options.ThroughDate = options.FromDate.AddDays(GoogleSearchConsoleCollector.MaximumCollectionDateCount);

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => new GoogleSearchConsoleCollector(httpClient, new FakeTokenProvider()).CollectAsync(options));

        Assert.Contains("daily partitions", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    private static void AssertPageScopeFilter(JsonElement request, string expectedExpression)
    {
        var group = Assert.Single(request.GetProperty("dimensionFilterGroups").EnumerateArray());
        Assert.Equal("and", group.GetProperty("groupType").GetString());
        var filter = Assert.Single(group.GetProperty("filters").EnumerateArray());
        Assert.Equal("page", filter.GetProperty("dimension").GetString());
        Assert.Equal("includingRegex", filter.GetProperty("operator").GetString());
        Assert.Equal(expectedExpression, filter.GetProperty("expression").GetString());
    }
}
