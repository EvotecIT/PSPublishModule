using System.Net;
using System.Text.Json;
using PowerForge.Web.Cli;
using PowerForge.Web.Protocols.IndexNow;

namespace PowerForge.Tests;

public sealed partial class WebPipelineRunnerIndexNowSitemapStateTests
{
    [Fact]
    public async Task StatefulSitemap_ThrottleReportPreservesDeadlineAndCheckpoint()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-indexnow-throttle-" + Guid.NewGuid().ToString("N"));
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
            var endpoint = $"http://127.0.0.1:{port}/indexnow/";
            server = Task.Run(async () =>
            {
                while (!cancellation.IsCancellationRequested)
                {
                    HttpListenerContext context;
                    try { context = await listener.GetContextAsync(); }
                    catch when (cancellation.IsCancellationRequested || !listener.IsListening) { break; }
                    Interlocked.Increment(ref calls);
                    context.Response.StatusCode = 429;
                    context.Response.Headers["Retry-After"] = "3600";
                    context.Response.Close();
                }
            });
            var sitemap = Path.Combine(root, "sitemap.xml");
            var state = Path.Combine(root, "state.json");
            File.WriteAllText(sitemap, Sitemap(("/one", "2026-09-01")));
            IndexNowSitemapCheckpoint.Load(sitemap, state, "https://example.com/", [endpoint]).Save();
            var previousCheckpoint = File.ReadAllBytes(state);
            File.WriteAllText(sitemap, Sitemap(("/one", "2026-09-02"), ("/two", "2026-09-02")));
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, $$"""
                { "steps": [{
                    "task": "indexnow", "baseUrl": "https://example.com/",
                    "sitemap": "./sitemap.xml", "sitemapStatePath": "./state.json",
                    "endpoint": "{{endpoint}}", "key": "examplekey",
                    "retryCount": 0, "batchSize": 1, "continueOnError": true,
                    "reportPath": "./report.json", "summaryPath": "./summary.md"
                }] }
                """);

            var before = DateTimeOffset.UtcNow;
            var result = WebPipelineRunner.RunPipeline(pipeline, logger: null);

            Assert.True(result.Success, result.Steps[0].Message);
            Assert.Equal(1, calls);
            Assert.Equal(previousCheckpoint, File.ReadAllBytes(state));
            Assert.Contains("checkpoint retained", result.Steps[0].Message);
            Assert.Contains("latest retry not before", result.Steps[0].Message);
            using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "report.json")));
            Assert.Equal(2, report.RootElement.GetProperty("failedRequestCount").GetInt32());
            var requests = report.RootElement.GetProperty("requests").EnumerateArray().ToArray();
            var deadline = requests[0].GetProperty("retryNotBefore").GetDateTimeOffset();
            Assert.InRange(deadline, before.AddHours(1), DateTimeOffset.UtcNow.AddHours(1));
            Assert.Equal(deadline, requests[1].GetProperty("retryNotBefore").GetDateTimeOffset());
            Assert.Equal(1, requests[0].GetProperty("attemptCount").GetInt32());
            Assert.Equal(0, requests[1].GetProperty("attemptCount").GetInt32());
            Assert.Equal(JsonValueKind.Null, requests[1].GetProperty("statusCode").ValueKind);
            Assert.Contains("Latest retry not before (UTC)", File.ReadAllText(Path.Combine(root, "summary.md")));
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
