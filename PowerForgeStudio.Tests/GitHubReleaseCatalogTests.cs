using System.Net;
using System.Net.Http;
using System.Text;
using PowerForgeStudio.Orchestrator.Hub;

namespace PowerForgeStudio.Tests;

public sealed class GitHubReleaseCatalogTests
{
    [Fact]
    public async Task RecentReleasesCarryAssetCountsAndBoundedCoverage()
    {
        var requests = 0;
        using var client = new HttpClient(new StubHttpMessageHandler(request => {
            requests++;
            Assert.Equal("/repos/EvotecIT/PowerForge/releases?per_page=10&page=1", request.RequestUri!.PathAndQuery);
            var response = Json("""
                [
                  {
                    "id": 11,
                    "tag_name": "v1.2.3",
                    "name": "PowerForge 1.2.3",
                    "html_url": "https://github.com/EvotecIT/PowerForge/releases/tag/v1.2.3",
                    "published_at": "2026-09-21T11:30:00Z",
                    "draft": false,
                    "prerelease": false,
                    "assets": [
                      { "name": "app.zip", "size": 1024, "download_count": 25 },
                      { "name": "symbols.zip", "size": 512, "download_count": 7 }
                    ]
                  },
                  { "id": 10, "tag_name": "v1.2.2", "draft": false, "prerelease": true, "assets": [] }
                ]
                """);
            response.Headers.TryAddWithoutValidation("Link", "<https://api.github.com/repos/EvotecIT/PowerForge/releases?per_page=10&page=2>; rel=\"next\"");
            return response;
        })) { BaseAddress = new Uri("https://api.github.com") };
        using var service = new GitHubProjectService(client);

        var page = await service.FetchRecentReleasesAsync("EvotecIT/PowerForge");

        Assert.Equal(1, requests);
        Assert.True(page.HasMore);
        Assert.Equal(2, page.Count);
        Assert.Equal(32, page[0].AssetDownloads);
        Assert.Equal("2 asset(s) · 32 asset download(s)", page[0].AssetSummary);
        Assert.Equal("Prerelease", page[1].StateDisplay);
        Assert.Equal(0, page[1].AssetDownloads);
    }

    [Fact]
    public async Task MissingAssetCountIsNotPresentedAsZero()
    {
        using var client = new HttpClient(new StubHttpMessageHandler(_ => Json("""
            [{"id":1,"tag_name":"v1","draft":false,"assets":[{"name":"app.zip","size":15}]}]
            """))) { BaseAddress = new Uri("https://api.github.com") };
        using var service = new GitHubProjectService(client);

        var release = Assert.Single(await service.FetchRecentReleasesAsync("EvotecIT/PowerForge"));

        Assert.Null(release.AssetDownloads);
        Assert.Equal("Not reported", Assert.Single(release.Assets).DownloadsDisplay);
    }

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) {
        Content = new StringContent(value, Encoding.UTF8, "application/json")
    };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response(request));
    }
}
