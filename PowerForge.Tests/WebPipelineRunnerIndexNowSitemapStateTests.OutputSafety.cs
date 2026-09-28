using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed partial class WebPipelineRunnerIndexNowSitemapStateTests
{
    [Fact]
    public void StatefulSitemap_RejectsLaterStepOutputCollisionBeforeFirstStepRuns()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-indexnow-multi-step-collision-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "sitemap.xml"), Sitemap(("/one", "2026-09-01")));
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, """
                { "steps": [
                    { "task": "indexnow", "baseUrl": "https://example.com/", "sitemap": "./sitemap.xml",
                      "sitemapStatePath": "./first-state.json", "reportPath": "./first-report.json",
                      "key": "examplekey", "dryRun": true },
                    { "task": "indexnow", "baseUrl": "https://example.com/", "sitemap": "./sitemap.xml",
                      "sitemapStatePath": "./first-report.json", "key": "examplekey", "dryRun": true }
                ] }
                """);

            Assert.False(WebPipelineRunner.RunPipeline(pipeline, logger: null).Success);
            Assert.False(File.Exists(Path.Combine(root, "first-report.json")));
            Assert.False(File.Exists(Path.Combine(root, "first-state.json")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void StatefulSitemap_PreflightProtectsInputsBeforeEarlierFailureProfile()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-indexnow-early-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var sitemap = Path.Combine(root, "sitemap.xml");
            var content = Sitemap(("/one", "2026-09-01"));
            File.WriteAllText(sitemap, content);
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, """
                { "profilePath": "./sitemap.xml", "steps": [
                    { "task": "unknown-task" },
                    { "task": "indexnow", "baseUrl": "https://example.com/", "sitemap": "./sitemap.xml",
                      "sitemapStatePath": "./state.json", "key": "examplekey", "dryRun": true }
                ] }
                """);

            Assert.False(WebPipelineRunner.RunPipeline(pipeline, logger: null).Success);
            Assert.Equal(content, File.ReadAllText(sitemap));
            Assert.False(File.Exists(Path.Combine(root, "state.json")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void StatefulSitemap_HardLinkedReportDoesNotOverwriteInput()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-indexnow-hardlink-report-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var sitemap = Path.Combine(root, "sitemap.xml");
            var content = Sitemap(("/one", "2026-09-01"));
            File.WriteAllText(sitemap, content);
            TestFileLink.CreateHardLink(Path.Combine(root, "report.json"), sitemap);
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, """
                { "steps": [{ "task": "indexnow", "baseUrl": "https://example.com/",
                    "sitemap": "./sitemap.xml", "sitemapStatePath": "./state.json",
                    "reportPath": "./report.json", "key": "examplekey", "dryRun": true }] }
                """);

            Assert.True(WebPipelineRunner.RunPipeline(pipeline, logger: null).Success);
            Assert.Equal(content, File.ReadAllText(sitemap));
            using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "report.json")));
            Assert.Equal(1, report.RootElement.GetProperty("urlCount").GetInt32());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void StatefulSitemap_RejectsSymbolicReportAliasBeforeSubmission()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-indexnow-symbolic-report-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var sitemap = Path.Combine(root, "sitemap.xml");
            var content = Sitemap(("/one", "2026-09-01"));
            File.WriteAllText(sitemap, content);
            try
            {
                File.CreateSymbolicLink(Path.Combine(root, "report.json"), sitemap);
            }
            catch (Exception error) when (error is UnauthorizedAccessException or IOException)
            {
                return; // Link creation is disabled on some Windows test hosts.
            }
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, """
                { "steps": [{ "task": "indexnow", "baseUrl": "https://example.com/",
                    "sitemap": "./sitemap.xml", "sitemapStatePath": "./state.json",
                    "reportPath": "./report.json", "key": "examplekey", "dryRun": true }] }
                """);

            Assert.False(WebPipelineRunner.RunPipeline(pipeline, logger: null).Success);
            Assert.Equal(content, File.ReadAllText(sitemap));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("./sitemap.xml", "./summary.md")]
    [InlineData("./report.json", "./report.json")]
    [InlineData("./pipeline.json", "./summary.md")]
    [InlineData("./indexnow.txt", "./summary.md")]
    public void StatelessSubmission_RejectsInputAndOutputCollisions(string reportPath, string summaryPath)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-indexnow-stateless-collision-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "sitemap.xml"), Sitemap(("/one", "2026-09-01")));
            File.WriteAllText(Path.Combine(root, "indexnow.txt"), "examplekey");
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, $$"""
                { "steps": [{
                    "task": "indexnow", "baseUrl": "https://example.com/", "sitemap": "./sitemap.xml",
                    "reportPath": "{{reportPath}}", "summaryPath": "{{summaryPath}}", "dryRun": true
                }] }
                """);
            Assert.False(WebPipelineRunner.RunPipeline(pipeline, logger: null).Success);
            Assert.Equal("examplekey", File.ReadAllText(Path.Combine(root, "indexnow.txt")));
            Assert.Equal(Sitemap(("/one", "2026-09-01")), File.ReadAllText(Path.Combine(root, "sitemap.xml")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("profilePath", false, "./state.json")]
    [InlineData("cachePath", true, "./state.json")]
    [InlineData("profilePath", false, "./report.json")]
    public void StatefulSitemap_RejectsPipelineOutputCollisions(string rootProperty, bool cacheEnabled, string outputPath)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-indexnow-pipeline-collision-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "sitemap.xml"), Sitemap(("/one", "2026-09-01")));
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, $$"""
                { "cache": {{cacheEnabled.ToString().ToLowerInvariant()}}, "{{rootProperty}}": "{{outputPath}}",
                  "steps": [{
                    "task": "indexnow", "baseUrl": "https://example.com/",
                    "sitemap": "./sitemap.xml", "sitemapStatePath": "./state.json",
                    "reportPath": "./report.json", "key": "examplekey", "dryRun": true
                }] }
                """);
            Assert.False(WebPipelineRunner.RunPipeline(pipeline, logger: null).Success);
            Assert.False(File.Exists(Path.Combine(root, "state.json")));
            Assert.False(File.Exists(Path.Combine(root, "report.json")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void StatefulSitemap_RejectsReportOverInheritedPipelineConfig()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-indexnow-inherited-collision-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var inherited = Path.Combine(root, "base.json");
            const string inheritedContent = "{ \"steps\": [] }";
            File.WriteAllText(inherited, inheritedContent);
            File.WriteAllText(Path.Combine(root, "sitemap.xml"), Sitemap(("/one", "2026-09-01")));
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, """
                { "extends": "./base.json", "steps": [{
                    "task": "indexnow", "baseUrl": "https://example.com/",
                    "sitemap": "./sitemap.xml", "sitemapStatePath": "./state.json",
                    "reportPath": "./base.json", "key": "examplekey", "dryRun": true
                }] }
                """);
            Assert.False(WebPipelineRunner.RunPipeline(pipeline, logger: null).Success);
            Assert.Equal(inheritedContent, File.ReadAllText(inherited));
            Assert.False(File.Exists(Path.Combine(root, "state.json")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task StatefulSitemap_PersistsSubmissionReportBeforeCheckpointFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-indexnow-save-failure-" + Guid.NewGuid().ToString("N"));
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
                context.Response.StatusCode = 200;
                context.Response.Close();
            });
            Directory.CreateDirectory(Path.Combine(root, "state.json"));
            File.WriteAllText(Path.Combine(root, "sitemap.xml"), Sitemap(("/one", "2026-09-01")));
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, $$"""
                { "steps": [{
                    "task": "indexnow", "baseUrl": "https://example.com/",
                    "sitemap": "./sitemap.xml", "sitemapStatePath": "./state.json",
                    "endpoint": "http://127.0.0.1:{{port}}/indexnow/", "key": "examplekey",
                    "retryCount": 0, "reportPath": "./report.json", "summaryPath": "./summary.md"
                }] }
                """);
            var result = WebPipelineRunner.RunPipeline(pipeline, logger: null);
            await response.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(result.Success);
            using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "report.json")));
            Assert.Equal(1, report.RootElement.GetProperty("urlCount").GetInt32());
            Assert.Equal(1, report.RootElement.GetProperty("requestCount").GetInt32());
            Assert.Contains("URLs: 1", File.ReadAllText(Path.Combine(root, "summary.md")));
        }
        finally
        {
            listener.Stop();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task StatefulSitemap_AdvancesCheckpointAndWritesSummaryWhenReportFails()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-indexnow-report-failure-" + Guid.NewGuid().ToString("N"));
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
                context.Response.StatusCode = 200;
                context.Response.Close();
            });
            Directory.CreateDirectory(Path.Combine(root, "report.json"));
            File.WriteAllText(Path.Combine(root, "sitemap.xml"), Sitemap(("/one", "2026-09-01")));
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, $$"""
                { "steps": [{
                    "task": "indexnow", "baseUrl": "https://example.com/",
                    "sitemap": "./sitemap.xml", "sitemapStatePath": "./state.json",
                    "endpoint": "http://127.0.0.1:{{port}}/indexnow/", "key": "examplekey",
                    "retryCount": 0, "reportPath": "./report.json", "summaryPath": "./summary.md"
                }] }
                """);

            Assert.False(WebPipelineRunner.RunPipeline(pipeline, logger: null).Success);
            await response.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(File.Exists(Path.Combine(root, "state.json")));
            Assert.Contains("URLs: 1", File.ReadAllText(Path.Combine(root, "summary.md")));

            Assert.False(WebPipelineRunner.RunPipeline(pipeline, logger: null).Success);
            Assert.Contains("URLs: 0", File.ReadAllText(Path.Combine(root, "summary.md")));
        }
        finally
        {
            listener.Stop();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void StatefulSitemap_PathCaseUsesTargetVolumeSemantics()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-indexnow-pathcase-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var sitemap = Path.Combine(root, "Sitemap.xml");
            var state = Path.Combine(root, "sitemap.xml");
            File.WriteAllText(sitemap, Sitemap(("/one", "2026-09-01")));
            if (File.Exists(state))
                Assert.Throws<InvalidOperationException>(() => IndexNowSitemapCheckpoint.Load(sitemap, state, "https://example.com/", []));
            else
                Assert.Single(IndexNowSitemapCheckpoint.Load(sitemap, state, "https://example.com/", []).ChangedUrls);
            Assert.Equal(Sitemap(("/one", "2026-09-01")), File.ReadAllText(sitemap));
            Assert.Empty(Directory.EnumerateFiles(root, ".powerforge-indexnow-case-*"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void StatefulSitemap_DirectoryCaseUsesParentDirectorySemantics()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-indexnow-directory-case-" + Guid.NewGuid().ToString("N"));
        var upper = Path.Combine(root, "Out");
        Directory.CreateDirectory(upper);
        try
        {
            var sitemap = Path.Combine(upper, "sitemap.xml");
            var state = Path.Combine(root, "out", "sitemap.xml");
            File.WriteAllText(sitemap, Sitemap(("/one", "2026-09-01")));
            if (Directory.Exists(Path.Combine(root, "out")))
                Assert.Throws<InvalidOperationException>(() => IndexNowSitemapCheckpoint.Load(sitemap, state, "https://example.com/", []));
            else
                Assert.Single(IndexNowSitemapCheckpoint.Load(sitemap, state, "https://example.com/", []).ChangedUrls);
            Assert.Equal(Sitemap(("/one", "2026-09-01")), File.ReadAllText(sitemap));
            Assert.Empty(Directory.EnumerateFiles(root, ".powerforge-indexnow-case-*"));
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
