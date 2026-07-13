using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class AppStoreConnectClientTests
{
    [Fact]
    public async Task GetSubscriptionPricePoints_ParsesTerritoryAndAmounts()
    {
        var handler = new RecordingHandler(
            """
            {
              "data": [
                {
                  "id": "point-1",
                  "type": "subscriptionPricePoints",
                  "attributes": {
                    "customerPrice": "4.99",
                    "proceeds": "3.50",
                    "proceedsYear2": "4.25"
                  },
                  "relationships": {
                    "territory": { "data": { "type": "territories", "id": "POL" } }
                  }
                }
              ]
            }
            """);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var pricePoints = await client.GetSubscriptionPricePointsAsync("subscription-1", "POL");

        var pricePoint = Assert.Single(pricePoints);
        Assert.Equal("point-1", pricePoint.Id);
        Assert.Equal("4.99", pricePoint.CustomerPrice);
        Assert.Equal("3.50", pricePoint.Proceeds);
        Assert.Equal("4.25", pricePoint.ProceedsYear2);
        Assert.Equal("POL", pricePoint.TerritoryId);
        Assert.Equal(
            "https://api.appstoreconnect.apple.com/v1/subscriptions/subscription-1/pricePoints?include=territory&filter%5Bterritory%5D=POL&limit=200",
            Assert.Single(handler.RequestUris).ToString());
    }

    [Fact]
    public async Task GetSubscriptionIntroductoryOffers_ParsesOfferAndRelationships()
    {
        var handler = new RecordingHandler(
            """
            {
              "data": [
                {
                  "id": "offer-1",
                  "type": "subscriptionIntroductoryOffers",
                  "attributes": {
                    "duration": "TWO_WEEKS",
                    "offerMode": "FREE_TRIAL",
                    "numberOfPeriods": 1,
                    "startDate": "2026-07-13"
                  },
                  "relationships": {
                    "subscription": { "data": { "type": "subscriptions", "id": "subscription-1" } }
                  }
                }
              ]
            }
            """);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var offers = await client.GetSubscriptionIntroductoryOffersAsync("subscription-1");

        var offer = Assert.Single(offers);
        Assert.Equal("offer-1", offer.Id);
        Assert.Equal("TWO_WEEKS", offer.Duration);
        Assert.Equal("FREE_TRIAL", offer.OfferMode);
        Assert.Equal(1, offer.NumberOfPeriods);
        Assert.Equal("2026-07-13", offer.StartDate);
        Assert.Equal("subscription-1", offer.SubscriptionId);
        Assert.Equal(
            "https://api.appstoreconnect.apple.com/v1/subscriptions/subscription-1/introductoryOffers?include=subscription%2Cterritory%2CsubscriptionPricePoint&limit=200",
            Assert.Single(handler.RequestUris).ToString());
    }

    [Fact]
    public async Task GetSubscriptionIntroductoryOffers_FollowsNextPageLink()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "offer-1",
                      "type": "subscriptionIntroductoryOffers",
                      "attributes": { "duration": "TWO_WEEKS", "offerMode": "FREE_TRIAL", "numberOfPeriods": 1 }
                    }
                  ],
                  "links": {
                    "next": "https://api.appstoreconnect.apple.com/v1/subscriptions/subscription-1/introductoryOffers?cursor=next-page"
                  }
                }
                """),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "offer-2",
                      "type": "subscriptionIntroductoryOffers",
                      "attributes": { "duration": "ONE_MONTH", "offerMode": "FREE_TRIAL", "numberOfPeriods": 1 }
                    }
                  ],
                  "links": { "next": null }
                }
                """));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var offers = await client.GetSubscriptionIntroductoryOffersAsync("subscription-1");

        Assert.Equal(new[] { "offer-1", "offer-2" }, offers.Select(static offer => offer.Id));
        Assert.Equal(2, handler.RequestUris.Count);
        Assert.Equal(
            "https://api.appstoreconnect.apple.com/v1/subscriptions/subscription-1/introductoryOffers?cursor=next-page",
            handler.RequestUris[1].ToString());
    }

    [Theory]
    [InlineData(AppStoreConnectSubscriptionOfferDuration.ThreeDays, "THREE_DAYS")]
    [InlineData(AppStoreConnectSubscriptionOfferDuration.TwoWeeks, "TWO_WEEKS")]
    public async Task CreateSubscriptionIntroductoryOffer_PostsFreeTrialRequest(
        AppStoreConnectSubscriptionOfferDuration duration,
        string expectedDuration)
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.Created,
                """
                {
                  "data": {
                    "id": "offer-1",
                    "type": "subscriptionIntroductoryOffers",
                    "attributes": {
                      "duration": "TWO_WEEKS",
                      "offerMode": "FREE_TRIAL",
                      "numberOfPeriods": 1
                    },
                    "relationships": {
                      "subscription": { "data": { "type": "subscriptions", "id": "subscription-1" } }
                    }
                  }
                }
                """));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var offer = await client.CreateSubscriptionIntroductoryOfferAsync(
            "subscription-1",
            duration,
            AppStoreConnectSubscriptionOfferMode.FreeTrial,
            territoryId: "POL");

        Assert.Equal("offer-1", offer.Id);
        Assert.Equal(HttpMethod.Post, Assert.Single(handler.Methods));
        Assert.Equal(
            "https://api.appstoreconnect.apple.com/v1/subscriptionIntroductoryOffers",
            Assert.Single(handler.RequestUris).ToString());
        var body = Assert.Single(handler.RequestBodies);
        Assert.Contains("\"type\":\"subscriptionIntroductoryOffers\"", body, StringComparison.Ordinal);
        Assert.Contains($"\"duration\":\"{expectedDuration}\"", body, StringComparison.Ordinal);
        Assert.Contains("\"offerMode\":\"FREE_TRIAL\"", body, StringComparison.Ordinal);
        Assert.Contains("\"numberOfPeriods\":1", body, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"subscriptions\",\"id\":\"subscription-1\"", body, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"territories\",\"id\":\"POL\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("subscriptionPricePoint", body, StringComparison.Ordinal);
    }
}
