using System.Net;

namespace PowerForge.Tests;

public sealed partial class AppStoreConnectClientTests
{
    [Fact]
    public async Task GetControlPlaneStateAsync_CompactsReleaseCriticalInventory()
    {
        static SequenceResponse One(string type, string id, string attributes = "{}")
            => new(HttpStatusCode.OK, $$"""{ "data": { "type": "{{type}}", "id": "{{id}}", "attributes": {{attributes}} } }""");
        static SequenceResponse Many(int total)
            => new(HttpStatusCode.OK, $$"""{ "data": [], "meta": { "paging": { "total": {{total}} } } }""");

        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "type": "appInfos", "id": "info-1", "attributes": { "state": "PREPARE_FOR_SUBMISSION" } }] }"""),
            One("ageRatingDeclarations", "age-1"),
            One("appStoreReviewDetails", "review-1", "{ \"contactFirstName\": \"Ada\", \"contactLastName\": \"Lovelace\", \"contactPhone\": \"+48123456789\", \"contactEmail\": \"review@example.test\", \"demoAccountRequired\": true, \"demoAccountName\": \"apple-review\", \"demoAccountPassword\": \"secret-present-not-retained\" }"),
            One("appStoreVersionPhasedReleases", "phase-1", "{ \"phasedReleaseState\": \"ACTIVE\" }"),
            Many(1),
            Many(2),
            One("appPriceSchedules", "price-1"),
            One("appAvailabilities", "availability-1"),
            Many(3),
            Many(1),
            new SequenceResponse(HttpStatusCode.OK, """
                {
                  "data": [{
                    "type": "betaFeedbackCrashSubmissions",
                    "id": "crash-1",
                    "attributes": {
                      "createdDate": "2026-07-28T08:00:00Z",
                      "comment": "Crashes after opening the camera.",
                      "email": "private@example.test",
                      "deviceModel": "iPhone17,1",
                      "osVersion": "26.0",
                      "appPlatform": "IOS",
                      "buildBundleId": "com.evotecit.casaray"
                    }
                  }],
                  "meta": { "paging": { "total": 4 } }
                }
                """),
            Many(5),
            Many(6),
            Many(0));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var state = await client.GetControlPlaneStateAsync("app-1", "version-1", "build-1");

        Assert.True(state.ReviewDetailsConfigured);
        Assert.True(state.ReviewContactConfigured);
        Assert.True(state.ReviewDemoAccountRequired);
        Assert.True(state.ReviewDemoCredentialsConfigured);
        Assert.True(state.AgeRatingDeclared);
        Assert.Equal(1, state.EncryptionDeclarationCount);
        Assert.Equal(2, state.AccessibilityDeclarationCount);
        Assert.True(state.PriceScheduleConfigured);
        Assert.True(state.AvailabilityConfigured);
        Assert.Equal("ACTIVE", state.PhasedReleaseState);
        Assert.Equal(3, state.InAppPurchaseCount);
        Assert.Equal(0, state.SubscriptionCount);
        Assert.Equal(1, state.WebhookCount);
        Assert.Equal(4, state.BetaCrashFeedbackCount);
        var crash = Assert.Single(state.RecentCrashFeedback);
        Assert.Equal("crash-1", crash.Id);
        Assert.Equal("Crashes after opening the camera.", crash.Comment);
        Assert.Equal("iPhone17,1", crash.DeviceModel);
        Assert.Equal(5, state.BetaScreenshotFeedbackCount);
        Assert.Equal(6, state.CustomerReviewCount);
        var crashRequest = Assert.Single(handler.RequestUris, uri => uri.AbsolutePath.EndsWith("/betaFeedbackCrashSubmissions", StringComparison.Ordinal));
        Assert.Contains("filter%5Bbuild%5D=build-1", crashRequest.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetControlPlaneStateAsync_RejectsEmptyReviewResourceWithoutRetainingCredentials()
    {
        static SequenceResponse One(string type, string id, string attributes = "{}")
            => new(HttpStatusCode.OK, $$"""{ "data": { "type": "{{type}}", "id": "{{id}}", "attributes": {{attributes}} } }""");
        static SequenceResponse Many(int total)
            => new(HttpStatusCode.OK, $$"""{ "data": [], "meta": { "paging": { "total": {{total}} } } }""");

        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "type": "appInfos", "id": "info-1" }] }"""),
            One("ageRatingDeclarations", "age-1"),
            One("appStoreReviewDetails", "review-1", "{ \"contactFirstName\": \"Ada\", \"contactLastName\": \"\", \"contactPhone\": \"+48123456789\", \"contactEmail\": \"review@example.test\", \"demoAccountRequired\": true, \"demoAccountName\": \"apple-review\", \"demoAccountPassword\": \"\" }"),
            One("appStoreVersionPhasedReleases", "phase-1"),
            Many(0), Many(0), One("appPriceSchedules", "price-1"), One("appAvailabilities", "availability-1"),
            Many(0), Many(0), Many(0), Many(0), Many(0), Many(0));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var state = await client.GetControlPlaneStateAsync("app-1", "version-1", "build-1");

        Assert.True(state.ReviewDetailsExist);
        Assert.False(state.ReviewContactConfigured);
        Assert.True(state.ReviewDemoAccountRequired);
        Assert.False(state.ReviewDemoCredentialsConfigured);
        Assert.False(state.ReviewDetailsConfigured);
        Assert.DoesNotContain("apple-review", System.Text.Json.JsonSerializer.Serialize(state), StringComparison.Ordinal);
    }
}
