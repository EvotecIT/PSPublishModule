using System.Net;
using System.Text.Json;
using PowerForge.IndexNow;

namespace PowerForge.Tests;

public sealed class IndexNowClientTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResponseStreamFailure_RetriesAndPreservesFailurePolicy(bool recover)
    {
        int calls = 0;
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = ++calls == 2 && recover ? new StringContent("accepted") : new StreamContent(new BrokenResponseStream())
        })));
        var result = await IndexNowSubmitter.SubmitAsync(new()
        {
            Key = "test-key", Urls = ["https://example.com/one"], RetryCount = 1,
            RetryDelayMs = 0, FailOnRequestError = false
        }, http);
        Assert.Equal(2, calls);
        Assert.True(result.Success);
        Assert.Equal(recover ? 0 : 1, result.FailedRequestCount);
        Assert.Equal(recover, Assert.Single(result.Requests).Success);
    }

    private sealed class BrokenResponseStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("Connection closed during response body."));
    }

    [Fact]
    public async Task AsyncSubmission_RetriesFailedBatch_AndPreservesProtocolPayload()
    {
        int calls = 0;
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal("example.com", payload.RootElement.GetProperty("host").GetString());
            Assert.Equal("test-key", payload.RootElement.GetProperty("key").GetString());
            Assert.Equal("https://example.com/test-key.txt", payload.RootElement.GetProperty("keyLocation").GetString());
            Assert.Equal(2, payload.RootElement.GetProperty("urlList").GetArrayLength());
            return new HttpResponseMessage(++calls == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Accepted)
                { Content = new StringContent(new string('x', 100_000)) };
        }));
        var result = await IndexNowSubmitter.SubmitAsync(new()
        {
            Key = "test-key", Urls = ["https://example.com/one", "https://example.com/two"],
            RetryDelayMs = 0, RetryCount = 1
        }, http);
        Assert.True(result.Success);
        Assert.Equal(2, Assert.Single(result.Requests).AttemptCount);
        Assert.Equal(303, result.Requests[0].ResponsePreview!.Length);
        // The client remains usable after the submission; ownership belongs to its caller.
        Assert.Equal(HttpStatusCode.Accepted, (await http.PostAsync("https://example.com",new StringContent("{\"host\":\"example.com\",\"key\":\"test-key\",\"keyLocation\":\"https://example.com/test-key.txt\",\"urlList\":[1,2]}"))).StatusCode);
    }

    [Fact]
    public async Task CallerCancellation_StopsAnInFlightRequestWithoutRetry()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        using var http = new HttpClient(new Handler(async (_, token) =>
        {
            calls++;
            entered.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        using var cancellation = new CancellationTokenSource();
        var pending = IndexNowSubmitter.SubmitAsync(new() { Key="test-key",Urls=["https://example.com/one"] },http,cancellationToken:cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(1,calls);
    }

    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) => send(request,token);
    }
}
