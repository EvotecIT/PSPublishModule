using System.Net;

namespace PowerForgeStudio.Orchestrator.Connections;

internal static class HttpConnectionSupport
{
    private static readonly HttpClient SharedClient = CreateClient();

    internal static async Task<bool> IsReachableAsync(string endpoint, CancellationToken cancellationToken, HttpClient? client = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        using var response = await (client ?? SharedClient).SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        return response.StatusCode is >= HttpStatusCode.OK and < HttpStatusCode.InternalServerError;
    }

    private static HttpClient CreateClient() => new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };
}
