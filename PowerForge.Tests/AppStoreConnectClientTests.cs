using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class AppStoreConnectClientTests
{
    [Fact]
    public void CreateToken_CreatesThreePartJwt()
    {
        var credential = CreateCredential();
        var token = new AppStoreConnectJwtTokenGenerator().CreateToken(credential);

        Assert.Equal(3, token.Split('.').Length);
        Assert.DoesNotContain("+", token, StringComparison.Ordinal);
        Assert.DoesNotContain("/", token, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindAppsAsync_UsesBundleIdFilterAndParsesApps()
    {
        var handler = new RecordingHandler(
            """
            {
              "data": [
                {
                  "id": "123",
                  "type": "apps",
                  "attributes": {
                    "name": "Tactra",
                    "bundleId": "com.example.Tactra",
                    "sku": "TACTRA",
                    "primaryLocale": "en-US"
                  }
                }
              ]
            }
            """);

        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var apps = await client.FindAppsAsync(bundleId: "com.example.Tactra", platform: ApplePlatform.iOS);

        var app = Assert.Single(apps);
        Assert.Equal("123", app.Id);
        Assert.Equal("Tactra", app.Name);
        Assert.Contains("apps?", handler.RequestUris[0].ToString(), StringComparison.Ordinal);
        Assert.Contains("filter%5BbundleId%5D=com.example.Tactra", handler.RequestUris[0].Query, StringComparison.Ordinal);
        Assert.Contains("filter%5BappStoreVersions.platform%5D=IOS", handler.RequestUris[0].Query, StringComparison.Ordinal);
        Assert.Equal("Bearer", handler.AuthorizationSchemes[0]);
    }

    [Fact]
    public async Task GetAppAsync_ReturnsNullForMissingApp()
    {
        var handler = new RecordingHandler("{}", HttpStatusCode.NotFound);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var app = await client.GetAppAsync("missing-app");

        Assert.Null(app);
        Assert.Contains("apps/missing-app", handler.RequestUris[0].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetBuildsAsync_UsesBuildNumberFilterAndParsesBuilds()
    {
        var handler = new RecordingHandler(
            """
            {
              "data": [
                {
                  "id": "build-1",
                  "type": "builds",
                  "attributes": {
                    "version": "9",
                    "processingState": "VALID",
                    "expired": false,
                    "minOsVersion": "17.0"
                  }
                }
              ]
            }
            """);

        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var builds = await client.GetBuildsAsync("123", buildNumber: "9");

        var build = Assert.Single(builds);
        Assert.Equal("build-1", build.Id);
        Assert.Equal("9", build.Version);
        Assert.Equal("VALID", build.ProcessingState);
        Assert.False(build.Expired);
        Assert.Contains("filter%5Bapp%5D=123", handler.RequestUris[0].Query, StringComparison.Ordinal);
        Assert.Contains("filter%5Bversion%5D=9", handler.RequestUris[0].Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetBuildsAsync_FiltersIncludedPreReleaseVersionByMarketingVersionAndPlatform()
    {
        var handler = new RecordingHandler(
            """
            {
              "data": [
                {
                  "id": "build-old",
                  "type": "builds",
                  "attributes": { "version": "9", "processingState": "VALID" },
                  "relationships": {
                    "preReleaseVersion": {
                      "data": { "id": "pre-old", "type": "preReleaseVersions" }
                    }
                  }
                },
                {
                  "id": "build-current",
                  "type": "builds",
                  "attributes": { "version": "9", "processingState": "VALID" },
                  "relationships": {
                    "preReleaseVersion": {
                      "data": { "id": "pre-current", "type": "preReleaseVersions" }
                    }
                  }
                }
              ],
              "included": [
                {
                  "id": "pre-old",
                  "type": "preReleaseVersions",
                  "attributes": { "version": "1.9.0", "platform": "IOS" }
                },
                {
                  "id": "pre-current",
                  "type": "preReleaseVersions",
                  "attributes": { "version": "2.0.0", "platform": "IOS" }
                }
              ]
            }
            """);

        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var builds = await client.GetBuildsAsync("123", buildNumber: "9", marketingVersion: "2.0.0", platform: ApplePlatform.iOS);

        var build = Assert.Single(builds);
        Assert.Equal("build-current", build.Id);
        Assert.Equal("2.0.0", build.MarketingVersion);
        Assert.Equal("IOS", build.Platform);
        Assert.Contains("include=preReleaseVersion", handler.RequestUris[0].Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetBuildUploadAsync_ParsesTerminalDeliveryErrors()
    {
        var handler = new RecordingHandler(
            """
            {
              "data": {
                "id": "upload-11",
                "type": "buildUploads",
                "attributes": {
                  "cfBundleShortVersionString": "1.4.0",
                  "cfBundleVersion": "11",
                  "platform": "IOS",
                  "uploadedDate": "2026-07-23T14:10:00-07:00",
                  "state": {
                    "state": "FAILED",
                    "errors": [
                      { "code": "90683", "description": "Missing purpose string in Info.plist." }
                    ],
                    "warnings": []
                  }
                }
              }
            }
            """);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var upload = await client.GetBuildUploadAsync("upload-11");

        Assert.NotNull(upload);
        Assert.Equal("FAILED", upload.State);
        Assert.Equal("1.4.0", upload.MarketingVersion);
        Assert.Equal("11", upload.BuildNumber);
        Assert.Equal("IOS", upload.Platform);
        var error = Assert.Single(upload.Errors);
        Assert.Equal("90683", error.Code);
        Assert.Equal("Missing purpose string in Info.plist.", error.Description);
        Assert.Empty(upload.Warnings);
        Assert.Contains("buildUploads/upload-11", handler.RequestUris[0].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetBuildUploadAsync_ParsesFlatDocumentedDeliveryState()
    {
        var handler = new RecordingHandler(
            """
            {
              "data": {
                "id": "upload-12",
                "type": "buildUploads",
                "attributes": {
                  "cfBundleShortVersionString": "1.4.0",
                  "cfBundleVersion": "12",
                  "platform": "IOS",
                  "state": "FAILED",
                  "errors": [
                    { "code": "90683", "message": "Missing purpose string in Info.plist." }
                  ],
                  "warnings": []
                }
              }
            }
            """);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var upload = await client.GetBuildUploadAsync("upload-12");

        Assert.NotNull(upload);
        Assert.Equal("FAILED", upload.State);
        var error = Assert.Single(upload.Errors);
        Assert.Equal("90683", error.Code);
        Assert.Equal("Missing purpose string in Info.plist.", error.Description);
    }

    [Fact]
    public async Task CreateVersionAsync_PostsVersionPlatformAndAppRelationship()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.Created,
                """
                {
                  "data": {
                    "id": "version-1",
                    "type": "appStoreVersions",
                    "attributes": {
                      "versionString": "1.0.1",
                      "platform": "IOS",
                      "appStoreState": "PREPARE_FOR_SUBMISSION"
                    }
                  }
                }
                """));

        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var version = await client.CreateVersionAsync("app-1", "1.0.1", ApplePlatform.iOS);

        Assert.Equal("version-1", version.Id);
        Assert.Equal("1.0.1", version.VersionString);
        Assert.Equal(HttpMethod.Post, handler.Methods[0]);
        Assert.Equal("https://api.appstoreconnect.apple.com/v1/appStoreVersions", handler.RequestUris[0].ToString());
        Assert.Contains("\"type\":\"appStoreVersions\"", handler.RequestBodies[0], StringComparison.Ordinal);
        Assert.Contains("\"platform\":\"IOS\"", handler.RequestBodies[0], StringComparison.Ordinal);
        Assert.Contains("\"versionString\":\"1.0.1\"", handler.RequestBodies[0], StringComparison.Ordinal);
        Assert.Contains("\"type\":\"apps\",\"id\":\"app-1\"", handler.RequestBodies[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetVersionBuildAsync_PatchesBuildRelationship()
    {
        var handler = new SequenceHandler(new SequenceResponse(HttpStatusCode.NoContent, string.Empty));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        await client.SetVersionBuildAsync("version-1", "build-5");

        Assert.Equal(new HttpMethod("PATCH"), handler.Methods[0]);
        Assert.Equal("https://api.appstoreconnect.apple.com/v1/appStoreVersions/version-1/relationships/build", handler.RequestUris[0].ToString());
        Assert.Contains("\"type\":\"builds\"", handler.RequestBodies[0], StringComparison.Ordinal);
        Assert.Contains("\"id\":\"build-5\"", handler.RequestBodies[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetVersionLocalizationsAsync_ParsesSubmissionMetadataFields()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "loc-1",
                      "type": "appStoreVersionLocalizations",
                      "attributes": {
                        "locale": "en-US",
                        "description": "Premium remote.",
                        "keywords": "media,remote",
                        "marketingUrl": "https://example.test",
                        "promotionalText": "Fresh release.",
                        "supportUrl": "https://example.test/support",
                        "whatsNew": "Improved releases."
                      }
                    }
                  ]
                }
                """));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var localizations = await client.GetVersionLocalizationsAsync("version-1", "en-US");

        var localization = Assert.Single(localizations);
        Assert.Equal("Premium remote.", localization.Description);
        Assert.Equal("media,remote", localization.Keywords);
        Assert.Equal("https://example.test", localization.MarketingUrl);
        Assert.Equal("Fresh release.", localization.PromotionalText);
        Assert.Equal("https://example.test/support", localization.SupportUrl);
        Assert.Equal("Improved releases.", localization.WhatsNew);
    }

    [Fact]
    public async Task UpdateVersionLocalizationAsync_PatchesOnlyProvidedMetadataFields()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": {
                    "id": "loc-1",
                    "type": "appStoreVersionLocalizations",
                    "attributes": {
                      "locale": "en-US",
                      "description": "Updated.",
                      "supportUrl": "https://example.test/support"
                    }
                  }
                }
                """));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var result = await client.UpdateVersionLocalizationAsync("loc-1", new AppStoreConnectVersionLocalizationUpdate
        {
            Description = "Updated.",
            SupportUrl = "https://example.test/support"
        });

        Assert.Equal("Updated.", result.Description);
        Assert.Equal(new HttpMethod("PATCH"), handler.Methods[0]);
        Assert.Equal("https://api.appstoreconnect.apple.com/v1/appStoreVersionLocalizations/loc-1", handler.RequestUris[0].ToString());
        Assert.Contains("\"description\":\"Updated.\"", handler.RequestBodies[0], StringComparison.Ordinal);
        Assert.Contains("\"supportUrl\":\"https://example.test/support\"", handler.RequestBodies[0], StringComparison.Ordinal);
        Assert.DoesNotContain("promotionalText", handler.RequestBodies[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReleaseReadinessService_RequiresSelectedBuildMetadataAndCompleteScreenshots()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "version-1",
                      "type": "appStoreVersions",
                      "attributes": { "versionString": "1.0.1", "platform": "IOS" }
                    }
                  ]
                }
                """),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "build-5",
                      "type": "builds",
                      "attributes": { "version": "5", "processingState": "VALID", "expired": false },
                      "relationships": { "preReleaseVersion": { "data": { "id": "pre-1", "type": "preReleaseVersions" } } }
                    }
                  ],
                  "included": [
                    { "id": "pre-1", "type": "preReleaseVersions", "attributes": { "version": "1.0.1", "platform": "IOS" } }
                  ]
                }
                """),
            new SequenceResponse(HttpStatusCode.OK,
                """
                { "data": { "id": "build-5", "type": "builds" } }
                """),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "loc-1",
                      "type": "appStoreVersionLocalizations",
                      "attributes": {
                        "locale": "en-US",
                        "description": "Premium remote.",
                        "keywords": "media,remote",
                        "supportUrl": "https://example.test/support"
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
                      "id": "set-1",
                      "type": "appScreenshotSets",
                      "attributes": { "screenshotDisplayType": "APP_IPHONE_65" }
                    }
                  ]
                }
                """),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "shot-1",
                      "type": "appScreenshots",
                      "attributes": {
                        "fileName": "01.png",
                        "assetDeliveryState": { "state": "COMPLETE" }
                      }
                    }
                  ]
                }
                """));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var service = new AppStoreConnectReleaseReadinessService(client);

        var result = await service.CheckAsync(new AppStoreConnectReleaseReadinessRequest
        {
            AppId = "app-1",
            VersionString = "1.0.1",
            BuildNumber = "5",
            Platform = ApplePlatform.iOS,
            RequiredScreenshotDisplayTypes = new[] { "APP_IPHONE_65" }
        });

        Assert.True(result.IsReady);
        Assert.All(result.Checks, check => Assert.True(check.Passed, check.Message));
        Assert.Equal("build-5", result.SelectedBuildId);
        Assert.Equal("Premium remote.", result.Localization?.Description);
        Assert.Equal("COMPLETE", Assert.Single(Assert.Single(result.ScreenshotSets).AssetDeliveryStates));
    }

    [Fact]
    public async Task ReleasePreparationService_CreatesMissingVersionAndSelectsValidBuild()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK, """{ "data": [] }"""),
            new SequenceResponse(HttpStatusCode.Created,
                """
                {
                  "data": {
                    "id": "version-1",
                    "type": "appStoreVersions",
                    "attributes": { "versionString": "1.0.1", "platform": "IOS" }
                  }
                }
                """),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "build-5",
                      "type": "builds",
                      "attributes": { "version": "5", "processingState": "VALID", "expired": false },
                      "relationships": {
                        "preReleaseVersion": {
                          "data": { "id": "pre-1", "type": "preReleaseVersions" }
                        }
                      }
                    }
                  ],
                  "included": [
                    {
                      "id": "pre-1",
                      "type": "preReleaseVersions",
                      "attributes": { "version": "1.0.1", "platform": "IOS" }
                    }
                  ]
                }
                """),
            new SequenceResponse(HttpStatusCode.OK, """{ "data": null }"""),
            new SequenceResponse(HttpStatusCode.NoContent, string.Empty));

        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var service = new AppStoreConnectReleasePreparationService(client);

        var result = await service.PrepareAsync(new AppStoreConnectReleasePreparationRequest
        {
            AppId = "app-1",
            VersionString = "1.0.1",
            BuildNumber = "5",
            Platform = ApplePlatform.iOS
        });

        Assert.True(result.CreatedVersion);
        Assert.True(result.SelectedBuild);
        Assert.Equal("version-1", result.Version?.Id);
        Assert.Equal("build-5", result.Build?.Id);
        Assert.Equal(
            new[] { HttpMethod.Get, HttpMethod.Post, HttpMethod.Get, HttpMethod.Get, new HttpMethod("PATCH") },
            handler.Methods);
        Assert.Contains("filter%5BversionString%5D=1.0.1", handler.RequestUris[0].Query, StringComparison.Ordinal);
        Assert.Contains("filter%5Bversion%5D=5", handler.RequestUris[2].Query, StringComparison.Ordinal);
        Assert.Contains("appStoreVersions/version-1/relationships/build", handler.RequestUris[4].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReleasePreparationService_ChecksRemoteScreenshotsWithoutLocalScreenshotSync()
    {
        var versionResponse = new SequenceResponse(HttpStatusCode.OK,
            """
            {
              "data": [
                {
                  "id": "version-1",
                  "type": "appStoreVersions",
                  "attributes": { "versionString": "1.0.5", "platform": "IOS" }
                }
              ]
            }
            """);
        var handler = new SequenceHandler(
            versionResponse,
            versionResponse,
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "build-9",
                      "type": "builds",
                      "attributes": { "version": "9", "processingState": "VALID", "expired": false },
                      "relationships": { "preReleaseVersion": { "data": { "id": "pre-1", "type": "preReleaseVersions" } } }
                    }
                  ],
                  "included": [
                    { "id": "pre-1", "type": "preReleaseVersions", "attributes": { "version": "1.0.5", "platform": "IOS" } }
                  ]
                }
                """),
            new SequenceResponse(HttpStatusCode.OK, """{ "data": { "id": "build-9", "type": "builds" } }"""),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "loc-1",
                      "type": "appStoreVersionLocalizations",
                      "attributes": {
                        "locale": "en-US",
                        "description": "Premium remote.",
                        "keywords": "media,remote",
                        "supportUrl": "https://example.test/support"
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
                      "id": "set-1",
                      "type": "appScreenshotSets",
                      "attributes": { "screenshotDisplayType": "APP_IPHONE_65" }
                    }
                  ]
                }
                """),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "shot-1",
                      "type": "appScreenshots",
                      "attributes": {
                        "fileName": "01.png",
                        "assetDeliveryState": { "state": "COMPLETE" }
                      }
                    }
                  ]
                }
                """));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var service = new AppStoreConnectReleasePreparationService(client);

        var result = await service.PrepareAsync(new AppStoreConnectReleasePreparationRequest
        {
            AppId = "app-1",
            VersionString = "1.0.5",
            BuildNumber = "9",
            Platform = ApplePlatform.iOS,
            CreateVersion = false,
            SelectBuild = false,
            CheckReadiness = true,
            ReadinessRequest = new AppStoreConnectReleaseReadinessRequest
            {
                ScreenshotSpec = new AppStoreConnectScreenshotSyncSpec
                {
                    ScreenshotSets = new[]
                    {
                        new AppStoreConnectScreenshotSetSyncSpec
                        {
                            ScreenshotDisplayType = "APP_IPHONE_65",
                            Path = "missing-local-screenshots"
                        }
                    }
                }
            }
        });

        Assert.Null(result.Screenshots);
        Assert.True(result.Readiness?.IsReady);
        Assert.Equal("APP_IPHONE_65", Assert.Single(result.Readiness!.ScreenshotSets).ScreenshotDisplayType);
        Assert.Equal(7, handler.RequestUris.Count);
    }

    [Fact]
    public async Task ReleasePreparationService_EnforcesScreenshotQualityBeforeRemoteMutation()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(root.FullName, "iphone-6-5"));
            var png = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl2n1sAAAAASUVORK5CYII=");
            await File.WriteAllBytesAsync(Path.Combine(folder.FullName, "01-home.png"), png);
            await File.WriteAllBytesAsync(Path.Combine(folder.FullName, "02-room.png"), png);
            var handler = new SequenceHandler(
                new SequenceResponse(HttpStatusCode.OK,
                    """
                    {
                      "data": [
                        {
                          "id": "version-1",
                          "type": "appStoreVersions",
                          "attributes": { "versionString": "1.0.5", "platform": "IOS" }
                        }
                      ]
                    }
                    """));
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
            using var client = new AppStoreConnectClient(CreateCredential(), http);
            var service = new AppStoreConnectReleasePreparationService(client);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.PrepareAsync(
                new AppStoreConnectReleasePreparationRequest
                {
                    AppId = "app-1",
                    VersionString = "1.0.5",
                    BuildNumber = "9",
                    Platform = ApplePlatform.iOS,
                    CreateVersion = false,
                    SelectBuild = false,
                    ScreenshotSpec = new AppStoreConnectScreenshotSyncSpec
                    {
                        AppId = "app-1",
                        VersionString = "1.0.5",
                        Platform = ApplePlatform.iOS,
                        Locale = "en-US",
                        Quality = new AppStoreConnectScreenshotQualitySpec
                        {
                            Enabled = true,
                            RejectDuplicates = true,
                            RequireConsistentDimensions = true,
                            MinimumFileBytes = 0,
                            MinimumKilobytesPerMegapixel = 0
                        },
                        ScreenshotSets = new[]
                        {
                            new AppStoreConnectScreenshotSetSyncSpec
                            {
                                ScreenshotDisplayType = "APP_IPHONE_65",
                                Path = folder.FullName
                            }
                        }
                    },
                    BaseDirectory = root.FullName
                }));

            Assert.Contains("duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Single(handler.Methods);
            Assert.All(handler.Methods, method => Assert.Equal(HttpMethod.Get, method));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task GetSubscriptionsForAppAsync_ListsGroupsAndSubscriptions()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "group-1",
                      "type": "subscriptionGroups",
                      "attributes": { "referenceName": "CasaRay Plus" }
                    }
                  ]
                }
                """),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "sub-monthly",
                      "type": "subscriptions",
                      "attributes": {
                        "productId": "com.evotecit.casaray.plus.monthly",
                        "name": "CasaRay Plus Monthly",
                        "state": "READY_TO_SUBMIT",
                        "subscriptionPeriod": "ONE_MONTH",
                        "familySharable": false
                      }
                    },
                    {
                      "id": "sub-yearly",
                      "type": "subscriptions",
                      "attributes": {
                        "productId": "com.evotecit.casaray.plus.yearly",
                        "name": "CasaRay Plus Yearly",
                        "state": "READY_TO_SUBMIT",
                        "subscriptionPeriod": "ONE_YEAR",
                        "familySharable": false
                      }
                    }
                  ]
                }
                """));

        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var subscriptions = await client.GetSubscriptionsForAppAsync("app-1");

        Assert.Equal(2, subscriptions.Length);
        Assert.All(subscriptions, subscription =>
        {
            Assert.Equal("group-1", subscription.SubscriptionGroupId);
            Assert.Equal("CasaRay Plus", subscription.SubscriptionGroupReferenceName);
            Assert.Equal("READY_TO_SUBMIT", subscription.State);
        });
        Assert.Equal("com.evotecit.casaray.plus.monthly", subscriptions[0].ProductId);
        Assert.Equal("ONE_MONTH", subscriptions[0].SubscriptionPeriod);
        Assert.False(subscriptions[0].FamilySharable);
        Assert.Contains("apps/app-1/subscriptionGroups?", handler.RequestUris[0].ToString(), StringComparison.Ordinal);
        Assert.Contains("subscriptionGroups/group-1/subscriptions?", handler.RequestUris[1].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UploadScreenshotAsync_CreatesReservationUploadsAssetAndCommitsChecksum()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var screenshotPath = Path.Combine(root.FullName, "remote.png");
            await File.WriteAllBytesAsync(screenshotPath, new byte[] { 1, 2, 3, 4, 5 });
            var handler = new SequenceHandler(
                new SequenceResponse(HttpStatusCode.Created,
                    """
                    {
                      "data": {
                        "id": "shot-1",
                        "type": "appScreenshots",
                        "attributes": {
                          "fileName": "remote.png",
                          "fileSize": 5,
                          "assetToken": "asset-token",
                          "uploadOperations": [
                            {
                              "method": "PUT",
                              "url": "https://asset-upload.example/upload/1",
                              "offset": 0,
                              "length": 5,
                              "requestHeaders": [
                                { "name": "Content-Type", "value": "image/png" }
                              ]
                            }
                          ]
                        }
                      }
                    }
                    """),
                new SequenceResponse(HttpStatusCode.OK, string.Empty),
                new SequenceResponse(HttpStatusCode.OK,
                    """
                    {
                      "data": {
                        "id": "shot-1",
                        "type": "appScreenshots",
                        "attributes": {
                          "fileName": "remote.png",
                          "fileSize": 5,
                          "sourceFileChecksum": "7cfdd07889b3295d6a550914ab35e068",
                          "assetDeliveryState": { "state": "UPLOAD_COMPLETE" }
                        }
                      }
                    }
                    """));

            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
            using var client = new AppStoreConnectClient(CreateCredential(), http);

            var result = await client.UploadScreenshotAsync("set-1", screenshotPath);

            Assert.Equal("shot-1", result.Screenshot.Id);
            Assert.Equal("7cfdd07889b3295d6a550914ab35e068", result.SourceFileChecksum);
            Assert.Equal(1, result.UploadOperationCount);
            Assert.Equal(new[] { HttpMethod.Post, HttpMethod.Put, new HttpMethod("PATCH") }, handler.Methods.ToArray());
            Assert.Equal("https://api.appstoreconnect.apple.com/v1/appScreenshots", handler.RequestUris[0].ToString());
            Assert.Equal("https://asset-upload.example/upload/1", handler.RequestUris[1].ToString());
            Assert.Equal("https://api.appstoreconnect.apple.com/v1/appScreenshots/shot-1", handler.RequestUris[2].ToString());
            Assert.Contains("\"type\":\"appScreenshots\"", handler.RequestBodies[0], StringComparison.Ordinal);
            Assert.Contains("\"id\":\"set-1\"", handler.RequestBodies[0], StringComparison.Ordinal);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, handler.RequestBodyBytes[1]);
            Assert.Contains("\"uploaded\":true", handler.RequestBodies[2], StringComparison.Ordinal);
            Assert.Contains("\"sourceFileChecksum\":\"7cfdd07889b3295d6a550914ab35e068\"", handler.RequestBodies[2], StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task ScreenshotSyncService_CreatesMissingSetAndUploadsMappedFolder()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(root.FullName, "iphone-6-5"));
            var screenshotPath = Path.Combine(folder.FullName, "01-home.png");
            await File.WriteAllBytesAsync(screenshotPath, new byte[] { 9, 8, 7 });

            var handler = new SequenceHandler(
                new SequenceResponse(HttpStatusCode.OK,
                    """
                    {
                      "data": [
                        {
                          "id": "version-1",
                          "type": "appStoreVersions",
                          "attributes": {
                            "versionString": "1.0.0",
                            "appStoreState": "PREPARE_FOR_SUBMISSION",
                            "platform": "IOS"
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
                          "id": "loc-1",
                          "type": "appStoreVersionLocalizations",
                          "attributes": { "locale": "en-US", "name": "Tactra" }
                        }
                      ]
                    }
                    """),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [] }"""),
                new SequenceResponse(HttpStatusCode.Created,
                    """
                    {
                      "data": {
                        "id": "set-1",
                        "type": "appScreenshotSets",
                        "attributes": { "screenshotDisplayType": "APP_IPHONE_65" }
                      }
                    }
                    """),
                new SequenceResponse(HttpStatusCode.Created,
                    """
                    {
                      "data": {
                        "id": "shot-1",
                        "type": "appScreenshots",
                        "attributes": {
                          "fileName": "01-home.png",
                          "fileSize": 3,
                          "uploadOperations": [
                            {
                              "method": "PUT",
                              "url": "https://asset-upload.example/upload/shot-1",
                              "offset": 0,
                              "length": 3,
                              "requestHeaders": []
                            }
                          ]
                        }
                      }
                    }
                    """),
                new SequenceResponse(HttpStatusCode.OK, string.Empty),
                new SequenceResponse(HttpStatusCode.OK,
                    """
                    {
                      "data": {
                        "id": "shot-1",
                        "type": "appScreenshots",
                        "attributes": {
                          "fileName": "01-home.png",
                          "fileSize": 3,
                          "sourceFileChecksum": "4bfb9f0ae77a3d74c7fc51ed853fddc1",
                          "assetDeliveryState": { "state": "UPLOAD_COMPLETE" }
                        }
                      }
                    }
                    """));

            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
            using var client = new AppStoreConnectClient(CreateCredential(), http);
            var service = new AppStoreConnectScreenshotSyncService(client);

            var result = await service.SyncAsync(new AppStoreConnectScreenshotSyncRequest
            {
                BaseDirectory = root.FullName,
                Spec = new AppStoreConnectScreenshotSyncSpec
                {
                    AppId = "app-1",
                    VersionString = "1.0.0",
                    Platform = ApplePlatform.iOS,
                    Locale = "en-US",
                    ScreenshotSets = new[]
                    {
                        new AppStoreConnectScreenshotSetSyncSpec
                        {
                            ScreenshotDisplayType = "APP_IPHONE_65",
                            Path = "iphone-6-5"
                        }
                    }
                }
            });

            var setResult = Assert.Single(result.ScreenshotSets);
            Assert.Equal("version-1", result.Version.Id);
            Assert.Equal("loc-1", result.Localization.Id);
            Assert.Equal("set-1", setResult.ScreenshotSetId);
            Assert.Equal("APP_IPHONE_65", setResult.ScreenshotDisplayType);
            Assert.Single(setResult.Uploaded);
            Assert.Equal(new byte[] { 9, 8, 7 }, handler.RequestBodyBytes[5]);
            Assert.Contains("apps/app-1/appStoreVersions?", handler.RequestUris[0].ToString(), StringComparison.Ordinal);
            Assert.Contains("appStoreVersions/version-1/appStoreVersionLocalizations", handler.RequestUris[1].ToString(), StringComparison.Ordinal);
            Assert.Contains("appStoreVersionLocalizations/loc-1/appScreenshotSets", handler.RequestUris[2].ToString(), StringComparison.Ordinal);
            Assert.Equal("https://api.appstoreconnect.apple.com/v1/appScreenshotSets", handler.RequestUris[3].ToString());
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task ScreenshotSyncService_PreflightsAllLocalMappingsBeforeRemoteRequests()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(root.FullName, "iphone-6-5"));
            await File.WriteAllBytesAsync(Path.Combine(folder.FullName, "01-home.png"), new byte[] { 1 });

            var handler = new SequenceHandler();
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
            using var client = new AppStoreConnectClient(CreateCredential(), http);
            var service = new AppStoreConnectScreenshotSyncService(client);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SyncAsync(new AppStoreConnectScreenshotSyncRequest
            {
                BaseDirectory = root.FullName,
                Spec = new AppStoreConnectScreenshotSyncSpec
                {
                    AppId = "app-1",
                    VersionString = "1.0.0",
                    Platform = ApplePlatform.iOS,
                    Locale = "en-US",
                    ScreenshotSets = new[]
                    {
                        new AppStoreConnectScreenshotSetSyncSpec
                        {
                            ScreenshotDisplayType = "APP_IPHONE_65",
                            Path = "iphone-6-5"
                        },
                        new AppStoreConnectScreenshotSetSyncSpec
                        {
                            ScreenshotDisplayType = "APP_IPHONE_67",
                            Path = "missing"
                        }
                    }
                }
            }));

            Assert.Contains("Screenshot preflight failed", exception.Message, StringComparison.Ordinal);
            Assert.Contains("folder was not found", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(handler.RequestUris);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task ScreenshotSyncService_RejectsQualityFailuresBeforeRemoteRequests()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(root.FullName, "iphone-6-5"));
            var png = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl2n1sAAAAASUVORK5CYII=");
            await File.WriteAllBytesAsync(Path.Combine(folder.FullName, "01-home.png"), png);
            await File.WriteAllBytesAsync(Path.Combine(folder.FullName, "02-room.png"), png);

            var handler = new SequenceHandler();
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
            using var client = new AppStoreConnectClient(CreateCredential(), http);
            var service = new AppStoreConnectScreenshotSyncService(client);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SyncAsync(
                new AppStoreConnectScreenshotSyncRequest
                {
                    BaseDirectory = root.FullName,
                    ReplaceExisting = true,
                    Spec = new AppStoreConnectScreenshotSyncSpec
                    {
                        AppId = "app-1",
                        VersionString = "1.0.0",
                        Platform = ApplePlatform.iOS,
                        Locale = "en-US",
                        Quality = new AppStoreConnectScreenshotQualitySpec
                        {
                            Enabled = true,
                            MinimumFileBytes = 0,
                            MinimumKilobytesPerMegapixel = 0,
                            RejectDuplicates = true
                        },
                        ScreenshotSets = new[]
                        {
                            new AppStoreConnectScreenshotSetSyncSpec
                            {
                                ScreenshotDisplayType = "APP_IPHONE_65",
                                Path = "iphone-6-5"
                            }
                        }
                    }
                }));

            Assert.Contains("duplicates", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(handler.RequestUris);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task ScreenshotSyncService_RejectsAppendWhenExistingScreenshotsWouldExceedLimit()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(root.FullName, "iphone-6-5"));
            for (var i = 1; i <= 5; i++)
                await File.WriteAllBytesAsync(Path.Combine(folder.FullName, $"{i:00}-shot.png"), new byte[] { 1 });

            var existingScreenshotsJson = string.Join(
                ",",
                Enumerable.Range(1, 8).Select(i => $$"""
                {
                  "id": "existing-{{i}}",
                  "type": "appScreenshots",
                  "attributes": { "fileName": "{{i}}.png", "fileSize": 1 }
                }
                """));

            var handler = new SequenceHandler(
                new SequenceResponse(HttpStatusCode.OK,
                    """
                    {
                      "data": [
                        {
                          "id": "version-1",
                          "type": "appStoreVersions",
                          "attributes": {
                            "versionString": "1.0.0",
                            "appStoreState": "PREPARE_FOR_SUBMISSION",
                            "platform": "IOS"
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
                          "id": "loc-1",
                          "type": "appStoreVersionLocalizations",
                          "attributes": { "locale": "en-US", "name": "Tactra" }
                        }
                      ]
                    }
                    """),
                new SequenceResponse(HttpStatusCode.OK,
                    """
                    {
                      "data": [
                        {
                          "id": "set-1",
                          "type": "appScreenshotSets",
                          "attributes": { "screenshotDisplayType": "APP_IPHONE_65" }
                        }
                      ]
                    }
                    """),
                new SequenceResponse(HttpStatusCode.OK, $$"""
                    { "data": [ {{existingScreenshotsJson}} ] }
                    """));

            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
            using var client = new AppStoreConnectClient(CreateCredential(), http);
            var service = new AppStoreConnectScreenshotSyncService(client);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SyncAsync(new AppStoreConnectScreenshotSyncRequest
            {
                BaseDirectory = root.FullName,
                Spec = new AppStoreConnectScreenshotSyncSpec
                {
                    AppId = "app-1",
                    VersionString = "1.0.0",
                    Platform = ApplePlatform.iOS,
                    Locale = "en-US",
                    ScreenshotSets = new[]
                    {
                        new AppStoreConnectScreenshotSetSyncSpec
                        {
                            ScreenshotDisplayType = "APP_IPHONE_65",
                            Path = "iphone-6-5"
                        }
                    }
                }
            }));

            Assert.Contains("would exceed Apple's 10 screenshots per set limit", ex.Message, StringComparison.Ordinal);
            Assert.Equal(4, handler.RequestUris.Count);
            Assert.All(handler.Methods, method => Assert.Equal(HttpMethod.Get, method));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task AddBuildsToBetaGroupAsync_PostsBuildRelationship()
    {
        var handler = new SequenceHandler(new SequenceResponse(HttpStatusCode.NoContent, string.Empty));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        await client.AddBuildsToBetaGroupAsync("group-1", new[] { "build-5" });

        Assert.Equal(HttpMethod.Post, handler.Methods[0]);
        Assert.Equal("https://api.appstoreconnect.apple.com/v1/betaGroups/group-1/relationships/builds", handler.RequestUris[0].ToString());
        Assert.Contains("\"type\":\"builds\"", handler.RequestBodies[0], StringComparison.Ordinal);
        Assert.Contains("\"id\":\"build-5\"", handler.RequestBodies[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestFlightDistributionService_AddsValidBuildAndTesterToGroups()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "build-5",
                      "type": "builds",
                      "attributes": { "version": "5", "processingState": "VALID", "expired": false },
                      "relationships": { "preReleaseVersion": { "data": { "id": "pre-1", "type": "preReleaseVersions" } } }
                    }
                  ],
                  "included": [
                    { "id": "pre-1", "type": "preReleaseVersions", "attributes": { "version": "1.0.1", "platform": "IOS" } }
                  ]
                }
                """),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "group-1",
                      "type": "betaGroups",
                      "attributes": { "name": "Internal", "publicLinkEnabled": false }
                    }
                  ]
                }
                """),
            new SequenceResponse(HttpStatusCode.NoContent, string.Empty),
            new SequenceResponse(HttpStatusCode.OK, """{ "data": [] }"""),
            new SequenceResponse(HttpStatusCode.Created,
                """
                {
                  "data": {
                    "id": "tester-1",
                    "type": "betaTesters",
                    "attributes": { "email": "tester@example.test", "firstName": "Test", "lastName": "User" }
                  }
                }
                """),
            new SequenceResponse(HttpStatusCode.NoContent, string.Empty));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var service = new AppStoreConnectTestFlightDistributionService(client);

        var result = await service.DistributeAsync(new AppStoreConnectTestFlightDistributionRequest
        {
            AppId = "app-1",
            VersionString = "1.0.1",
            BuildNumber = "5",
            Platform = ApplePlatform.iOS,
            BetaGroupNames = new[] { "Internal" },
            Testers = new[]
            {
                new AppStoreConnectBetaTesterSpec
                {
                    Email = "tester@example.test",
                    FirstName = "Test",
                    LastName = "User"
                }
            }
        });

        Assert.Equal("build-5", result.Build.Id);
        Assert.Equal("Internal", Assert.Single(result.BetaGroups).Name);
        Assert.Equal("tester@example.test", Assert.Single(result.Testers).Email);
        Assert.Contains("filter%5Bapp%5D=app-1", handler.RequestUris[1].Query, StringComparison.Ordinal);
        Assert.Contains("filter%5Bname%5D=Internal", handler.RequestUris[1].Query, StringComparison.Ordinal);
        Assert.Equal("https://api.appstoreconnect.apple.com/v1/betaGroups/group-1/relationships/builds", handler.RequestUris[2].ToString());
        Assert.Equal("https://api.appstoreconnect.apple.com/v1/betaTesters?limit=10&filter%5Bemail%5D=tester%40example.test", handler.RequestUris[3].ToString());
        Assert.Equal("https://api.appstoreconnect.apple.com/v1/betaTesters", handler.RequestUris[4].ToString());
        Assert.Equal("https://api.appstoreconnect.apple.com/v1/betaGroups/group-1/relationships/betaTesters", handler.RequestUris[5].ToString());
    }

    [Fact]
    public async Task TestFlightDistributionService_SkipsOnlyAutomaticInternalGroupBuildAssignment()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "build-9",
                      "type": "builds",
                      "attributes": { "version": "9", "processingState": "VALID", "expired": false },
                      "relationships": { "preReleaseVersion": { "data": { "id": "pre-1", "type": "preReleaseVersions" } } }
                    }
                  ],
                  "included": [
                    { "id": "pre-1", "type": "preReleaseVersions", "attributes": { "version": "1.0.5", "platform": "IOS" } }
                  ]
                }
                """),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "group-internal",
                      "type": "betaGroups",
                      "attributes": { "name": "Home", "isInternalGroup": true, "hasAccessToAllBuilds": true }
                    }
                  ]
                }
                """),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "group-manual-internal",
                      "type": "betaGroups",
                      "attributes": { "name": "Manual Internal", "isInternalGroup": true, "hasAccessToAllBuilds": false }
                    }
                  ]
                }
                """),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "group-external",
                      "type": "betaGroups",
                      "attributes": { "name": "Discord Testers", "isInternalGroup": false }
                    }
                  ]
                }
                """),
            new SequenceResponse(HttpStatusCode.NoContent, string.Empty),
            new SequenceResponse(HttpStatusCode.NoContent, string.Empty));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var service = new AppStoreConnectTestFlightDistributionService(client);

        var result = await service.DistributeAsync(new AppStoreConnectTestFlightDistributionRequest
        {
            AppId = "app-1",
            VersionString = "1.0.5",
            BuildNumber = "9",
            Platform = ApplePlatform.iOS,
            BetaGroupNames = new[] { "Home", "Manual Internal", "Discord Testers" }
        });

        Assert.Equal(3, result.BetaGroups.Length);
        Assert.Contains(result.Messages, message => message.Contains("skipped explicit build assignment", StringComparison.Ordinal));
        Assert.Contains(result.Messages, message => message.Contains("Manual Internal", StringComparison.Ordinal));
        Assert.Contains(result.Messages, message => message.Contains("Discord Testers", StringComparison.Ordinal));
        Assert.Equal(6, handler.RequestUris.Count);
        Assert.Equal("https://api.appstoreconnect.apple.com/v1/betaGroups/group-manual-internal/relationships/builds", handler.RequestUris[4].ToString());
        Assert.Equal("https://api.appstoreconnect.apple.com/v1/betaGroups/group-external/relationships/builds", handler.RequestUris[5].ToString());
        Assert.DoesNotContain(handler.RequestUris, uri => uri.ToString().Contains("group-internal/relationships/builds", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TestFlightDistributionService_AssignsExistingTesterToManualInternalGroup()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "build-9",
                      "type": "builds",
                      "attributes": { "version": "9", "processingState": "VALID", "expired": false },
                      "relationships": { "preReleaseVersion": { "data": { "id": "pre-1", "type": "preReleaseVersions" } } }
                    }
                  ],
                  "included": [
                    { "id": "pre-1", "type": "preReleaseVersions", "attributes": { "version": "1.0.5", "platform": "IOS" } }
                  ]
                }
                """),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "group-manual-internal",
                      "type": "betaGroups",
                      "attributes": { "name": "Manual Internal", "isInternalGroup": true, "hasAccessToAllBuilds": false }
                    }
                  ]
                }
                """),
            new SequenceResponse(HttpStatusCode.NoContent, string.Empty),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "tester-internal",
                      "type": "betaTesters",
                      "attributes": { "email": "internal@example.test", "firstName": "Internal", "lastName": "User" }
                    }
                  ]
                }
                """),
            new SequenceResponse(HttpStatusCode.NoContent, string.Empty));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var service = new AppStoreConnectTestFlightDistributionService(client);

        var result = await service.DistributeAsync(new AppStoreConnectTestFlightDistributionRequest
        {
            AppId = "app-1",
            VersionString = "1.0.5",
            BuildNumber = "9",
            Platform = ApplePlatform.iOS,
            BetaGroupNames = new[] { "Manual Internal" },
            Testers = new[]
            {
                new AppStoreConnectBetaTesterSpec { Email = "internal@example.test" }
            }
        });

        Assert.Equal("internal@example.test", Assert.Single(result.Testers).Email);
        Assert.Equal(5, handler.RequestUris.Count);
        Assert.Equal("https://api.appstoreconnect.apple.com/v1/betaGroups/group-manual-internal/relationships/builds", handler.RequestUris[2].ToString());
        Assert.Equal("https://api.appstoreconnect.apple.com/v1/betaGroups/group-manual-internal/relationships/betaTesters", handler.RequestUris[4].ToString());
    }

    [Fact]
    public async Task TestFlightDistributionService_RejectsMissingTesterWhenAnyTargetGroupIsInternal()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "build-9",
                      "type": "builds",
                      "attributes": { "version": "9", "processingState": "VALID", "expired": false },
                      "relationships": { "preReleaseVersion": { "data": { "id": "pre-1", "type": "preReleaseVersions" } } }
                    }
                  ],
                  "included": [
                    { "id": "pre-1", "type": "preReleaseVersions", "attributes": { "version": "1.0.5", "platform": "IOS" } }
                  ]
                }
                """),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "group-manual-internal",
                      "type": "betaGroups",
                      "attributes": { "name": "Manual Internal", "isInternalGroup": true, "hasAccessToAllBuilds": false }
                    }
                  ]
                }
                """),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "group-external",
                      "type": "betaGroups",
                      "attributes": { "name": "Discord Testers", "isInternalGroup": false }
                    }
                  ]
                }
                """),
            new SequenceResponse(HttpStatusCode.NoContent, string.Empty),
            new SequenceResponse(HttpStatusCode.NoContent, string.Empty),
            new SequenceResponse(HttpStatusCode.OK, """{ "data": [] }"""));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var service = new AppStoreConnectTestFlightDistributionService(client);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DistributeAsync(
            new AppStoreConnectTestFlightDistributionRequest
            {
                AppId = "app-1",
                VersionString = "1.0.5",
                BuildNumber = "9",
                Platform = ApplePlatform.iOS,
                BetaGroupNames = new[] { "Manual Internal", "Discord Testers" },
                Testers = new[]
                {
                    new AppStoreConnectBetaTesterSpec { Email = "missing@example.test" }
                }
            }));

        Assert.Contains("must already exist in App Store Connect", ex.Message, StringComparison.Ordinal);
        Assert.Equal(6, handler.RequestUris.Count);
        Assert.DoesNotContain(handler.RequestUris, uri =>
            string.Equals(uri.AbsolutePath, "/v1/betaTesters", StringComparison.Ordinal) &&
            !string.IsNullOrEmpty(handler.RequestBodies[handler.RequestUris.IndexOf(uri)]));
    }

    [Fact]
    public async Task BetaAppReviewSubmissionService_SubmitsBuildForExternalTesting()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "build-6",
                      "type": "builds",
                      "attributes": { "version": "6", "processingState": "VALID", "expired": false },
                      "relationships": { "preReleaseVersion": { "data": { "id": "pre-1", "type": "preReleaseVersions" } } }
                    }
                  ],
                  "included": [
                    { "id": "pre-1", "type": "preReleaseVersions", "attributes": { "version": "1.0.2", "platform": "IOS" } }
                  ]
                }
                """),
            new SequenceResponse(HttpStatusCode.OK, """{ "data": null }"""),
            new SequenceResponse(HttpStatusCode.Created,
                """
                {
                  "data": {
                    "id": "submission-6",
                    "type": "betaAppReviewSubmissions",
                    "attributes": { "betaReviewState": "WAITING_FOR_REVIEW" },
                    "relationships": {
                      "build": { "data": { "type": "builds", "id": "build-6" } }
                    }
                  }
                }
                """));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var service = new AppStoreConnectBetaAppReviewSubmissionService(client);

        var result = await service.SubmitAsync(new AppStoreConnectBetaAppReviewSubmissionRequest
        {
            AppId = "app-1",
            VersionString = "1.0.2",
            BuildNumber = "6",
            Platform = ApplePlatform.iOS
        });

        Assert.Equal("build-6", result.Build.Id);
        Assert.Equal("submission-6", result.Submission.Id);
        Assert.Equal("WAITING_FOR_REVIEW", result.Submission.BetaReviewState);
        Assert.Equal("https://api.appstoreconnect.apple.com/v1/builds/build-6/betaAppReviewSubmission", handler.RequestUris[1].ToString());
        Assert.Equal(HttpMethod.Post, handler.Methods[2]);
        Assert.Equal("https://api.appstoreconnect.apple.com/v1/betaAppReviewSubmissions", handler.RequestUris[2].ToString());
        Assert.Contains("\"type\":\"betaAppReviewSubmissions\"", handler.RequestBodies[2], StringComparison.Ordinal);
        Assert.Contains("\"type\":\"builds\",\"id\":\"build-6\"", handler.RequestBodies[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task BetaAppReviewSubmissionService_ReusesExistingSubmission()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "build-6",
                      "type": "builds",
                      "attributes": { "version": "6", "processingState": "VALID", "expired": false },
                      "relationships": { "preReleaseVersion": { "data": { "id": "pre-1", "type": "preReleaseVersions" } } }
                    }
                  ],
                  "included": [
                    { "id": "pre-1", "type": "preReleaseVersions", "attributes": { "version": "1.0.2", "platform": "IOS" } }
                  ]
                }
                """),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": {
                    "id": "submission-6",
                    "type": "betaAppReviewSubmissions",
                    "attributes": { "betaReviewState": "APPROVED" },
                    "relationships": {
                      "build": { "data": { "type": "builds", "id": "build-6" } }
                    }
                  }
                }
                """));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var service = new AppStoreConnectBetaAppReviewSubmissionService(client);

        var result = await service.SubmitAsync(new AppStoreConnectBetaAppReviewSubmissionRequest
        {
            AppId = "app-1",
            VersionString = "1.0.2",
            BuildNumber = "6",
            Platform = ApplePlatform.iOS
        });

        Assert.Equal("submission-6", result.Submission.Id);
        Assert.Equal("APPROVED", result.Submission.BetaReviewState);
        Assert.Equal(2, handler.RequestUris.Count);
        Assert.All(handler.Methods, method => Assert.Equal(HttpMethod.Get, method));
    }

    [Fact]
    public async Task ReleaseStateService_SummarizesAppStoreTestFlightAndBetaGroups()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "version-1",
                      "type": "appStoreVersions",
                      "attributes": { "versionString": "1.0.2", "platform": "IOS", "appStoreState": "WAITING_FOR_REVIEW", "appVersionState": "WAITING_FOR_REVIEW" }
                    }
                  ]
                }
                """),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "build-6",
                      "type": "builds",
                      "attributes": { "version": "6", "processingState": "VALID", "expired": false },
                      "relationships": { "preReleaseVersion": { "data": { "id": "pre-1", "type": "preReleaseVersions" } } }
                    }
                  ],
                  "included": [
                    { "id": "pre-1", "type": "preReleaseVersions", "attributes": { "version": "1.0.2", "platform": "IOS" } }
                  ]
                }
                """),
            new SequenceResponse(HttpStatusCode.OK, """{ "data": { "type": "builds", "id": "build-6" } }"""),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": {
                    "id": "build-6",
                    "type": "builds",
                    "attributes": { "version": "6", "processingState": "VALID", "expired": false },
                    "relationships": { "preReleaseVersion": { "data": { "id": "pre-1", "type": "preReleaseVersions" } } }
                  },
                  "included": [
                    { "id": "pre-1", "type": "preReleaseVersions", "attributes": { "version": "1.0.2", "platform": "IOS" } }
                  ]
                }
                """),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "review-1",
                      "type": "reviewSubmissions",
                      "attributes": { "platform": "IOS", "state": "WAITING_FOR_REVIEW", "submitted": true }
                    }
                  ]
                }
                """),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": {
                    "id": "detail-1",
                    "type": "buildBetaDetails",
                    "attributes": { "internalBuildState": "IN_BETA_TESTING", "externalBuildState": "WAITING_FOR_BETA_REVIEW", "autoNotifyEnabled": true }
                  }
                }
                """),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": {
                    "id": "beta-review-1",
                    "type": "betaAppReviewSubmissions",
                    "attributes": { "betaReviewState": "WAITING_FOR_REVIEW" },
                    "relationships": { "build": { "data": { "type": "builds", "id": "build-6" } } }
                  }
                }
                """),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "group-1",
                      "type": "betaGroups",
                      "attributes": { "name": "Discord Testers", "publicLinkEnabled": true, "publicLinkLimit": 10, "publicLink": "https://testflight.apple.com/join/example", "isInternalGroup": false }
                    }
                  ]
                }
                """),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "tester-1",
                      "type": "betaTesters",
                      "attributes": { "email": "tester@example.test" }
                    }
                  ]
                }
                """));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var service = new AppStoreConnectReleaseStateService(client);

        var result = await service.GetAsync(new AppStoreConnectReleaseStateRequest
        {
            AppId = "app-1",
            VersionString = "1.0.2",
            BuildNumber = "6",
            Platforms = new[] { ApplePlatform.iOS },
            BetaGroupNames = new[] { "Discord Testers" }
        });

        var platform = Assert.Single(result.Platforms);
        Assert.Equal("version-1", platform.Version?.Id);
        Assert.Equal("build-6", platform.MatchedBuild?.Id);
        Assert.Equal("build-6", platform.SelectedBuild?.Id);
        Assert.True(platform.MatchedBuildSelected);
        Assert.Equal("WAITING_FOR_REVIEW", Assert.Single(platform.ReviewSubmissions).State);
        Assert.Equal("WAITING_FOR_BETA_REVIEW", platform.BetaDetail?.ExternalBuildState);
        Assert.Equal("WAITING_FOR_REVIEW", platform.BetaReviewSubmission?.BetaReviewState);
        Assert.Contains("iOS: Wait for App Review.", result.NextActions);
        Assert.Contains("iOS: Wait for Beta App Review.", result.NextActions);

        var group = Assert.Single(result.BetaGroups);
        Assert.Equal("Discord Testers", group.Name);
        Assert.True(group.PublicLinkEnabled);
        Assert.Equal(1, group.TesterCount);
        Assert.False(group.IsFull);
    }

    [Fact]
    public async Task ReviewSubmissionClient_CreatesItemAndSubmits()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.Created,
                """
                {
                  "data": {
                    "id": "submission-1",
                    "type": "reviewSubmissions",
                    "attributes": { "platform": "IOS", "submitted": false, "state": "IN_PROGRESS" }
                  }
                }
                """),
            new SequenceResponse(HttpStatusCode.Created,
                """
                {
                  "data": {
                    "id": "item-1",
                    "type": "reviewSubmissionItems",
                    "relationships": {
                      "reviewSubmission": { "data": { "type": "reviewSubmissions", "id": "submission-1" } },
                      "appStoreVersion": { "data": { "type": "appStoreVersions", "id": "version-1" } }
                    }
                  }
                }
                """),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": {
                    "id": "submission-1",
                    "type": "reviewSubmissions",
                    "attributes": { "platform": "IOS", "submitted": true, "state": "WAITING_FOR_REVIEW" }
                  }
                }
                """));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var submission = await client.CreateReviewSubmissionAsync("app-1", ApplePlatform.iOS);
        var item = await client.CreateReviewSubmissionItemAsync(submission.Id, "version-1");
        var submitted = await client.SubmitReviewSubmissionAsync(submission.Id);

        Assert.Equal("submission-1", submission.Id);
        Assert.False(submission.IsSubmitted);
        Assert.Equal("item-1", item.Id);
        Assert.Equal("version-1", item.AppStoreVersionId);
        Assert.True(submitted.IsSubmitted);
        Assert.Equal(HttpMethod.Post, handler.Methods[0]);
        Assert.Equal("https://api.appstoreconnect.apple.com/v1/reviewSubmissions", handler.RequestUris[0].ToString());
        Assert.Contains("\"type\":\"reviewSubmissions\"", handler.RequestBodies[0], StringComparison.Ordinal);
        Assert.Contains("\"platform\":\"IOS\"", handler.RequestBodies[0], StringComparison.Ordinal);
        Assert.Equal("https://api.appstoreconnect.apple.com/v1/reviewSubmissionItems", handler.RequestUris[1].ToString());
        Assert.Contains("\"type\":\"appStoreVersions\"", handler.RequestBodies[1], StringComparison.Ordinal);
        Assert.Equal(new HttpMethod("PATCH"), handler.Methods[2]);
        Assert.Equal("https://api.appstoreconnect.apple.com/v1/reviewSubmissions/submission-1", handler.RequestUris[2].ToString());
        Assert.Contains("\"submitted\":true", handler.RequestBodies[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReviewSubmissionService_SubmitsSelectedValidBuild()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "version-1",
                      "type": "appStoreVersions",
                      "attributes": { "versionString": "1.0.1", "platform": "IOS", "appStoreState": "PREPARE_FOR_SUBMISSION" }
                    }
                  ]
                }
                """),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "build-5",
                      "type": "builds",
                      "attributes": { "version": "5", "processingState": "VALID", "expired": false },
                      "relationships": { "preReleaseVersion": { "data": { "id": "pre-1", "type": "preReleaseVersions" } } }
                    }
                  ],
                  "included": [
                    { "id": "pre-1", "type": "preReleaseVersions", "attributes": { "version": "1.0.1", "platform": "IOS" } }
                  ]
                }
                """),
            new SequenceResponse(HttpStatusCode.OK, """{ "data": { "type": "builds", "id": "build-5" } }"""),
            new SequenceResponse(HttpStatusCode.OK, """{ "data": [] }"""),
            new SequenceResponse(HttpStatusCode.Created,
                """
                {
                  "data": {
                    "id": "submission-1",
                    "type": "reviewSubmissions",
                    "attributes": { "platform": "IOS", "submitted": false }
                  }
                }
                """),
            new SequenceResponse(HttpStatusCode.Created,
                """
                {
                  "data": {
                    "id": "item-1",
                    "type": "reviewSubmissionItems",
                    "relationships": {
                      "reviewSubmission": { "data": { "type": "reviewSubmissions", "id": "submission-1" } },
                      "appStoreVersion": { "data": { "type": "appStoreVersions", "id": "version-1" } }
                    }
                  }
                }
                """),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": {
                    "id": "submission-1",
                    "type": "reviewSubmissions",
                    "attributes": { "platform": "IOS", "submitted": true, "state": "WAITING_FOR_REVIEW" }
                  }
                }
                """));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var service = new AppStoreConnectReviewSubmissionService(client);

        var result = await service.SubmitAsync(new AppStoreConnectReviewSubmissionRequest
        {
            AppId = "app-1",
            VersionString = "1.0.1",
            BuildNumber = "5",
            Platform = ApplePlatform.iOS,
            CheckReadiness = false
        });

        Assert.Equal("version-1", result.Version.Id);
        Assert.Equal("build-5", result.Build?.Id);
        Assert.Equal("submission-1", result.ReviewSubmission.Id);
        Assert.True(result.ReviewSubmission.IsSubmitted);
        Assert.Equal("item-1", result.ReviewSubmissionItem?.Id);
        Assert.Equal("https://api.appstoreconnect.apple.com/v1/appStoreVersions/version-1/relationships/build", handler.RequestUris[2].ToString());
        Assert.Equal("https://api.appstoreconnect.apple.com/v1/reviewSubmissions?filter%5Bapp%5D=app-1&limit=50&filter%5Bplatform%5D=IOS", handler.RequestUris[3].ToString());
        Assert.Equal("https://api.appstoreconnect.apple.com/v1/reviewSubmissions", handler.RequestUris[4].ToString());
        Assert.Equal("https://api.appstoreconnect.apple.com/v1/reviewSubmissionItems", handler.RequestUris[5].ToString());
        Assert.Equal("https://api.appstoreconnect.apple.com/v1/reviewSubmissions/submission-1", handler.RequestUris[6].ToString());
    }

    [Fact]
    public async Task ReviewSubmissionService_ReusesExistingReadySubmissionItem()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "version-1",
                      "type": "appStoreVersions",
                      "attributes": { "versionString": "1.0.1", "platform": "IOS", "appStoreState": "PREPARE_FOR_SUBMISSION" }
                    }
                  ]
                }
                """),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "build-5",
                      "type": "builds",
                      "attributes": { "version": "5", "processingState": "VALID", "expired": false },
                      "relationships": { "preReleaseVersion": { "data": { "id": "pre-1", "type": "preReleaseVersions" } } }
                    }
                  ],
                  "included": [
                    { "id": "pre-1", "type": "preReleaseVersions", "attributes": { "version": "1.0.1", "platform": "IOS" } }
                  ]
                }
                """),
            new SequenceResponse(HttpStatusCode.OK, """{ "data": { "type": "builds", "id": "build-5" } }"""),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "submission-1",
                      "type": "reviewSubmissions",
                      "attributes": { "platform": "IOS", "state": "READY_FOR_REVIEW" }
                    }
                  ]
                }
                """),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "item-1",
                      "type": "reviewSubmissionItems",
                      "attributes": { "state": "READY_FOR_REVIEW" },
                      "relationships": {
                        "appStoreVersion": { "data": { "type": "appStoreVersions", "id": "version-1" } }
                      }
                    }
                  ]
                }
                """),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": {
                    "id": "submission-1",
                    "type": "reviewSubmissions",
                    "attributes": { "platform": "IOS", "state": "WAITING_FOR_REVIEW" }
                  }
                }
                """));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var service = new AppStoreConnectReviewSubmissionService(client);

        var result = await service.SubmitAsync(new AppStoreConnectReviewSubmissionRequest
        {
            AppId = "app-1",
            VersionString = "1.0.1",
            BuildNumber = "5",
            Platform = ApplePlatform.iOS,
            CheckReadiness = false
        });

        Assert.Equal("submission-1", result.ReviewSubmission.Id);
        Assert.True(result.ReviewSubmission.IsSubmitted);
        Assert.Equal("item-1", result.ReviewSubmissionItem?.Id);
        Assert.Equal("https://api.appstoreconnect.apple.com/v1/reviewSubmissions/submission-1/items?fields%5BreviewSubmissionItems%5D=state%2CappStoreVersion&include=appStoreVersion&limit=50", handler.RequestUris[4].ToString());
        Assert.Equal("https://api.appstoreconnect.apple.com/v1/reviewSubmissions/submission-1", handler.RequestUris[5].ToString());
    }

    [Fact]
    public async Task VersionReleaseService_ReleasesPendingDeveloperVersion()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "version-1",
                      "type": "appStoreVersions",
                      "attributes": {
                        "versionString": "1.0.1",
                        "platform": "IOS",
                        "appStoreState": "PENDING_DEVELOPER_RELEASE"
                      }
                    }
                  ]
                }
                """),
            new SequenceResponse(HttpStatusCode.Created,
                """
                {
                  "data": {
                    "id": "release-1",
                    "type": "appStoreVersionReleaseRequests",
                    "relationships": {
                      "appStoreVersion": { "data": { "type": "appStoreVersions", "id": "version-1" } }
                    }
                  }
                }
                """));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var service = new AppStoreConnectVersionReleaseService(client);

        var result = await service.ReleaseAsync(new AppStoreConnectVersionReleaseRequest
        {
            AppId = "app-1",
            VersionString = "1.0.1",
            Platform = ApplePlatform.iOS
        });

        Assert.Equal("version-1", result.Version.Id);
        Assert.Equal("release-1", result.ReleaseRequest.Id);
        Assert.Equal("version-1", result.ReleaseRequest.AppStoreVersionId);
        Assert.Equal("https://api.appstoreconnect.apple.com/v1/appStoreVersionReleaseRequests", handler.RequestUris[1].ToString());
        Assert.Contains("\"type\":\"appStoreVersionReleaseRequests\"", handler.RequestBodies[1], StringComparison.Ordinal);
        Assert.Contains("\"id\":\"version-1\"", handler.RequestBodies[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task VersionReleaseService_BlocksNonPendingDeveloperVersionByDefault()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "version-1",
                      "type": "appStoreVersions",
                      "attributes": {
                        "versionString": "1.0.1",
                        "platform": "IOS",
                        "appStoreState": "READY_FOR_REVIEW"
                      }
                    }
                  ]
                }
                """));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var service = new AppStoreConnectVersionReleaseService(client);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReleaseAsync(new AppStoreConnectVersionReleaseRequest
        {
            AppId = "app-1",
            VersionString = "1.0.1",
            Platform = ApplePlatform.iOS
        }));

        Assert.Contains("PENDING_DEVELOPER_RELEASE", ex.Message, StringComparison.Ordinal);
        Assert.Single(handler.RequestUris);
    }

    private static AppStoreConnectApiCredential CreateCredential()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pem = ecdsa.ExportPkcs8PrivateKeyPem();
        return new AppStoreConnectApiCredential
        {
            IssuerId = "11111111-2222-3333-4444-555555555555",
            KeyId = "ABC123DEFG",
            PrivateKey = pem
        };
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _json;

        public List<Uri> RequestUris { get; } = new();

        public List<string?> AuthorizationSchemes { get; } = new();

        private readonly HttpStatusCode _statusCode;

        public RecordingHandler(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _json = json;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            AuthorizationSchemes.Add(request.Headers.Authorization?.Scheme);

            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_json)
            });
        }
    }

    private sealed class SequenceResponse
    {
        public SequenceResponse(HttpStatusCode statusCode, string content, TimeSpan? retryAfter = null)
        {
            StatusCode = statusCode;
            Content = content;
            RetryAfter = retryAfter;
        }

        public HttpStatusCode StatusCode { get; }

        public string Content { get; }

        public TimeSpan? RetryAfter { get; }
    }

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<SequenceResponse> _responses;

        public SequenceHandler(params SequenceResponse[] responses)
        {
            _responses = new Queue<SequenceResponse>(responses);
        }

        public List<HttpMethod> Methods { get; } = new();

        public List<Uri> RequestUris { get; } = new();

        public List<string> RequestBodies { get; } = new();

        public List<byte[]> RequestBodyBytes { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_responses.Count == 0)
                throw new InvalidOperationException("No response was configured for request.");

            Methods.Add(request.Method);
            RequestUris.Add(request.RequestUri!);
            if (request.Content is not null)
            {
                var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
                RequestBodyBytes.Add(bytes);
                RequestBodies.Add(Encoding.UTF8.GetString(bytes));
            }
            else
            {
                RequestBodyBytes.Add(Array.Empty<byte>());
                RequestBodies.Add(string.Empty);
            }

            var response = _responses.Dequeue();
            var message = new HttpResponseMessage(response.StatusCode)
            {
                Content = new StringContent(response.Content)
            };
            if (response.RetryAfter.HasValue)
                message.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(response.RetryAfter.Value);
            return message;
        }
    }
}
