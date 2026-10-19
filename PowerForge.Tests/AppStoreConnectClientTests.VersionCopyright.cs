using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class AppStoreConnectClientTests
{
    [Fact]
    public async Task SetVersionCopyrightAsync_UpdatesOnlyTheExactVersionsNotice()
    {
        var handler = new SequenceHandler(new SequenceResponse(HttpStatusCode.OK,
            """{"data":{"type":"appStoreVersions","id":"mac-1","attributes":{"versionString":"1.0","platform":"MAC_OS","releaseType":"MANUAL","copyright":"2022 Example"}}}"""));
        using var http = new HttpClient(handler);
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var version = await client.SetVersionCopyrightAsync(" mac-1 ", " 2022 Example ");

        Assert.Equal("2022 Example", version.Copyright);
        Assert.Equal("MANUAL", version.ReleaseType);
        Assert.Equal("MAC_OS", version.Platform);
        Assert.Equal(new HttpMethod("PATCH"), Assert.Single(handler.Methods));
        Assert.Equal("https://api.appstoreconnect.apple.com/v1/appStoreVersions/mac-1", Assert.Single(handler.RequestUris).ToString());
        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        var data = body.RootElement.GetProperty("data");
        Assert.Equal("mac-1", data.GetProperty("id").GetString());
        Assert.Equal("appStoreVersions", data.GetProperty("type").GetString());
        var field = Assert.Single(data.GetProperty("attributes").EnumerateObject());
        Assert.Equal("copyright", field.Name);
        Assert.Equal("2022 Example", field.Value.GetString());
        Assert.Equal(3, data.EnumerateObject().Count()); // No build or release-policy relationship changes.
    }

    [Theory]
    [InlineData("other", "\"2022 Example\"")]
    [InlineData("mac-1", "null")]
    [InlineData("mac-1", "\"different\"")]
    [InlineData("mac-1", "2022")]
    public async Task SetVersionCopyrightAsync_RejectsUnconfirmedNotice(string id, string value)
    {
        var response = "{\"data\":{\"type\":\"appStoreVersions\",\"id\":\"" + id + "\",\"attributes\":{\"copyright\":" + value + "}}}";
        var handler = new SequenceHandler(new SequenceResponse(HttpStatusCode.OK, response));
        using var http = new HttpClient(handler);
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SetVersionCopyrightAsync("mac-1", "2022 Example"));
        Assert.Single(handler.RequestUris);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task SetVersionCopyrightAsync_RequiresExplicitNoticeBeforeSending(string? notice)
    {
        var handler = new SequenceHandler();
        using var http = new HttpClient(handler);
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        await Assert.ThrowsAsync<ArgumentException>(() => client.SetVersionCopyrightAsync("mac-1", notice!));
        Assert.Empty(handler.RequestUris);
    }

    [Theory]
    [InlineData("\"2022 Example\"", true)]
    [InlineData("null", false)]
    [InlineData("\" \"", false)]
    [InlineData("2022", false)]
    public async Task ReleaseReadiness_RequiresTheVersionWideCopyright(string copyright, bool ready)
    {
        var handler = new SequenceHandler(
            new(HttpStatusCode.OK, "{\"data\":[{\"type\":\"appStoreVersions\",\"id\":\"mac-1\",\"attributes\":{\"copyright\":" + copyright + "}}]}"),
            new(HttpStatusCode.OK, """{"data":[{"type":"appStoreVersionLocalizations","id":"loc-1","attributes":{"locale":"en-US"}}]}"""));
        using var http = new HttpClient(handler);
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var result = await new AppStoreConnectReleaseReadinessService(client).CheckAsync(new()
        {
            AppId = "app-1", VersionString = "1.0", RequireDescription = false,
            RequireKeywords = false, RequireSupportUrl = false, RequireScreenshots = false
        });
        Assert.Equal(ready, result.IsReady);
        Assert.Equal(ready, Assert.Single(result.Checks, x => x.Name == "version.copyright").Passed);
        Assert.All(handler.Methods, method => Assert.Equal(HttpMethod.Get, method));
    }

    [Fact]
    public async Task MetadataSync_AppliesCopyrightBeforeLocalizedMetadata()
    {
        var handler = new SequenceHandler(
            new(HttpStatusCode.OK, """{"data":{"type":"appStoreVersions","id":"mac-1","attributes":{"copyright":"2022 Example"}}}"""),
            new(HttpStatusCode.OK, """{"data":[{"id":"loc-1","type":"appStoreVersionLocalizations","attributes":{"locale":"en-US"}}]}"""),
            new(HttpStatusCode.OK, """{"data":{"id":"loc-1","type":"appStoreVersionLocalizations","attributes":{"locale":"en-US","description":"Documents"}}}"""));
        using var http = new HttpClient(handler);
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var result = await new AppStoreConnectVersionMetadataSyncService(client).SyncAsync(new()
        {
            Spec = new() { AppId = "app-1", VersionId = "mac-1", Copyright = "2022 Example", Metadata = new() { Description = "Documents" } }
        });
        Assert.Equal("2022 Example", result.Version.Copyright);
        Assert.Contains("copyright", result.UpdatedFields);
        Assert.Equal("Documents", result.After.Description);
        Assert.EndsWith("/appStoreVersions/mac-1", handler.RequestUris[0].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReleasePreparation_PreservesCopyrightWhenBindingMetadataToTheSelectedVersion()
    {
        var handler = new SequenceHandler(
            new(HttpStatusCode.OK, """{"data":{"type":"appStoreVersions","id":"mac-1","attributes":{"copyright":"2022 Example"}}}"""),
            new(HttpStatusCode.OK, """{"data":[{"id":"loc-1","type":"appStoreVersionLocalizations","attributes":{"locale":"en-US"}}]}"""),
            new(HttpStatusCode.OK, """{"data":{"id":"loc-1","type":"appStoreVersionLocalizations","attributes":{"locale":"en-US","description":"Documents"}}}"""));
        using var http = new HttpClient(handler);
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var result = await new AppStoreConnectReleasePreparationService(client).PrepareAsync(new()
        {
            AppId = "app-1", VersionString = "1.0", CreateVersion = false, SelectBuild = false,
            MetadataSpecs = new[] { new AppStoreConnectVersionMetadataSpec
            {
                VersionId = "mac-1", Copyright = "2022 Example", Metadata = new() { Description = "Documents" }
            } }
        });
        Assert.Equal("2022 Example", Assert.Single(result.MetadataResults).Version.Copyright);
        Assert.Equal(new HttpMethod("PATCH"), handler.Methods[0]);
        Assert.EndsWith("/appStoreVersions/mac-1", handler.RequestUris[0].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReleasePreparation_RejectsConflictingLocaleCopyrightsBeforeAnyRequest()
    {
        var handler = new SequenceHandler();
        using var http = new HttpClient(handler);
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var service = new AppStoreConnectReleasePreparationService(client);
        await Assert.ThrowsAsync<ArgumentException>(() => service.PrepareAsync(new()
        {
            AppId = "app-1", VersionString = "1.0", MetadataSpecs = new[]
            {
                new AppStoreConnectVersionMetadataSpec { Locale = "en-US", Copyright = "2022 First", Metadata = new() { Description = "Documents" } },
                new AppStoreConnectVersionMetadataSpec { Locale = "pl", Copyright = "2022 Second", Metadata = new() { Description = "Dokumenty" } }
            }
        }));
        Assert.Empty(handler.RequestUris);
    }
}
