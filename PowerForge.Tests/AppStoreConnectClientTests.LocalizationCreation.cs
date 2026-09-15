using System.Net;
using System.Net.Http;
using System.Text.Json;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class AppStoreConnectClientTests
{
    [Fact]
    public async Task ReleasePreparation_SyncsExistingAndMissingVersionLocales()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK, """{"data":[{"type":"appStoreVersions","id":"version-2","attributes":{"versionString":"2.0","platform":"IOS"}}]}"""),
            new SequenceResponse(HttpStatusCode.OK, """{"data":[{"type":"appStoreVersionLocalizations","id":"english","attributes":{"locale":"en-US","description":"Old"}}]}"""),
            new SequenceResponse(HttpStatusCode.OK, """{"data":{"type":"appStoreVersionLocalizations","id":"english","attributes":{"locale":"en-US","description":"English"}}}"""),
            new SequenceResponse(HttpStatusCode.OK, """{"data":[]}"""),
            new SequenceResponse(HttpStatusCode.Created, """{"data":{"type":"appStoreVersionLocalizations","id":"polish","attributes":{"locale":"pl","description":"Polski"}}}"""));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var result = await new AppStoreConnectReleasePreparationService(client).PrepareAsync(new()
        {
            AppId = "app-1", VersionString = "2.0", CreateVersion = false, SelectBuild = false,
            MetadataSpecs = new[]
            {
                VersionListing("en-US", "English"), VersionListing("pl", "Polski")
            }
        });

        Assert.Equal(2, result.MetadataResults.Length);
        Assert.Same(result.MetadataResults[0], result.Metadata);
        Assert.False(result.MetadataResults[0].CreatedLocalization);
        Assert.Equal("Old", result.MetadataResults[0].Before!.Description);
        Assert.True(result.MetadataResults[1].CreatedLocalization);
        Assert.Null(result.MetadataResults[1].Before);
        Assert.Equal("Polski", result.MetadataResults[1].After.Description);
        Assert.All(result.MetadataResults, value => Assert.Equal("version-2", value.Version.Id));
        Assert.Equal(HttpMethod.Patch, handler.Methods[2]);
        Assert.Equal(HttpMethod.Post, handler.Methods[4]);
        Assert.Equal("/v1/appStoreVersionLocalizations", handler.RequestUris[4].AbsolutePath);
        using var body = JsonDocument.Parse(handler.RequestBodies[4]!);
        var data = body.RootElement.GetProperty("data");
        Assert.Equal("pl", data.GetProperty("attributes").GetProperty("locale").GetString());
        Assert.False(data.GetProperty("attributes").TryGetProperty("keywords", out _));
        var parent = data.GetProperty("relationships").GetProperty("appStoreVersion").GetProperty("data");
        Assert.Equal("appStoreVersions", parent.GetProperty("type").GetString());
        Assert.Equal("version-2", parent.GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("version-id")]
    [InlineData("platform")]
    [InlineData("empty-locale")]
    public async Task ReleasePreparation_RejectsInvalidLocaleBatchBeforeRemoteWork(string defect)
    {
        var handler = new SequenceHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var first = VersionListing("en-US", "English");
        var second = VersionListing("pl", "Polski");
        switch (defect)
        {
            case "duplicate": second.Locale = " EN-us "; break;
            case "version-id": first.VersionId = "one"; second.VersionId = "two"; break;
            case "platform": second.Platform = ApplePlatform.macOS; break;
            case "empty-locale": second.Locale = " "; break;
        }
        var exception = await Record.ExceptionAsync(() => new AppStoreConnectReleasePreparationService(client).PrepareAsync(new()
        {
            AppId = "app-1", VersionString = "2.0", CreateVersion = true, SelectBuild = false,
            MetadataSpec = first, MetadataSpecs = new[] { second }
        }));
        Assert.NotNull(exception);
        Assert.True(exception is ArgumentException or InvalidOperationException);
        Assert.Empty(handler.Methods);
    }

    [Theory]
    [InlineData("READY_FOR_SALE")]
    [InlineData("READY_FOR_DISTRIBUTION")]
    [InlineData("REMOVED_FROM_SALE")]
    [InlineData("DEVELOPER_REMOVED_FROM_SALE")]
    [InlineData("REPLACED_WITH_NEW_VERSION")]
    public async Task AppInfoSync_CreatesMissingEditableLocaleBesideReleasedRecord_ThenConverges(string state)
    {
        var infos = """{"data":[{"type":"appInfos","id":"live","attributes":{"state":"STATE"}},{"type":"appInfos","id":"editable","attributes":{"state":"PREPARE_FOR_SUBMISSION"}}]}""".Replace("STATE", state);
        const string localization = """{"type":"appInfoLocalizations","id":"info-pl","attributes":{"locale":"pl","name":"Smart Home","subtitle":"Sterowanie"}}""";
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK, infos),
            new SequenceResponse(HttpStatusCode.OK, """{"data":[]}"""),
            new SequenceResponse(HttpStatusCode.OK, """{"data":[]}"""),
            new SequenceResponse(HttpStatusCode.Created, "{\"data\":" + localization + "}"),
            new SequenceResponse(HttpStatusCode.OK, infos),
            new SequenceResponse(HttpStatusCode.OK, """{"data":[]}"""),
            new SequenceResponse(HttpStatusCode.OK, "{\"data\":[" + localization + "]}"));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var service = new AppStoreConnectAppInfoMetadataSyncService(client);
        var request = new AppStoreConnectAppInfoMetadataSyncRequest
        {
            Spec = new() { AppId = "app-1", Locale = "pl", Metadata = new() { Name = "Smart Home", Subtitle = "Sterowanie" } }
        };

        var created = await service.SyncAsync(request);
        var converged = await service.SyncAsync(request);

        Assert.True(created.CreatedLocalization);
        Assert.Null(created.Before);
        Assert.Equal("editable", created.AppInfo.Id);
        Assert.Equal("info-pl", created.After.Id);
        Assert.False(converged.CreatedLocalization);
        Assert.Empty(converged.UpdatedFields);
        Assert.Single(handler.Methods, method => method != HttpMethod.Get);
        Assert.Equal(HttpMethod.Post, handler.Methods[3]);
        Assert.Equal("/v1/appInfoLocalizations", handler.RequestUris[3].AbsolutePath);
        using var body = JsonDocument.Parse(handler.RequestBodies[3]!);
        var data = body.RootElement.GetProperty("data");
        Assert.Equal("pl", data.GetProperty("attributes").GetProperty("locale").GetString());
        Assert.Equal("Smart Home", data.GetProperty("attributes").GetProperty("name").GetString());
        var parent = data.GetProperty("relationships").GetProperty("appInfo").GetProperty("data");
        Assert.Equal("appInfos", parent.GetProperty("type").GetString());
        Assert.Equal("editable", parent.GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("READY_FOR_DISTRIBUTION")]
    [InlineData("REMOVED_FROM_SALE")]
    [InlineData("DEVELOPER_REMOVED_FROM_SALE")]
    [InlineData("REPLACED_WITH_NEW_VERSION")]
    [InlineData("IN_REVIEW")]
    public async Task AppInfoSync_DoesNotCreateLocaleInLockedResource(string state)
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK, "{\"data\":[{\"type\":\"appInfos\",\"id\":\"locked\",\"attributes\":{\"state\":\"" + state + "\"}}]}"),
            new SequenceResponse(HttpStatusCode.OK, """{"data":[]}"""));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AppStoreConnectAppInfoMetadataSyncService(client).SyncAsync(new()
        {
            Spec = new() { AppId = "app-1", Locale = "pl", Metadata = new() { Name = "Smart Home" } }
        }));
        Assert.All(handler.Methods, method => Assert.Equal(HttpMethod.Get, method));
    }

    private static AppStoreConnectVersionMetadataSpec VersionListing(string locale, string description)
        => new() { AppId = "app-1", Locale = locale, UseReleaseVersion = true, Metadata = new() { Description = description } };
}
