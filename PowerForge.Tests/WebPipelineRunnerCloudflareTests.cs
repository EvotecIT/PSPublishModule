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
                      "urls": "http://127.0.0.1:{{port}}/"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success, result.Steps[0].Message);
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

    private static (HttpListener listener, CancellationTokenSource cts, Task serverTask, RequestCounter requestCounter) StartCloudflareStatusServer(
        int port,
        string cacheStatus)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        var cts = new CancellationTokenSource();
        var requestCounter = new RequestCounter();
        var payload = Encoding.UTF8.GetBytes("ok");

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
                context.Response.StatusCode = 200;
                context.Response.Headers["cf-cache-status"] = cacheStatus;
                context.Response.OutputStream.Write(payload, 0, payload.Length);
                context.Response.Close();
            }
        }, cts.Token);

        return (listener, cts, serverTask, requestCounter);
    }

    private sealed class RequestCounter
    {
        public int Count;
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
