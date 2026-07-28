using System.Net;
using System.Net.Http;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class AppStoreConnectClientTests
{
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
    public async Task GovernanceApply_RejectsStaleReviewedPlanBeforeMutation()
    {
        var handler = new SequenceHandler(new SequenceResponse(HttpStatusCode.OK, """{ "data": [] }"""));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var result = await new AppStoreConnectGovernanceService(client).ApplyAsync(
            new AppStoreConnectGovernanceApplyRequest
            {
                ConfirmApply = true,
                Spec = new AppStoreConnectGovernanceSpec
                {
                    AppId = "app-1",
                    Accessibility = [new AppStoreConnectAccessibilityDeclarationSpec { DeviceFamily = "IPHONE", SupportsVoiceover = true }]
                },
                ReviewedPlan = new AppStoreConnectGovernancePlan
                {
                    AppId = "app-1",
                    Changes =
                    [
                        new AppStoreConnectGovernanceChange
                        {
                            Section = "Accessibility",
                            ResourceType = "AccessibilityDeclaration",
                            Key = "IPAD",
                            Action = AppStoreConnectGovernanceChangeAction.Create,
                            Summary = "Create reviewed accessibility facts for 'IPAD'."
                        }
                    ]
                }
            });

        Assert.False(result.Success);
        Assert.Empty(result.AppliedChanges);
        Assert.Contains("no longer matches the reviewed governance plan", Assert.Single(result.NextActions), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpMethod.Get, Assert.Single(handler.Methods));
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
    public async Task GovernanceApply_ReturnsSuccessWhenTheFinalAllowedChangeConverges()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK, """{ "data": [] }"""),
            new SequenceResponse(HttpStatusCode.Created,
                """{ "data": { "type": "accessibilityDeclarations", "id": "a11y-1", "attributes": { "deviceFamily": "IPHONE", "state": "DRAFT", "supportsVoiceover": true } } }"""),
            new SequenceResponse(HttpStatusCode.OK,
                """{ "data": [ { "type": "accessibilityDeclarations", "id": "a11y-1", "attributes": { "deviceFamily": "IPHONE", "state": "DRAFT", "supportsVoiceover": true } } ] }"""));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var result = await new AppStoreConnectGovernanceService(client).ApplyAsync(
            new AppStoreConnectGovernanceApplyRequest
            {
                ConfirmApply = true,
                MaximumChanges = 1,
                ReviewedPlan = new AppStoreConnectGovernancePlan
                {
                    AppId = "app-1",
                    Changes =
                    [
                        new AppStoreConnectGovernanceChange
                        {
                            Section = "Accessibility",
                            ResourceType = "AccessibilityDeclaration",
                            Key = "IPHONE",
                            Action = AppStoreConnectGovernanceChangeAction.Create,
                            Summary = "Create reviewed accessibility facts for 'IPHONE'."
                        }
                    ]
                },
                Spec = new AppStoreConnectGovernanceSpec
                {
                    AppId = "app-1",
                    Accessibility = [new AppStoreConnectAccessibilityDeclarationSpec { DeviceFamily = "IPHONE", SupportsVoiceover = true }]
                }
            });

        Assert.True(result.Success);
        Assert.Single(result.AppliedChanges);
        Assert.True(result.FinalPlan.IsConverged);
        Assert.Equal(new[] { HttpMethod.Get, HttpMethod.Post, HttpMethod.Get }, handler.Methods);
    }

    [Fact]
    public async Task GovernanceApply_RefusesEveryMutationWhenAnyPlannedChangeIsBlocked()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK,
                """{ "data": { "type": "appAvailabilities", "id": "availability-1", "attributes": { "availableInNewTerritories": true } } }"""),
            new SequenceResponse(HttpStatusCode.OK, """{ "data": [] }"""),
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
                    Availability = new AppStoreConnectAppAvailabilitySpec { AvailableInNewTerritories = false },
                    Accessibility = [new AppStoreConnectAccessibilityDeclarationSpec { DeviceFamily = "IPHONE", SupportsVoiceover = true }]
                }
            });

        Assert.False(result.Success);
        Assert.Empty(result.AppliedChanges);
        Assert.False(result.FinalPlan.CanApply);
        Assert.Equal(1, result.FinalPlan.BlockedCount);
        Assert.Equal(new[] { HttpMethod.Get, HttpMethod.Get, HttpMethod.Get }, handler.Methods);
    }
}
