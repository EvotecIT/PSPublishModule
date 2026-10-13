using System.Net;
using System.Net.Http;
using PowerForge.IndexNow;

namespace PowerForge.Web.Protocols.Tests;

public sealed class IndexNowTransportTests
{
    [Fact]
    public async Task MaximumBatchCanExceedTwoMillionJsonCharacters()
    {
        int calls = 0;
        var urls = Enumerable.Range(0, 10000).Select(i => "https://example.org/" + new string('a', 220) + "/" + i).ToArray();
        using var client = new HttpClient(new Handler(async request =>
        {
            calls++;
            var json = await request.Content!.ReadAsStringAsync();
            Assert.True(json.Length > 2097152);
#if NETFRAMEWORK
            var payload = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue }
                .Deserialize<Dictionary<string, object>>(json);
            Assert.Equal(urls, ((System.Collections.IEnumerable)payload["urlList"]).Cast<string>());
#else
            using var payload = System.Text.Json.JsonDocument.Parse(json);
            Assert.Equal(urls, payload.RootElement.GetProperty("urlList").EnumerateArray().Select(url => url.GetString()));
#endif
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }));
        var result = await IndexNowSubmitter.SubmitAsync(new IndexNowSubmissionOptions
        {
            Key = "test-key", Urls = urls, BatchSize = 10000, RetryCount = 0
        }, client);
        Assert.True(result.Success);
        Assert.Equal(10000, result.UrlCount);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task PlatformSerializerAndTransportPreserveTheWireContract()
    {
        int calls = 0;
        using var client = new HttpClient(new Handler(async request =>
        {
            var json = await request.Content!.ReadAsStringAsync();
#if NETFRAMEWORK
            var payload = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            Assert.Equal("example.org", payload["host"]);
            Assert.Equal("test-key", payload["key"]);
#else
            using var payload = System.Text.Json.JsonDocument.Parse(json);
            Assert.Equal("example.org", payload.RootElement.GetProperty("host").GetString());
            Assert.Equal("test-key", payload.RootElement.GetProperty("key").GetString());
#endif
            Assert.Contains("urlList", json);
            return new HttpResponseMessage(++calls == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Accepted)
            { Content = new StringContent(new string('x', 1000)) };
        }));
        var result = await IndexNowSubmitter.SubmitAsync(new IndexNowSubmissionOptions
        {
            Key = "test-key", Urls = new[] { "https://example.org/item" }, RetryCount = 1, RetryDelayMs = 0
        }, client);
        Assert.True(result.Success);
        Assert.Equal(2, calls);
        Assert.Equal(303, Assert.Single(result.Requests).ResponsePreview!.Length);
    }

    private sealed class Handler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _send;
        internal Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) => _send = send;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => _send(request);
    }
}
