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
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (response.IsSuccessStatusCode ||
                attempt >= MaximumReadAttempts ||
                !IsTransientReadFailure(response.StatusCode))
            {
                return new AppStoreConnectHttpResponse(
                    response.StatusCode,
                    response.ReasonPhrase,
                    content);
            }

            var delay = ResolveTransientReadDelay(response.Headers.RetryAfter, attempt);
            if (!delay.HasValue)
            {
                return new AppStoreConnectHttpResponse(
                    response.StatusCode,
                    response.ReasonPhrase,
                    content);
            }

            await TransientReadDelayAsync(delay.Value, cancellationToken).ConfigureAwait(false);
        }
    }

    private static TimeSpan? ResolveTransientReadDelay(RetryConditionHeaderValue? retryAfter, int attempt)
    {
        var fallback = TimeSpan.FromSeconds(attempt);
        if (retryAfter is null)
            return fallback;

        var requested = retryAfter.Delta;
        if (!requested.HasValue && retryAfter.Date.HasValue)
            requested = retryAfter.Date.Value - DateTimeOffset.UtcNow;
        if (!requested.HasValue || requested.Value <= TimeSpan.Zero)
            return fallback;

        var maximum = TimeSpan.FromMinutes(2);
        return requested.Value <= maximum ? requested.Value : null;
    }

    private static bool IsTransientReadFailure(HttpStatusCode statusCode)
        => (int)statusCode == 429
            || statusCode is HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private readonly struct AppStoreConnectHttpResponse
    {
        public AppStoreConnectHttpResponse(
            HttpStatusCode statusCode,
            string? reasonPhrase,
            string content)
        {
            StatusCode = statusCode;
            ReasonPhrase = reasonPhrase;
            Content = content;
        }

        public HttpStatusCode StatusCode { get; }

        public string? ReasonPhrase { get; }

        public string Content { get; }

        public bool IsSuccessStatusCode => (int)StatusCode is >= 200 and <= 299;
    }
}
