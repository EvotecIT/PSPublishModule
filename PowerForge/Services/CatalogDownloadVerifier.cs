using System.Net;
using System.Net.Http;
using System.Security.Cryptography;

namespace PowerForge;

/// <summary>Checks catalog delivery bytes; Store downloads must not redirect or require cookies.</summary>
internal sealed class CatalogDownloadVerifier : IDisposable
{
    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    public CatalogDownloadVerifier(HttpClient? client = null)
    {
        _ownsClient = client is null;
        _client = client ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        { Timeout = TimeSpan.FromMinutes(10) };
    }

    public async Task VerifyAsync(CatalogInstaller artifact, bool store, CancellationToken cancellationToken)
    {
        var uri = RequireHttps(store ? artifact.StoreUrl : artifact.WingetUrl);
        for (var redirects = 0; ; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400 && !store && redirects < 5 && response.Headers.Location is { } location)
            {
                uri = RequireHttps(new Uri(uri, location).AbsoluteUri);
                continue;
            }
            if (response.StatusCode != HttpStatusCode.OK ||
                !string.Equals(response.RequestMessage?.RequestUri?.AbsoluteUri ?? uri.AbsoluteUri, uri.AbsoluteUri, StringComparison.Ordinal))
                throw new InvalidOperationException("Catalog download must return HTTP 200; Store downloads cannot redirect.");
            using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var hash = SHA256.Create();
            var buffer = new byte[81920];
            long length = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) != 0)
            {
                length += read;
                if (length > artifact.Length) throw new InvalidOperationException("Catalog download length exceeds the signed release artifact.");
                hash.TransformBlock(buffer, 0, read, buffer, 0);
            }
            hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            if (length != artifact.Length || !string.Equals(BitConverter.ToString(hash.Hash!).Replace("-", ""), artifact.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Catalog download bytes do not match the signed release artifact.");
            return;
        }
    }

    internal static Uri RequireHttps(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment) || value.Contains("{"))
            throw new InvalidOperationException("Catalog downloads require resolved HTTPS URLs without credentials or fragments.");
        return uri;
    }

    public void Dispose() { if (_ownsClient) _client.Dispose(); }
}
