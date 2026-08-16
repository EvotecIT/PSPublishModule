using System.Net;
using PowerForge.Web;

namespace PowerForge.Tests;

public partial class WebAgentReadinessTests
{
    [Fact]
    public async Task RequestPacingHandler_SpacesRequestStarts()
    {
        var recorder = new RequestStartRecorder();
        using var client = new HttpClient(new WebAgentReadiness.RequestPacingHandler(
            recorder,
            TimeSpan.FromMilliseconds(100)));

        _ = await client.GetAsync("https://example.test/first");
        _ = await client.GetAsync("https://example.test/second");

        Assert.Equal(2, recorder.StartedAt.Count);
        Assert.True(
            recorder.StartedAt[1] - recorder.StartedAt[0] >= TimeSpan.FromMilliseconds(75),
            $"Requests started only {(recorder.StartedAt[1] - recorder.StartedAt[0]).TotalMilliseconds:F0} ms apart.");
    }

    private sealed class RequestStartRecorder : HttpMessageHandler
    {
        internal List<DateTimeOffset> StartedAt { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            StartedAt.Add(DateTimeOffset.UtcNow);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
