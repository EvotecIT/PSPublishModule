using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class AppStoreConnectClientTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetBuildUsesNonExemptEncryptionAsync_ChangesOnlyTheReviewedDeclaration(bool usesNonExemptEncryption)
    {
        var response = JsonSerializer.Serialize(new
        {
            data = new
            {
                type = "builds", id = "build-2",
                attributes = new { version = "2", processingState = "VALID", expired = false, usesNonExemptEncryption }
            }
        });
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK, response),
            new SequenceResponse(HttpStatusCode.OK, response));
        using var http = new HttpClient(handler);
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var updated = await client.SetBuildUsesNonExemptEncryptionAsync(" build-2 ", usesNonExemptEncryption);
        var readback = await client.GetBuildAsync("build-2");

        Assert.Equal("build-2", updated.Id);
        Assert.Equal("2", updated.Version);
        Assert.Equal("VALID", updated.ProcessingState);
        Assert.False(updated.Expired);
        Assert.Equal(usesNonExemptEncryption, updated.UsesNonExemptEncryption);
        Assert.Equal(usesNonExemptEncryption, readback!.UsesNonExemptEncryption);
        Assert.Equal(new HttpMethod("PATCH"), handler.Methods[0]);
        Assert.Equal("https://api.appstoreconnect.apple.com/v1/builds/build-2", handler.RequestUris[0].ToString());
        using var body = JsonDocument.Parse(handler.RequestBodies[0]);
        var data = body.RootElement.GetProperty("data");
        Assert.Equal("builds", data.GetProperty("type").GetString());
        Assert.Equal("build-2", data.GetProperty("id").GetString());
        var attribute = Assert.Single(data.GetProperty("attributes").EnumerateObject());
        Assert.Equal("usesNonExemptEncryption", attribute.Name);
        Assert.Equal(usesNonExemptEncryption, attribute.Value.GetBoolean());
        // No expiration or declaration-document relationship may accompany this update.
        Assert.Equal(3, data.EnumerateObject().Count());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"usesNonExemptEncryption\":null}")]
    public async Task GetBuildAsync_PreservesAnUnknownEncryptionDeclaration(string attributes)
    {
        var handler = new SequenceHandler(new SequenceResponse(HttpStatusCode.OK,
            "{\"data\":{\"type\":\"builds\",\"id\":\"build-2\",\"attributes\":" + attributes + "}}"));
        using var http = new HttpClient(handler);
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var build = await client.GetBuildAsync("build-2");

        Assert.Equal("build-2", build!.Id);
        Assert.Null(build.UsesNonExemptEncryption);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"data\":null}")]
    [InlineData("{\"data\":{\"type\":\"apps\",\"id\":\"build-2\",\"attributes\":{\"usesNonExemptEncryption\":false}}}")]
    [InlineData("{\"data\":{\"type\":\"builds\",\"id\":\"other-build\",\"attributes\":{\"usesNonExemptEncryption\":false}}}")]
    [InlineData("{\"data\":{\"type\":\"builds\",\"id\":\"build-2\",\"attributes\":{\"usesNonExemptEncryption\":true}}}")]
    [InlineData("{\"data\":{\"type\":\"builds\",\"id\":\"build-2\",\"attributes\":{}}}")]
    public async Task SetBuildUsesNonExemptEncryptionAsync_RejectsUnconfirmedCompliance(string response)
    {
        var handler = new SequenceHandler(new SequenceResponse(HttpStatusCode.OK, response));
        using var http = new HttpClient(handler);
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SetBuildUsesNonExemptEncryptionAsync("build-2", false));

        Assert.Single(handler.RequestUris);
    }

    [Fact]
    public async Task SetBuildUsesNonExemptEncryptionAsync_DoesNotRetryARejectedMutation()
    {
        var handler = new SequenceHandler(new SequenceResponse(HttpStatusCode.UnprocessableEntity, "rejected"));
        using var http = new HttpClient(handler);
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SetBuildUsesNonExemptEncryptionAsync("build-2", false));

        Assert.Contains("422", error.Message, StringComparison.Ordinal);
        Assert.Single(handler.RequestUris);
    }

    [Fact]
    public async Task SetBuildUsesNonExemptEncryptionAsync_RejectsABlankBuildBeforeSending()
    {
        var handler = new SequenceHandler();
        using var http = new HttpClient(handler);
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        await Assert.ThrowsAsync<ArgumentException>(() => client.SetBuildUsesNonExemptEncryptionAsync(" ", false));

        Assert.Empty(handler.RequestUris);
    }
}
