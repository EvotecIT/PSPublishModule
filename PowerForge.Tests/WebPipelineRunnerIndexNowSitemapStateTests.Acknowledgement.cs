using System.Net;
using System.Text.Json;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed partial class WebPipelineRunnerIndexNowSitemapStateTests
{
    [Fact]
    public async Task StatefulSitemap_AcceptedStatusWithBrokenPreviewAdvancesCheckpointWithoutResending()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-indexnow-ack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var listener = new HttpListener();
        using var cancellation = new CancellationTokenSource();
        Task? server = null;
        int calls = 0;
        try
        {
            var port = FreePort();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/indexnow/");
            listener.Start();
            server = Task.Run(async () =>
            {
                while (!cancellation.IsCancellationRequested)
                {
                    HttpListenerContext context;
                    try { context = await listener.GetContextAsync(); }
                    catch when (cancellation.IsCancellationRequested || !listener.IsListening) { break; }
                    Interlocked.Increment(ref calls);
                    context.Response.StatusCode = 202;
                    context.Response.ContentLength64 = 100;
                    context.Response.KeepAlive = false;
                    await context.Response.OutputStream.WriteAsync(new byte[] { (byte)'x' });
                    context.Response.Abort();
                }
            });
            File.WriteAllText(Path.Combine(root, "sitemap.xml"), Sitemap(("/one", "2026-09-01")));
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, $$"""
                { "steps": [{
                    "task": "indexnow", "baseUrl": "https://example.com/",
                    "sitemap": "./sitemap.xml", "sitemapStatePath": "./state.json",
                    "endpoint": "http://127.0.0.1:{{port}}/indexnow/", "key": "examplekey",
                    "retryCount": 1, "retryDelayMs": 0, "timeoutSeconds": 1,
                    "reportPath": "./report.json", "summaryPath": "./summary.md"
                }] }
                """);

            var result = WebPipelineRunner.RunPipeline(pipeline, logger: null);

            Assert.True(result.Success, result.Steps[0].Message);
            Assert.Equal(1, calls);
            Assert.True(File.Exists(Path.Combine(root, "state.json")));
            using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "report.json")));
            Assert.Equal(0, report.RootElement.GetProperty("failedRequestCount").GetInt32());
            Assert.Empty(report.RootElement.GetProperty("errors").EnumerateArray());
            Assert.Single(report.RootElement.GetProperty("warnings").EnumerateArray());
            var request = report.RootElement.GetProperty("requests")[0];
            Assert.True(request.GetProperty("success").GetBoolean());
            Assert.Equal(202, request.GetProperty("statusCode").GetInt32());
            Assert.Equal(1, request.GetProperty("attemptCount").GetInt32());
            Assert.Contains("response preview unavailable", File.ReadAllText(Path.Combine(root, "summary.md")));

            var unchanged = WebPipelineRunner.RunPipeline(pipeline, logger: null);
            Assert.True(unchanged.Success, unchanged.Steps[0].Message);
            Assert.Equal(1, calls);
        }
        finally
        {
            cancellation.Cancel();
            listener.Stop();
            if (server is not null)
                await server.WaitAsync(TimeSpan.FromSeconds(5));
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
