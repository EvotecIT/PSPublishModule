using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using Xunit;

namespace PowerForge.Tests;

public sealed class AppStoreConnectTerritoryTests
{
    [Fact]
    public async Task GetTerritoryIdsAsync_ReturnsEveryPageForWorldwideAvailability()
    {
        using var handler = new TerritoryHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var client = new AppStoreConnectClient(new AppStoreConnectApiCredential
        {
            IssuerId = "11111111-2222-3333-4444-555555555555",
            KeyId = "ABC123DEFG",
            PrivateKey = key.ExportPkcs8PrivateKeyPem()
        }, http);

        var ids = await client.GetTerritoryIdsAsync();

        Assert.Equal(new[] { "USA", "POL", "GBR" }, ids);
        Assert.Equal(new[]
        {
            "https://api.appstoreconnect.apple.com/v1/territories?limit=200",
            "https://api.appstoreconnect.apple.com/v1/territories?cursor=next"
        }, handler.Urls);
    }

    private sealed class TerritoryHandler : HttpMessageHandler
    {
        public List<string> Urls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Urls.Add(request.RequestUri!.ToString());
            var json = Urls.Count == 1
                ? """{"data":[{"type":"territories","id":"USA"},{"type":"territories","id":"POL"}],"links":{"next":"https://api.appstoreconnect.apple.com/v1/territories?cursor=next"}}"""
                : """{"data":[{"type":"territories","id":"GBR"}],"links":{"next":null}}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}
