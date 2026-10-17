using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class AppStoreConnectClientTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreateBetaGroup_UsesExplicitAudienceWithoutInvitingTestersOrPublishingLink(bool isInternal)
    {
        var handler = new SequenceHandler(new SequenceResponse(HttpStatusCode.Created, """{"data":{"type":"betaGroups","id":"group-1","attributes":{"name":"Team","hasAccessToAllBuilds":false}}}"""));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var result = await client.CreateBetaGroupAsync("app-1", "Team", isInternal);

        Assert.Equal("group-1", result.Id);
        Assert.Equal("/v1/betaGroups", Assert.Single(handler.RequestUris).AbsolutePath);
        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        var data = body.RootElement.GetProperty("data");
        var attributes = data.GetProperty("attributes");
        Assert.Equal(isInternal, attributes.GetProperty("isInternalGroup").GetBoolean());
        Assert.False(attributes.GetProperty("hasAccessToAllBuilds").GetBoolean());
        if (!isInternal) Assert.False(attributes.GetProperty("publicLinkEnabled").GetBoolean());
        Assert.Equal("app-1", data.GetProperty("relationships").GetProperty("app").GetProperty("data").GetProperty("id").GetString());
        Assert.False(data.GetProperty("relationships").TryGetProperty("betaTesters", out _));
    }
}
