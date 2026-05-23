using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace PowerForge;

internal static class PrivateGalleryHttp
{
    internal static HttpClient CreateClient(int timeoutSeconds, HttpMessageHandler? handler)
        => handler is null
            ? new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds > 0 ? timeoutSeconds : 30) }
            : new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(timeoutSeconds > 0 ? timeoutSeconds : 30) };

    internal static void ApplyAuthentication(HttpRequestMessage request, string? token, PrivateGalleryAuthenticationKind authenticationKind)
    {
        if (string.IsNullOrWhiteSpace(token) || authenticationKind == PrivateGalleryAuthenticationKind.None)
            return;

        if (authenticationKind == PrivateGalleryAuthenticationKind.BasicToken)
        {
            var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + token));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);
            return;
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}
