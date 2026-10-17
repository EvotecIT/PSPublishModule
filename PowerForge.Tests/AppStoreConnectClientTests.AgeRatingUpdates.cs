using System.Net;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class AppStoreConnectClientTests
{
    [Fact]
    public async Task AgeQuestionnaire_DeveloperUrlRoundTripsEditsAndExplicitClear()
    {
        var handler = new SequenceHandler(
            new(HttpStatusCode.OK, """{"data":{"id":"age-1","attributes":{"developerAgeRatingInfoUrl":"https://example.com/old","advertising":false}}}"""),
            new(HttpStatusCode.OK, """{"data":{"id":"age-1","attributes":{"developerAgeRatingInfoUrl":"https://example.com/new","advertising":false}}}"""),
            new(HttpStatusCode.Conflict, """{"errors":[{"status":"409","title":"Retry later"}]}"""),
            new(HttpStatusCode.OK, """{"data":{"id":"age-1","attributes":{"developerAgeRatingInfoUrl":null,"advertising":false}}}"""));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var answers = (await client.GetAgeRatingDeclarationAsync("info-1"))!.Answers;
        Assert.Equal("https://example.com/old", answers.DeveloperAgeRatingInfoUrl);
        answers.DeveloperAgeRatingInfoUrl = "https://example.com/new";
        var after = await client.UpdateAgeRatingDeclarationAsync("age-1", answers);
        Assert.Equal("https://example.com/new", after.Answers.DeveloperAgeRatingInfoUrl);
        using (var patch = JsonDocument.Parse(handler.RequestBodies[1]))
        {
            var attributes = patch.RootElement.GetProperty("data").GetProperty("attributes");
            Assert.Single(attributes.EnumerateObject());
            Assert.Equal("https://example.com/new", attributes.GetProperty("developerAgeRatingInfoUrl").GetString());
        }
        answers.ClearDeveloperAgeRatingInfoUrl();
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.UpdateAgeRatingDeclarationAsync("age-1", answers));
        await client.UpdateAgeRatingDeclarationAsync("age-1", answers);
        Assert.Equal(handler.RequestBodies[2], handler.RequestBodies[3]);
        using var clear = JsonDocument.Parse(handler.RequestBodies[3]);
        Assert.Equal(JsonValueKind.Null, clear.RootElement.GetProperty("data").GetProperty("attributes").GetProperty("developerAgeRatingInfoUrl").ValueKind);
        await Assert.ThrowsAsync<ArgumentException>(() => client.UpdateAgeRatingDeclarationAsync("age-1", answers));
        answers.DeveloperAgeRatingInfoUrl = "relative/age-info";
        await Assert.ThrowsAsync<ArgumentException>(() => client.UpdateAgeRatingDeclarationAsync("age-1", answers));
        Assert.Equal(4, handler.RequestUris.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AgeQuestionnaire_ReusedAnswersSendOnlyNewEditsAfterSuccess(bool readFirst)
    {
        var responses = new List<SequenceResponse>();
        if (readFirst)
            responses.Add(new(HttpStatusCode.OK, """{"data":{"id":"age-1","attributes":{"advertising":true,"messagingAndChat":false}}}"""));
        responses.Add(new(HttpStatusCode.OK, """{"data":{"id":"age-1","attributes":{"advertising":false,"messagingAndChat":false}}}"""));
        // Another publisher changes advertising after our first successful update.
        responses.Add(new(HttpStatusCode.OK, """{"data":{"id":"age-1","attributes":{"advertising":true,"messagingAndChat":true}}}"""));
        var handler = new SequenceHandler(responses.ToArray());
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var answers = readFirst
            ? (await client.GetAgeRatingDeclarationAsync("info-1"))!.Answers
            : new AppStoreConnectAgeRatingAnswers();
        answers.Advertising = false;
        await client.UpdateAgeRatingDeclarationAsync("age-1", answers);
        answers.MessagingAndChat = true;
        await client.UpdateAgeRatingDeclarationAsync("age-1", answers);

        using var secondPatch = JsonDocument.Parse(handler.RequestBodies.Last());
        var attributes = secondPatch.RootElement.GetProperty("data").GetProperty("attributes");
        Assert.Single(attributes.EnumerateObject());
        Assert.True(attributes.GetProperty("messagingAndChat").GetBoolean());
    }

    [Fact]
    public async Task AgeQuestionnaire_FailedPatchKeepsAnswersPendingForRetry()
    {
        var handler = new SequenceHandler(
            new(HttpStatusCode.OK, """{"data":{"id":"age-1","attributes":{"advertising":true}}}"""),
            new(HttpStatusCode.Conflict, """{"errors":[{"status":"409","title":"Not changeable"}]}"""),
            new(HttpStatusCode.OK, """{"data":{"id":"age-1","attributes":{"advertising":false}}}"""));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var answers = (await client.GetAgeRatingDeclarationAsync("info-1"))!.Answers;
        answers.Advertising = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.UpdateAgeRatingDeclarationAsync("age-1", answers));
        await client.UpdateAgeRatingDeclarationAsync("age-1", answers);
        Assert.Equal(handler.RequestBodies[1], handler.RequestBodies[2]);
    }

    [Fact]
    public async Task AgeQuestionnaire_ClearsKidsBandOnlyWhenExplicitlyRequested()
    {
        var handler = new SequenceHandler(
            new(HttpStatusCode.OK, """{"data":{"id":"age-1","attributes":{"kidsAgeBand":"SIX_TO_EIGHT","advertising":false}}}"""),
            new(HttpStatusCode.OK, """{"data":{"id":"age-1","attributes":{"kidsAgeBand":"SIX_TO_EIGHT","advertising":true}}}"""),
            new(HttpStatusCode.Conflict, """{"errors":[{"status":"409","title":"Not changeable"}]}"""),
            new(HttpStatusCode.OK, """{"data":{"id":"age-1","attributes":{"kidsAgeBand":null,"advertising":true}}}"""),
            new(HttpStatusCode.OK, """{"data":{"id":"age-1","attributes":{"kidsAgeBand":null,"advertising":true}}}"""));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var answers = (await client.GetAgeRatingDeclarationAsync("info-1"))!.Answers;
        answers.KidsAgeBand = null;
        answers.Advertising = true;
        await client.UpdateAgeRatingDeclarationAsync("age-1", answers);
        using (var plainNullPatch = JsonDocument.Parse(handler.RequestBodies[1]))
            Assert.False(plainNullPatch.RootElement.GetProperty("data").GetProperty("attributes").TryGetProperty("kidsAgeBand", out _));

        answers.ClearKidsAgeBand();
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.UpdateAgeRatingDeclarationAsync("age-1", answers));
        await client.UpdateAgeRatingDeclarationAsync("age-1", answers);
        Assert.Equal(handler.RequestBodies[2], handler.RequestBodies[3]);
        using var clearPatch = JsonDocument.Parse(handler.RequestBodies[3]);
        var attributes = clearPatch.RootElement.GetProperty("data").GetProperty("attributes");
        Assert.Single(attributes.EnumerateObject());
        Assert.Equal(JsonValueKind.Null, attributes.GetProperty("kidsAgeBand").ValueKind);
        await Assert.ThrowsAsync<ArgumentException>(() => client.UpdateAgeRatingDeclarationAsync("age-1", answers));
        Assert.Equal(4, handler.RequestUris.Count);
        answers.ClearKidsAgeBand();
        await client.UpdateAgeRatingDeclarationAsync("age-1", answers);
        Assert.Equal(handler.RequestBodies[3], handler.RequestBodies[4]);
    }

    [Fact]
    public async Task AgeQuestionnaire_ChangesMadeDuringPatchRemainPending()
    {
        var handler = new SequenceHandler(
            new(HttpStatusCode.OK, """{"data":{"id":"age-1","attributes":{"advertising":false}}}"""),
            new(HttpStatusCode.OK, """{"data":{"id":"age-1","attributes":{"advertising":false,"messagingAndChat":true}}}"""));
        var answers = new AppStoreConnectAgeRatingAnswers { Advertising = false };
        handler.OnRequest = count => { if (count == 1) answers.MessagingAndChat = true; };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        await client.UpdateAgeRatingDeclarationAsync("age-1", answers);
        await client.UpdateAgeRatingDeclarationAsync("age-1", answers);
        using var secondPatch = JsonDocument.Parse(handler.RequestBodies[1]);
        var attributes = secondPatch.RootElement.GetProperty("data").GetProperty("attributes");
        Assert.Single(attributes.EnumerateObject());
        Assert.True(attributes.GetProperty("messagingAndChat").GetBoolean());
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(false, "kidsAgeBand")]
    [InlineData(false, "developerAgeRatingInfoUrl")]
    [InlineData(true, null)]
    [InlineData(true, "kidsAgeBand")]
    [InlineData(true, "developerAgeRatingInfoUrl")]
    public async Task AgeQuestionnaire_ReusedAnswersRetainExplicitValuesForDifferentDeclaration(bool readFirst, string? clearField)
    {
        var handler = new SequenceHandler(
            new(HttpStatusCode.OK, """{"data":{"id":"age-A","attributes":{"advertising":false}}}"""),
            new(HttpStatusCode.OK, """{"data":{"id":"age-B","attributes":{"advertising":false,"kidsAgeBand":null}}}"""));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var answers = readFirst
            ? (await client.GetAgeRatingDeclarationAsync("info-A"))!.Answers
            : new AppStoreConnectAgeRatingAnswers { Advertising = false };
        if (clearField == "kidsAgeBand") answers.ClearKidsAgeBand();
        else if (clearField == "developerAgeRatingInfoUrl") answers.ClearDeveloperAgeRatingInfoUrl();
        if (!readFirst) await client.UpdateAgeRatingDeclarationAsync("age-A", answers);
        await client.UpdateAgeRatingDeclarationAsync("age-B", answers);

        using var patch = JsonDocument.Parse(handler.RequestBodies.Last());
        var data = patch.RootElement.GetProperty("data");
        Assert.Equal("age-B", data.GetProperty("id").GetString());
        var attributes = data.GetProperty("attributes");
        Assert.Equal(clearField is not null ? 2 : 1, attributes.EnumerateObject().Count());
        Assert.False(attributes.GetProperty("advertising").GetBoolean());
        if (clearField is not null) Assert.Equal(JsonValueKind.Null, attributes.GetProperty(clearField).ValueKind);
        await Assert.ThrowsAsync<ArgumentException>(() => client.UpdateAgeRatingDeclarationAsync("age-B", answers));
    }
}
