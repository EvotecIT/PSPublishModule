using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class AppStoreConnectClientTests
{
    [Theory]
    [InlineData("FUTURE_RELEASE_POLICY")]
    [InlineData(null)]
    public async Task GetVersionsAsync_PreservesReleasePolicyWithoutInventingADefault(string? releaseType)
    {
        var response = JsonSerializer.Serialize(new
        {
            data = new[] { new { type = "appStoreVersions", id = "version-1", attributes = new { releaseType } } }
        }, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
        var handler = new SequenceHandler(new SequenceResponse(HttpStatusCode.OK, response));
        using var http = new HttpClient(handler);
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var version = Assert.Single(await client.GetVersionsAsync("app-1"));

        Assert.Equal(releaseType, version.ReleaseType);
    }

    [Fact]
    public async Task SetVersionManualReleaseAsync_PatchesOnlyTheExactVersionAndCanBeReadBack()
    {
        const string resource = """
            {"type":"appStoreVersions","id":"version-mac","attributes":{"versionString":"2.2","platform":"MAC_OS","appStoreState":"PREPARE_FOR_SUBMISSION","releaseType":"MANUAL"}}
            """;
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK, "{\"data\":" + resource + "}"),
            new SequenceResponse(HttpStatusCode.OK, "{\"data\":[" + resource + "]}"));
        using var http = new HttpClient(handler);
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var updated = await client.SetVersionManualReleaseAsync(" version-mac ");
        var readback = Assert.Single(await client.GetVersionsAsync("app-1", "2.2", ApplePlatform.macOS));

        Assert.Equal("version-mac", updated.Id);
        Assert.Equal("2.2", updated.VersionString);
        Assert.Equal("MAC_OS", updated.Platform);
        Assert.Equal("MANUAL", updated.ReleaseType);
        Assert.Equal(updated.Id, readback.Id);
        Assert.Equal("MANUAL", readback.ReleaseType);
        Assert.Equal(new HttpMethod("PATCH"), handler.Methods[0]);
        Assert.Equal("https://api.appstoreconnect.apple.com/v1/appStoreVersions/version-mac", handler.RequestUris[0].ToString());
        using var body = JsonDocument.Parse(handler.RequestBodies[0]);
        var data = body.RootElement.GetProperty("data");
        Assert.Equal("appStoreVersions", data.GetProperty("type").GetString());
        Assert.Equal("version-mac", data.GetProperty("id").GetString());
        var attribute = Assert.Single(data.GetProperty("attributes").EnumerateObject());
        Assert.Equal("releaseType", attribute.Name);
        Assert.Equal("MANUAL", attribute.Value.GetString());
        Assert.Equal(3, data.EnumerateObject().Count());
        Assert.Equal(HttpMethod.Get, handler.Methods[1]);
        Assert.Contains("filter%5Bplatform%5D=MAC_OS", handler.RequestUris[1].OriginalString, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"data\":null}")]
    [InlineData("{\"data\":{\"type\":\"apps\",\"id\":\"version-mac\",\"attributes\":{\"releaseType\":\"MANUAL\"}}}")]
    [InlineData("{\"data\":{\"type\":\"appStoreVersions\",\"id\":\"different-version\",\"attributes\":{\"releaseType\":\"MANUAL\"}}}")]
    [InlineData("{\"data\":{\"type\":\"appStoreVersions\",\"id\":\"version-mac\",\"attributes\":{\"releaseType\":\"AFTER_APPROVAL\"}}}")]
    [InlineData("{\"data\":{\"type\":\"appStoreVersions\",\"id\":\"version-mac\",\"attributes\":{}}}")]
    public async Task SetVersionManualReleaseAsync_RejectsAnUnconfirmedResponse(string response)
    {
        var handler = new SequenceHandler(new SequenceResponse(HttpStatusCode.OK, response));
        using var http = new HttpClient(handler);
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SetVersionManualReleaseAsync("version-mac"));

        Assert.Single(handler.RequestUris);
    }

    [Fact]
    public async Task SetVersionManualReleaseAsync_DoesNotRetryAMutationAfterServerFailure()
    {
        var handler = new SequenceHandler(new SequenceResponse(HttpStatusCode.ServiceUnavailable, "unavailable"));
        using var http = new HttpClient(handler);
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SetVersionManualReleaseAsync("version-mac"));

        Assert.Contains("503", error.Message, StringComparison.Ordinal);
        Assert.Single(handler.RequestUris);
    }

    [Fact]
    public async Task SetVersionManualReleaseAsync_RejectsABlankVersionBeforeSending()
    {
        var handler = new SequenceHandler();
        using var http = new HttpClient(handler);
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        await Assert.ThrowsAsync<ArgumentException>(() => client.SetVersionManualReleaseAsync(" "));

        Assert.Empty(handler.RequestUris);
    }
}
