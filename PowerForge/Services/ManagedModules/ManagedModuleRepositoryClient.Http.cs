using System.Net;
using System.Net.Http;

namespace PowerForge;

public sealed partial class ManagedModuleRepositoryClient
{
    internal static HttpMessageHandler CreateDefaultHttpMessageHandler(ManagedModuleRepositoryClientOptions options)
    {
        var handler = new HttpClientHandler
        {
            UseProxy = options.UseProxy,
            AllowAutoRedirect = false
        };
        if (!options.UseProxy || options.ProxyAddress is null)
            return handler;

        var proxy = new WebProxy(options.ProxyAddress, options.BypassProxyOnLocal);
        if (options.ProxyCredential is not null)
            proxy.Credentials = ToNetworkCredential(options.ProxyCredential);

        handler.Proxy = proxy;
        return handler;
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
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && attempt < attempts)
            {
                _logger.Verbose($"Managed module repository request timed out on attempt {attempt}: {ex.Message}");
                await DelayBeforeRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("Managed module repository request timed out.", ex);
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
        var request = new HttpRequestMessage(source.Method, redirectUri);
        foreach (var header in source.Headers.Accept)
            request.Headers.Accept.Add(header);
        foreach (var header in source.Headers.UserAgent)
            request.Headers.UserAgent.Add(header);
        if (source.Headers.Authorization is not null &&
            source.RequestUri is not null &&
            string.Equals(source.RequestUri.Host, redirectUri.Host, StringComparison.OrdinalIgnoreCase))
            request.Headers.Authorization = source.Headers.Authorization;

        return request;
    }

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

    private static NetworkCredential? ToNetworkCredential(RepositoryCredential credential)
    {
        if (string.IsNullOrWhiteSpace(credential.UserName) && string.IsNullOrWhiteSpace(credential.Secret))
            return null;

        return new NetworkCredential(credential.UserName ?? string.Empty, credential.Secret ?? string.Empty);
    }
}
