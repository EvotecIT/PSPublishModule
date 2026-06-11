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

public sealed class AppStoreConnectClientTests
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

            await Assert.ThrowsAsync<DirectoryNotFoundException>(() => service.SyncAsync(new AppStoreConnectScreenshotSyncRequest
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
        public SequenceResponse(HttpStatusCode statusCode, string content)
        {
            StatusCode = statusCode;
            Content = content;
        }

        public HttpStatusCode StatusCode { get; }

        public string Content { get; }
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
            return new HttpResponseMessage(response.StatusCode)
            {
                Content = new StringContent(response.Content)
            };
        }
    }
}
