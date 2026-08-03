using System.Net;
using System.Net.Http;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class AppStoreConnectClientTests
{
    [Fact]
    public void GovernanceConfiguration_RejectsSubscriptionLocalizationDescriptionsOverAppleLimit()
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
                    Localizations = [new AppStoreConnectSubscriptionLocalizationSpec
                    {
                        Locale = "en-US",
                        Name = "Pro Monthly",
                        Description = new string('x', 56)
                    }]
                }]
            }]
        });

        var finding = Assert.Single(findings, item => item.Code == "Governance.Subscriptions.LocalizationDescriptionTooLong");
        Assert.True(finding.IsError);
        Assert.EndsWith(".description", finding.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void GovernanceConfiguration_ValidatesNormalizedSubscriptionLocalizationDescriptionLength()
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
                    Localizations = [new AppStoreConnectSubscriptionLocalizationSpec
                    {
                        Locale = "en-US",
                        Name = "Pro Monthly",
                        Description = " " + new string('x', 55) + " "
                    }]
                }]
            }]
        });

        Assert.DoesNotContain(findings, item => item.Code == "Governance.Subscriptions.LocalizationDescriptionTooLong");
    }

    [Fact]
    public async Task UpdateSubscriptionLocalizationAsync_RejectsDescriptionsOverAppleLimitBeforeHttp()
    {
        var handler = new SequenceHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => client.UpdateSubscriptionLocalizationAsync(
            "localization-1",
            new AppStoreConnectSubscriptionLocalizationSpec
            {
                Locale = "en-US",
                Name = "Pro Monthly",
                Description = new string('x', 56)
            }));

        Assert.Contains("55", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Methods);
    }

    [Fact]
    public async Task UpdateSubscriptionLocalizationAsync_AcceptsAndSendsNormalizedDescriptionAtAppleLimit()
    {
        var handler = new SequenceHandler(new SequenceResponse(HttpStatusCode.OK,
            """{ "data": { "type": "subscriptionLocalizations", "id": "localization-1", "attributes": { "locale": "en-US", "name": "Pro Monthly", "description": "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx", "state": "PREPARE_FOR_SUBMISSION" } } }"""));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var result = await client.UpdateSubscriptionLocalizationAsync(
            "localization-1",
            new AppStoreConnectSubscriptionLocalizationSpec
            {
                Locale = "en-US",
                Name = "Pro Monthly",
                Description = " " + new string('x', 55) + " "
            });

        Assert.Equal(new string('x', 55), result.Description);
        using var body = System.Text.Json.JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        Assert.Equal(new string('x', 55), body.RootElement.GetProperty("data").GetProperty("attributes").GetProperty("description").GetString());
    }

    [Theory]
    [InlineData("APPROVED")]
    [InlineData("WAITING_FOR_REVIEW")]
    [InlineData("FUTURE_STATE")]
    public async Task GovernancePlan_BlocksDriftForImmutableSubscriptionLocalizationBeforeMutation(string state)
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK, """{ "data": [ { "type": "subscriptionGroups", "id": "group-1", "attributes": { "referenceName": "Pro" } } ] }"""),
            new SequenceResponse(HttpStatusCode.OK, """{ "data": [] }"""),
            new SequenceResponse(HttpStatusCode.OK, """{ "data": [ { "type": "subscriptions", "id": "sub-1", "attributes": { "productId": "pro.monthly", "name": "Pro Monthly", "subscriptionPeriod": "ONE_MONTH" } } ] }"""),
            new SequenceResponse(HttpStatusCode.OK, $$"""{ "data": [ { "type": "subscriptionLocalizations", "id": "localization-1", "attributes": { "locale": "en-US", "name": "Pro Monthly", "description": "Accepted description", "state": "{{state}}" } } ] }"""),
            new SequenceResponse(HttpStatusCode.OK, """{ "data": [] }"""));
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
                    ProductId = "pro.monthly",
                    Name = "Pro Monthly",
                    SubscriptionPeriod = "ONE_MONTH",
                    Localizations = [new AppStoreConnectSubscriptionLocalizationSpec
                    {
                        Locale = "en-US",
                        Name = "Pro Monthly",
                        Description = "Replacement description"
                    }]
                }]
            }]
        });

        var change = Assert.Single(plan.Changes);
        Assert.Equal("SubscriptionLocalization", change.ResourceType);
        Assert.Equal(AppStoreConnectGovernanceChangeAction.Blocked, change.Action);
        Assert.Contains(state, change.Summary, StringComparison.Ordinal);
        Assert.All(handler.Methods, method => Assert.Equal(HttpMethod.Get, method));
    }

    [Theory]
    [InlineData("APPROVED")]
    [InlineData("WAITING_FOR_REVIEW")]
    [InlineData("FUTURE_STATE")]
    public async Task GovernancePlan_BlocksDriftForImmutableSubscriptionGroupLocalizationBeforeMutation(string state)
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK, """{ "data": [ { "type": "subscriptionGroups", "id": "group-1", "attributes": { "referenceName": "Pro" } } ] }"""),
            new SequenceResponse(HttpStatusCode.OK, $$"""{ "data": [ { "type": "subscriptionGroupLocalizations", "id": "group-localization-1", "attributes": { "locale": "en-US", "name": "Accepted Pro", "state": "{{state}}" } } ] }"""),
            new SequenceResponse(HttpStatusCode.OK, """{ "data": [] }"""));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var plan = await new AppStoreConnectGovernanceService(client).PlanAsync(new AppStoreConnectGovernanceSpec
        {
            AppId = "app-1",
            SubscriptionGroups = [new AppStoreConnectSubscriptionGroupSpec
            {
                ReferenceName = "Pro",
                Localizations = [new AppStoreConnectSubscriptionGroupLocalizationSpec
                {
                    Locale = "en-US",
                    Name = "Replacement Pro"
                }]
            }]
        });

        var change = Assert.Single(plan.Changes);
        Assert.Equal("SubscriptionGroupLocalization", change.ResourceType);
        Assert.Equal(AppStoreConnectGovernanceChangeAction.Blocked, change.Action);
        Assert.Contains(state, change.Summary, StringComparison.Ordinal);
        Assert.All(handler.Methods, method => Assert.Equal(HttpMethod.Get, method));
    }
}
