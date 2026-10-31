using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;

namespace PowerForge;

public sealed partial class ManagedModuleRepositoryClient
{
    private readonly ConditionalWeakTable<HttpResponseMessage, RequestAttemptDeadline> _responseDeadlines = new();

    internal static HttpClient CreateDefaultHttpClient(ManagedModuleRepositoryClientOptions options)
    {
        var client = new HttpClient(CreateDefaultHttpMessageHandler(options));
#if !NET472
        client.DefaultRequestVersion = HttpVersion.Version20;
        client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
#endif
        return client;
    }

    internal static HttpMessageHandler CreateDefaultHttpMessageHandler(ManagedModuleRepositoryClientOptions options)
    {
        var decompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
#if !NET472
        decompression |= DecompressionMethods.Brotli;
        return CreateSocketsHttpMessageHandler(options, decompression);
#else
        return CreateLegacyHttpMessageHandler(options, decompression);
#endif
    }

#if !NET472
    private static HttpMessageHandler CreateSocketsHttpMessageHandler(
        ManagedModuleRepositoryClientOptions options,
        DecompressionMethods decompression)
    {
        AppContext.SetSwitch("System.Net.SocketsHttpHandler.Http3Support", true);
        var handler = new SocketsHttpHandler
        {
            UseProxy = options.UseProxy,
            AllowAutoRedirect = false,
            AutomaticDecompression = decompression,
            MaxConnectionsPerServer = Math.Max(1, options.MaxConnectionsPerServer),
            EnableMultipleHttp2Connections = true,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            InitialHttp2StreamWindowSize = 16 * 1024 * 1024
        };
        ConfigureProxy(handler, options);
        return handler;
    }
#endif

    private static HttpMessageHandler CreateLegacyHttpMessageHandler(
        ManagedModuleRepositoryClientOptions options,
        DecompressionMethods decompression)
    {
        var handler = new HttpClientHandler
        {
            UseProxy = options.UseProxy,
            AllowAutoRedirect = false,
            AutomaticDecompression = decompression,
            MaxConnectionsPerServer = Math.Max(1, options.MaxConnectionsPerServer)
        };
        ConfigureProxy(handler, options);
        return handler;
    }

    private static WebProxy? CreateProxy(ManagedModuleRepositoryClientOptions options)
    {
        if (!options.UseProxy || options.ProxyAddress is null)
            return null;

        var proxy = new WebProxy(options.ProxyAddress, options.BypassProxyOnLocal);
        if (options.ProxyCredential is not null)
            proxy.Credentials = ToNetworkCredential(options.ProxyCredential);

        return proxy;
    }

#if !NET472
    private static void ConfigureProxy(SocketsHttpHandler handler, ManagedModuleRepositoryClientOptions options)
    {
        var proxy = CreateProxy(options);
        if (proxy is null)
            return;

        handler.Proxy = proxy;
    }
#endif

    private static void ConfigureProxy(HttpClientHandler handler, ManagedModuleRepositoryClientOptions options)
    {
        var proxy = CreateProxy(options);
        if (proxy is null)
            return;

        handler.Proxy = proxy;
    }

    private Task<HttpResponseMessage> SendWithPolicyAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
        => SendWithPolicyAsync(requestFactory, (response, _) => Task.FromResult(response), cancellationToken, disposeResponse: false);

    // A package attempt includes headers and its complete body; readers receive the
    // remaining request budget and each retry starts with a fresh response and destination.
    private async Task<T> SendWithPolicyAsync<T>(
        Func<HttpRequestMessage> requestFactory,
        Func<HttpResponseMessage, CancellationToken, Task<T>> readResponse,
        CancellationToken cancellationToken,
        bool disposeResponse = true)
    {
        var attempts = Math.Max(0, _options.MaxRetries) + 1;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            using var request = requestFactory();
            HttpResponseMessage? response = null;
            CancellationTokenSource? readTimeout = null;
            var transferResponse = false;
            try
            {
                response = await SendAttemptAsync(request, cancellationToken).ConfigureAwait(false);
                var transient = ModuleRepositoryRetryPolicy.IsTransientStatus(response.StatusCode,
                    includeNotFound: request.Method == HttpMethod.Get || request.Method == HttpMethod.Head);
                if (transient && attempt < attempts)
                {
                    _logger.Warn($"Repository {request.Method}: HTTP {(int)response.StatusCode} on attempt {attempt}/{attempts}.");
                }
                else
                {
                    if (transient)
                        _logger.Warn($"Repository {request.Method} failed with HTTP {(int)response.StatusCode} after {attempt}/{attempts} attempts. Verify the repository and requested package version; retry later if the feed is unavailable.");
                    if (disposeResponse)
                        readTimeout = CreateRepositoryResponseReadTimeout(response, cancellationToken);
                    var readToken = readTimeout?.Token ?? cancellationToken;
                    // Disposing the response also aborts native streams which do not
                    // implement cancellation of an outstanding ReadAsync themselves.
                    using var abort = disposeResponse ? readToken.Register(response.Dispose) : default;
                    var value = await readResponse(response, readToken).ConfigureAwait(false);
                    if (readToken.IsCancellationRequested)
                    {
                        (value as IDisposable)?.Dispose();
                        readToken.ThrowIfCancellationRequested();
                    }
                    transferResponse = !disposeResponse;
                    return value;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException ||
                                       (ex is ObjectDisposedException &&
                                        (readTimeout?.IsCancellationRequested == true || cancellationToken.IsCancellationRequested)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                // A failed upload response is ambiguous. Retrying reads is safe;
                // a publisher must reconcile its outcome before submitting again.
                if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head) throw;
                var timedOut = ex is OperationCanceledException || readTimeout?.IsCancellationRequested == true;
                if (attempt == attempts)
                {
                    if (timedOut) throw new TimeoutException($"Managed module repository transfer timed out after {attempt} attempts: {FormatRequest(request)}. Check repository availability and the request timeout.", ex);
                    throw new HttpRequestException($"Managed module repository transfer failed after {attempt} attempts: {FormatRequest(request)}. Check repository availability and network/proxy access. {ex.Message}", ex);
                }
                _logger.Warn($"Managed module repository {(timedOut ? "timeout" : "transport failure")} on attempt {attempt}/{attempts}.");
            }
            finally
            {
                readTimeout?.Dispose();
                if (!transferResponse) response?.Dispose();
            }
            await DelayBeforeRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException("Managed module repository request timed out.");
    }

    private async Task<HttpResponseMessage> SendAttemptAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var current = request;
        for (var redirect = 0; redirect < 5; redirect++)
        {
            RecordRequestAttempt();
            var response = _options.RequestTimeout is null
                ? await _httpClient.SendAsync(current, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false)
                : await SendWithTimeoutAsync(current, cancellationToken).ConfigureAwait(false);
            if (!IsRedirect(response.StatusCode) || response.Headers.Location is null || !CanRedirect(current.Method))
                return response;

            var redirectUri = response.Headers.Location.IsAbsoluteUri
                ? response.Headers.Location
                : new Uri(current.RequestUri!, response.Headers.Location);
            response.Dispose();
            RecordRedirectFollowed();
            current = CreateRedirectRequest(current, redirectUri);
        }

        return new HttpResponseMessage((HttpStatusCode)310)
        {
            RequestMessage = request,
            ReasonPhrase = "Too many redirects"
        };
    }

    private async Task<HttpResponseMessage> SendWithTimeoutAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (_options.RequestTimeout is null)
            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        var requestTimeout = _options.RequestTimeout.Value;
        if (requestTimeout == Timeout.InfiniteTimeSpan)
            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        var deadlineTimestamp = CalculateRequestDeadlineTimestamp(requestTimeout);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(requestTimeout);
        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        _responseDeadlines.Remove(response);
        _responseDeadlines.Add(response, new RequestAttemptDeadline(deadlineTimestamp));
        return response;
    }

    private static long CalculateRequestDeadlineTimestamp(TimeSpan requestTimeout)
    {
        var duration = requestTimeout.TotalSeconds * Stopwatch.Frequency;
        var current = Stopwatch.GetTimestamp();
        if (duration >= long.MaxValue - current)
            return long.MaxValue;

        return current + Math.Max(0L, (long)Math.Ceiling(duration));
    }

    private bool TryGetRemainingRequestTime(HttpResponseMessage response, out TimeSpan remaining)
    {
        if (!_responseDeadlines.TryGetValue(response, out var deadline))
        {
            remaining = default;
            return false;
        }

        var remainingTicks = deadline.Timestamp - Stopwatch.GetTimestamp();
        remaining = remainingTicks <= 0
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds(remainingTicks / (double)Stopwatch.Frequency);
        return true;
    }

    private sealed class RequestAttemptDeadline
    {
        public RequestAttemptDeadline(long timestamp)
        {
            Timestamp = timestamp;
        }

        public long Timestamp { get; }
    }

    private static HttpRequestMessage CreateRedirectRequest(HttpRequestMessage source, Uri redirectUri)
    {
        var request = new HttpRequestMessage(source.Method, redirectUri)
        {
            Version = source.Version
        };
#if !NET472
        request.VersionPolicy = source.VersionPolicy;
#endif
        foreach (var header in source.Headers.Accept)
            request.Headers.Accept.Add(header);
        foreach (var header in source.Headers.UserAgent)
            request.Headers.UserAgent.Add(header);
        if (source.Headers.Authorization is not null &&
            source.RequestUri is not null &&
            IsSameOrigin(source.RequestUri, redirectUri))
        {
            request.Headers.Authorization = source.Headers.Authorization;
        }

        if (source.RequestUri is not null &&
            IsSameOrigin(source.RequestUri, redirectUri) &&
            source.Headers.TryGetValues("X-NuGet-ApiKey", out var apiKeyValues))
        {
            request.Headers.TryAddWithoutValidation("X-NuGet-ApiKey", apiKeyValues);
        }

        return request;
    }

    private static bool IsSameOrigin(Uri sourceUri, Uri redirectUri)
        => string.Equals(sourceUri.Scheme, redirectUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(sourceUri.Host, redirectUri.Host, StringComparison.OrdinalIgnoreCase) &&
           sourceUri.Port == redirectUri.Port;

    private async Task DelayBeforeRetryAsync(int attempt, CancellationToken cancellationToken)
    {
        var delay = ModuleRepositoryRetryPolicy.Backoff(_options.RetryDelay, attempt);
        _logger.Warn($"Managed module repository transient failure on attempt {attempt}/{Math.Max(0, _options.MaxRetries) + 1}; retrying attempt {attempt + 1} after {delay}.");
        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.Moved ||
           statusCode == HttpStatusCode.Redirect ||
           statusCode == HttpStatusCode.TemporaryRedirect ||
           (int)statusCode == 308;

    private static bool CanRedirect(HttpMethod method)
        => method == HttpMethod.Get || method == HttpMethod.Head;

    private static string FormatRequest(HttpRequestMessage request)
        => $"{request.Method} {request.RequestUri}";

    private static NetworkCredential? ToNetworkCredential(RepositoryCredential credential)
    {
        if (string.IsNullOrWhiteSpace(credential.UserName) && string.IsNullOrWhiteSpace(credential.Secret))
            return null;

        return new NetworkCredential(credential.UserName ?? string.Empty, credential.Secret ?? string.Empty);
    }
}
