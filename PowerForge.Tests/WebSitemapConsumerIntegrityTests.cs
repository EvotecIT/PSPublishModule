using System.Text.Json;
using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class WebSitemapConsumerIntegrityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndexNowReadsLocalIndexLeavesAndProtectsThemFromOutputs(bool stateful)
    {
        WithSite(root =>
        {
            File.WriteAllText(Path.Combine(root, "sitemap.xml"), """
                <sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                <sitemap><loc>https://example.test/part.xml</loc></sitemap></sitemapindex>
                """);
            var leaf = Path.Combine(root, "part.xml");
            const string leafXml = """
                <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" xmlns:image="http://www.google.com/schemas/sitemap-image/1.1">
                <url><loc>https://example.test/page/</loc><lastmod>2026-01-01</lastmod><image:image><image:loc>https://example.test/image.png</image:loc></image:image></url></urlset>
                """;
            File.WriteAllText(leaf, leafXml);
            var pipeline = Path.Combine(root, "pipeline.json");
            var state = stateful ? ",\"sitemapStatePath\":\"state.json\"" : "";
            File.WriteAllText(pipeline, $$"""{"steps":[{"task":"indexnow","baseUrl":"https://example.test/","sitemap":"sitemap.xml","key":"fixture","dryRun":true,"reportPath":"report.json"{{state}}}]}""");
            var result = WebPipelineRunner.RunPipeline(pipeline, logger: null);
            Assert.True(result.Success, result.Steps.Single().Message);
            using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "report.json")));
            Assert.Equal(1, report.RootElement.GetProperty("urlCount").GetInt32());
            if (stateful)
            {
                var checkpoint = IndexNowSitemapCheckpoint.Load(Path.Combine(root, "sitemap.xml"), Path.Combine(root, "state.json"), "https://example.test/", []);
                Assert.Equal(new[] { "https://example.test/page/" }, checkpoint.ChangedUrls);
                checkpoint.Save();
                Assert.Empty(IndexNowSitemapCheckpoint.Load(Path.Combine(root, "sitemap.xml"), Path.Combine(root, "state.json"), "https://example.test/", []).ChangedUrls);
            }
            File.WriteAllText(pipeline, $$"""{"steps":[{"task":"indexnow","baseUrl":"https://example.test/","sitemap":"sitemap.xml","key":"fixture","dryRun":true,"reportPath":"part.xml"{{state}}}]}""");
            Assert.False(WebPipelineRunner.RunPipeline(pipeline, logger: null).Success);
            Assert.Equal(leafXml, File.ReadAllText(leaf));
            File.WriteAllText(Path.Combine(root, "sitemap.xml"), "<sitemapindex><sitemap><loc>https://other.test/part.xml</loc></sitemap></sitemapindex>");
            Assert.Throws<InvalidOperationException>(() => WebLocalSitemapReader.Read(Path.Combine(root, "sitemap.xml"), "https://example.test/"));
        });
    }

    [Fact]
    public void CachedSitemapRegeneratesMissingPartition()
    {
        WithSite(root =>
        {
            File.WriteAllText(Path.Combine(root, "site.json"), "{\"baseUrl\":\"https://example.test\"}");
            File.WriteAllText(Path.Combine(root, "entries.json"), JsonSerializer.Serialize(
                Enumerable.Range(0, 50_001).Select(index => new { path = $"/page-{index}/" })));
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, """
                {"cache":true,"steps":[{"task":"build","config":"site.json","out":"site"},
                {"task":"sitemap","config":"site.json","out":"site/sitemap.xml","entriesJson":"entries.json","includeHtmlFiles":false,"includeTextFiles":false}]}
                """);
            Assert.True(WebPipelineRunner.RunPipeline(pipeline, logger: null).Success);
            var warm = WebPipelineRunner.RunPipeline(pipeline, logger: null);
            Assert.True(warm.Success, warm.Steps.Last().Message);
            Assert.True(warm.Steps.Last().Cached);
            var leaf = Path.Combine(root, "site", "sitemap.part-0002.xml");
            Assert.True(File.Exists(leaf));
            File.Delete(leaf);
            var repair = WebPipelineRunner.RunPipeline(pipeline, logger: null);
            Assert.True(repair.Success, repair.Steps.Last().Message);
            Assert.False(repair.Steps.Last().Cached);
            Assert.True(File.Exists(leaf));
        });
    }

    private static void WithSite(Action<string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-sitemap-consumer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { action(root); }
        finally { Directory.Delete(root, true); }
    }
}
