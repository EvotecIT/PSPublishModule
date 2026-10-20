using System.Net;
using System.Net.Http;

namespace PowerForge.Tests;

public sealed partial class AppStoreConnectClientTests
{
    [Fact]
    public async Task GetBuildUploadsAsync_FollowsPagesAndRetainsUnprocessedAttempts()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK, """
                {"data":[{"id":"upload-pending","type":"buildUploads","attributes":{
                  "cfBundleShortVersionString":"1.7","cfBundleVersion":"15","platform":"MAC_OS","state":"PROCESSING"
                }}],"links":{"next":"https://api.appstoreconnect.apple.com/v1/apps/123/buildUploads?cursor=next"}}
                """),
            new SequenceResponse(HttpStatusCode.OK, """
                {"data":[{"id":"upload-failed","type":"buildUploads","attributes":{
                  "cfBundleShortVersionString":"1.7","cfBundleVersion":"15","platform":"MAC_OS",
                  "state":{"state":"FAILED","errors":[{"code":"invalid-bundle","description":"Invalid bundle."}]}
                }}],"links":{"next":null}}
                """));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var uploads = await client.GetBuildUploadsAsync(" 123 ", buildNumber: " 15 ",
            marketingVersion: " 1.7 ", platform: ApplePlatform.macOS, limit: 1);

        Assert.Equal(new[] { "upload-pending", "upload-failed" }, uploads.Select(value => value.Id));
        Assert.Equal("PROCESSING", uploads[0].State);
        Assert.Equal("MAC_OS", uploads[0].Platform);
        Assert.Equal("1.7", uploads[0].MarketingVersion);
        Assert.Equal("15", uploads[0].BuildNumber);
        Assert.Equal("invalid-bundle", Assert.Single(uploads[1].Errors).Code);
        Assert.Equal(2, handler.RequestUris.Count);
        Assert.All(handler.Methods, method => Assert.Equal(HttpMethod.Get, method));
        Assert.DoesNotContain(handler.RequestBodies, body => body.Length != 0);
        var first = handler.RequestUris[0];
        Assert.Equal("/v1/apps/123/buildUploads", first.AbsolutePath);
        var query = Uri.UnescapeDataString(first.Query);
        Assert.Contains("filter[cfBundleVersion]=15", query);
        Assert.Contains("filter[cfBundleShortVersionString]=1.7", query);
        Assert.Contains("filter[platform]=MAC_OS", query);
        Assert.Contains("limit=1", query);
        Assert.Equal("?cursor=next", handler.RequestUris[1].Query);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task GetBuildUploadsAsync_RequiresAppIdentityBeforeRequest(string? appId)
    {
        var handler = new RecordingHandler("{\"data\":[]}");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        await Assert.ThrowsAsync<ArgumentException>(() => client.GetBuildUploadsAsync(appId!));
        Assert.Empty(handler.RequestUris);
    }
}
