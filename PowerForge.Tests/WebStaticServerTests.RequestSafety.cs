using System.Collections.Concurrent;
using System.Net;
using PowerForge.Web;

namespace PowerForge.Tests;

public partial class WebStaticServerTests
{
    [Theory]
    [InlineData("/docs/missing.js", HttpStatusCode.NotFound)]
    [InlineData("/docs-other", HttpStatusCode.NotFound)]
    [InlineData("/playground-other", HttpStatusCode.NotFound)]
    [InlineData("/docs/valid-route", HttpStatusCode.OK)]
    public async Task Serve_SpaFallbackOnlyMatchesDocumentRoutes(string path, HttpStatusCode status)
    {
        await WithPreview(async (root, client) =>
        {
            Directory.CreateDirectory(Path.Combine(root, "docs"));
            File.WriteAllText(Path.Combine(root, "docs", "index.html"), "Docs SPA");
            File.WriteAllText(Path.Combine(root, "404.html"), "Not found");
            using var response = await client.GetAsync(path);
            Assert.Equal(status, response.StatusCode);
            Assert.Equal(status == HttpStatusCode.OK ? "Docs SPA" : "Not found", await response.Content.ReadAsStringAsync());
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Serve_DoesNotExposeLinkedFilesOrDirectories(bool directoryLink)
    {
        await WithPreview(async (root, client) =>
        {
            var outside = root + "-outside";
            Directory.CreateDirectory(outside);
            var link = Path.Combine(root, directoryLink ? "linked" : "linked.txt");
            try
            {
                File.WriteAllText(Path.Combine(outside, "private.txt"), "PRIVATE SOURCE");
                if (directoryLink) Directory.CreateSymbolicLink(link, outside);
                else File.CreateSymbolicLink(link, Path.Combine(outside, "private.txt"));
                using var response = await client.GetAsync(directoryLink ? "/linked/private.txt" : "/linked.txt");
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                Assert.DoesNotContain("PRIVATE SOURCE", await response.Content.ReadAsStringAsync());
                Assert.Equal("Home", await client.GetStringAsync("/"));
            }
            finally
            {
                if (directoryLink && Directory.Exists(link)) Directory.Delete(link);
                else if (File.Exists(link)) File.Delete(link);
                Directory.Delete(outside, recursive: true);
            }
        });
    }

    [Fact]
    public async Task Serve_HeadUsesMetadataWithoutOpeningPayload_AndGetStreamsCompleteFile()
    {
        await WithPreview(async (root, client) =>
        {
            var file = Path.Combine(root, "download.bin");
            var payload = new byte[4 * 1024 * 1024];
            new Random(42).NextBytes(payload);
            File.WriteAllBytes(file, payload);
            using (var locked = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, "/download.bin");
                using var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(payload.Length, response.Content.Headers.ContentLength);
                Assert.Empty(await response.Content.ReadAsByteArrayAsync());
            }
            Assert.Equal(payload, await client.GetByteArrayAsync("/download.bin"));
        });
    }

    private static async Task WithPreview(Func<string, HttpClient, Task> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-preview-safety-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "index.html"), "Home");
        using var cancellation = new CancellationTokenSource();
        var logs = new ConcurrentQueue<string>();
        var port = GetFreePortRange(10);
        var server = Task.Run(() => WebStaticServer.ServeWithPortFallback(root, "localhost", port,
            cancellation.Token, logs.Enqueue, maxPortAttempts: 10));
        try
        {
            var listening = await WaitForLogAsync(logs, m => m.StartsWith("Listening on http://localhost:"), TimeSpan.FromSeconds(15));
            Assert.NotNull(listening);
            var boundPort = ExtractPortFromListeningLog(listening!);
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{boundPort}"), Timeout = TimeSpan.FromSeconds(10) };
            await action(root, client);
        }
        finally
        {
            cancellation.Cancel();
            await server.WaitAsync(TimeSpan.FromSeconds(15));
            Directory.Delete(root, recursive: true);
        }
    }
}
