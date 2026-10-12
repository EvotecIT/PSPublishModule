using System.Net;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class AppStoreConnectClientTests
{
    [Fact]
    public async Task SigningBundleIds_ListFiltersAndFollowsPagination()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK, """
                { "data": [{ "id": "mac-id", "type": "bundleIds", "attributes": {
                  "identifier": "com.example.helper", "name": "Helper", "platform": "MAC_OS", "seedId": "PREFIX" }
                }], "links": { "next": "https://api.appstoreconnect.apple.com/v1/bundleIds?cursor=next" } }
                """),
            new SequenceResponse(HttpStatusCode.OK, """
                { "data": [{ "id": "future-id", "type": "bundleIds", "attributes": { "platform": "FUTURE_PLATFORM" } }] }
                """));
        using var http = new HttpClient(handler);
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var ids = await client.GetBundleIdsAsync(" com.example.helper & other ", AppStoreConnectBundleIdPlatform.macOS, limit: 999);

        Assert.Equal(2, ids.Length);
        Assert.Equal("mac-id", ids[0].Id);
        Assert.Equal("com.example.helper", ids[0].Identifier);
        Assert.Equal("Helper", ids[0].Name);
        Assert.Equal("PREFIX", ids[0].SeedId);
        Assert.Equal("MAC_OS", ids[0].Platform);
        Assert.Equal("FUTURE_PLATFORM", ids[1].Platform);
        Assert.Null(ids[1].Identifier);
        Assert.Equal("?filter%5Bidentifier%5D=com.example.helper%20%26%20other&filter%5Bplatform%5D=MAC_OS&limit=200", handler.RequestUris[0].Query);
        Assert.Equal("?cursor=next", handler.RequestUris[1].Query);
        Assert.All(handler.Methods, method => Assert.Equal(HttpMethod.Get, method));
    }

    [Theory]
    [InlineData(AppStoreConnectBundleIdPlatform.iOS, "IOS")]
    [InlineData(AppStoreConnectBundleIdPlatform.macOS, "MAC_OS")]
    [InlineData(AppStoreConnectBundleIdPlatform.Universal, "UNIVERSAL")]
    public async Task SigningBundleIds_RegisterUsesAppleResourceContract(AppStoreConnectBundleIdPlatform platform, string expected)
    {
        var handler = new SequenceHandler(new SequenceResponse(HttpStatusCode.Created,
            """{ "data": { "id": "new-id", "type": "bundleIds", "attributes": { "identifier": "com.example.helper" } } }"""));
        using var http = new HttpClient(handler);
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var result = await client.RegisterBundleIdAsync(" com.example.helper ", " Helper \"native\" ", platform);

        Assert.Equal("new-id", result.Id);
        Assert.Equal("com.example.helper", result.Identifier);
        Assert.Equal(HttpMethod.Post, Assert.Single(handler.Methods));
        Assert.Equal("https://api.appstoreconnect.apple.com/v1/bundleIds", Assert.Single(handler.RequestUris).AbsoluteUri);
        using var json = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        var data = json.RootElement.GetProperty("data");
        Assert.Equal("bundleIds", data.GetProperty("type").GetString());
        var attributes = data.GetProperty("attributes");
        Assert.Equal("com.example.helper", attributes.GetProperty("identifier").GetString());
        Assert.Equal("Helper \"native\"", attributes.GetProperty("name").GetString());
        Assert.Equal(expected, attributes.GetProperty("platform").GetString());
        Assert.Equal(3, attributes.EnumerateObject().Count());
    }

    [Theory]
    [InlineData("", "Helper")]
    [InlineData("com.example.helper", " ")]
    public async Task SigningBundleIds_RejectsMissingRegistrationFields(string identifier, string name)
    {
        var handler = new SequenceHandler();
        using var http = new HttpClient(handler);
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        await Assert.ThrowsAsync<ArgumentException>(() => client.RegisterBundleIdAsync(identifier, name, AppStoreConnectBundleIdPlatform.macOS));
        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task SigningBundleIds_RejectsUnknownPlatformBeforeSending()
    {
        var handler = new SequenceHandler();
        using var http = new HttpClient(handler);
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetBundleIdsAsync(platform: (AppStoreConnectBundleIdPlatform)99));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.RegisterBundleIdAsync("com.example.helper", "Helper", (AppStoreConnectBundleIdPlatform)99));
        Assert.Empty(handler.RequestUris);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task SigningBundleIds_ReportsAccountOrConflictFailureWithoutRetry(HttpStatusCode status)
    {
        var handler = new SequenceHandler(new SequenceResponse(status, """
            { "errors": [{ "status": "403", "code": "NOT_AUTHORIZED", "title": "Not authorized", "detail": "Identifier registration denied." }] }
            """));
        using var http = new HttpClient(handler);
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.RegisterBundleIdAsync("com.example.helper", "Helper", AppStoreConnectBundleIdPlatform.macOS));

        Assert.Contains("Identifier registration denied", error.Message, StringComparison.Ordinal);
        Assert.Single(handler.RequestUris);
    }
}
