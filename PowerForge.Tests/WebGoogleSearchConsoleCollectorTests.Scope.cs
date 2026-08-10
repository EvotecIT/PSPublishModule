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
            AssertPageScopeFilter(document.RootElement, "^https://officeimo\\.com/docs(?:/|$)");
        });
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
