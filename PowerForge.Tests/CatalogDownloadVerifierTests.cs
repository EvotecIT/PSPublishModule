using System.Net;
using System.Security.Cryptography;

namespace PowerForge.Tests;

public sealed class CatalogDownloadVerifierTests
{
    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, false, false)]
    [InlineData(false, true, false, true)]
    [InlineData(true, false, true, false)]
    public async Task Verify_RequiresReleaseBytesAndRejectsStoreRedirects(bool store, bool redirect, bool changed, bool succeeds)
    {
        var bytes = new byte[] { 1, 2, 3 };
        using var sha = SHA256.Create();
        var artifact = new CatalogInstaller { StoreUrl = "https://downloads.example.test/app.msi", WingetUrl = "https://github.com/example/app.msi",
            Length = bytes.Length, Sha256 = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "") };
        using var client = new HttpClient(new Handler(request =>
        {
            if (redirect && !request.RequestUri!.AbsolutePath.Contains("final"))
            {
                var response = new HttpResponseMessage(HttpStatusCode.Found);
                response.Headers.Location = new Uri("https://cdn.example.test/final.msi"); return response;
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(changed ? new byte[] { 4, 5, 6 } : bytes) };
        }));
        using var service = new CatalogDownloadVerifier(client);
        if (succeeds) await service.VerifyAsync(artifact, store, default);
        else await Assert.ThrowsAsync<InvalidOperationException>(() => service.VerifyAsync(artifact, store, default));
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(send(request));
    }
}
