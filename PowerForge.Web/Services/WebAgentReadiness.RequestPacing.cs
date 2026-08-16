namespace PowerForge.Web;

public static partial class WebAgentReadiness
{
    internal sealed class RequestPacingHandler : DelegatingHandler
    {
        private readonly TimeSpan _minimumInterval;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private DateTimeOffset _lastRequestStarted;

        internal RequestPacingHandler(HttpMessageHandler innerHandler, TimeSpan minimumInterval)
            : base(innerHandler)
        {
            _minimumInterval = minimumInterval;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var remaining = _minimumInterval - (DateTimeOffset.UtcNow - _lastRequestStarted);
                if (remaining > TimeSpan.Zero)
                    await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);

                _lastRequestStarted = DateTimeOffset.UtcNow;
            }
            finally
            {
                _gate.Release();
            }

            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _gate.Dispose();
            base.Dispose(disposing);
        }
    }
}
