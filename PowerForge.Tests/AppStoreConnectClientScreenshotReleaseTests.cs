using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class AppStoreConnectClientTests
{
    [Fact]
    public void Constructor_OwnedClientUsesReleaseAssetTimeout()
    {
        using var client = new AppStoreConnectClient(CreateCredential());

        Assert.Equal(TimeSpan.FromMinutes(10), client.RequestTimeout);
    }

    [Fact]
    public async Task GetVersionsAsync_RetriesTransientServerFailureWithoutRetryingMutations()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.InternalServerError, """{ "errors": [{ "code": "UNEXPECTED_ERROR" }] }"""),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "version-1",
                      "type": "appStoreVersions",
                      "attributes": {
                        "versionString": "1.4.0",
                        "appStoreState": "PREPARE_FOR_SUBMISSION",
                        "platform": "IOS"
                      }
                    }
                  ]
                }
                """));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var delays = new List<TimeSpan>();
        client.TransientReadDelayAsync = (delay, _) =>
        {
            delays.Add(delay);
            return Task.CompletedTask;
        };

        var versions = await client.GetVersionsAsync("app-1", "1.4.0", ApplePlatform.iOS);

        Assert.Equal("version-1", Assert.Single(versions).Id);
        Assert.Equal(2, handler.RequestUris.Count);
        Assert.All(handler.Methods, method => Assert.Equal(HttpMethod.Get, method));
        Assert.Equal(new[] { TimeSpan.FromSeconds(1) }, delays);
    }

    [Fact]
    public async Task GetVersionsAsync_HonorsRetryAfterForRateLimit()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(
                (HttpStatusCode)429,
                """{ "errors": [{ "code": "RATE_LIMIT_EXCEEDED" }] }""",
                TimeSpan.FromSeconds(30)),
            new SequenceResponse(HttpStatusCode.OK,
                """
                {
                  "data": [
                    {
                      "id": "version-1",
                      "type": "appStoreVersions",
                      "attributes": {
                        "versionString": "1.4.0",
                        "appStoreState": "PREPARE_FOR_SUBMISSION",
                        "platform": "IOS"
                      }
                    }
                  ]
                }
                """));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var delays = new List<TimeSpan>();
        client.TransientReadDelayAsync = (delay, _) =>
        {
            delays.Add(delay);
            return Task.CompletedTask;
        };

        var versions = await client.GetVersionsAsync("app-1", "1.4.0", ApplePlatform.iOS);

        Assert.Equal("version-1", Assert.Single(versions).Id);
        Assert.Equal(new[] { TimeSpan.FromSeconds(30) }, delays);
    }

    [Fact]
    public async Task GetVersionsAsync_StopsAfterBoundedTransientRetries()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.InternalServerError, """{ "errors": [{ "code": "UNEXPECTED_ERROR" }] }"""),
            new SequenceResponse(HttpStatusCode.BadGateway, """{ "errors": [{ "code": "UPSTREAM_ERROR" }] }"""),
            new SequenceResponse(HttpStatusCode.ServiceUnavailable, """{ "errors": [{ "code": "UNAVAILABLE" }] }"""));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var delays = new List<TimeSpan>();
        client.TransientReadDelayAsync = (delay, _) =>
        {
            delays.Add(delay);
            return Task.CompletedTask;
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetVersionsAsync("app-1", "1.4.0", ApplePlatform.iOS));

        Assert.Contains("503 Service Unavailable", exception.Message, StringComparison.Ordinal);
        Assert.Equal(3, handler.RequestUris.Count);
        Assert.Equal(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2) }, delays);
    }

    [Fact]
    public async Task ReleasePreparationService_UsesConfiguredScreenshotVersionIdWithoutVersionLookup()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(root.FullName, "desktop"));
            await File.WriteAllBytesAsync(Path.Combine(folder.FullName, "01-home.png"), new byte[] { 1, 2, 3 });
            var handler = new SequenceHandler(
                new SequenceResponse(HttpStatusCode.OK,
                    """
                    {
                      "data": [
                        {
                          "id": "loc-1",
                          "type": "appStoreVersionLocalizations",
                          "attributes": { "locale": "en-US" }
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
                          "attributes": { "screenshotDisplayType": "APP_DESKTOP" }
                        }
                      ]
                    }
                    """),
                new SequenceResponse(HttpStatusCode.OK, """{ "data": [] }"""),
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
                          "sourceFileChecksum": "5289df737df57326fcdd22597afb1fac",
                          "assetDeliveryState": { "state": "UPLOAD_COMPLETE" }
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
                VersionString = "1.4.0",
                BuildNumber = "12",
                Platform = ApplePlatform.macOS,
                CreateVersion = false,
                SelectBuild = false,
                ScreenshotSpec = new AppStoreConnectScreenshotSyncSpec
                {
                    AppId = "app-1",
                    VersionString = "1.4.0",
                    VersionId = "version-exact",
                    Platform = ApplePlatform.macOS,
                    Locale = "en-US",
                    ScreenshotSets = new[]
                    {
                        new AppStoreConnectScreenshotSetSyncSpec
                        {
                            ScreenshotDisplayType = "APP_DESKTOP",
                            Path = folder.FullName
                        }
                    }
                },
                BaseDirectory = root.FullName
            });

            Assert.Equal("version-exact", result.Version?.Id);
            Assert.Single(result.Screenshots!.ScreenshotSets);
            Assert.DoesNotContain(handler.RequestUris, uri => uri.AbsolutePath.Contains("/apps/app-1/appStoreVersions", StringComparison.Ordinal));
            Assert.Contains("appStoreVersions/version-exact/appStoreVersionLocalizations", handler.RequestUris[0].ToString(), StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }
}
