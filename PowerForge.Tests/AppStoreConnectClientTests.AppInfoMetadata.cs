using System.Net;
using System.Net.Http;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class AppStoreConnectClientTests
{
    [Fact]
    public async Task GetAppInfoMetadataAsync_ParsesAppInformationAndLocalizationFields()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "info-1",
                      "type": "appInfos",
                      "attributes": {
                        "state": "PREPARE_FOR_SUBMISSION",
                        "appStoreState": "PREPARE_FOR_SUBMISSION"
                      }
                    }
                  ]
                }
                """),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "info-loc-1",
                      "type": "appInfoLocalizations",
                      "attributes": {
                        "locale": "en-US",
                        "name": "Tactra Remote",
                        "subtitle": "Premium Home Assistant remote",
                        "privacyPolicyUrl": "https://tactra.dev/privacy/",
                        "privacyChoicesUrl": "https://tactra.dev/privacy/choices/"
                      }
                    }
                  ]
                }
                """));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var appInfo = Assert.Single(await client.GetAppInfosAsync("app-1"));
        var localization = Assert.Single(await client.GetAppInfoLocalizationsAsync(appInfo.Id, "en-US"));

        Assert.Equal("PREPARE_FOR_SUBMISSION", appInfo.State);
        Assert.Equal("Tactra Remote", localization.Name);
        Assert.Equal("Premium Home Assistant remote", localization.Subtitle);
        Assert.Equal("https://tactra.dev/privacy/", localization.PrivacyPolicyUrl);
        Assert.Equal("https://tactra.dev/privacy/choices/", localization.PrivacyChoicesUrl);
        Assert.Contains("apps/app-1/appInfos?limit=50", handler.RequestUris[0].ToString(), StringComparison.Ordinal);
        Assert.Contains("appInfos/info-1/appInfoLocalizations", handler.RequestUris[1].ToString(), StringComparison.Ordinal);
        Assert.Contains("filter%5Blocale%5D=en-US", handler.RequestUris[1].Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateAppInfoLocalizationAsync_PatchesOnlyProvidedMetadataFields()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": {
                    "id": "info-loc-1",
                    "type": "appInfoLocalizations",
                    "attributes": {
                      "locale": "en-US",
                      "name": "Tactra Remote",
                      "privacyPolicyUrl": "https://tactra.dev/privacy/"
                    }
                  }
                }
                """));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var result = await client.UpdateAppInfoLocalizationAsync("info-loc-1", new AppStoreConnectAppInfoLocalizationUpdate
        {
            Name = "Tactra Remote",
            PrivacyPolicyUrl = "https://tactra.dev/privacy/"
        });

        Assert.Equal("https://tactra.dev/privacy/", result.PrivacyPolicyUrl);
        Assert.Equal(new HttpMethod("PATCH"), handler.Methods[0]);
        Assert.Equal("https://api.appstoreconnect.apple.com/v1/appInfoLocalizations/info-loc-1", handler.RequestUris[0].ToString());
        Assert.Contains("\"name\":\"Tactra Remote\"", handler.RequestBodies[0], StringComparison.Ordinal);
        Assert.Contains("\"privacyPolicyUrl\":\"https://tactra.dev/privacy/\"", handler.RequestBodies[0], StringComparison.Ordinal);
        Assert.DoesNotContain("subtitle", handler.RequestBodies[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppInfoMetadataSyncService_SelectsEditableResource()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "info-live",
                      "type": "appInfos",
                      "attributes": {
                        "state": "READY_FOR_DISTRIBUTION",
                        "appStoreState": "READY_FOR_SALE"
                      }
                    },
                    { "id": "info-editable", "type": "appInfos", "attributes": { "state": "WAITING_FOR_REVIEW" } }
                  ]
                }
                """),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "info-loc-1",
                      "type": "appInfoLocalizations",
                      "attributes": {
                        "locale": "en-US",
                        "privacyPolicyUrl": "https://old.example/privacy/"
                      }
                    }
                  ]
                }
                """),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": {
                    "id": "info-loc-1",
                    "type": "appInfoLocalizations",
                    "attributes": {
                      "locale": "en-US",
                      "privacyPolicyUrl": "https://tactra.dev/privacy/"
                    }
                  }
                }
                """));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var service = new AppStoreConnectAppInfoMetadataSyncService(client);

        var result = await service.SyncAsync(new AppStoreConnectAppInfoMetadataSyncRequest
        {
            Spec = new AppStoreConnectAppInfoMetadataSpec
            {
                AppId = "app-1",
                Locale = "en-US",
                Metadata = new AppStoreConnectAppInfoLocalizationUpdate
                {
                    PrivacyPolicyUrl = "https://tactra.dev/privacy/"
                }
            }
        });

        Assert.Equal("info-editable", result.AppInfo.Id);
        Assert.Equal("https://old.example/privacy/", result.Before.PrivacyPolicyUrl);
        Assert.Equal("https://tactra.dev/privacy/", result.After.PrivacyPolicyUrl);
        Assert.Equal(new[] { "privacyPolicyUrl" }, result.UpdatedFields);
        Assert.Contains("appInfos/info-editable/appInfoLocalizations", handler.RequestUris[1].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppInfoMetadataSyncService_RejectsAppInfoIdFromAnotherApp()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    { "id": "info-for-app-1", "type": "appInfos", "attributes": { "state": "PREPARE_FOR_SUBMISSION" } }
                  ]
                }
                """));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var service = new AppStoreConnectAppInfoMetadataSyncService(client);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SyncAsync(
            new AppStoreConnectAppInfoMetadataSyncRequest
            {
                Spec = new AppStoreConnectAppInfoMetadataSpec
                {
                    AppId = "app-1",
                    AppInfoId = "info-for-another-app",
                    Locale = "en-US",
                    Metadata = new AppStoreConnectAppInfoLocalizationUpdate
                    {
                        PrivacyPolicyUrl = "https://tactra.dev/privacy/"
                    }
                }
            }));

        Assert.Contains("does not belong to app 'app-1'", exception.Message, StringComparison.Ordinal);
        Assert.Single(handler.RequestUris);
    }

    [Fact]
    public async Task ReleasePreparationService_SyncsAppInfoWithoutVersionOrBuildLookup()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    { "id": "info-editable", "type": "appInfos", "attributes": { "state": "PREPARE_FOR_SUBMISSION" } }
                  ]
                }
                """),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "info-loc-1",
                      "type": "appInfoLocalizations",
                      "attributes": {
                        "locale": "en-US",
                        "privacyPolicyUrl": "https://old.example/privacy/"
                      }
                    }
                  ]
                }
                """),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": {
                    "id": "info-loc-1",
                    "type": "appInfoLocalizations",
                    "attributes": {
                      "locale": "en-US",
                      "privacyPolicyUrl": "https://tactra.dev/privacy/"
                    }
                  }
                }
                """),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    { "id": "info-editable", "type": "appInfos", "attributes": { "state": "PREPARE_FOR_SUBMISSION" } }
                  ]
                }
                """),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "info-loc-2",
                      "type": "appInfoLocalizations",
                      "attributes": {
                        "locale": "pl-PL",
                        "privacyPolicyUrl": "https://old.example/pl/privacy/"
                      }
                    }
                  ]
                }
                """),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": {
                    "id": "info-loc-2",
                    "type": "appInfoLocalizations",
                    "attributes": {
                      "locale": "pl-PL",
                      "privacyPolicyUrl": "https://tactra.dev/pl/privacy/"
                    }
                  }
                }
                """));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var service = new AppStoreConnectReleasePreparationService(client);

        var result = await service.PrepareAsync(new AppStoreConnectReleasePreparationRequest
        {
            AppId = "app-1",
            CreateVersion = false,
            SelectBuild = false,
            AppInfoMetadataSpecs = new[]
            {
                new AppStoreConnectAppInfoMetadataSpec
                {
                    AppId = "app-1",
                    Locale = "en-US",
                    Metadata = new AppStoreConnectAppInfoLocalizationUpdate
                    {
                        PrivacyPolicyUrl = "https://tactra.dev/privacy/"
                    }
                },
                new AppStoreConnectAppInfoMetadataSpec
                {
                    AppId = "app-1",
                    Locale = "pl-PL",
                    Metadata = new AppStoreConnectAppInfoLocalizationUpdate
                    {
                        PrivacyPolicyUrl = "https://tactra.dev/pl/privacy/"
                    }
                }
            }
        });

        Assert.Null(result.Version);
        Assert.Equal(2, result.AppInfoMetadataResults.Length);
        Assert.Equal("https://tactra.dev/privacy/", result.AppInfoMetadataResults[0].After.PrivacyPolicyUrl);
        Assert.Equal("https://tactra.dev/pl/privacy/", result.AppInfoMetadataResults[1].After.PrivacyPolicyUrl);
        Assert.DoesNotContain(handler.RequestUris, uri => uri.AbsolutePath.Contains("appStoreVersions", StringComparison.Ordinal));
    }
}
