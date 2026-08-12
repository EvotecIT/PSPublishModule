using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public class WebPipelineRunnerCloudflareTests
{
    [Fact]
    public async Task RunPipeline_CloudflareVerify_Succeeds_WhenAllowedStatusIsReturned()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-cloudflare-verify-ok-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        HttpListener? listener = null;
        CancellationTokenSource? cts = null;
        Task? serverTask = null;

        try
        {
            var port = GetFreePort();
            (listener, cts, serverTask, _) = StartCloudflareStatusServer(port, "HIT");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                $$"""
                {
                  "steps": [
                    {
                      "task": "cloudflare",
                      "operation": "verify",
                      "warmupRequests": 0,
                      "allowStatuses": "HIT",
                      "urls": "http://127.0.0.1:{{port}}/",
                      "reportPath": "./_reports/cloudflare-verify.json",
                      "summaryPath": "./_reports/cloudflare-verify.md"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success, result.Steps[0].Message);

            var reportPath = Path.Combine(root, "_reports", "cloudflare-verify.json");
            var summaryPath = Path.Combine(root, "_reports", "cloudflare-verify.md");
            Assert.True(File.Exists(reportPath));
            Assert.True(File.Exists(summaryPath));
            Assert.Contains("\"ok\": true", File.ReadAllText(reportPath), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("CF-Cache-Status", File.ReadAllText(summaryPath), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await StopServerAsync(listener, cts, serverTask);
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunPipeline_CloudflareVerify_Fails_WhenStatusIsNotAllowed()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-cloudflare-verify-fail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        HttpListener? listener = null;
        CancellationTokenSource? cts = null;
        Task? serverTask = null;

        try
        {
            var port = GetFreePort();
            (listener, cts, serverTask, _) = StartCloudflareStatusServer(port, "MISS");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                $$"""
                {
                  "steps": [
                    {
                      "task": "cloudflare",
                      "operation": "verify",
                      "warmupRequests": 0,
                      "allowStatuses": "HIT",
                      "urls": "http://127.0.0.1:{{port}}/"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("Cloudflare cache verify failed", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await StopServerAsync(listener, cts, serverTask);
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunPipeline_CloudflareVerify_CanUseSiteConfigRouteProfile()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-cloudflare-verify-siteconfig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        HttpListener? listener = null;
        CancellationTokenSource? cts = null;
        Task? serverTask = null;
        RequestCounter? requestCounter = null;

        try
        {
            var port = GetFreePort();
            (listener, cts, serverTask, requestCounter) = StartCloudflareStatusServer(port, "HIT");

            File.WriteAllText(Path.Combine(root, "site.json"),
                $$"""
                {
                  "Name": "Cloudflare Verify SiteConfig Test",
                  "BaseUrl": "http://127.0.0.1:{{port}}",
                  "Features": [ "docs" ]
                }
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "cloudflare",
                      "operation": "verify",
                      "siteConfig": "./site.json",
                      "warmupRequests": 0,
                      "allowStatuses": "HIT"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success, result.Steps[0].Message);
            Assert.NotNull(requestCounter);
            Assert.True(requestCounter!.Count >= 3, "Expected at least three route profile requests (/, /docs/, /sitemap.xml).");
        }
        finally
        {
            await StopServerAsync(listener, cts, serverTask);
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunPipeline_CloudflareVerify_DiscoversFingerprintAssetFromDeployedHtml()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-cloudflare-discovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        HttpListener? listener = null;
        CancellationTokenSource? cts = null;
        Task? serverTask = null;
        RequestCounter? requestCounter = null;

        try
        {
            var port = GetFreePort();
            const string discoveryPath = "/redirect";
            const string appPath = "/apps/converter/";
            var html = """<html><head><base href="./"></head><body><script src="_framework/blazor.webassembly.abc123.js?v=release"></script></body></html>""";
            (listener, cts, serverTask, requestCounter) = StartCloudflareStatusServer(
                port, "HIT", appPath, html, discoveryPath, appPath);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                $$"""
                {
                  "steps": [
                    {
                      "task": "cloudflare",
                      "operation": "verify",
                      "baseUrl": "http://127.0.0.1:{{port}}",
                      "warmupRequests": 0,
                      "allowStatuses": "HIT",
                      "paths": [ "{{appPath}}" ],
                      "discoverAssetsFrom": [ "{{discoveryPath}}" ],
                      "assetPathPatterns": [ "/apps/converter/_framework/blazor.webassembly.*.js" ]
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success, result.Steps.Single().Message);
            Assert.NotNull(requestCounter);
            Assert.Contains(appPath, requestCounter!.Paths);
            Assert.Contains("/apps/converter/_framework/blazor.webassembly.abc123.js?v=release", requestCounter!.Paths);
        }
        finally
        {
            await StopServerAsync(listener, cts, serverTask);
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunPipeline_CloudflareVerify_RejectsCrossOriginDiscoveryRedirect()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-cloudflare-cross-origin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        HttpListener? listener = null;
        CancellationTokenSource? cts = null;
        Task? serverTask = null;
        try
        {
            var port = GetFreePort();
            (listener, cts, serverTask, _) = StartCloudflareStatusServer(
                port,
                "HIT",
                redirectPath: "/redirect",
                redirectTarget: "https://outside.example/apps/converter/");
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                $$"""
                {
                  "steps": [
                    {
                      "task": "cloudflare",
                      "operation": "verify",
                      "baseUrl": "http://127.0.0.1:{{port}}",
                      "discoverAssetsFrom": "/redirect",
                      "assetPathPatterns": "/apps/converter/_framework/*.js"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Contains("outside the configured site origin", result.Steps.Single().Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await StopServerAsync(listener, cts, serverTask);
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunPipeline_CloudflareVerify_RejectsCachedErrorResponse()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-cloudflare-cached-error-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        HttpListener? listener = null;
        CancellationTokenSource? cts = null;
        Task? serverTask = null;
        try
        {
            var port = GetFreePort();
            (listener, cts, serverTask, _) = StartCloudflareStatusServer(
                port, "HIT", responseStatus: HttpStatusCode.NotFound);
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                $$"""
                {
                  "steps": [
                    {
                      "task": "cloudflare",
                      "operation": "verify",
                      "baseUrl": "http://127.0.0.1:{{port}}",
                      "warmupRequests": 0,
                      "allowStatuses": "HIT",
                      "paths": "/missing.js"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Contains("Cloudflare cache verify failed", result.Steps.Single().Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await StopServerAsync(listener, cts, serverTask);
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task CloudflareVerifyCommand_ShouldCombineExplicitUrlsAndPaths()
    {
        HttpListener? listener = null;
        CancellationTokenSource? cts = null;
        Task? serverTask = null;
        RequestCounter? requestCounter = null;
        try
        {
            var port = GetFreePort();
            (listener, cts, serverTask, requestCounter) = StartCloudflareStatusServer(port, "HIT");
            var baseUrl = $"http://127.0.0.1:{port}";

            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "cloudflare",
                ["verify", "--base-url", baseUrl, "--url", $"{baseUrl}/explicit", "--path", "/from-path", "--warmup", "0", "--allow-status", "HIT"],
                outputJson: false,
                new WebConsoleLogger(),
                outputSchemaVersion: 1);

            Assert.Equal(0, exitCode);
            Assert.NotNull(requestCounter);
            Assert.Contains("/explicit", requestCounter!.Paths);
            Assert.Contains("/from-path", requestCounter.Paths);
        }
        finally
        {
            await StopServerAsync(listener, cts, serverTask);
        }
    }

    [Fact]
    public async Task RunPipeline_CloudflareVerify_ResolvesRootDiscoveryPathUnderConfiguredSiteBase()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-cloudflare-base-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        HttpListener? listener = null;
        CancellationTokenSource? cts = null;
        Task? serverTask = null;
        RequestCounter? requestCounter = null;
        try
        {
            var port = GetFreePort();
            const string appPath = "/project/";
            const string assetPath = "/project/_framework/app.abc123.js";
            var html = """<html><body><script src="_framework/app.abc123.js"></script></body></html>""";
            (listener, cts, serverTask, requestCounter) = StartCloudflareStatusServer(port, "HIT", appPath, html);
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                $$"""
                {
                  "steps": [
                    {
                      "task": "cloudflare",
                      "operation": "verify",
                      "baseUrl": "http://127.0.0.1:{{port}}/project/",
                      "warmupRequests": 0,
                      "allowStatuses": "HIT",
                      "discoverAssetsFrom": "/",
                      "assetPathPatterns": "{{assetPath}}"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success, result.Steps.Single().Message);
            Assert.NotNull(requestCounter);
            Assert.Contains(appPath, requestCounter!.Paths);
            Assert.Contains(assetPath, requestCounter.Paths);
            Assert.DoesNotContain("/", requestCounter.Paths);
        }
        finally
        {
            await StopServerAsync(listener, cts, serverTask);
            TryDeleteDirectory(root);
        }
    }

    private static (HttpListener listener, CancellationTokenSource cts, Task serverTask, RequestCounter requestCounter) StartCloudflareStatusServer(
        int port,
        string cacheStatus,
        string? htmlPath = null,
        string? html = null,
        string? redirectPath = null,
        string? redirectTarget = null,
        HttpStatusCode responseStatus = HttpStatusCode.OK)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        var cts = new CancellationTokenSource();
        var requestCounter = new RequestCounter();
        var serverTask = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                HttpListenerContext? context = null;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (HttpListenerException) when (cts.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException) when (cts.IsCancellationRequested)
                {
                    break;
                }

                if (context is null)
                    continue;

                Interlocked.Increment(ref requestCounter.Count);
                var requestPath = context.Request.Url?.PathAndQuery ?? "/";
                requestCounter.Paths.Enqueue(requestPath);
                if (string.Equals(context.Request.Url?.AbsolutePath, redirectPath, StringComparison.Ordinal))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.Redirect;
                    context.Response.RedirectLocation = redirectTarget;
                    context.Response.Close();
                    continue;
                }
                var content = string.Equals(context.Request.Url?.AbsolutePath, htmlPath, StringComparison.Ordinal) ? html ?? string.Empty : "ok";
                var payload = Encoding.UTF8.GetBytes(content);
                context.Response.StatusCode = (int)responseStatus;
                context.Response.Headers["cf-cache-status"] = cacheStatus;
                context.Response.ContentType = string.Equals(context.Request.Url?.AbsolutePath, htmlPath, StringComparison.Ordinal) ? "text/html" : "text/plain";
                context.Response.OutputStream.Write(payload, 0, payload.Length);
                context.Response.Close();
            }
        }, cts.Token);

        return (listener, cts, serverTask, requestCounter);
    }

    private sealed class RequestCounter
    {
        public int Count;
        public ConcurrentQueue<string> Paths { get; } = new();
    }

    private static async Task StopServerAsync(HttpListener? listener, CancellationTokenSource? cts, Task? serverTask)
    {
        if (cts is not null)
        {
            try { cts.Cancel(); } catch { }
        }

        if (listener is not null)
        {
            try { listener.Stop(); } catch { }
            try { listener.Close(); } catch { }
        }

        if (serverTask is not null)
        {
            try { await serverTask.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
        }

        cts?.Dispose();
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
            // ignore cleanup failures in tests
        }
    }
}
