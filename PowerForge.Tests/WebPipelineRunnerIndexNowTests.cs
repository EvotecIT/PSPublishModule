using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public class WebPipelineRunnerIndexNowTests
{
    [Fact]
    public void RunPipeline_IndexNow_OptionalKey_SkipsWhenKeyMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-indexnow-optional-key-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "indexnow",
                      "optionalKey": true,
                      "sitemap": "./missing-sitemap.xml"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("sitemap file not found", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);

            File.WriteAllText(Path.Combine(root, "sitemap.xml"),
                """
                <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                  <url><loc>https://example.com/</loc></url>
                </urlset>
                """);

            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "indexnow",
                      "optionalKey": true,
                      "sitemap": "./sitemap.xml"
                    }
                  ]
                }
                """);

            var second = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.Single(second.Steps);
            Assert.True(second.Steps[0].Success, second.Steps[0].Message);
            Assert.Contains("skipped", second.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_IndexNow_DryRun_CanCollectUrlsAndWriteReports()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-indexnow-dryrun-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "indexnow-urls.txt"),
                """
                /blog/
                https://example.com/api/
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "indexnow",
                      "baseUrl": "https://example.com",
                      "key": "abc123",
                      "dryRun": true,
                      "paths": "/,/docs/;/docs/",
                      "urlFile": "./indexnow-urls.txt",
                      "reportPath": "./_reports/indexnow.json",
                      "summaryPath": "./_reports/indexnow.md"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success, result.Steps[0].Message);
            Assert.True(result.Success, result.Steps[0].Message);

            var reportPath = Path.Combine(root, "_reports", "indexnow.json");
            var summaryPath = Path.Combine(root, "_reports", "indexnow.md");
            Assert.True(File.Exists(reportPath));
            Assert.True(File.Exists(summaryPath));

            using var reportDoc = JsonDocument.Parse(File.ReadAllText(reportPath));
            Assert.True(reportDoc.RootElement.GetProperty("dryRun").GetBoolean());
            Assert.Equal(4, reportDoc.RootElement.GetProperty("urlCount").GetInt32());
            Assert.Equal(1, reportDoc.RootElement.GetProperty("requestCount").GetInt32());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunPipeline_IndexNow_PostsExpectedPayload()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-indexnow-post-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        HttpListener? listener = null;
        CancellationTokenSource? cts = null;
        Task<string?>? serverTask = null;

        try
        {
            var port = GetFreePort();
            listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/indexnow/");
            listener.Start();

            cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            serverTask = Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    HttpListenerContext context;
                    try
                    {
                        context = await listener.GetContextAsync();
                    }
                    catch when (cts.IsCancellationRequested)
                    {
                        break;
                    }

                    string body;
                    using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
                    {
                        body = await reader.ReadToEndAsync();
                    }

                    context.Response.StatusCode = 200;
                    var responseBytes = Encoding.UTF8.GetBytes("{\"ok\":true}");
                    context.Response.OutputStream.Write(responseBytes, 0, responseBytes.Length);
                    context.Response.Close();
                    return body;
                }

                return null;
            }, cts.Token);

            File.WriteAllText(Path.Combine(root, "indexnow.key"), "myindexnowkey");
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                $$"""
                {
                  "steps": [
                    {
                      "task": "indexnow",
                      "baseUrl": "https://example.com",
                      "endpoint": "http://127.0.0.1:{{port}}/indexnow/",
                      "keyPath": "./indexnow.key",
                      "paths": "/docs/,/docs/",
                      "urls": "https://example.com/api/",
                      "batchSize": 10,
                      "retryCount": 0
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success, result.Steps[0].Message);
            Assert.True(result.Success, result.Steps[0].Message);

            var body = await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(string.IsNullOrWhiteSpace(body));

            using var payload = JsonDocument.Parse(body!);
            Assert.Equal("example.com", payload.RootElement.GetProperty("host").GetString());
            Assert.Equal("myindexnowkey", payload.RootElement.GetProperty("key").GetString());
            Assert.Equal("https://example.com/myindexnowkey.txt", payload.RootElement.GetProperty("keyLocation").GetString());

            var submittedUrls = payload.RootElement.GetProperty("urlList")
                .EnumerateArray()
                .Select(static item => item.GetString())
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .ToArray();
            Assert.Equal(2, submittedUrls.Length);
            Assert.Contains("https://example.com/docs/", submittedUrls);
            Assert.Contains("https://example.com/api/", submittedUrls);
        }
        finally
        {
            if (cts is not null)
            {
                try { cts.Cancel(); } catch { }
                cts.Dispose();
            }
            if (listener is not null)
            {
                try { listener.Stop(); } catch { }
                try { listener.Close(); } catch { }
            }
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_IndexNow_DryRun_FiltersLocalAndPrivateTargetUrls()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-indexnow-filter-private-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "indexnow",
                      "key": "abc123",
                      "dryRun": true,
                      "urls": "https://example.com/docs/,http://127.0.0.1/internal,https://192.168.1.10/internal",
                      "reportPath": "./_reports/indexnow.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success, result.Steps[0].Message);

            var reportPath = Path.Combine(root, "_reports", "indexnow.json");
            Assert.True(File.Exists(reportPath));
            using var reportDoc = JsonDocument.Parse(File.ReadAllText(reportPath));
            Assert.Equal(1, reportDoc.RootElement.GetProperty("urlCount").GetInt32());
            Assert.True(reportDoc.RootElement.GetProperty("warnings").GetArrayLength() >= 2);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_IndexNow_FailsForUnsafeKeyCharacters()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-indexnow-unsafe-key-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "indexnow",
                      "key": "abc/123",
                      "dryRun": true,
                      "urls": "https://example.com/docs/"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.False(result.Success);
            Assert.Contains("unsupported characters", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
            // ignore test cleanup failures
        }
    }
}
