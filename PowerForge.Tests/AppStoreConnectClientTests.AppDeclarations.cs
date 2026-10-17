using System.Net;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class AppStoreConnectClientTests
{
    [Fact]
    public async Task AgeQuestionnaire_PreservesUnansweredValuesAndSendsOnlyExplicitAnswers()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK, """{"data":{"type":"ageRatingDeclarations","id":"age-1","attributes":{"advertising":null,"messagingAndChat":false}}}"""),
            new SequenceResponse(HttpStatusCode.OK, """{"data":{"type":"ageRatingDeclarations","id":"age-1","attributes":{"advertising":false,"profanityOrCrudeHumor":"NONE"}}}"""));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var before = await client.GetAgeRatingDeclarationAsync("info-1");
        Assert.Null(before!.Answers.Advertising);
        Assert.False(before.Answers.MessagingAndChat);
        var after = await client.UpdateAgeRatingDeclarationAsync(before.Id, new AppStoreConnectAgeRatingAnswers
        {
            Advertising = false, ProfanityOrCrudeHumor = "NONE"
        });
        Assert.False(after.Answers.Advertising);
        Assert.Equal("NONE", after.Answers.ProfanityOrCrudeHumor);
        Assert.EndsWith("/appInfos/info-1/ageRatingDeclaration", handler.RequestUris[0].AbsolutePath, StringComparison.Ordinal);
        Assert.EndsWith("/ageRatingDeclarations/age-1", handler.RequestUris[1].AbsolutePath, StringComparison.Ordinal);
        using var body = JsonDocument.Parse(handler.RequestBodies[1]);
        var data = body.RootElement.GetProperty("data");
        Assert.Equal("age-1", data.GetProperty("id").GetString());
        Assert.Equal(2, data.GetProperty("attributes").EnumerateObject().Count());
        Assert.False(data.GetProperty("attributes").GetProperty("advertising").GetBoolean());
        Assert.Equal(new HttpMethod("PATCH"), handler.Methods[1]);
    }

    [Fact]
    public async Task AgeQuestionnaire_RejectsEmptyOrInvalidAnswersBeforeSending()
    {
        var handler = new SequenceHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        await Assert.ThrowsAsync<ArgumentException>(() => client.UpdateAgeRatingDeclarationAsync("age-1", new()));
        await Assert.ThrowsAsync<ArgumentException>(() => client.UpdateAgeRatingDeclarationAsync("age-1", new() { ViolenceRealistic = "safe" }));
        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task AppDeclarations_BindExactResourcesAndLeaveOtherAttributesUntouched()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK, """{"data":{"type":"appInfos","id":"info-1"}}"""),
            new SequenceResponse(HttpStatusCode.OK, """{"data":{"type":"appInfos","id":"info-1","attributes":{"appStoreAgeRating":"FOUR_PLUS"},"relationships":{"primaryCategory":{"data":{"type":"appCategories","id":"PRODUCTIVITY"}},"secondaryCategory":{"data":{"type":"appCategories","id":"UTILITIES"}}}}}"""),
            new SequenceResponse(HttpStatusCode.OK, """{"data":{"type":"apps","id":"app-1","attributes":{"contentRightsDeclaration":"USES_THIRD_PARTY_CONTENT"}}}"""));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        await client.UpdateAppCategoriesAsync("info-1", primaryCategoryId: "PRODUCTIVITY");
        var info = await client.GetAppInformationAsync("info-1");
        Assert.Equal("PRODUCTIVITY", info!.PrimaryCategoryId);
        Assert.Equal("UTILITIES", info.SecondaryCategoryId);
        Assert.Equal("FOUR_PLUS", info.AppStoreAgeRating);
        var app = await client.UpdateAppContentRightsAsync("app-1", usesThirdPartyContent: true);
        Assert.Equal("USES_THIRD_PARTY_CONTENT", app.ContentRightsDeclaration);
        using var categories = JsonDocument.Parse(handler.RequestBodies[0]);
        Assert.Single(categories.RootElement.GetProperty("data").GetProperty("relationships").EnumerateObject());
        Assert.DoesNotContain("attributes", handler.RequestBodies[0], StringComparison.Ordinal);
        using var rights = JsonDocument.Parse(handler.RequestBodies[2]);
        Assert.Single(rights.RootElement.GetProperty("data").GetProperty("attributes").EnumerateObject());
        Assert.Equal("/v1/apps/app-1", handler.RequestUris[2].AbsolutePath);
    }
}
