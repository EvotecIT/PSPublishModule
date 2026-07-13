using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class AppStoreConnectClientTests
{
    [Fact]
    public async Task GetSubscriptionPrices_ParsesTerritoryAndPricePoint()
    {
        var handler = new RecordingHandler(
            """
            {
              "data": [
                {
                  "id": "price-1",
                  "type": "subscriptionPrices",
                  "attributes": {
                    "startDate": "2026-07-01",
                    "preserved": false,
                    "planType": "MONTHLY"
                  },
                  "relationships": {
                    "territory": { "data": { "type": "territories", "id": "POL" } },
                    "subscriptionPricePoint": { "data": { "type": "subscriptionPricePoints", "id": "point-1" } }
                  }
                }
              ]
            }
            """);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var prices = await client.GetSubscriptionPricesAsync("subscription-1");

        var price = Assert.Single(prices);
        Assert.Equal("price-1", price.Id);
        Assert.Equal("2026-07-01", price.StartDate);
        Assert.False(price.Preserved);
        Assert.Equal("MONTHLY", price.PlanType);
        Assert.Equal("POL", price.TerritoryId);
        Assert.Equal("point-1", price.SubscriptionPricePointId);
        Assert.Equal(
            "https://api.appstoreconnect.apple.com/v1/subscriptions/subscription-1/prices?include=territory%2CsubscriptionPricePoint&limit=200",
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

    [Theory]
    [InlineData(AppStoreConnectSubscriptionOfferDuration.OneDay, "ONE_DAY")]
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
