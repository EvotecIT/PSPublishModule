using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace PowerForge;

public sealed partial class AppStoreConnectClient
{
    internal static readonly TimeSpan DefaultOwnedClientTimeout = TimeSpan.FromMinutes(10);

    private const int MaximumReadAttempts = 3;

    internal TimeSpan RequestTimeout => _httpClient.Timeout;

    internal Func<TimeSpan, CancellationToken, Task> TransientReadDelayAsync { get; set; }
        = static (delay, cancellationToken) => Task.Delay(delay, cancellationToken);

    private static HttpClient CreateDefaultHttpClient()
        => new()
        {
            BaseAddress = DefaultBaseUri,
            Timeout = DefaultOwnedClientTimeout
        };

    private async Task<AppStoreConnectHttpResponse> SendGetWithTransientRetryAsync(
        string relativeUrl,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokenGenerator.CreateToken(_credential));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode ||
                attempt >= MaximumReadAttempts ||
                !IsTransientReadFailure(response.StatusCode))
            {
                return new AppStoreConnectHttpResponse(
                    response.StatusCode,
                    response.ReasonPhrase,
                    content);
            }

            await TransientReadDelayAsync(TimeSpan.FromSeconds(attempt), cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsTransientReadFailure(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private readonly record struct AppStoreConnectHttpResponse(
        HttpStatusCode StatusCode,
        string? ReasonPhrase,
        string Content)
    {
        public bool IsSuccessStatusCode => (int)StatusCode is >= 200 and <= 299;
    }
}
