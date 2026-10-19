using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class AppStoreConnectClientTests
{
    private static SequenceResponse InternalAccessBuild() => new(HttpStatusCode.OK,
        """{"data":[{"type":"builds","id":"build-1","attributes":{"version":"2","processingState":"VALID","expired":false},"relationships":{"preReleaseVersion":{"data":{"type":"preReleaseVersions","id":"pre-1"}}}}],"included":[{"type":"preReleaseVersions","id":"pre-1","attributes":{"version":"1.0","platform":"IOS"}}]}""");

    private static SequenceResponse InternalAccessGroup() => new(HttpStatusCode.OK,
        """{"data":[{"type":"betaGroups","id":"internal-1","attributes":{"name":"Internal","isInternalGroup":true,"hasAccessToAllBuilds":true,"publicLinkEnabled":false}}]}""");

    private static AppStoreConnectTestFlightDistributionRequest InternalAccessRequest() => new()
    {
        AppId = "app-1", VersionString = "1.0", BuildNumber = "2", Platform = ApplePlatform.iOS,
        BetaGroupIds = new[] { "internal-1" }, TestFlightPolicy = AppleTestFlightPolicy.Internal,
        Testers = new[] { new AppStoreConnectBetaTesterSpec { Email = "team@example.test", FirstName = "Team", LastName = "Member" } }
    };

    [Fact]
    public async Task InternalDistribution_UsesTheAppsTesterIdentityWithoutGlobalEmailLookup()
    {
        var handler = new SequenceHandler(InternalAccessBuild(), InternalAccessGroup(),
            new(HttpStatusCode.OK, """{"data":[{"id":"studio-tester","type":"betaTesters","attributes":{"email":"TEAM@example.test"}}]}"""),
            new(HttpStatusCode.NoContent, ""));
        using var http = new HttpClient(handler);
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var result = await new AppStoreConnectTestFlightDistributionService(client).DistributeAsync(InternalAccessRequest());
        Assert.Equal("studio-tester", Assert.Single(result.Testers).Id);
        Assert.Equal("/v1/betaTesters", handler.RequestUris[2].AbsolutePath);
        Assert.Contains("filter%5Bapps%5D=app-1", handler.RequestUris[2].Query, StringComparison.Ordinal);
        Assert.Contains("filter%5Bemail%5D=team%40example.test", handler.RequestUris[2].Query, StringComparison.Ordinal);
        Assert.Contains("studio-tester", handler.RequestBodies[3], StringComparison.Ordinal);
        Assert.Single(handler.Methods, method => method == HttpMethod.Post);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InternalDistribution_RegistersOnlyAnExistingEligibleUserWithAppAccess(bool allApps)
    {
        var responses = new List<SequenceResponse>
        {
            InternalAccessBuild(), InternalAccessGroup(), new(HttpStatusCode.OK, """{"data":[]}"""),
            new(HttpStatusCode.OK, "{\"data\":[{\"type\":\"users\",\"id\":\"user-1\",\"attributes\":{\"username\":\"team@example.test\",\"roles\":[\"DEVELOPER\"],\"allAppsVisible\":" + (allApps ? "true" : "false") + "}}]}")
        };
        if (!allApps) responses.Add(new(HttpStatusCode.OK, """{"data":[{"type":"apps","id":"app-1"}]}"""));
        responses.Add(new(HttpStatusCode.Created, """{"data":{"type":"betaTesters","id":"new-app-tester","attributes":{"email":"team@example.test"}}}"""));
        responses.Add(new(HttpStatusCode.NoContent, ""));
        var handler = new SequenceHandler(responses.ToArray());
        using var http = new HttpClient(handler);
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var result = await new AppStoreConnectTestFlightDistributionService(client).DistributeAsync(InternalAccessRequest());
        Assert.Equal("new-app-tester", Assert.Single(result.Testers).Id);
        var create = handler.RequestUris.FindIndex(uri => uri.AbsolutePath == "/v1/betaTesters" && handler.Methods[handler.RequestUris.IndexOf(uri)] == HttpMethod.Post);
        Assert.Equal(HttpMethod.Post, handler.Methods[create]);
        using var body = JsonDocument.Parse(handler.RequestBodies[create]);
        var group = Assert.Single(body.RootElement.GetProperty("data").GetProperty("relationships").GetProperty("betaGroups").GetProperty("data").EnumerateArray());
        Assert.Equal("internal-1", group.GetProperty("id").GetString());
        Assert.All(handler.RequestUris.Select((uri, index) => (uri, index)).Where(x => x.uri.AbsolutePath.Contains("users", StringComparison.Ordinal)),
            entry => Assert.Equal(HttpMethod.Get, handler.Methods[entry.index]));
    }

    [Theory]
    [InlineData("FINANCE", true)]
    [InlineData("DEVELOPER", false)]
    public async Task InternalDistribution_RejectsIneligibleRoleOrMissingAppAccessWithoutGrantingPermissions(string role, bool allApps)
    {
        var responses = new List<SequenceResponse>
        {
            InternalAccessBuild(), InternalAccessGroup(), new(HttpStatusCode.OK, """{"data":[]}"""),
            new(HttpStatusCode.OK, "{\"data\":[{\"type\":\"users\",\"id\":\"user-1\",\"attributes\":{\"username\":\"team@example.test\",\"roles\":[\""+role+"\"],\"allAppsVisible\":"+(allApps?"true":"false")+"}}]}")
        };
        if (!allApps) responses.Add(new(HttpStatusCode.OK, """{"data":[{"type":"apps","id":"other-app"}]}"""));
        var handler = new SequenceHandler(responses.ToArray());
        using var http = new HttpClient(handler);
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AppStoreConnectTestFlightDistributionService(client).DistributeAsync(InternalAccessRequest()));
        Assert.All(handler.Methods, method => Assert.Equal(HttpMethod.Get, method));
    }

    [Fact]
    public async Task InternalDistribution_FiltersEachEmailAndReusesResolvedIdentities()
    {
        var handler = new SequenceHandler(InternalAccessBuild(), InternalAccessGroup(),
            new(HttpStatusCode.OK, """{"data":[{"id":"team","type":"betaTesters","attributes":{"email":"team@example.test"}}]}"""),
            new(HttpStatusCode.NoContent, ""),
            new(HttpStatusCode.OK, """{"data":[{"id":"other","type":"betaTesters","attributes":{"email":"other@example.test"}}]}"""),
            new(HttpStatusCode.NoContent, ""), new(HttpStatusCode.NoContent, ""));
        using var http = new HttpClient(handler);
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var request = InternalAccessRequest();
        request.Testers = new[] { new AppStoreConnectBetaTesterSpec { Email = "team@example.test" },
            new AppStoreConnectBetaTesterSpec { Email = "other@example.test" }, new AppStoreConnectBetaTesterSpec { Email = "TEAM@example.test" } };
        var result = await new AppStoreConnectTestFlightDistributionService(client).DistributeAsync(request);
        Assert.Equal(new[] { "team", "other", "team" }, result.Testers.Select(t => t.Id));
        var lookups = handler.RequestUris.Where(uri => uri.AbsolutePath == "/v1/betaTesters").ToArray();
        Assert.Equal(2, lookups.Length);
        Assert.All(lookups, uri =>
        {
            Assert.Contains("filter%5Bapps%5D=app-1", uri.Query, StringComparison.Ordinal);
            Assert.Contains("filter%5Bemail%5D=", uri.Query, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExternalDistribution_ReusesTheGlobalTesterForAnExistingPerson(bool createMissing)
    {
        var handler = new SequenceHandler(InternalAccessBuild(),
            new(HttpStatusCode.OK, """{"data":[{"id":"external-1","type":"betaGroups","attributes":{"isInternalGroup":false}}]}"""),
            new(HttpStatusCode.OK, """{"data":[{"id":"build-1","type":"builds"}]}"""),
            new(HttpStatusCode.OK, """{"data":[{"id":"existing-person","type":"betaTesters","attributes":{"email":"team@example.test"}}]}"""),
            new(HttpStatusCode.NoContent, ""));
        using var http = new HttpClient(handler);
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var request = InternalAccessRequest();
        request.BetaGroupIds = new[] { "external-1" };
        request.TestFlightPolicy = AppleTestFlightPolicy.External;
        request.CreateMissingTesters = createMissing;
        var result = await new AppStoreConnectTestFlightDistributionService(client).DistributeAsync(request);
        Assert.Equal("existing-person", Assert.Single(result.Testers).Id);
        Assert.Equal("/v1/betaTesters", handler.RequestUris[3].AbsolutePath);
        Assert.DoesNotContain("filter%5Bapps%5D", handler.RequestUris[3].Query, StringComparison.Ordinal);
        Assert.Contains("filter%5Bemail%5D=team%40example.test", handler.RequestUris[3].Query, StringComparison.Ordinal);
        Assert.Equal("/v1/betaGroups/external-1/relationships/betaTesters", handler.RequestUris[4].AbsolutePath);
        Assert.Single(handler.Methods, method => method == HttpMethod.Post);
    }

    [Fact]
    public async Task GetUsersAsync_PaginatesAndPreservesUnknownAccessWithoutMutatingAccounts()
    {
        var handler = new SequenceHandler(
            new(HttpStatusCode.OK, """{"data":[{"type":"users","id":"one","attributes":{"username":"team+one@example.test","roles":["ADMIN"]}}],"links":{"next":"https://api.appstoreconnect.apple.com/v1/users?cursor=next"}}"""),
            new(HttpStatusCode.OK, """{"data":[{"type":"users","id":"two","attributes":{"username":"team+one@example.test","roles":["DEVELOPER"],"allAppsVisible":false}}]}"""));
        using var http = new HttpClient(handler);
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var users = await client.GetUsersAsync(" team+one@example.test ");
        Assert.Equal(2, users.Length);
        Assert.Null(users[0].AllAppsVisible);
        Assert.False(users[1].AllAppsVisible);
        Assert.Contains("filter%5Busername%5D=team%2Bone%40example.test", handler.RequestUris[0].Query, StringComparison.Ordinal);
        Assert.All(handler.Methods, method => Assert.Equal(HttpMethod.Get, method));
    }
}
