using System.Net;
using System.Net.Http;
using PowerForge.Web.Protocols.IndexNow;

namespace PowerForge.Web.Protocols.Tests;

public sealed class IndexNowAcknowledgementTests
{
    [Theory]
    [InlineData(200, 1)]
    [InlineData(202, 1)]
    [InlineData(204, 1)]
    [InlineData(299, 1)]
    [InlineData(202, int.MaxValue)]
    public async Task AcceptedStatusDoesNotResendWhenOptionalPreviewFails(int statusCode, int retryCount)
    {
        int calls = 0;
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(
            new HttpResponseMessage((HttpStatusCode)statusCode)
            {
                Content = ++calls == 1 ? new BrokenContent(new IOException("preview connection closed")) : new StringContent("accepted")
            })));
        var options = Options();
        options.RetryCount = retryCount;

        var result = await IndexNowSubmitter.SubmitAsync(options, client);

        Assert.Equal(1, calls);
        Assert.True(result.Success);
        Assert.Equal(0, result.FailedRequestCount);
        Assert.Empty(result.Errors);
        Assert.Contains("preview", Assert.Single(result.Warnings));
        var request = Assert.Single(result.Requests);
        Assert.True(request.Success);
        Assert.Equal(1, request.AttemptCount);
        Assert.Equal(statusCode, request.StatusCode);
        Assert.Null(request.Error);
        Assert.Null(request.RetryNotBefore);
    }

    [Theory]
    [InlineData("http")]
    [InlineData("timeout")]
    [InlineData("disposed")]
    public async Task AcceptedPreviewTransportErrorsAreWarningsWithoutAnotherAttempt(string failure)
    {
        int calls = 0;
        Exception error = failure switch
        {
            "http" => new HttpRequestException("preview connection failed"),
            "timeout" => new OperationCanceledException("preview deadline elapsed"),
            _ => new ObjectDisposedException("response stream")
        };
        using var client = new HttpClient(new Handler((_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new BrokenContent(error) });
        }));

        var result = await IndexNowSubmitter.SubmitAsync(Options(), client);

        Assert.Equal(1, calls);
        Assert.True(result.Success);
        Assert.Equal(0, result.FailedRequestCount);
        Assert.Empty(result.Errors);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public async Task AcceptedPreviewDeadlineDoesNotTurnAcknowledgmentIntoARetry()
    {
        int calls = 0;
        using var client = new HttpClient(new Handler((_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = new StreamContent(new StallingStream())
            });
        }));
        var options = Options();
        options.TimeoutSeconds = 1;

        var result = await IndexNowSubmitter.SubmitAsync(options, client);

        Assert.Equal(1, calls);
        Assert.True(result.Success);
        Assert.Equal(202, Assert.Single(result.Requests).StatusCode);
        Assert.Single(result.Warnings);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task CallerCancellationDuringAcceptedPreviewStopsLaterBatches()
    {
        int calls = 0;
        using var cancellation = new CancellationTokenSource();
        using var client = new HttpClient(new Handler((_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = new CancelingContent(cancellation)
            });
        }));
        var options = Options();
        options.Urls = new[] { "https://example.org/one", "https://example.org/two" };
        options.BatchSize = 1;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            IndexNowSubmitter.SubmitAsync(options, client, cancellationToken: cancellation.Token));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task AcceptedPreviewFailureDoesNotDeferLaterBatches()
    {
        int calls = 0;
        using var client = new HttpClient(new Handler((_, _) =>
        {
            calls++;
            var response = new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = new BrokenContent(new IOException("preview unavailable"))
            };
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromHours(1));
            return Task.FromResult(response);
        }));
        var options = Options();
        options.Urls = new[] { "https://example.org/one", "https://example.org/two" };
        options.BatchSize = 1;

        var result = await IndexNowSubmitter.SubmitAsync(options, client);

        Assert.Equal(2, calls);
        Assert.True(result.Success);
        Assert.Equal(0, result.FailedRequestCount);
        Assert.Equal(2, result.Warnings.Length);
        Assert.All(result.Requests, request =>
        {
            Assert.True(request.Success);
            Assert.Equal(1, request.AttemptCount);
            Assert.Null(request.RetryNotBefore);
        });
    }

    private static IndexNowSubmissionOptions Options() => new()
    {
        Key = "test-key",
        Urls = new[] { "https://example.org/item" },
        RetryCount = 1,
        RetryDelayMs = 0
    };

    private sealed class BrokenContent : HttpContent
    {
        private readonly Exception _error;
        internal BrokenContent(Exception error) => _error = error;
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => Task.FromException(_error);
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }

    private sealed class CancelingContent : HttpContent
    {
        private readonly CancellationTokenSource _cancellation;
        internal CancelingContent(CancellationTokenSource cancellation) => _cancellation = cancellation;
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            _cancellation.Cancel();
            return Task.FromException(new IOException("preview interrupted"));
        }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }

    private sealed class StallingStream : Stream
    {
        private readonly CancellationTokenSource _closed = new();
        private bool _disposed;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => WaitForCancellation(cancellationToken);
#if !NETFRAMEWORK
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => new(WaitForCancellation(cancellationToken));
#endif
        private async Task<int> WaitForCancellation(CancellationToken token)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _closed.Token);
            await Task.Delay(Timeout.Infinite, linked.Token);
            return 0;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                _closed.Cancel();
                _closed.Dispose();
            }
            base.Dispose(disposing);
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class Handler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;
        internal Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) => _send = send;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => _send(request, cancellationToken);
    }
}
