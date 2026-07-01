using System.Net;
using System.Net.Http;

namespace PowerForge;

public sealed partial class ManagedModuleRepositoryClient
{
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

    private async Task<HttpResponseMessage> SendWithPolicyAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        var attempts = Math.Max(0, _options.MaxRetries) + 1;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            using var request = requestFactory();
            try
            {
                var response = await SendAttemptAsync(request, cancellationToken).ConfigureAwait(false);
                if (!IsTransientStatus(response.StatusCode) || attempt == attempts)
                    return response;

                response.Dispose();
                await DelayBeforeRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException) when (attempt < attempts)
            {
                await DelayBeforeRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw CreateTransportException(request, ex);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && attempt < attempts)
            {
                _logger.Verbose($"Managed module repository request timed out on attempt {attempt}: {ex.Message}");
                await DelayBeforeRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw CreateTimeoutException(request, ex);
            }
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

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.RequestTimeout.Value);
        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
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
        var delay = _options.RetryDelay <= TimeSpan.Zero ? TimeSpan.Zero : _options.RetryDelay;
        _logger.Verbose($"Managed module repository transient failure; retrying attempt {attempt + 1} after {delay}.");
        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsTransientStatus(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.RequestTimeout ||
           statusCode == (HttpStatusCode)429 ||
           (int)statusCode >= 500;

    private static bool IsRedirect(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.Moved ||
           statusCode == HttpStatusCode.Redirect ||
           statusCode == HttpStatusCode.TemporaryRedirect ||
           (int)statusCode == 308;

    private static bool CanRedirect(HttpMethod method)
        => method == HttpMethod.Get || method == HttpMethod.Head;

    private static HttpRequestException CreateTransportException(HttpRequestMessage request, HttpRequestException exception)
        => new(
            $"Managed module repository request failed: {FormatRequest(request)}. {exception.Message}",
            exception);

    private static TimeoutException CreateTimeoutException(HttpRequestMessage request, Exception exception)
        => new($"Managed module repository request timed out: {FormatRequest(request)}.", exception);

    private static string FormatRequest(HttpRequestMessage request)
        => $"{request.Method} {request.RequestUri}";

    private static NetworkCredential? ToNetworkCredential(RepositoryCredential credential)
    {
        if (string.IsNullOrWhiteSpace(credential.UserName) && string.IsNullOrWhiteSpace(credential.Secret))
            return null;

        return new NetworkCredential(credential.UserName ?? string.Empty, credential.Secret ?? string.Empty);
    }
}
