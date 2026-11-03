using PowerForge.Web;
using System.Text.Json;
using YamlDotNet.Serialization;

public class WebSiteVerifierTocCoverageTests
{
    [Theory]
    [InlineData("/docs", TrailingSlashMode.Always, "json")]
    [InlineData("/docs", TrailingSlashMode.Never, "json")]
    [InlineData("/docs", TrailingSlashMode.Always, "yml")]
    [InlineData("/docs", TrailingSlashMode.Never, "yml")]
    [InlineData("/", TrailingSlashMode.Always, "json")]
    [InlineData("/", TrailingSlashMode.Never, "json")]
    [InlineData("/", TrailingSlashMode.Always, "yml")]
    [InlineData("/", TrailingSlashMode.Never, "yml")]
    public void Verify_ResolvesCollectionRelativeTocLinksAndRetainsRealCoverageWarnings(
        string output, TrailingSlashMode slashMode, string format)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-toc-coverage-" + Guid.NewGuid().ToString("N"));
        var content = Path.Combine(root, "content", "docs");
        Directory.CreateDirectory(Path.Combine(content, "guides"));
        try
        {
            foreach (var page in new[] { "index", "install", "overview", "guides/index", "guides/setup", "rooted", "unlisted" })
                File.WriteAllText(Path.Combine(content, page + ".md"), $"---\ntitle: {page}\n---\n\n# {page}\n");

            var prefix = output.TrimEnd('/');
            var items = new List<object>
            {
                new { title = "Home", href = "./" },
                new { title = "Home source", url = "index.md" },
                new { title = "Install", href = "install.md#requirements" },
                new { title = "Overview", href = "overview/?view=all#details" },
                new { title = "Guides", href = "guides/index.md", items = new[]
                {
                    new { title = "Setup", href = "./guides/setup.md#steps" }
                } },
                new { title = "Rooted", href = prefix + "/rooted/" },
                new { title = "Section", href = "#local-section" },
                new { title = "External", href = "https://example.test/external/" },
                new { title = "External CDN", href = "//example.test/cdn/" },
                new { title = "Broken", href = "missing/" }
            };
            if (output != "/")
                items.Add(new { title = "Other collection", href = "/docs-other/outside/" });

            var tocText = format == "json"
                ? JsonSerializer.Serialize(items)
                : new SerializerBuilder().Build().Serialize(items);
            var tocPath = Path.Combine(content, "toc." + format);
            File.WriteAllText(tocPath, tocText);
            var spec = new SiteSpec
            {
                Name = "TOC coverage",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                TrailingSlash = slashMode,
                Collections = [new CollectionSpec
                {
                    Name = "docs", Input = "content/docs", Output = output, UseToc = true
                }]
            };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");

            var result = WebSiteVerifier.Verify(spec, WebSitePlanner.Plan(spec, configPath));

            Assert.True(result.Success);
            var warnings = result.Warnings.Where(w => w.StartsWith("TOC for collection 'docs'", StringComparison.Ordinal)).ToArray();
            var suffix = slashMode == TrailingSlashMode.Always ? "/" : "";
            Assert.Equal(2, warnings.Length);
            Assert.Contains($"TOC for collection 'docs' is missing 1 page(s): {prefix}/unlisted{suffix}", warnings);
            Assert.Contains($"TOC for collection 'docs' contains 1 missing page(s): {prefix}/missing{suffix}", warnings);
            Assert.Equal(tocText, File.ReadAllText(tocPath));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
