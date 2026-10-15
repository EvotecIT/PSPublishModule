using System.Net;
using System.Net.Http;

namespace PowerForge.Tests;

public sealed partial class AppStoreConnectClientTests
{
    [Fact]
    public async Task ReleaseState_MissingRequestedBuildDoesNotReadSelectedOlderBuildBetaState()
    {
        var handler = new SequenceHandler(
            new(HttpStatusCode.OK, """{"data":[{"id":"version","type":"appStoreVersions","attributes":{"versionString":"2.3","platform":"IOS"}}]}"""),
            new(HttpStatusCode.OK, """{"data":[]}"""),
            new(HttpStatusCode.OK, """{"data":{"id":"older-build","type":"builds"}}"""),
            new(HttpStatusCode.OK, """{"data":{"id":"older-build","type":"builds","attributes":{"version":"29","processingState":"VALID"}}}"""),
            new(HttpStatusCode.OK, """{"data":[]}"""));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var result = await new AppStoreConnectReleaseStateService(client).GetAsync(new()
        {
            AppId = "app", VersionString = "2.3", BuildNumber = "30", Platforms = [ApplePlatform.iOS]
        });
        var platform = Assert.Single(result.Platforms);
        Assert.Equal("older-build", platform.SelectedBuild!.Id);
        Assert.Null(platform.MatchedBuild);
        Assert.Null(platform.TestFlightBuild);
        Assert.Null(platform.BetaDetail);
        Assert.Null(platform.BetaReviewSubmission);
        Assert.Equal(5, handler.RequestUris.Count);
    }
}
