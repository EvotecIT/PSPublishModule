using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using PowerForge.Web.Protocols.IndexNow;

namespace PowerForge.Web.Protocols.Tests;

public sealed class IndexNowRetryTests
{
    [Theory]
    [InlineData(400)]
    [InlineData(403)]
    [InlineData(422)]
    [InlineData(302)]
    public async Task PermanentResponseStopsAfterOneAttempt(int statusCode)
    {
        int calls = 0;
        using var client = new HttpClient(new Handler(_ =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)statusCode)
            {
                Content = new StringContent("request must be corrected")
            });
        }));

        var result = await IndexNowSubmitter.SubmitAsync(Options(), client);

        Assert.False(result.Success);
        Assert.Equal(1, calls);
        var request = Assert.Single(result.Requests);
        Assert.Equal(1, request.AttemptCount);
        Assert.Equal(statusCode, request.StatusCode);
        Assert.Equal("request must be corrected", request.Error);
    }

    [Fact]
    public async Task PermanentResponseWithUnreadableBodyStillStopsAfterOneAttempt()
    {
        int calls = 0;
        using var client = new HttpClient(new Handler(_ =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new BrokenContent() });
        }));

        var result = await IndexNowSubmitter.SubmitAsync(Options(), client);

        Assert.False(result.Success);
        Assert.Equal(1, calls);
        Assert.Equal(403, Assert.Single(result.Requests).StatusCode);
    }

    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task TransientResponseCanRecoverOnTheNextAttempt(int statusCode)
    {
        int calls = 0;
        using var client = new HttpClient(new Handler(_ => Task.FromResult(
            new HttpResponseMessage(++calls == 1 ? (HttpStatusCode)statusCode : HttpStatusCode.Accepted))));

        var result = await IndexNowSubmitter.SubmitAsync(Options(), client);

        Assert.True(result.Success);
        Assert.Equal(2, calls);
        Assert.Equal(2, Assert.Single(result.Requests).AttemptCount);
        Assert.Equal(202, result.Requests[0].StatusCode);
    }

    [Fact]
    public async Task TransientResponsesStopAtTheConfiguredAttemptLimit()
    {
        int calls = 0;
        using var client = new HttpClient(new Handler(_ =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }));

        var result = await IndexNowSubmitter.SubmitAsync(Options(), client);

        Assert.False(result.Success);
        Assert.Equal(3, calls);
        Assert.Equal(3, Assert.Single(result.Requests).AttemptCount);
        Assert.Equal(503, result.Requests[0].StatusCode);
    }

    [Theory]
    [InlineData("http")]
    [InlineData("timeout")]
    [InlineData("io")]
    public async Task TransportFailureAndTimeoutCanRecover(string failure)
    {
        int calls = 0;
        using var client = new HttpClient(new Handler(_ =>
        {
            if (++calls == 1)
                throw failure switch
                {
                    "timeout" => new TaskCanceledException("request timed out"),
                    "io" => new IOException("connection closed before response headers"),
                    _ => new HttpRequestException("connection closed")
                };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }));

        var result = await IndexNowSubmitter.SubmitAsync(Options(), client);

        Assert.True(result.Success);
        Assert.Equal(2, calls);
        Assert.Equal(2, Assert.Single(result.Requests).AttemptCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetryAfterBeyondTheWaitBoundStopsInsteadOfRetryingEarly(bool useDate)
    {
        int calls = 0;
        using var client = new HttpClient(new Handler(_ =>
        {
            calls++;
            var response = new HttpResponseMessage((HttpStatusCode)429);
            response.Headers.RetryAfter = useDate
                ? new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddHours(1))
                : new RetryConditionHeaderValue(TimeSpan.FromHours(1));
            return Task.FromResult(response);
        }));

        var result = await IndexNowSubmitter.SubmitAsync(Options(), client);

        Assert.False(result.Success);
        Assert.Equal(1, calls);
        Assert.Equal(1, Assert.Single(result.Requests).AttemptCount);
        Assert.Equal(429, result.Requests[0].StatusCode);
    }

    [Fact]
    public async Task RetryAfterSecondsDelaysTheRetryEvenWhenConfiguredDelayIsZero()
    {
        var sinceResponse = new Stopwatch();
        int calls = 0;
        using var client = new HttpClient(new Handler(_ =>
        {
            if (++calls > 1)
            {
                Assert.True(sinceResponse.Elapsed >= TimeSpan.FromMilliseconds(950), "Retried before Retry-After elapsed.");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted));
            }
            var response = new HttpResponseMessage((HttpStatusCode)429);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
            sinceResponse.Start();
            return Task.FromResult(response);
        }));

        var result = await IndexNowSubmitter.SubmitAsync(Options(), client);

        Assert.True(result.Success);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerCanCancelBackoffWithoutAnotherRequest(bool useRetryAfterDate)
    {
        int calls = 0;
        using var client = new HttpClient(new Handler(_ =>
        {
            calls++;
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            if (useRetryAfterDate)
                response.Headers.RetryAfter = new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(30));
            return Task.FromResult(response);
        }));
        var options = Options();
        options.RetryDelayMs = useRetryAfterDate ? 0 : 30_000;
        using var cancellation = new CancellationTokenSource();

        var pending = IndexNowSubmitter.SubmitAsync(options, client, cancellationToken: cancellation.Token);
        Assert.False(pending.IsCompleted);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingInvalidOrPastRetryAfterUsesIncreasingFallbackDelays(bool usePastDate)
    {
        var sinceResponse = new Stopwatch();
        int calls = 0;
        using var client = new HttpClient(new Handler(_ =>
        {
            calls++;
            if (calls > 1)
                Assert.True(sinceResponse.ElapsedMilliseconds >= (calls == 2 ? 90 : 190), "Fallback delay did not increase.");
            sinceResponse.Restart();
            var response = new HttpResponseMessage(calls == 3 ? HttpStatusCode.Accepted : HttpStatusCode.ServiceUnavailable);
            if (calls == 2)
            {
                if (usePastDate)
                    response.Headers.RetryAfter = new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddHours(-1));
                else
                    response.Headers.TryAddWithoutValidation("Retry-After", "invalid");
            }
            return Task.FromResult(response);
        }));
        var options = Options();
        options.RetryDelayMs = 100;

        var result = await IndexNowSubmitter.SubmitAsync(options, client);

        Assert.True(result.Success);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task LargeConfiguredDelayRemainsCancellableInsteadOfOverflowing()
    {
        using var client = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));
        var options = Options();
        options.RetryDelayMs = int.MaxValue;
        using var cancellation = new CancellationTokenSource();

        var pending = IndexNowSubmitter.SubmitAsync(options, client, cancellationToken: cancellation.Token);
        Assert.False(pending.IsCompleted);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    private sealed class BrokenContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            Task.FromException(new IOException("response body connection closed"));
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }

    private static IndexNowSubmissionOptions Options() => new()
    {
        Key = "test-key",
        Urls = new[] { "https://example.org/item" },
        RetryCount = 2,
        RetryDelayMs = 0
    };

    private sealed class Handler : HttpMessageHandler
    {
        private readonly Func<CancellationToken, Task<HttpResponseMessage>> _send;
        internal Handler(Func<CancellationToken, Task<HttpResponseMessage>> send) => _send = send;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => _send(cancellationToken);
    }
}
