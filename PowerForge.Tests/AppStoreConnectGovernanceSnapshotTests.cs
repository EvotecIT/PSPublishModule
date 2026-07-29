using System.Net;
using System.Net.Http;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class AppStoreConnectClientTests
{
    [Fact]
    public async Task GovernanceSnapshot_PreservesSubscriptionPriceAndIntroductoryOfferFacts()
    {
        using var http = new HttpClient(new SubscriptionGovernanceSnapshotHandler()) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var snapshot = await new AppStoreConnectGovernanceService(client).SnapshotAsync("app-1");

        var subscription = Assert.Single(Assert.Single(snapshot.SubscriptionGroups).Subscriptions);
        Assert.True(Assert.Single(subscription.Prices).PreserveCurrentPrice);
        var offer = Assert.Single(subscription.IntroductoryOffers);
        Assert.Equal("TWO_WEEKS", offer.Duration);
        Assert.Equal("FREE_TRIAL", offer.OfferMode);
        Assert.Equal(new[] { "USA" }, offer.TerritoryIds);
    }

    [Fact]
    public async Task GovernanceSnapshot_ExportsObservedFactsWithoutInventingMissingSections()
    {
        var handler = new GovernanceSnapshotHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var snapshot = await new AppStoreConnectGovernanceService(client).SnapshotAsync("app-1");

        Assert.Equal("app-1", snapshot.AppId);
        Assert.Null(snapshot.Pricing);
        Assert.Null(snapshot.Availability);
        var accessibility = Assert.Single(snapshot.Accessibility);
        Assert.Equal("IPHONE", accessibility.DeviceFamily);
        Assert.True(accessibility.SupportsVoiceover);
        Assert.True(accessibility.Publish);
        Assert.Empty(snapshot.EncryptionDeclarations);
        Assert.Empty(snapshot.SubscriptionGroups);
        Assert.Equal(5, handler.RequestCount);
    }

    [Fact]
    public async Task GovernanceSnapshot_RejectsOmittedLegalAndAvailabilityBooleans()
    {
        using var http = new HttpClient(new IncompleteGovernanceSnapshotHandler())
        {
            BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/")
        };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new AppStoreConnectGovernanceService(client).SnapshotAsync("app-1"));

        Assert.Contains("containsProprietaryCryptography", error.Message, StringComparison.Ordinal);
        Assert.Contains("incomplete governance declaration", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GovernanceSnapshot_DeduplicatesHistoricalEncryptionDeclarationsByReviewedFacts()
    {
        using var http = new HttpClient(new DuplicateEncryptionGovernanceSnapshotHandler())
        {
            BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/")
        };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var snapshot = await new AppStoreConnectGovernanceService(client).SnapshotAsync("app-1");

        var declaration = Assert.Single(snapshot.EncryptionDeclarations);
        Assert.Equal("Reviewed description", declaration.AppDescription);
        Assert.DoesNotContain(
            new AppStoreConnectGovernanceConfiguration().Validate(snapshot),
            finding => finding.Code == "Governance.Encryption.Duplicate");
        var plan = await new AppStoreConnectGovernanceService(client).PlanAsync(snapshot);
        Assert.True(plan.IsConverged);
    }

    private sealed class GovernanceSnapshotHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/appPriceSchedule", StringComparison.Ordinal) || path.EndsWith("/appAvailabilityV2", StringComparison.Ordinal))
                return Response(HttpStatusCode.NotFound, "{}");
            if (path.EndsWith("/accessibilityDeclarations", StringComparison.Ordinal))
                return Response(HttpStatusCode.OK, """{ "data": [ { "type": "accessibilityDeclarations", "id": "a11y-1", "attributes": { "deviceFamily": "IPHONE", "state": "PUBLISHED", "supportsVoiceover": true } } ] }""");
            return Response(HttpStatusCode.OK, """{ "data": [] }""");
        }

        private static Task<HttpResponseMessage> Response(HttpStatusCode status, string body) => Task.FromResult(
            new HttpResponseMessage(status) { Content = new StringContent(body) });
    }

    private sealed class IncompleteGovernanceSnapshotHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/appPriceSchedule", StringComparison.Ordinal) || path.EndsWith("/appAvailabilityV2", StringComparison.Ordinal))
                return Response(HttpStatusCode.NotFound, "{}");
            if (path.EndsWith("/appEncryptionDeclarations", StringComparison.Ordinal))
            {
                return Response(HttpStatusCode.OK,
                    """{ "data": [ { "type": "appEncryptionDeclarations", "id": "encryption-1", "attributes": { "appDescription": "Reviewed description", "containsThirdPartyCryptography": false, "availableOnFrenchStore": false } } ] }""");
            }
            return Response(HttpStatusCode.OK, """{ "data": [] }""");
        }

        private static Task<HttpResponseMessage> Response(HttpStatusCode status, string body) => Task.FromResult(
            new HttpResponseMessage(status) { Content = new StringContent(body) });
    }

    private sealed class DuplicateEncryptionGovernanceSnapshotHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/appPriceSchedule", StringComparison.Ordinal) || path.EndsWith("/appAvailabilityV2", StringComparison.Ordinal))
                return Response(HttpStatusCode.NotFound, "{}");
            if (path.EndsWith("/appEncryptionDeclarations", StringComparison.Ordinal))
            {
                return Response(HttpStatusCode.OK,
                    """
                    {
                      "data": [
                        {
                          "type": "appEncryptionDeclarations",
                          "id": "encryption-expired",
                          "attributes": {
                            "appDescription": "Reviewed description",
                            "containsProprietaryCryptography": false,
                            "containsThirdPartyCryptography": true,
                            "availableOnFrenchStore": false,
                            "appEncryptionDeclarationState": "EXPIRED"
                          }
                        },
                        {
                          "type": "appEncryptionDeclarations",
                          "id": "encryption-approved",
                          "attributes": {
                            "appDescription": "Reviewed description",
                            "containsProprietaryCryptography": false,
                            "containsThirdPartyCryptography": true,
                            "availableOnFrenchStore": false,
                            "appEncryptionDeclarationState": "APPROVED"
                          }
                        }
                      ]
                    }
                    """);
            }
            return Response(HttpStatusCode.OK, """{ "data": [] }""");
        }

        private static Task<HttpResponseMessage> Response(HttpStatusCode status, string body) => Task.FromResult(
            new HttpResponseMessage(status) { Content = new StringContent(body) });
    }

    private sealed class SubscriptionGovernanceSnapshotHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/appPriceSchedule", StringComparison.Ordinal) || path.EndsWith("/appAvailabilityV2", StringComparison.Ordinal))
                return Response(HttpStatusCode.NotFound, "{}");
            if (path.EndsWith("/subscriptionGroups", StringComparison.Ordinal))
                return Response(HttpStatusCode.OK, """{ "data": [ { "type": "subscriptionGroups", "id": "group-1", "attributes": { "referenceName": "Pro" } } ] }""");
            if (path.EndsWith("/group-1/subscriptions", StringComparison.Ordinal))
                return Response(HttpStatusCode.OK, """{ "data": [ { "type": "subscriptions", "id": "sub-1", "attributes": { "productId": "pro.monthly", "name": "Pro Monthly", "subscriptionPeriod": "ONE_MONTH" } } ] }""");
            if (path.EndsWith("/sub-1/prices", StringComparison.Ordinal))
                return Response(HttpStatusCode.OK, """{ "data": [ { "type": "subscriptionPrices", "id": "price-1", "attributes": { "planType": "MONTHLY", "preserved": true }, "relationships": { "territory": { "data": { "type": "territories", "id": "USA" } }, "subscriptionPricePoint": { "data": { "type": "subscriptionPricePoints", "id": "point-1" } } } } ] }""");
            if (path.EndsWith("/sub-1/introductoryOffers", StringComparison.Ordinal))
                return Response(HttpStatusCode.OK, """{ "data": [ { "type": "subscriptionIntroductoryOffers", "id": "offer-1", "attributes": { "duration": "TWO_WEEKS", "offerMode": "FREE_TRIAL", "numberOfPeriods": 1 }, "relationships": { "subscription": { "data": { "type": "subscriptions", "id": "sub-1" } }, "territory": { "data": { "type": "territories", "id": "USA" } } } } ] }""");
            return Response(HttpStatusCode.OK, """{ "data": [] }""");
        }

        private static Task<HttpResponseMessage> Response(HttpStatusCode status, string body) => Task.FromResult(
            new HttpResponseMessage(status) { Content = new StringContent(body) });
    }
}
