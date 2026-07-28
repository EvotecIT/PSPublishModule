using System.Net;
using System.Net.Http;
using System.Text.Json;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class AppStoreConnectClientTests
{
    [Fact]
    public async Task CreateAppPriceScheduleAsync_UsesOfficialRelationshipAndIncludedShape()
    {
        var handler = new SequenceHandler(new SequenceResponse(HttpStatusCode.Created,
            """{ "data": { "type": "appPriceSchedules", "id": "schedule-1" } }"""));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var result = await client.CreateAppPriceScheduleAsync("app-1", new AppStoreConnectAppPricingSpec
        {
            BaseTerritoryId = "USA",
            Prices =
            [
                new AppStoreConnectAppPriceSpec
                {
                    TerritoryId = "USA",
                    AppPricePointId = "price-point-1",
                    StartDate = "2026-08-01"
                }
            ]
        });

        Assert.Equal("schedule-1", result.Id);
        Assert.Equal(HttpMethod.Post, Assert.Single(handler.Methods));
        Assert.Equal("https://api.appstoreconnect.apple.com/v1/appPriceSchedules", Assert.Single(handler.RequestUris).ToString());
        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        var data = body.RootElement.GetProperty("data");
        Assert.Equal("app-1", data.GetProperty("relationships").GetProperty("app").GetProperty("data").GetProperty("id").GetString());
        Assert.Equal("USA", data.GetProperty("relationships").GetProperty("baseTerritory").GetProperty("data").GetProperty("id").GetString());
        var included = Assert.Single(body.RootElement.GetProperty("included").EnumerateArray());
        Assert.Equal("price-point-1", included.GetProperty("relationships").GetProperty("appPricePoint").GetProperty("data").GetProperty("id").GetString());
    }

    [Fact]
    public async Task GetAppPriceScheduleAsync_TreatsMissingManualPricesRelationshipAsAnEmptyFreeSchedule()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK,
                """{ "data": { "type": "appPriceSchedules", "id": "app-1", "relationships": { "baseTerritory": { "data": { "type": "territories", "id": "USA" } } } } }"""),
            new SequenceResponse(HttpStatusCode.NotFound,
                """{ "errors": [ { "status": "404", "code": "NOT_FOUND" } ] }"""));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var schedule = await client.GetAppPriceScheduleAsync("app-1");

        Assert.NotNull(schedule);
        Assert.Equal("USA", schedule.BaseTerritoryId);
        Assert.Empty(schedule.Prices);
        Assert.Equal(2, handler.RequestUris.Count);
        Assert.Contains("appPriceSchedules/app-1/manualPrices", handler.RequestUris[1].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAppAvailabilityAsync_TargetsV2AndCarriesExplicitTerritories()
    {
        var handler = new SequenceHandler(new SequenceResponse(HttpStatusCode.Created,
            """{ "data": { "type": "appAvailabilities", "id": "availability-1", "attributes": { "availableInNewTerritories": false } } }"""));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var result = await client.CreateAppAvailabilityAsync("app-1", new AppStoreConnectAppAvailabilitySpec
        {
            AvailableInNewTerritories = false,
            Territories =
            [
                new AppStoreConnectTerritoryAvailabilitySpec { TerritoryId = "POL", Available = true }
            ]
        });

        Assert.Equal("availability-1", result.Id);
        Assert.Equal("https://api.appstoreconnect.apple.com/v2/appAvailabilities", Assert.Single(handler.RequestUris).ToString());
        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        var territory = Assert.Single(body.RootElement.GetProperty("included").EnumerateArray());
        Assert.True(territory.GetProperty("attributes").GetProperty("available").GetBoolean());
        Assert.Equal("POL", territory.GetProperty("relationships").GetProperty("territory").GetProperty("data").GetProperty("id").GetString());
    }

    [Fact]
    public async Task GetAppAvailabilityAsync_HydratesAllTerritoriesThroughTheV2Relationship()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK,
                """{ "data": { "type": "appAvailabilities", "id": "availability-1", "attributes": { "availableInNewTerritories": true } } }"""),
            new SequenceResponse(HttpStatusCode.OK,
                """{ "data": [ { "type": "territoryAvailabilities", "id": "territory-1", "attributes": { "available": true }, "relationships": { "territory": { "data": { "type": "territories", "id": "POL" } } } } ] }"""));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var availability = await client.GetAppAvailabilityAsync("app-1");

        Assert.NotNull(availability);
        Assert.True(availability.AvailableInNewTerritories);
        Assert.Equal("POL", Assert.Single(availability.Territories).TerritoryId);
        Assert.Equal(2, handler.RequestUris.Count);
        Assert.Equal("https://api.appstoreconnect.apple.com/v1/apps/app-1/appAvailabilityV2", handler.RequestUris[0].ToString());
        Assert.Equal("https://api.appstoreconnect.apple.com/v2/appAvailabilities/availability-1/territoryAvailabilities?include=territory&limit=200", handler.RequestUris[1].ToString());
    }

    [Fact]
    public async Task CreateAccessibilityDeclarationAsync_OmitsPublishUntilFollowupPatch()
    {
        var handler = new SequenceHandler(new SequenceResponse(HttpStatusCode.Created,
            """{ "data": { "type": "accessibilityDeclarations", "id": "a11y-1", "attributes": { "deviceFamily": "IPHONE", "state": "DRAFT" } } }"""));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        _ = await client.CreateAccessibilityDeclarationAsync("app-1", new AppStoreConnectAccessibilityDeclarationSpec
        {
            DeviceFamily = "IPHONE",
            SupportsVoiceover = true,
            Publish = true
        });

        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        var attributes = body.RootElement.GetProperty("data").GetProperty("attributes");
        Assert.Equal("IPHONE", attributes.GetProperty("deviceFamily").GetString());
        Assert.True(attributes.GetProperty("supportsVoiceover").GetBoolean());
        Assert.False(attributes.TryGetProperty("publish", out _));
    }

    [Fact]
    public async Task CreateSubscriptionAsync_UsesStableProductAndGroupRelationship()
    {
        var handler = new SequenceHandler(new SequenceResponse(HttpStatusCode.Created,
            """{ "data": { "type": "subscriptions", "id": "sub-1", "attributes": { "productId": "com.example.pro", "name": "Pro", "subscriptionPeriod": "ONE_MONTH", "groupLevel": 1 } } }"""));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var result = await client.CreateSubscriptionAsync("group-1", new AppStoreConnectSubscriptionSpec
        {
            ProductId = "com.example.pro",
            Name = "Pro",
            SubscriptionPeriod = "ONE_MONTH",
            GroupLevel = 1,
            FamilySharable = true
        });

        Assert.Equal("sub-1", result.Id);
        Assert.Equal(1, result.GroupLevel);
        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        var data = body.RootElement.GetProperty("data");
        Assert.Equal("com.example.pro", data.GetProperty("attributes").GetProperty("productId").GetString());
        Assert.Equal("group-1", data.GetProperty("relationships").GetProperty("group").GetProperty("data").GetProperty("id").GetString());
    }

    [Fact]
    public async Task CreateSubscriptionPriceAsync_PreservesExplicitCommercialChoice()
    {
        var handler = new SequenceHandler(new SequenceResponse(HttpStatusCode.Created,
            """{ "data": { "type": "subscriptionPrices", "id": "price-1", "attributes": { "startDate": "2026-08-01", "planType": "MONTHLY" }, "relationships": { "territory": { "data": { "type": "territories", "id": "POL" } }, "subscriptionPricePoint": { "data": { "type": "subscriptionPricePoints", "id": "point-1" } } } } }"""));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var result = await client.CreateSubscriptionPriceAsync("sub-1", new AppStoreConnectSubscriptionPriceSpec
        {
            TerritoryId = "POL",
            SubscriptionPricePointId = "point-1",
            StartDate = "2026-08-01",
            PlanType = "monthly"
        });

        Assert.Equal("POL", result.TerritoryId);
        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        var data = body.RootElement.GetProperty("data");
        Assert.Equal("MONTHLY", data.GetProperty("attributes").GetProperty("planType").GetString());
        Assert.Equal("point-1", data.GetProperty("relationships").GetProperty("subscriptionPricePoint").GetProperty("data").GetProperty("id").GetString());
    }

    [Fact]
    public async Task GetSubscriptionPlanAvailabilitiesAsync_HydratesTheCompleteTerritoryRelationship()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK,
                """{ "data": [ { "type": "subscriptionPlanAvailabilities", "id": "availability-1", "attributes": { "planType": "MONTHLY", "availableInNewTerritories": false } } ] }"""),
            new SequenceResponse(HttpStatusCode.OK,
                """{ "data": [ { "type": "territories", "id": "USA" }, { "type": "territories", "id": "POL" } ] }"""));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var availability = Assert.Single(await client.GetSubscriptionPlanAvailabilitiesAsync("sub-1"));

        Assert.Equal(new[] { "USA", "POL" }, availability.TerritoryIds);
        Assert.Equal(2, handler.RequestUris.Count);
        Assert.Contains("subscriptionPlanAvailabilities/availability-1/availableTerritories", handler.RequestUris[1].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void GovernanceConfiguration_RejectsUnsafeOrAmbiguousDeclarations()
    {
        var findings = new AppStoreConnectGovernanceConfiguration().Validate(new AppStoreConnectGovernanceSpec
        {
            AppId = "app-1",
            Accessibility =
            [
                new AppStoreConnectAccessibilityDeclarationSpec { DeviceFamily = "CARPLAY" }
            ],
            SubscriptionGroups =
            [
                new AppStoreConnectSubscriptionGroupSpec
                {
                    ReferenceName = "Pro",
                    Subscriptions =
                    [
                        new AppStoreConnectSubscriptionSpec { ProductId = "pro", Name = "Pro", SubscriptionPeriod = "FOREVER" }
                    ]
                }
            ]
        });

        Assert.Contains(findings, finding => finding.Code == "Governance.Accessibility.DeviceFamily");
        Assert.Contains(findings, finding => finding.Code == "Governance.Accessibility.Empty");
        Assert.Contains(findings, finding => finding.Code == "Governance.Subscriptions.Period");
    }

    [Fact]
    public async Task GovernancePlan_IsReadOnlyAndConvergedForMatchingPriceSchedule()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK,
            """
            {
              "data": {
                "type": "appPriceSchedules",
                "id": "schedule-1",
                "relationships": { "baseTerritory": { "data": { "type": "territories", "id": "USA" } } }
              }
            }
            """),
            new SequenceResponse(HttpStatusCode.OK,
            """
            {
              "data": [
                {
                  "type": "appPrices",
                  "id": "price-1",
                  "attributes": { "startDate": "2026-08-01", "endDate": null },
                  "relationships": {
                    "appPricePoint": { "data": { "type": "appPricePoints", "id": "point-1" } },
                    "territory": { "data": { "type": "territories", "id": "USA" } }
                  }
                }
              ]
            }
            """));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var service = new AppStoreConnectGovernanceService(client);

        var plan = await service.PlanAsync(new AppStoreConnectGovernanceSpec
        {
            AppId = "app-1",
            Pricing = new AppStoreConnectAppPricingSpec
            {
                BaseTerritoryId = "USA",
                Prices =
                [
                    new AppStoreConnectAppPriceSpec { TerritoryId = "USA", AppPricePointId = "point-1", StartDate = "2026-08-01" }
                ]
            }
        });

        Assert.True(plan.IsConverged);
        Assert.Empty(plan.Changes);
        Assert.Equal(new[] { HttpMethod.Get, HttpMethod.Get }, handler.Methods);
        Assert.Contains("appPriceSchedules/schedule-1/manualPrices", handler.RequestUris[1].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GovernanceApply_RequiresExplicitConfirmationWithoutCallingApple()
    {
        var handler = new SequenceHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var service = new AppStoreConnectGovernanceService(client);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(
            new AppStoreConnectGovernanceApplyRequest
            {
                Spec = new AppStoreConnectGovernanceSpec { AppId = "app-1", Accessibility = [new AppStoreConnectAccessibilityDeclarationSpec { DeviceFamily = "IPHONE", SupportsVoiceover = true }] }
            }));

        Assert.Contains("ConfirmApply=true", error.Message, StringComparison.Ordinal);
        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task GovernanceApply_StopsInsteadOfDuplicatingAnEventuallyConsistentCreate()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK, """{ "data": [] }"""),
            new SequenceResponse(HttpStatusCode.Created,
                """{ "data": { "type": "accessibilityDeclarations", "id": "a11y-1", "attributes": { "deviceFamily": "IPHONE", "state": "DRAFT", "supportsVoiceover": true } } }"""),
            new SequenceResponse(HttpStatusCode.OK, """{ "data": [] }"""));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var result = await new AppStoreConnectGovernanceService(client).ApplyAsync(
            new AppStoreConnectGovernanceApplyRequest
            {
                ConfirmApply = true,
                Spec = new AppStoreConnectGovernanceSpec
                {
                    AppId = "app-1",
                    Accessibility =
                    [
                        new AppStoreConnectAccessibilityDeclarationSpec { DeviceFamily = "IPHONE", SupportsVoiceover = true }
                    ]
                }
            });

        Assert.False(result.Success);
        Assert.Single(result.AppliedChanges);
        Assert.Contains("prevent a duplicate", Assert.Single(result.NextActions), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { HttpMethod.Get, HttpMethod.Post, HttpMethod.Get }, handler.Methods);
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
}
