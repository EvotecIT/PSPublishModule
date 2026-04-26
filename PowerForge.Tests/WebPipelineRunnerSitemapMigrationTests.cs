using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PowerForge.Web.Cli;
using Xunit;

namespace PowerForge.Tests;

public sealed class WebPipelineRunnerSitemapMigrationTests
{
    [Fact]
    public void RunPipeline_LinksCompareSitemaps_WritesRedirectAndReviewArtifacts()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-links-sitemaps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "_site"));
            Directory.CreateDirectory(Path.Combine(root, "data", "migration"));
            File.WriteAllText(Path.Combine(root, "data", "migration", "legacy-sitemap.xml"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                  <url><loc>https://evotec.xyz/old-post/</loc></url>
                  <url><loc>https://evotec.xyz/category/powershell/</loc></url>
                  <url><loc>https://evotec.xyz/missing/</loc></url>
                  <url><loc>not-a-url</loc></url>
                </urlset>
                """);
            File.WriteAllText(Path.Combine(root, "_site", "sitemap.xml"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                  <url><loc>https://evotec.xyz/blog/old-post/</loc></url>
                  <url><loc>https://evotec.xyz/categories/powershell/</loc></url>
                </urlset>
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "links-compare-sitemaps",
                      "legacySitemaps": ["./data/migration/legacy-sitemap.xml"],
                      "newSitemap": "./_site/sitemap.xml",
                      "newSiteRoot": "./_site",
                      "out": "./Build/link-reports/sitemap-migration.json",
                      "redirectCsv": "./data/redirects/legacy-generated.csv",
                      "reviewCsv": "./Build/link-reports/sitemap-review.csv"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.Contains("links-compare-sitemaps ok", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);

            var redirectCsv = File.ReadAllText(Path.Combine(root, "data", "redirects", "legacy-generated.csv"));
            Assert.Contains("https://evotec.xyz/old-post/,https://evotec.xyz/blog/old-post/,301,root-to-blog", redirectCsv, StringComparison.Ordinal);
            Assert.Contains("https://evotec.xyz/category/powershell/,https://evotec.xyz/categories/powershell/,301,category-to-categories", redirectCsv, StringComparison.Ordinal);

            var reviewCsv = File.ReadAllText(Path.Combine(root, "Build", "link-reports", "sitemap-review.csv"));
            Assert.Contains("https://evotec.xyz/missing/,,missing", reviewCsv, StringComparison.Ordinal);

            using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "Build", "link-reports", "sitemap-migration.json")));
            Assert.Equal(3, summary.RootElement.GetProperty("legacyUrlCount").GetInt32());
            Assert.Equal(2, summary.RootElement.GetProperty("newUrlCount").GetInt32());
            // MissingLegacyCount tracks legacy URLs absent from the new sitemap before
            // redirect heuristics map them, so it includes auto-redirect rows plus review rows.
            Assert.Equal(3, summary.RootElement.GetProperty("missingLegacyCount").GetInt32());
            Assert.Equal(1, summary.RootElement.GetProperty("reviewCount").GetInt32());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_LinksCompareSitemaps_StopsAtConfiguredSitemapDepth()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-links-sitemaps-depth-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "_site"));
            File.WriteAllText(Path.Combine(root, "legacy-index.xml"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                  <sitemap><loc>legacy-nested.xml</loc></sitemap>
                </sitemapindex>
                """);
            File.WriteAllText(Path.Combine(root, "legacy-nested.xml"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                  <url><loc>https://evotec.xyz/old-post/</loc></url>
                </urlset>
                """);
            File.WriteAllText(Path.Combine(root, "_site", "sitemap.xml"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                  <url><loc>https://evotec.xyz/blog/old-post/</loc></url>
                </urlset>
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "links-compare-sitemaps",
                      "legacySitemaps": ["./legacy-index.xml"],
                      "newSitemap": "./_site/sitemap.xml",
                      "maxSitemapDepth": 0
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.Contains("maxSitemapDepth", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_LinksCompareSitemaps_RejectsNestedLocalSitemapOutsideBaseDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-links-sitemaps-escape-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "sitemaps"));
            Directory.CreateDirectory(Path.Combine(root, "_site"));
            File.WriteAllText(Path.Combine(root, "sitemaps", "legacy-index.xml"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                  <sitemap><loc>../outside.xml</loc></sitemap>
                </sitemapindex>
                """);
            File.WriteAllText(Path.Combine(root, "outside.xml"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                  <url><loc>https://evotec.xyz/old-post/</loc></url>
                </urlset>
                """);
            File.WriteAllText(Path.Combine(root, "_site", "sitemap.xml"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                  <url><loc>https://evotec.xyz/blog/old-post/</loc></url>
                </urlset>
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "links-compare-sitemaps",
                      "legacySitemaps": ["./sitemaps/legacy-index.xml"],
                      "newSitemap": "./_site/sitemap.xml"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.Contains("escapes the sitemap directory", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunPipeline_LinksCompareSitemaps_RejectsNestedRemoteCrossOriginSitemaps()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-links-sitemaps-remote-origin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        HttpListener? listener = null;
        CancellationTokenSource? cts = null;
        Task? serverTask = null;

        try
        {
            var port = GetFreePort();
            (listener, cts, serverTask) = StartSitemapIndexServer(port);

            Directory.CreateDirectory(Path.Combine(root, "_site"));
            File.WriteAllText(Path.Combine(root, "_site", "sitemap.xml"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                  <url><loc>https://evotec.xyz/blog/old-post/</loc></url>
                </urlset>
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                $$"""
                {
                  "steps": [
                    {
                      "task": "links-compare-sitemaps",
                      "legacySitemaps": ["http://localhost:{{port}}/legacy-index.xml"],
                      "newSitemap": "./_site/sitemap.xml"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.Contains("crosses origins", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await StopServerAsync(listener, cts, serverTask);
            TryDeleteDirectory(root);
        }
    }

    private static (HttpListener listener, CancellationTokenSource cts, Task serverTask) StartSitemapIndexServer(int port)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        var cts = new CancellationTokenSource();
        var serverTask = Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                HttpListenerContext? context = null;
                try
                {
                    context = listener.GetContext();
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

                if (!string.Equals(context.Request.Url?.AbsolutePath, "/legacy-index.xml", StringComparison.OrdinalIgnoreCase))
                {
                    WriteXml(context.Response, 404, "<error>not found</error>");
                    continue;
                }

                WriteXml(
                    context.Response,
                    200,
                    $$"""
                    <?xml version="1.0" encoding="utf-8"?>
                    <sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                      <sitemap><loc>http://127.0.0.1:{{port}}/nested.xml</loc></sitemap>
                    </sitemapindex>
                    """);
            }
        }, cts.Token);

        return (listener, cts, serverTask);
    }

    private static void WriteXml(HttpListenerResponse response, int statusCode, string xml)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/xml";
        var bytes = Encoding.UTF8.GetBytes(xml);
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
        response.Close();
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
            // best-effort cleanup
        }
    }
}
