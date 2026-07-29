using System.Net;
using System.Net.Http;
using System.Text.Json;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class AppStoreConnectClientTests
{
    [Fact]
    public async Task GovernancePlan_AccessibilityCreateIncludesPublicationIntent()
    {
        var handler = new SequenceHandler(new SequenceResponse(HttpStatusCode.OK, """{ "data": [] }"""));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var plan = await new AppStoreConnectGovernanceService(client).PlanAsync(CreatePublishableAccessibilitySpec());

        Assert.Collection(
            plan.Changes,
            change => Assert.Equal(AppStoreConnectGovernanceChangeAction.Create, change.Action),
            change => Assert.Equal(AppStoreConnectGovernanceChangeAction.Publish, change.Action));
    }

    [Fact]
    public async Task GovernanceApply_AccessibilityCreatePublishesOnlyAfterReviewedCreation()
    {
        const string empty = """{ "data": [] }""";
        const string created = """{ "data": { "type": "accessibilityDeclarations", "id": "a11y-1", "attributes": { "deviceFamily": "IPHONE", "state": "DRAFT", "supportsVoiceover": true } } }""";
        const string draft = """{ "data": [ { "type": "accessibilityDeclarations", "id": "a11y-1", "attributes": { "deviceFamily": "IPHONE", "state": "DRAFT", "supportsVoiceover": true } } ] }""";
        const string updated = """{ "data": { "type": "accessibilityDeclarations", "id": "a11y-1", "attributes": { "deviceFamily": "IPHONE", "state": "PUBLISHED", "supportsVoiceover": true } } }""";
        const string published = """{ "data": [ { "type": "accessibilityDeclarations", "id": "a11y-1", "attributes": { "deviceFamily": "IPHONE", "state": "PUBLISHED", "supportsVoiceover": true } } ] }""";
        var spec = CreatePublishableAccessibilitySpec();
        var reviewHandler = new SequenceHandler(new SequenceResponse(HttpStatusCode.OK, empty));
        using var reviewHttp = new HttpClient(reviewHandler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var reviewClient = new AppStoreConnectClient(CreateCredential(), reviewHttp);
        var reviewedPlan = await new AppStoreConnectGovernanceService(reviewClient).PlanAsync(spec);

        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK, empty),
            new SequenceResponse(HttpStatusCode.Created, created),
            new SequenceResponse(HttpStatusCode.OK, draft),
            new SequenceResponse(HttpStatusCode.OK, updated),
            new SequenceResponse(HttpStatusCode.OK, published));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var result = await new AppStoreConnectGovernanceService(client).ApplyAsync(new AppStoreConnectGovernanceApplyRequest
        {
            ConfirmApply = true,
            Spec = spec,
            ReviewedPlan = reviewedPlan
        });

        Assert.True(result.Success, string.Join(" ", result.NextActions));
        Assert.Collection(
            result.AppliedChanges,
            change => Assert.Equal(AppStoreConnectGovernanceChangeAction.Create, change.Action),
            change => Assert.Equal(AppStoreConnectGovernanceChangeAction.Publish, change.Action));
        Assert.Equal(new[] { HttpMethod.Get, HttpMethod.Post, HttpMethod.Get, HttpMethod.Patch, HttpMethod.Get }, handler.Methods);
        var bodies = handler.RequestBodies.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        Assert.Equal(2, bodies.Length);
        using var createBody = JsonDocument.Parse(bodies[0]);
        using var publishBody = JsonDocument.Parse(bodies[1]);
        Assert.False(createBody.RootElement.GetProperty("data").GetProperty("attributes").TryGetProperty("publish", out _));
        Assert.True(publishBody.RootElement.GetProperty("data").GetProperty("attributes").GetProperty("publish").GetBoolean());
    }

    [Fact]
    public async Task GovernanceApply_AccessibilityCreateHonorsMaximumChangesBeforePublication()
    {
        const string empty = """{ "data": [] }""";
        const string created = """{ "data": { "type": "accessibilityDeclarations", "id": "a11y-1", "attributes": { "deviceFamily": "IPHONE", "state": "DRAFT", "supportsVoiceover": true } } }""";
        const string draft = """{ "data": [ { "type": "accessibilityDeclarations", "id": "a11y-1", "attributes": { "deviceFamily": "IPHONE", "state": "DRAFT", "supportsVoiceover": true } } ] }""";
        var spec = CreatePublishableAccessibilitySpec();
        var reviewHandler = new SequenceHandler(new SequenceResponse(HttpStatusCode.OK, empty));
        using var reviewHttp = new HttpClient(reviewHandler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var reviewClient = new AppStoreConnectClient(CreateCredential(), reviewHttp);
        var reviewedPlan = await new AppStoreConnectGovernanceService(reviewClient).PlanAsync(spec);
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK, empty),
            new SequenceResponse(HttpStatusCode.Created, created),
            new SequenceResponse(HttpStatusCode.OK, draft));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var result = await new AppStoreConnectGovernanceService(client).ApplyAsync(new AppStoreConnectGovernanceApplyRequest
        {
            ConfirmApply = true,
            MaximumChanges = 1,
            Spec = spec,
            ReviewedPlan = reviewedPlan
        });

        Assert.False(result.Success);
        Assert.Equal(AppStoreConnectGovernanceChangeAction.Create, Assert.Single(result.AppliedChanges).Action);
        Assert.Equal(AppStoreConnectGovernanceChangeAction.Publish, Assert.Single(result.FinalPlan.Changes).Action);
        Assert.Contains("maximum of 1", Assert.Single(result.NextActions), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { HttpMethod.Get, HttpMethod.Post, HttpMethod.Get }, handler.Methods);
    }

    [Fact]
    public async Task GovernanceApply_AccessibilityPublicationFailureReceiptsCompletedCreation()
    {
        const string empty = """{ "data": [] }""";
        const string created = """{ "data": { "type": "accessibilityDeclarations", "id": "a11y-1", "attributes": { "deviceFamily": "IPHONE", "state": "DRAFT", "supportsVoiceover": true } } }""";
        const string draft = """{ "data": [ { "type": "accessibilityDeclarations", "id": "a11y-1", "attributes": { "deviceFamily": "IPHONE", "state": "DRAFT", "supportsVoiceover": true } } ] }""";
        var spec = CreatePublishableAccessibilitySpec();
        var reviewHandler = new SequenceHandler(new SequenceResponse(HttpStatusCode.OK, empty));
        using var reviewHttp = new HttpClient(reviewHandler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var reviewClient = new AppStoreConnectClient(CreateCredential(), reviewHttp);
        var reviewedPlan = await new AppStoreConnectGovernanceService(reviewClient).PlanAsync(spec);
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK, empty),
            new SequenceResponse(HttpStatusCode.Created, created),
            new SequenceResponse(HttpStatusCode.OK, draft),
            new SequenceResponse(HttpStatusCode.UnprocessableEntity,
                """{ "errors": [ { "status": "422", "detail": "Publication rejected" } ] }"""));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var result = await new AppStoreConnectGovernanceService(client).ApplyAsync(new AppStoreConnectGovernanceApplyRequest
        {
            ConfirmApply = true,
            Spec = spec,
            ReviewedPlan = reviewedPlan
        });

        Assert.False(result.Success);
        Assert.Equal(AppStoreConnectGovernanceChangeAction.Create, Assert.Single(result.AppliedChanges).Action);
        Assert.Equal(AppStoreConnectGovernanceChangeAction.Publish, Assert.Single(result.FinalPlan.Changes).Action);
        Assert.Contains("Publication rejected", Assert.Single(result.NextActions), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { HttpMethod.Get, HttpMethod.Post, HttpMethod.Get, HttpMethod.Patch }, handler.Methods);
    }

    [Fact]
    public async Task GovernanceApply_AccessibilityFactUpdateIncludesPublicationInReviewedPlanAndReceipt()
    {
        const string draft = """{ "data": [ { "type": "accessibilityDeclarations", "id": "a11y-1", "attributes": { "deviceFamily": "IPHONE", "state": "DRAFT", "supportsVoiceover": false } } ] }""";
        const string published = """{ "data": [ { "type": "accessibilityDeclarations", "id": "a11y-1", "attributes": { "deviceFamily": "IPHONE", "state": "PUBLISHED", "supportsVoiceover": true } } ] }""";
        const string updated = """{ "data": { "type": "accessibilityDeclarations", "id": "a11y-1", "attributes": { "deviceFamily": "IPHONE", "state": "PUBLISHED", "supportsVoiceover": true } } }""";
        var spec = CreatePublishableAccessibilitySpec();
        var reviewHandler = new SequenceHandler(new SequenceResponse(HttpStatusCode.OK, draft));
        using var reviewHttp = new HttpClient(reviewHandler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var reviewClient = new AppStoreConnectClient(CreateCredential(), reviewHttp);
        var reviewedPlan = await new AppStoreConnectGovernanceService(reviewClient).PlanAsync(spec);
        Assert.Collection(
            reviewedPlan.Changes,
            change => Assert.Equal(AppStoreConnectGovernanceChangeAction.Update, change.Action),
            change => Assert.Equal(AppStoreConnectGovernanceChangeAction.Publish, change.Action));

        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK, draft),
            new SequenceResponse(HttpStatusCode.OK, updated),
            new SequenceResponse(HttpStatusCode.OK, published));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var result = await new AppStoreConnectGovernanceService(client).ApplyAsync(new AppStoreConnectGovernanceApplyRequest
        {
            ConfirmApply = true,
            Spec = spec,
            ReviewedPlan = reviewedPlan
        });

        Assert.True(result.Success, string.Join(" ", result.NextActions));
        Assert.Collection(
            result.AppliedChanges,
            change => Assert.Equal(AppStoreConnectGovernanceChangeAction.Update, change.Action),
            change => Assert.Equal(AppStoreConnectGovernanceChangeAction.Publish, change.Action));
        Assert.Equal(new[] { HttpMethod.Get, HttpMethod.Patch, HttpMethod.Get }, handler.Methods);
        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies, value => !string.IsNullOrWhiteSpace(value)));
        Assert.True(body.RootElement.GetProperty("data").GetProperty("attributes").GetProperty("publish").GetBoolean());
    }

    [Fact]
    public async Task GovernanceApply_RefusesCompoundAccessibilityUpdateBeyondMaximumChanges()
    {
        const string draft = """{ "data": [ { "type": "accessibilityDeclarations", "id": "a11y-1", "attributes": { "deviceFamily": "IPHONE", "state": "DRAFT", "supportsVoiceover": false } } ] }""";
        var spec = CreatePublishableAccessibilitySpec();
        var reviewHandler = new SequenceHandler(new SequenceResponse(HttpStatusCode.OK, draft));
        using var reviewHttp = new HttpClient(reviewHandler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var reviewClient = new AppStoreConnectClient(CreateCredential(), reviewHttp);
        var reviewedPlan = await new AppStoreConnectGovernanceService(reviewClient).PlanAsync(spec);
        var handler = new SequenceHandler(new SequenceResponse(HttpStatusCode.OK, draft));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var result = await new AppStoreConnectGovernanceService(client).ApplyAsync(new AppStoreConnectGovernanceApplyRequest
        {
            ConfirmApply = true,
            MaximumChanges = 1,
            Spec = spec,
            ReviewedPlan = reviewedPlan
        });

        Assert.False(result.Success);
        Assert.Empty(result.AppliedChanges);
        Assert.Equal(2, result.FinalPlan.Changes.Length);
        Assert.Contains("represents 2 reviewed changes", Assert.Single(result.NextActions), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { HttpMethod.Get }, handler.Methods);
    }

    private static AppStoreConnectGovernanceSpec CreatePublishableAccessibilitySpec() => new()
    {
        AppId = "app-1",
        Accessibility =
        [
            new AppStoreConnectAccessibilityDeclarationSpec
            {
                DeviceFamily = "IPHONE",
                SupportsVoiceover = true,
                Publish = true
            }
        ]
    };
}
