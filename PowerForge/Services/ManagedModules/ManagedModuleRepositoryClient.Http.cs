using System.Net;
using System.Net.Http;

namespace PowerForge;

public sealed partial class ManagedModuleRepositoryClient
{
    internal static HttpMessageHandler CreateDefaultHttpMessageHandler(ManagedModuleRepositoryClientOptions options)
    {
        var handler = new HttpClientHandler
        {
            UseProxy = options.UseProxy
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
        if (_options.RequestTimeout is null)
            return await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.RequestTimeout.Value);
        return await _httpClient.SendAsync(request, timeout.Token).ConfigureAwait(false);
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

    private static NetworkCredential? ToNetworkCredential(RepositoryCredential credential)
    {
        if (string.IsNullOrWhiteSpace(credential.UserName) && string.IsNullOrWhiteSpace(credential.Secret))
            return null;

        return new NetworkCredential(credential.UserName ?? string.Empty, credential.Secret ?? string.Empty);
    }
}
