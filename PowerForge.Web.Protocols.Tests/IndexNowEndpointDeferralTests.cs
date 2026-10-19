using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using PowerForge.Web.Protocols.IndexNow;

namespace PowerForge.Web.Protocols.Tests;

public sealed class IndexNowEndpointDeferralTests
{
    [Theory]
    [InlineData(0, 30)]
    [InlineData(0, 3600)]
    [InlineData(2, 3600)]
    public async Task ThrottledEndpointDefersRemainingBatchesAndHostsWhileOtherEndpointsContinue(int retryCount, int retryAfterSeconds)
    {
        int throttledCalls = 0;
        int otherCalls = 0;
        using var client = new HttpClient(new Handler((request, _) =>
        {
            if (request.RequestUri!.Host == "throttled.example")
            {
                throttledCalls++;
                var response = new HttpResponseMessage((HttpStatusCode)429);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(retryAfterSeconds));
                return Task.FromResult(response);
            }
            otherCalls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted));
        }));

        var before = DateTimeOffset.UtcNow;
        var result = await IndexNowSubmitter.SubmitAsync(Options(retryCount), client);

        Assert.False(result.Success);
        Assert.Equal(1, throttledCalls);
        Assert.Equal(3, otherCalls);
        Assert.Equal(6, result.RequestCount);
        Assert.Equal(3, result.FailedRequestCount);
        var throttled = result.Requests.Where(request => request.Endpoint.Contains("throttled.example")).ToArray();
        Assert.NotNull(throttled[0].RetryNotBefore);
        Assert.InRange(throttled[0].RetryNotBefore!.Value,
            before.AddSeconds(retryAfterSeconds), DateTimeOffset.UtcNow.AddSeconds(retryAfterSeconds));
        Assert.Equal(1, throttled[0].AttemptCount);
        Assert.All(throttled.Skip(1), request =>
        {
            Assert.False(request.Success);
            Assert.Equal(0, request.AttemptCount);
            Assert.Null(request.StatusCode);
            Assert.Equal(throttled[0].RetryNotBefore, request.RetryNotBefore);
        });
        Assert.All(result.Requests.Where(request => request.Success), request => Assert.Null(request.RetryNotBefore));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AbsoluteServerDeadlineSurvivesExhaustionAndFailurePolicy(bool failOnRequestError)
    {
        var deadline = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(1), TimeSpan.Zero);
        using var client = new HttpClient(new Handler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(deadline);
            return Task.FromResult(response);
        }));
        var options = Options(retryCount: 0);
        options.Endpoints = new[] { "https://throttled.example/indexnow" };
        options.FailOnRequestError = failOnRequestError;

        var result = await IndexNowSubmitter.SubmitAsync(options, client);

        Assert.Equal(!failOnRequestError, result.Success);
        Assert.Equal(3, result.FailedRequestCount);
        Assert.All(result.Requests, request => Assert.Equal(deadline, request.RetryNotBefore));
    }

    [Theory]
    [InlineData(400, false)]
    [InlineData(503, true)]
    public async Task PermanentOrExpiredResponseDoesNotDeferLaterBatches(int statusCode, bool expired)
    {
        int calls = 0;
        using var client = new HttpClient(new Handler((_, _) =>
        {
            calls++;
            var response = new HttpResponseMessage((HttpStatusCode)statusCode);
            response.Headers.RetryAfter = expired
                ? new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddHours(-1))
                : new RetryConditionHeaderValue(TimeSpan.FromHours(1));
            return Task.FromResult(response);
        }));
        var options = Options(retryCount: 0);
        options.Endpoints = new[] { "https://throttled.example/indexnow" };

        var result = await IndexNowSubmitter.SubmitAsync(options, client);

        Assert.Equal(3, calls);
        Assert.Equal(3, result.FailedRequestCount);
        Assert.All(result.Requests, request =>
        {
            Assert.Equal(1, request.AttemptCount);
            Assert.Null(request.RetryNotBefore);
        });
    }

    [Fact]
    public async Task CallerCancellationStopsSubmissionAfterAThrottledResponse()
    {
        int calls = 0;
        using var cancellation = new CancellationTokenSource();
        using var client = new HttpClient(new Handler((_, _) =>
        {
            calls++;
            var response = new HttpResponseMessage((HttpStatusCode)429);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(1));
            cancellation.Cancel();
            return Task.FromResult(response);
        }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            IndexNowSubmitter.SubmitAsync(Options(), client, cancellationToken: cancellation.Token));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task DryRunDoesNotCreateDeferralsOrSendRequests()
    {
        using var client = new HttpClient(new Handler((_, _) => throw new InvalidOperationException("Dry run sent a request.")));
        var options = Options();
        options.DryRun = true;

        var result = await IndexNowSubmitter.SubmitAsync(options, client);

        Assert.True(result.Success);
        Assert.Equal(6, result.RequestCount);
        Assert.Equal(0, result.FailedRequestCount);
        Assert.All(result.Requests, request =>
        {
            Assert.True(request.Success);
            Assert.Equal(0, request.AttemptCount);
            Assert.Null(request.RetryNotBefore);
        });
    }

    private static IndexNowSubmissionOptions Options(int retryCount = 2) => new()
    {
        Key = "test-key",
        Urls = new[] { "https://example.org/one", "https://example.org/two", "https://example.net/three" },
        Endpoints = new[] { "https://throttled.example/indexnow", "https://other.example/indexnow" },
        BatchSize = 1,
        RetryCount = retryCount,
        RetryDelayMs = 0
    };

    private sealed class Handler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;
        internal Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) => _send = send;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => _send(request, cancellationToken);
    }
}
