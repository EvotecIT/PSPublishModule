using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
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
}
