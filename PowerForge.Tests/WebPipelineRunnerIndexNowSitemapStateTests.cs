using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class WebPipelineRunnerIndexNowSitemapStateTests
{
    [Fact]
    public async Task RunPipeline_StatefulSitemap_SubmitsOnlyChangedUrlsAndRetriesFailedBatch()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-indexnow-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var listener = new HttpListener();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            var port = FreePort();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/indexnow/");
            listener.Start();
            var requested = new List<string[]>();
            var responses = new Queue<int>([200, 200, 500, 200]);
            var server = Task.Run(async () =>
            {
                while (!cancellation.IsCancellationRequested)
                {
                    HttpListenerContext context;
                    try { context = await listener.GetContextAsync(); }
                    catch when (cancellation.IsCancellationRequested || !listener.IsListening) { break; }
                    using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
                    using var body = JsonDocument.Parse(await reader.ReadToEndAsync());
                    requested.Add(body.RootElement.GetProperty("urlList").EnumerateArray()
                        .Select(static item => item.GetString()!).ToArray());
                    context.Response.StatusCode = responses.Dequeue();
                    context.Response.OutputStream.Write(Encoding.UTF8.GetBytes("{}"));
                    context.Response.Close();
                }
            }, cancellation.Token);

            var sitemap = Path.Combine(root, "sitemap.xml");
            var state = Path.Combine(root, "indexnow-state.json");
            File.WriteAllText(sitemap, Sitemap(("/one", "2026-09-01"), ("/two", "2026-09-01")));
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, $$"""
                { "steps": [{
                    "task": "indexnow",
                    "baseUrl": "https://example.com/",
                    "sitemap": "./sitemap.xml",
                    "sitemapStatePath": "./indexnow-state.json",
                    "endpoint": "http://127.0.0.1:{{port}}/indexnow/",
                    "reportPath": "./report.json", "summaryPath": "./summary.md",
                    "key": "examplekey",
                    "retryCount": 0
                }] }
                """);

            var first = WebPipelineRunner.RunPipeline(pipeline, logger: null);
            Assert.True(first.Success, first.Steps[0].Message);
            Assert.True(File.Exists(state));
            Assert.Equal(2, Assert.Single(requested).Length);

            var warm = WebPipelineRunner.RunPipeline(pipeline, logger: null);
            Assert.True(warm.Success, warm.Steps[0].Message);
            Assert.Single(requested);
            using (var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "report.json"))))
                Assert.Equal(0, report.RootElement.GetProperty("urlCount").GetInt32());
            Assert.Contains("URLs: 0", File.ReadAllText(Path.Combine(root, "summary.md")));

            File.WriteAllText(sitemap, Sitemap(("/one", "2026-09-02"), ("/two", "2026-09-01")));
            var changed = WebPipelineRunner.RunPipeline(pipeline, logger: null);
            Assert.True(changed.Success, changed.Steps[0].Message);
            Assert.Equal(["https://example.com/one"], requested[1]);

            var goodState = File.ReadAllText(state);
            File.WriteAllText(sitemap, Sitemap(("/one", "2026-09-03"), ("/two", "2026-09-01")));
            var failed = WebPipelineRunner.RunPipeline(pipeline, logger: null);
            Assert.False(failed.Success);
            Assert.Equal(goodState, File.ReadAllText(state));
            Assert.Equal(["https://example.com/one"], requested[2]);

            var retry = WebPipelineRunner.RunPipeline(pipeline, logger: null);
            Assert.True(retry.Success, retry.Steps[0].Message);
            Assert.Equal(["https://example.com/one"], requested[3]);
            Assert.NotEqual(goodState, File.ReadAllText(state));

            cancellation.Cancel();
            listener.Stop();
            await server.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            cancellation.Cancel();
            listener.Stop();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void StatefulSitemap_DryRunAndUnsafeInputDoNotAdvanceCheckpoint()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-indexnow-state-safety-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var sitemap = Path.Combine(root, "sitemap.xml");
            var state = Path.Combine(root, "state.json");
            File.WriteAllText(sitemap, Sitemap(("/one", "2026-09-01")));
            var checkpoint = IndexNowSitemapCheckpoint.Load(sitemap, state, "https://example.com/", []);
            Assert.Equal(["https://example.com/one"], checkpoint.ChangedUrls);

            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, """
                { "steps": [{
                    "task": "indexnow", "baseUrl": "https://example.com/",
                    "sitemap": "./sitemap.xml", "sitemapStatePath": "./state.json",
                    "key": "examplekey", "dryRun": true
                }] }
                """);
            Assert.True(WebPipelineRunner.RunPipeline(pipeline, logger: null).Success);
            Assert.False(File.Exists(state));

            File.WriteAllText(sitemap, Sitemap(("/one", "2026-09-01"), ("/two", "2026-09-01")));
            File.WriteAllText(pipeline, """
                { "steps": [{
                    "task": "indexnow", "baseUrl": "https://example.com/",
                    "sitemap": "./sitemap.xml", "sitemapStatePath": "./state.json",
                    "key": "examplekey", "dryRun": true, "maxUrls": 1
                }] }
                """);
            Assert.False(WebPipelineRunner.RunPipeline(pipeline, logger: null).Success);
            Assert.False(File.Exists(state));

            checkpoint.Save();
            File.WriteAllText(sitemap, Sitemap());
            Assert.Throws<InvalidOperationException>(() => IndexNowSitemapCheckpoint.Load(sitemap, state, "https://example.com/", []));
            File.WriteAllText(state, "not-json");
            File.WriteAllText(sitemap, Sitemap(("/one", "2026-09-01")));
            Assert.Throws<JsonException>(() => IndexNowSitemapCheckpoint.Load(sitemap, state, "https://example.com/", []));
            File.Delete(state);

            File.WriteAllText(sitemap, Sitemap(("https://other.example/two", "2026-09-01")));
            var rejected = WebPipelineRunner.RunPipeline(pipeline, logger: null);
            Assert.False(rejected.Success);
            Assert.False(File.Exists(state));

            File.WriteAllText(sitemap, Sitemap(("/Case", "2026-09-01"), ("/case", "2026-09-01")));
            Assert.Throws<InvalidOperationException>(() => IndexNowSitemapCheckpoint.Load(sitemap, state, "https://example.com/", []));

            File.WriteAllText(sitemap, Sitemap(("/case", "2026-09-01")));
            File.WriteAllText(pipeline, """
                { "steps": [{
                    "task": "indexnow", "baseUrl": "https://example.com/",
                    "urls": "https://example.com/Case",
                    "sitemap": "./sitemap.xml", "sitemapStatePath": "./state.json",
                    "key": "examplekey", "dryRun": true
                }] }
                """);
            Assert.False(WebPipelineRunner.RunPipeline(pipeline, logger: null).Success);
            Assert.False(File.Exists(state));

            File.WriteAllText(pipeline, """
                { "steps": [{
                    "task": "indexnow", "baseUrl": "https://example.com/",
                    "urls": "https://EXAMPLE.com/case",
                    "sitemap": "./sitemap.xml", "sitemapStatePath": "./state.json",
                    "key": "examplekey", "dryRun": true
                }] }
                """);
            Assert.True(WebPipelineRunner.RunPipeline(pipeline, logger: null).Success);
            Assert.False(File.Exists(state));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunPipeline_ContinueOnError_ReportsFailureWithoutAdvancingCheckpoint()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-indexnow-continue-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var listener = new HttpListener();
        try
        {
            var port = FreePort();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/indexnow/");
            listener.Start();
            var response = Task.Run(async () =>
            {
                var context = await listener.GetContextAsync();
                context.Response.StatusCode = 500;
                context.Response.Close();
            });

            File.WriteAllText(Path.Combine(root, "sitemap.xml"), Sitemap(("/one", "2026-09-01")));
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, $$"""
                { "steps": [{
                    "task": "indexnow", "baseUrl": "https://example.com/",
                    "sitemap": "./sitemap.xml", "sitemapStatePath": "./state.json",
                    "endpoint": "http://127.0.0.1:{{port}}/indexnow/", "key": "examplekey",
                    "retryCount": 0, "continueOnError": true,
                    "reportPath": "./report.json", "summaryPath": "./summary.md"
                }] }
                """);
            var result = WebPipelineRunner.RunPipeline(pipeline, logger: null);
            await response.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(result.Success, result.Steps[0].Message);
            Assert.Contains("checkpoint retained", result.Steps[0].Message);
            Assert.False(File.Exists(Path.Combine(root, "state.json")));
            using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "report.json")));
            Assert.Equal(1, report.RootElement.GetProperty("failedRequestCount").GetInt32());
            Assert.Contains("Failed requests: 1", File.ReadAllText(Path.Combine(root, "summary.md")));
        }
        finally
        {
            listener.Stop();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void StatefulSitemap_EndpointChangeResubmitsWithoutChangingLastmod()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-indexnow-endpoints-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var sitemap = Path.Combine(root, "sitemap.xml");
            var state = Path.Combine(root, "state.json");
            File.WriteAllText(sitemap, Sitemap(("/one", "2026-09-01")));
            var first = IndexNowSitemapCheckpoint.Load(sitemap, state, "https://example.com/", ["https://api.example.net/one"]);
            Assert.Single(first.ChangedUrls);
            first.Save();

            Assert.Empty(IndexNowSitemapCheckpoint.Load(sitemap, state, "https://example.com/", ["https://api.example.net/one"]).ChangedUrls);
            Assert.Equal(["https://example.com/one"],
                IndexNowSitemapCheckpoint.Load(sitemap, state, "https://example.com/", ["https://api.example.net/two"]).ChangedUrls);
            Assert.Equal(["https://example.com/one"],
                IndexNowSitemapCheckpoint.Load(sitemap, state, "https://example.com/", ["https://api.example.net/one", "https://api.example.net/two"]).ChangedUrls);

            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, """
                { "steps": [{
                    "task": "indexnow", "baseUrl": "https://example.com/",
                    "sitemap": "./sitemap.xml", "sitemapStatePath": "./state.json",
                    "endpoint": "https://api.example.net/two", "key": "examplekey",
                    "dryRun": true, "reportPath": "./report.json"
                }] }
                """);
            Assert.True(WebPipelineRunner.RunPipeline(pipeline, logger: null).Success);
            using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "report.json")));
            Assert.Equal(1, report.RootElement.GetProperty("urlCount").GetInt32());
            Assert.Empty(IndexNowSitemapCheckpoint.Load(sitemap, state, "https://example.com/", ["https://api.example.net/one"]).ChangedUrls);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void StatefulSitemap_FragmentVariantsHaveOneSubmissionIdentity()
    {
        Assert.Equal(["https://example.com/one"], IndexNowSubmitter.NormalizeCheckpointUrls([
            "https://example.com/one#first", "https://example.com/one#second"]));
        Assert.Throws<InvalidOperationException>(() => IndexNowSubmitter.NormalizeCheckpointUrls([
            "https://example.com/One", "https://example.com/one"]));
    }

    [Fact]
    public void StatefulSitemap_PathCaseUsesOperatingSystemSemantics()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-indexnow-pathcase-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var sitemap = Path.Combine(root, "Sitemap.xml");
            var state = Path.Combine(root, "sitemap.xml");
            File.WriteAllText(sitemap, Sitemap(("/one", "2026-09-01")));
            if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
                Assert.Throws<InvalidOperationException>(() => IndexNowSitemapCheckpoint.Load(sitemap, state, "https://example.com/", []));
            else
                Assert.Single(IndexNowSitemapCheckpoint.Load(sitemap, state, "https://example.com/", []).ChangedUrls);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void StatefulSitemap_OversizedCheckpointFailsBeforeSubmission()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-indexnow-checkpoint-size-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var sitemap = Path.Combine(root, "sitemap.xml");
            var state = Path.Combine(root, "state.json");
            var builder = new StringBuilder("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
            var suffix = new string('a', 32_000);
            for (var index = 0; index < 1_100; index++)
                builder.Append("<url><loc>https://example.com/").Append(index).Append(suffix).Append("</loc></url>");
            File.WriteAllText(sitemap, builder.Append("</urlset>").ToString());

            var error = Assert.Throws<InvalidOperationException>(() =>
                IndexNowSitemapCheckpoint.Load(sitemap, state, "https://example.com/", []));
            Assert.Contains("checkpoint exceeds 32 MiB", error.Message);
            Assert.False(File.Exists(state));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string Sitemap(params (string Url, string Lastmod)[] entries)
    {
        var builder = new StringBuilder("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
        foreach (var (url, lastmod) in entries)
        {
            var absolute = url.StartsWith('/') ? "https://example.com" + url : url;
            builder.Append("<url><loc>").Append(absolute).Append("</loc><lastmod>")
                .Append(lastmod).Append("</lastmod></url>");
        }
        return builder.Append("</urlset>").ToString();
    }

    private static int FreePort()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        return ((IPEndPoint)socket.LocalEndpoint).Port;
    }
}
