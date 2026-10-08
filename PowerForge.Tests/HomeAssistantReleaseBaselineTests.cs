using System.Net;
using System.Text;

namespace PowerForge.Tests;

public sealed class HomeAssistantReleaseBaselineTests {
    [Fact]
    public void GetLatestRelease_PreservesGitHubsStableBaselineWithoutListingPrereleases() {
        using var http = CreateHttpClient(uri => uri.PathAndQuery switch {
            "/repos/EvotecIT/example/releases/latest" => Json(HttpStatusCode.OK, """
                { "tag_name": "v0.1.5", "draft": false, "prerelease": false }
                """),
            _ => throw new InvalidOperationException($"Unexpected request: {uri.PathAndQuery}")
        });

        var release = CreateClient(http).GetLatestRelease();

        Assert.NotNull(release);
        Assert.Equal("v0.1.5", release.TagName);
        Assert.False(release.IsPrerelease);
    }

    [Fact]
    public void GetLatestRelease_UsesTheHighestPublishedThreePartVersionWhenOnlyPrereleasesExist() {
        using var http = CreateHttpClient(uri => uri.PathAndQuery switch {
            "/repos/EvotecIT/example/releases/latest" => Json(HttpStatusCode.NotFound, "{}"),
            "/repos/EvotecIT/example/releases?per_page=100&page=1" => Json(HttpStatusCode.OK, """
                [
                  { "tag_name": "v9.0.0", "draft": true, "prerelease": true },
                  { "tag_name": "v0.1.9", "draft": false, "prerelease": true },
                  { "tag_name": "v0.1.10", "draft": false, "prerelease": true },
                  { "tag_name": "v0.1.11-beta.1", "draft": false, "prerelease": true },
                  { "tag_name": "legacy-build", "draft": false, "prerelease": true }
                ]
                """),
            _ => throw new InvalidOperationException($"Unexpected request: {uri.PathAndQuery}")
        });

        var release = CreateClient(http).GetLatestRelease();

        Assert.NotNull(release);
        Assert.Equal("v0.1.10", release.TagName);
        Assert.True(release.IsPrerelease);
    }

    [Fact]
    public void GetLatestRelease_ScansLaterPagesBeforeChoosingTheBaseline() {
        var firstPage = "[" + string.Join(",", Enumerable.Repeat(
            "{\"tag_name\":\"v0.1.5\",\"draft\":false,\"prerelease\":true}", 100)) + "]";
        using var http = CreateHttpClient(uri => uri.PathAndQuery switch {
            "/repos/EvotecIT/example/releases/latest" => Json(HttpStatusCode.NotFound, "{}"),
            "/repos/EvotecIT/example/releases?per_page=100&page=1" => Json(HttpStatusCode.OK, firstPage),
            "/repos/EvotecIT/example/releases?per_page=100&page=2" => Json(HttpStatusCode.OK, """
                [{ "tag_name": "v0.2.0", "draft": false, "prerelease": true }]
                """),
            _ => throw new InvalidOperationException($"Unexpected request: {uri.PathAndQuery}")
        });

        Assert.Equal("v0.2.0", CreateClient(http).GetLatestRelease()!.TagName);
    }

    [Fact]
    public void GetLatestRelease_LeavesANewRepositoryWithoutABaseline() {
        using var http = CreateHttpClient(uri => uri.PathAndQuery switch {
            "/repos/EvotecIT/example/releases/latest" => Json(HttpStatusCode.NotFound, "{}"),
            "/repos/EvotecIT/example/releases?per_page=100&page=1" => Json(HttpStatusCode.OK, "[]"),
            _ => throw new InvalidOperationException($"Unexpected request: {uri.PathAndQuery}")
        });

        Assert.Null(CreateClient(http).GetLatestRelease());
    }

    [Fact]
    public void GetLatestRelease_DoesNotTreatAnApiFailureAsAMissingStableRelease() {
        using var http = CreateHttpClient(uri => uri.PathAndQuery switch {
            "/repos/EvotecIT/example/releases/latest" => Json(HttpStatusCode.Forbidden, "{}"),
            _ => throw new InvalidOperationException($"Unexpected request: {uri.PathAndQuery}")
        });

        var exception = Assert.Throws<InvalidOperationException>(() => CreateClient(http).GetLatestRelease());

        Assert.Contains("403", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetLatestRelease_RejectsAnIncompleteBaselineSearch() {
        var fullPage = "[" + string.Join(",", Enumerable.Repeat(
            "{\"tag_name\":\"v0.1.5\",\"draft\":false,\"prerelease\":true}", 100)) + "]";
        var pages = 0;
        using var http = CreateHttpClient(uri => {
            if (uri.AbsolutePath.EndsWith("/latest", StringComparison.Ordinal))
                return Json(HttpStatusCode.NotFound, "{}");
            pages++;
            return Json(HttpStatusCode.OK, fullPage);
        });

        var exception = Assert.Throws<InvalidOperationException>(() => CreateClient(http).GetLatestRelease());

        Assert.Equal(10, pages);
        Assert.Contains("baseline search exceeded", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static HomeAssistantGitHubClient CreateClient(HttpClient http)
        => new("EvotecIT", "example", "fixture-token", "https://api.github.test", http);

    private static HttpClient CreateHttpClient(Func<Uri, HttpResponseMessage> response)
        => new(new ResponseHandler(response));

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class ResponseHandler(Func<Uri, HttpResponseMessage> response) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response(request.RequestUri ?? throw new InvalidOperationException("Request URI was missing.")));
    }
}
