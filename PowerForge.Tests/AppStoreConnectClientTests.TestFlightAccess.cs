using System.Net;
using System.Net.Http;

namespace PowerForge.Tests;

public sealed partial class AppStoreConnectClientTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, null)]
    [InlineData(false, true, null)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    [InlineData(true, false, null)]
    [InlineData(true, true, null)]
    public async Task BetaMutation_RejectsExpiredOrUnknownExpiryBeforeAnyWrite(
        bool submitReview, bool requireValid, bool? expired)
    {
        var handler = new SequenceHandler(new SequenceResponse(HttpStatusCode.OK,
            System.Text.Json.JsonSerializer.Serialize(new
            {
                data = new[] { new { id = "build-9", type = "builds", attributes = new { version = "9", processingState = "VALID", expired },
                    relationships = new { preReleaseVersion = new { data = new { id = "pre-1", type = "preReleaseVersions" } } } } },
                included = new[] { new { id = "pre-1", type = "preReleaseVersions", attributes = new { version = "2.2", platform = "IOS" } } }
            })));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            if (submitReview)
                await new AppStoreConnectBetaAppReviewSubmissionService(client).SubmitAsync(new()
                {
                    AppId = "app", VersionString = "2.2", BuildNumber = "9", Platform = ApplePlatform.iOS,
                    RequireValidBuild = requireValid
                });
            else
                await new AppStoreConnectTestFlightDistributionService(client).DistributeAsync(new()
                {
                    AppId = "app", VersionString = "2.2", BuildNumber = "9", Platform = ApplePlatform.iOS,
                    BetaGroupIds = ["home"], RequireValidBuild = requireValid
                });
        });

        Assert.Contains("expired", error.Message);
        Assert.Single(handler.RequestUris);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BetaMutation_PreservesProcessingOverrideForKnownUnexpiredBuild(bool submitReview)
    {
        var handler = new SequenceHandler(
            new(HttpStatusCode.OK, """
                {"data":[{"id":"build-9","type":"builds","attributes":{"version":"9","processingState":"PROCESSING","expired":false},"relationships":{"preReleaseVersion":{"data":{"id":"pre-1","type":"preReleaseVersions"}}}}],"included":[{"id":"pre-1","type":"preReleaseVersions","attributes":{"version":"2.2","platform":"IOS"}}]}
                """),
            new(HttpStatusCode.OK, submitReview
                ? """{"data":{"id":"review-9","type":"betaAppReviewSubmissions","attributes":{"betaReviewState":"WAITING_FOR_REVIEW"}}}"""
                : """{"data":[{"id":"home","type":"betaGroups","attributes":{"name":"Home","isInternalGroup":true,"hasAccessToAllBuilds":true}}]}"""));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        if (submitReview)
        {
            var result = await new AppStoreConnectBetaAppReviewSubmissionService(client).SubmitAsync(new()
            {
                AppId = "app", VersionString = "2.2", BuildNumber = "9", Platform = ApplePlatform.iOS,
                RequireValidBuild = false
            });
            Assert.Equal("review-9", result.Submission!.Id);
        }
        else
        {
            var result = await new AppStoreConnectTestFlightDistributionService(client).DistributeAsync(new()
            {
                AppId = "app", VersionString = "2.2", BuildNumber = "9", Platform = ApplePlatform.iOS,
                BetaGroupIds = ["home"], RequireValidBuild = false
            });
            Assert.Equal("home", Assert.Single(result.VerifiedBetaGroups).Id);
        }
        Assert.Equal(2, handler.RequestUris.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Distribution_ExistingExactBuildSkipsAssignmentButStillAddsRequestedTester(bool isInternal)
    {
        var handler = new SequenceHandler(
            new(HttpStatusCode.OK, """
                {"data":[{"id":"build-9","type":"builds","attributes":{"version":"9","processingState":"VALID","expired":false},"relationships":{"preReleaseVersion":{"data":{"id":"pre-1","type":"preReleaseVersions"}}}}],"included":[{"id":"pre-1","type":"preReleaseVersions","attributes":{"version":"2.2","platform":"IOS"}}]}
                """),
            new(HttpStatusCode.OK, System.Text.Json.JsonSerializer.Serialize(new { data = new[] { new { id = "group", type = "betaGroups", attributes = new { name = "Testers", isInternalGroup = isInternal, hasAccessToAllBuilds = false } } } })),
            new(HttpStatusCode.OK, """{"data":[{"id":"build-9","type":"builds","attributes":{"version":"9","processingState":"VALID"}}]}"""),
            new(HttpStatusCode.OK, """{"data":[{"id":"tester","type":"betaTesters","attributes":{"email":"existing@example.test"}}]}"""),
            new(HttpStatusCode.NoContent, string.Empty));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var result = await new AppStoreConnectTestFlightDistributionService(client).DistributeAsync(new()
        {
            AppId = "app", VersionString = "2.2", BuildNumber = "9", Platform = ApplePlatform.iOS,
            BetaGroupIds = new[] { "group" }, TestFlightPolicy = isInternal ? AppleTestFlightPolicy.Internal : AppleTestFlightPolicy.External,
            Testers = new[] { new AppStoreConnectBetaTesterSpec { Email = "existing@example.test" } }
        });
        Assert.Equal("group", Assert.Single(result.VerifiedBetaGroups).Id);
        Assert.Equal("tester", Assert.Single(result.Testers).Id);
        Assert.DoesNotContain(handler.RequestUris, uri => uri.AbsolutePath.EndsWith("relationships/builds", StringComparison.Ordinal));
        Assert.Equal("/v1/betaGroups/group/relationships/betaTesters", handler.RequestUris.Last().AbsolutePath);
        Assert.Equal(5, handler.RequestUris.Count);
    }

    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, true, true)]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, true, false)]
    public async Task Distribution_ReportsOnlyReadBackBuildAccess(bool automatic, bool present, bool verified, bool isInternal)
    {
        var responses = new List<SequenceResponse>
        {
            new(HttpStatusCode.OK, """
                {"data":[{"id":"build-9","type":"builds","attributes":{"version":"9","processingState":"VALID","expired":false},"relationships":{"preReleaseVersion":{"data":{"id":"pre-1","type":"preReleaseVersions"}}}}],"included":[{"id":"pre-1","type":"preReleaseVersions","attributes":{"version":"2.2","platform":"IOS"}}]}
                """),
            new(HttpStatusCode.OK, System.Text.Json.JsonSerializer.Serialize(new { data = new[] { new { id = "home", type = "betaGroups", attributes = new { name = "Home", isInternalGroup = isInternal, hasAccessToAllBuilds = automatic } } } }))
        };
        if (!automatic)
        {
            responses.Add(new(HttpStatusCode.OK, "{\"data\":[]}"));
            responses.Add(new(HttpStatusCode.NoContent, string.Empty));
            responses.Add(new(HttpStatusCode.OK, "{\"data\":[{\"id\":\"" + (present ? "build-9" : "other-build") + "\",\"type\":\"builds\",\"attributes\":{\"version\":\"9\",\"processingState\":\"VALID\"}}]}"));
        }
        var handler = new SequenceHandler(responses.ToArray());
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var result = await new AppStoreConnectTestFlightDistributionService(client).DistributeAsync(new()
        {
            AppId = "app", VersionString = "2.2", BuildNumber = "9", Platform = ApplePlatform.iOS,
            BetaGroupIds = new[] { "home" }, TestFlightPolicy = isInternal ? AppleTestFlightPolicy.Internal : AppleTestFlightPolicy.External
        });
        Assert.Single(result.BetaGroups);
        Assert.Equal(verified, result.VerifiedBetaGroups.Length == 1);
        if (!automatic) Assert.Equal("/v1/betaGroups/home/builds", handler.RequestUris.Last().AbsolutePath);
        Assert.Equal(automatic ? 2 : 5, handler.RequestUris.Count);
    }
}
