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
        var attributes = territory.GetProperty("attributes");
        Assert.True(attributes.GetProperty("available").GetBoolean());
        Assert.Equal(JsonValueKind.Null, attributes.GetProperty("releaseDate").ValueKind);
        Assert.False(attributes.TryGetProperty("preOrderEnabled", out _));
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
    public async Task UpdateTerritoryAvailabilityAsync_TargetsTheV2Resource()
    {
        var handler = new SequenceHandler(new SequenceResponse(HttpStatusCode.OK,
            """{ "data": { "type": "territoryAvailabilities", "id": "territory-1", "attributes": { "available": false }, "relationships": { "territory": { "data": { "type": "territories", "id": "POL" } } } } }"""));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        _ = await client.UpdateTerritoryAvailabilityAsync(
            "territory-1",
            new AppStoreConnectTerritoryAvailabilitySpec { TerritoryId = "POL", Available = false });

        Assert.Equal(HttpMethod.Patch, Assert.Single(handler.Methods));
        Assert.Equal(
            "https://api.appstoreconnect.apple.com/v2/territoryAvailabilities/territory-1",
            Assert.Single(handler.RequestUris).ToString());
        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        var attributes = body.RootElement.GetProperty("data").GetProperty("attributes");
        Assert.False(attributes.GetProperty("available").GetBoolean());
        Assert.Equal(JsonValueKind.Null, attributes.GetProperty("releaseDate").ValueKind);
        Assert.False(attributes.TryGetProperty("preOrderEnabled", out _));
    }

    [Fact]
    public async Task UpdateTerritoryAvailabilityAsync_IncludesExplicitPreorderChoice()
    {
        var handler = new SequenceHandler(new SequenceResponse(HttpStatusCode.OK,
            """{ "data": { "type": "territoryAvailabilities", "id": "territory-1", "attributes": { "available": true, "preOrderEnabled": false }, "relationships": { "territory": { "data": { "type": "territories", "id": "POL" } } } } }"""));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        _ = await client.UpdateTerritoryAvailabilityAsync(
            "territory-1",
            new AppStoreConnectTerritoryAvailabilitySpec
            {
                TerritoryId = "POL",
                Available = true,
                PreOrderEnabled = false
            });

        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        Assert.False(body.RootElement.GetProperty("data").GetProperty("attributes").GetProperty("preOrderEnabled").GetBoolean());
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
    public async Task UpdateSubscriptionAsync_DistinguishesClearedAndUnmanagedReviewNotes()
    {
        var response = new SequenceResponse(HttpStatusCode.OK,
            """{ "data": { "type": "subscriptions", "id": "sub-1", "attributes": { "productId": "com.example.pro", "name": "Pro", "subscriptionPeriod": "ONE_MONTH" } } }""");
        var handler = new SequenceHandler(response, response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var spec = new AppStoreConnectSubscriptionSpec
        {
            ProductId = "com.example.pro",
            Name = "Pro",
            SubscriptionPeriod = "ONE_MONTH",
            ReviewNote = string.Empty
        };

        _ = await client.UpdateSubscriptionAsync("sub-1", spec);
        spec.ReviewNote = null;
        _ = await client.UpdateSubscriptionAsync("sub-1", spec);

        using var clearBody = JsonDocument.Parse(handler.RequestBodies[0]);
        using var unmanagedBody = JsonDocument.Parse(handler.RequestBodies[1]);
        Assert.Equal(string.Empty, clearBody.RootElement.GetProperty("data").GetProperty("attributes").GetProperty("reviewNote").GetString());
        Assert.False(unmanagedBody.RootElement.GetProperty("data").GetProperty("attributes").TryGetProperty("reviewNote", out _));
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
    public void GovernanceConfiguration_AllowsIntroductoryOffersToReuseReviewedPlanTerritories()
    {
        var findings = new AppStoreConnectGovernanceConfiguration().Validate(new AppStoreConnectGovernanceSpec
        {
            AppId = "app-1",
            SubscriptionGroups = [new AppStoreConnectSubscriptionGroupSpec
            {
                ReferenceName = "Pro",
                Subscriptions = [new AppStoreConnectSubscriptionSpec
                {
                    ProductId = "pro.monthly",
                    Name = "Pro Monthly",
                    SubscriptionPeriod = "ONE_MONTH",
                    IntroductoryOffers = [new AppStoreConnectSubscriptionIntroductoryOfferSpec
                    {
                        Duration = "TWO_WEEKS",
                        OfferMode = "FREE_TRIAL",
                        NumberOfPeriods = 1,
                        TerritoriesFromPlanType = "UPFRONT"
                    }],
                    Availabilities = [new AppStoreConnectSubscriptionAvailabilitySpec
                    {
                        PlanType = "UPFRONT",
                        AvailableInNewTerritories = true,
                        TerritoryIds = ["USA", "POL"]
                    }]
                }]
            }]
        });

        Assert.DoesNotContain(findings, finding => finding.IsError);
    }

    [Fact]
    public void GovernanceConfiguration_RejectsUnknownPropertiesInsteadOfSilentlyIgnoringIntent()
    {
        var path = Path.Combine(Path.GetTempPath(), $"powerforge-governance-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path,
                """{ "schemaVersion": 1, "appId": "app-1", "pricing": { "baseTerritoryId": "USA", "prcies": [] } }""");

            var error = Assert.Throws<InvalidOperationException>(() => new AppStoreConnectGovernanceConfiguration().Load(path));

            Assert.Contains("prcies", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void GovernanceConfiguration_LoadsDocumentedSchemaHint()
    {
        var path = Path.Combine(Path.GetTempPath(), $"powerforge-governance-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path,
                """{ "$schema": "../../Schemas/appstore-connect-governance.schema.json", "schemaVersion": 1, "appId": "app-1" }""");

            var spec = new AppStoreConnectGovernanceConfiguration().Load(path);

            Assert.Equal("../../Schemas/appstore-connect-governance.schema.json", spec.Schema);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Theory]
    [InlineData("{ \"schemaVersion\": 1, \"appId\": \"app-1\", \"availability\": { \"territories\": [] } }", "availableInNewTerritories")]
    [InlineData("{ \"schemaVersion\": 1, \"appId\": \"app-1\", \"availability\": { \"availableInNewTerritories\": false, \"territories\": [ { \"territoryId\": \"USA\" } ] } }", "available")]
    [InlineData("{ \"schemaVersion\": 1, \"appId\": \"app-1\", \"encryptionDeclarations\": [ { \"appDescription\": \"Reviewed\", \"containsThirdPartyCryptography\": false, \"availableOnFrenchStore\": false } ] }", "containsProprietaryCryptography")]
    [InlineData("{ \"schemaVersion\": 1, \"appId\": \"app-1\", \"subscriptionGroups\": [ { \"referenceName\": \"Pro\", \"subscriptions\": [ { \"productId\": \"pro\", \"name\": \"Pro\", \"subscriptionPeriod\": \"ONE_MONTH\", \"availabilities\": [ { \"planType\": \"MONTHLY\", \"territoryIds\": [ \"USA\" ] } ] } ] } ] }", "availableInNewTerritories")]
    [InlineData("{ \"schemaVersion\": 1, \"appId\": \"app-1\", \"subscriptionGroups\": [ { \"referenceName\": \"Pro\", \"subscriptions\": [ { \"productId\": \"pro\", \"name\": \"Pro\", \"subscriptionPeriod\": \"ONE_MONTH\", \"availabilities\": [ { \"availableInNewTerritories\": false, \"territoryIds\": [ \"USA\" ] } ] } ] } ] }", "planType")]
    [InlineData("{ \"schemaVersion\": 1, \"appId\": \"app-1\", \"subscriptionGroups\": [ { \"referenceName\": \"Pro\", \"subscriptions\": [ { \"productId\": \"pro\", \"name\": \"Pro\", \"subscriptionPeriod\": \"ONE_MONTH\", \"availabilities\": [ { \"planType\": \"MONTHLY\", \"availableInNewTerritories\": false } ] } ] } ] }", "territoryIds")]
    public void GovernanceConfiguration_RejectsOmittedRequiredFacts(string json, string property)
    {
        var path = Path.Combine(Path.GetTempPath(), $"powerforge-governance-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, json);

            var error = Assert.Throws<InvalidOperationException>(() => new AppStoreConnectGovernanceConfiguration().Load(path));

            Assert.Contains(property, error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
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
    public async Task GovernanceApply_BaseTerritoryReplacementPlansAndReceiptsEveryIncludedPrice()
    {
        var oldSchedule = """{ "data": { "type": "appPriceSchedules", "id": "schedule-1", "relationships": { "baseTerritory": { "data": { "type": "territories", "id": "CAN" } } } } }""";
        var oldPrices = """{ "data": [ { "type": "appPrices", "id": "price-1", "attributes": { "startDate": "2026-08-01", "endDate": null }, "relationships": { "appPricePoint": { "data": { "type": "appPricePoints", "id": "point-1" } }, "territory": { "data": { "type": "territories", "id": "USA" } } } } ] }""";
        var createdSchedule = """{ "data": { "type": "appPriceSchedules", "id": "schedule-2", "relationships": { "baseTerritory": { "data": { "type": "territories", "id": "USA" } } } } }""";
        var newSchedule = """{ "data": { "type": "appPriceSchedules", "id": "schedule-2", "relationships": { "baseTerritory": { "data": { "type": "territories", "id": "USA" } } } } }""";
        var newPrices = """{ "data": [ { "type": "appPrices", "id": "price-1", "attributes": { "startDate": "2026-08-01", "endDate": null }, "relationships": { "appPricePoint": { "data": { "type": "appPricePoints", "id": "point-1" } }, "territory": { "data": { "type": "territories", "id": "USA" } } } }, { "type": "appPrices", "id": "price-2", "attributes": { "startDate": "2026-08-01", "endDate": null }, "relationships": { "appPricePoint": { "data": { "type": "appPricePoints", "id": "point-2" } }, "territory": { "data": { "type": "territories", "id": "GBR" } } } } ] }""";
        var spec = new AppStoreConnectGovernanceSpec
        {
            AppId = "app-1",
            Pricing = new AppStoreConnectAppPricingSpec
            {
                BaseTerritoryId = "USA",
                Prices =
                [
                    new AppStoreConnectAppPriceSpec { TerritoryId = "USA", AppPricePointId = "point-1", StartDate = "2026-08-01" },
                    new AppStoreConnectAppPriceSpec { TerritoryId = "GBR", AppPricePointId = "point-2", StartDate = "2026-08-01" }
                ]
            }
        };
        var reviewHandler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK, oldSchedule),
            new SequenceResponse(HttpStatusCode.OK, oldPrices));
        using var reviewHttp = new HttpClient(reviewHandler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var reviewClient = new AppStoreConnectClient(CreateCredential(), reviewHttp);
        var reviewedPlan = await new AppStoreConnectGovernanceService(reviewClient).PlanAsync(spec);
        Assert.Collection(
            reviewedPlan.Changes,
            change => Assert.Equal("AppPriceSchedule", change.ResourceType),
            change => Assert.Equal(("AppPrice", AppStoreConnectGovernanceChangeAction.Update), (change.ResourceType, change.Action)),
            change => Assert.Equal(("AppPrice", AppStoreConnectGovernanceChangeAction.Create), (change.ResourceType, change.Action)));

        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK, oldSchedule),
            new SequenceResponse(HttpStatusCode.OK, oldPrices),
            new SequenceResponse(HttpStatusCode.Created, createdSchedule),
            new SequenceResponse(HttpStatusCode.OK, newSchedule),
            new SequenceResponse(HttpStatusCode.OK, newPrices));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var result = await new AppStoreConnectGovernanceService(client).ApplyAsync(new AppStoreConnectGovernanceApplyRequest
        {
            ConfirmApply = true,
            Spec = spec,
            ReviewedPlan = reviewedPlan
        });

        Assert.True(result.Success, string.Join(" ", result.NextActions));
        Assert.True(result.FinalPlan.IsConverged);
        Assert.Collection(
            result.AppliedChanges,
            change => Assert.Equal("AppPriceSchedule", change.ResourceType),
            change => Assert.Equal(("AppPrice", AppStoreConnectGovernanceChangeAction.Update), (change.ResourceType, change.Action)),
            change => Assert.Equal(("AppPrice", AppStoreConnectGovernanceChangeAction.Create), (change.ResourceType, change.Action)));
        Assert.Equal(new[] { HttpMethod.Get, HttpMethod.Get, HttpMethod.Post, HttpMethod.Get, HttpMethod.Get }, handler.Methods);
        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies, value => !string.IsNullOrWhiteSpace(value)));
        Assert.Equal(2, body.RootElement.GetProperty("included").GetArrayLength());
    }

    [Fact]
    public async Task GovernancePlan_BlocksAnUnmatchedExplicitSubscriptionGroupIdWithoutCreatingADuplicate()
    {
        var handler = new SequenceHandler(new SequenceResponse(HttpStatusCode.OK,
            """{ "data": [ { "type": "subscriptionGroups", "id": "group-current", "attributes": { "referenceName": "Pro" } } ] }"""));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var plan = await new AppStoreConnectGovernanceService(client).PlanAsync(
            new AppStoreConnectGovernanceSpec
            {
                AppId = "app-1",
                SubscriptionGroups =
                [
                    new AppStoreConnectSubscriptionGroupSpec { Id = "group-stale", ReferenceName = "Pro" }
                ]
            });

        var change = Assert.Single(plan.Changes);
        Assert.Equal(AppStoreConnectGovernanceChangeAction.Blocked, change.Action);
        Assert.Contains("group-current", change.Summary, StringComparison.Ordinal);
        Assert.Equal(HttpMethod.Get, Assert.Single(handler.Methods));
    }

    [Fact]
    public async Task GovernancePlan_DetectsExplicitPreserveCurrentPriceDrift()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK, """{ "data": [ { "type": "subscriptionGroups", "id": "group-1", "attributes": { "referenceName": "Pro" } } ] }"""),
            new SequenceResponse(HttpStatusCode.OK, """{ "data": [] }"""),
            new SequenceResponse(HttpStatusCode.OK, """{ "data": [ { "type": "subscriptions", "id": "sub-1", "attributes": { "productId": "pro.monthly", "name": "Pro Monthly", "subscriptionPeriod": "ONE_MONTH" } } ] }"""),
            new SequenceResponse(HttpStatusCode.OK, """{ "data": [] }"""),
            new SequenceResponse(HttpStatusCode.OK, """{ "data": [ { "type": "subscriptionPrices", "id": "price-1", "attributes": { "planType": "MONTHLY", "preserved": false }, "relationships": { "territory": { "data": { "type": "territories", "id": "USA" } }, "subscriptionPricePoint": { "data": { "type": "subscriptionPricePoints", "id": "point-1" } } } } ] }"""));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var plan = await new AppStoreConnectGovernanceService(client).PlanAsync(new AppStoreConnectGovernanceSpec
        {
            AppId = "app-1",
            SubscriptionGroups = [new AppStoreConnectSubscriptionGroupSpec
            {
                ReferenceName = "Pro",
                Subscriptions = [new AppStoreConnectSubscriptionSpec
                {
                    ProductId = "pro.monthly", Name = "Pro Monthly", SubscriptionPeriod = "ONE_MONTH",
                    Prices = [new AppStoreConnectSubscriptionPriceSpec { TerritoryId = "USA", SubscriptionPricePointId = "point-1", PlanType = "MONTHLY", PreserveCurrentPrice = true }]
                }]
            }]
        });

        Assert.Equal("SubscriptionPrice", Assert.Single(plan.Changes).ResourceType);
    }

    [Fact]
    public async Task GovernancePlan_ConvergesDeclaredIntroductoryFreeTrial()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK, """{ "data": [ { "type": "subscriptionGroups", "id": "group-1", "attributes": { "referenceName": "Pro" } } ] }"""),
            new SequenceResponse(HttpStatusCode.OK, """{ "data": [] }"""),
            new SequenceResponse(HttpStatusCode.OK, """{ "data": [ { "type": "subscriptions", "id": "sub-1", "attributes": { "productId": "pro.monthly", "name": "Pro Monthly", "subscriptionPeriod": "ONE_MONTH" } } ] }"""),
            new SequenceResponse(HttpStatusCode.OK, """{ "data": [] }"""),
            new SequenceResponse(HttpStatusCode.OK, """{ "data": [] }"""),
            new SequenceResponse(HttpStatusCode.OK, """{ "data": [ { "type": "subscriptionIntroductoryOffers", "id": "offer-1", "attributes": { "duration": "TWO_WEEKS", "offerMode": "FREE_TRIAL", "numberOfPeriods": 1 }, "relationships": { "subscription": { "data": { "type": "subscriptions", "id": "sub-1" } }, "territory": { "data": { "type": "territories", "id": "USA" } } } } ] }"""));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var plan = await new AppStoreConnectGovernanceService(client).PlanAsync(new AppStoreConnectGovernanceSpec
        {
            AppId = "app-1",
            SubscriptionGroups = [new AppStoreConnectSubscriptionGroupSpec
            {
                ReferenceName = "Pro",
                Subscriptions = [new AppStoreConnectSubscriptionSpec
                {
                    ProductId = "pro.monthly", Name = "Pro Monthly", SubscriptionPeriod = "ONE_MONTH",
                    IntroductoryOffers = [new AppStoreConnectSubscriptionIntroductoryOfferSpec { Duration = "TWO_WEEKS", OfferMode = "FREE_TRIAL", NumberOfPeriods = 1, TerritoryIds = ["USA"] }]
                }]
            }]
        });

        Assert.True(plan.IsConverged);
        Assert.Empty(plan.Changes);
        Assert.Contains(handler.RequestUris, uri => uri.AbsolutePath.EndsWith("/subscriptions/sub-1/introductoryOffers", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GovernanceApply_CreatesAndConvergesDeclaredIntroductoryFreeTrial()
    {
        var handler = new IntroductoryOfferGovernanceHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var result = await new AppStoreConnectGovernanceService(client).ApplyAsync(new AppStoreConnectGovernanceApplyRequest
        {
            ConfirmApply = true,
            MaximumChanges = 1,
            Spec = new AppStoreConnectGovernanceSpec
            {
                AppId = "app-1",
                SubscriptionGroups = [new AppStoreConnectSubscriptionGroupSpec
                {
                    ReferenceName = "Pro",
                    Subscriptions = [new AppStoreConnectSubscriptionSpec
                    {
                        ProductId = "pro.monthly", Name = "Pro Monthly", SubscriptionPeriod = "ONE_MONTH",
                        IntroductoryOffers = [new AppStoreConnectSubscriptionIntroductoryOfferSpec
                        {
                            Duration = "TWO_WEEKS", OfferMode = "FREE_TRIAL", NumberOfPeriods = 1, TerritoryIds = ["USA"]
                        }]
                    }]
                }]
            }
        });

        Assert.True(result.Success);
        Assert.True(result.FinalPlan.IsConverged);
        Assert.Equal("SubscriptionIntroductoryOffer", Assert.Single(result.AppliedChanges).ResourceType);
        Assert.Equal(1, handler.PostCount);
    }

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

    private sealed class IntroductoryOfferGovernanceHandler : HttpMessageHandler
    {
        private bool _offerCreated;
        public int PostCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path.EndsWith("/subscriptionIntroductoryOffers", StringComparison.Ordinal))
            {
                PostCount++;
                _offerCreated = true;
                return Response(HttpStatusCode.Created, OfferJson);
            }
            if (path.EndsWith("/subscriptionGroups", StringComparison.Ordinal))
                return Response(HttpStatusCode.OK, """{ "data": [ { "type": "subscriptionGroups", "id": "group-1", "attributes": { "referenceName": "Pro" } } ] }""");
            if (path.EndsWith("/group-1/subscriptions", StringComparison.Ordinal))
                return Response(HttpStatusCode.OK, """{ "data": [ { "type": "subscriptions", "id": "sub-1", "attributes": { "productId": "pro.monthly", "name": "Pro Monthly", "subscriptionPeriod": "ONE_MONTH" } } ] }""");
            if (path.EndsWith("/sub-1/introductoryOffers", StringComparison.Ordinal))
                return Response(HttpStatusCode.OK, _offerCreated ? $"{{ \"data\": [ {OfferJsonDocument} ] }}" : """{ "data": [] }""");
            return Response(HttpStatusCode.OK, """{ "data": [] }""");
        }

        private const string OfferJsonDocument = "{ \"type\": \"subscriptionIntroductoryOffers\", \"id\": \"offer-1\", \"attributes\": { \"duration\": \"TWO_WEEKS\", \"offerMode\": \"FREE_TRIAL\", \"numberOfPeriods\": 1 }, \"relationships\": { \"subscription\": { \"data\": { \"type\": \"subscriptions\", \"id\": \"sub-1\" } }, \"territory\": { \"data\": { \"type\": \"territories\", \"id\": \"USA\" } } } }";
        private const string OfferJson = "{ \"data\": " + OfferJsonDocument + " }";

        private static Task<HttpResponseMessage> Response(HttpStatusCode status, string body) => Task.FromResult(
            new HttpResponseMessage(status) { Content = new StringContent(body) });
    }
}
