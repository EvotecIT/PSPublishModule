using System.Net;
using System.Net.Http;

namespace PowerForge.Tests;

public sealed partial class AppStoreConnectClientTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    public async Task InternalDistribution_ReportsOnlyReadBackBuildAccess(bool automatic, bool present, bool verified)
    {
        var responses = new List<SequenceResponse>
        {
            new(HttpStatusCode.OK, """
                {"data":[{"id":"build-9","type":"builds","attributes":{"version":"9","processingState":"VALID","expired":false},"relationships":{"preReleaseVersion":{"data":{"id":"pre-1","type":"preReleaseVersions"}}}}],"included":[{"id":"pre-1","type":"preReleaseVersions","attributes":{"version":"2.2","platform":"IOS"}}]}
                """),
            new(HttpStatusCode.OK, "{\"data\":[{\"id\":\"home\",\"type\":\"betaGroups\",\"attributes\":{\"name\":\"Home\",\"isInternalGroup\":true,\"hasAccessToAllBuilds\":" + automatic.ToString().ToLowerInvariant() + "}}]}")
        };
        if (!automatic)
        {
            responses.Add(new(HttpStatusCode.NoContent, string.Empty));
            responses.Add(new(HttpStatusCode.OK, "{\"data\":[{\"id\":\"" + (present ? "build-9" : "other-build") + "\",\"type\":\"builds\",\"attributes\":{\"version\":\"9\",\"processingState\":\"VALID\"}}]}"));
        }
        var handler = new SequenceHandler(responses.ToArray());
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var result = await new AppStoreConnectTestFlightDistributionService(client).DistributeAsync(new()
        {
            AppId = "app", VersionString = "2.2", BuildNumber = "9", Platform = ApplePlatform.iOS,
            BetaGroupIds = new[] { "home" }, TestFlightPolicy = AppleTestFlightPolicy.Internal
        });
        Assert.Single(result.BetaGroups);
        Assert.Equal(verified, result.VerifiedBetaGroups.Length == 1);
        if (!automatic) Assert.Equal("/v1/betaGroups/home/builds", handler.RequestUris.Last().AbsolutePath);
        Assert.Equal(automatic ? 2 : 4, handler.RequestUris.Count);
    }
}
