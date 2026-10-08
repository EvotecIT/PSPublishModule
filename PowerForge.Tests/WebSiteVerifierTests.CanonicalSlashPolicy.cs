using System.Text.Json;
using PowerForge.Web;

public partial class WebSiteVerifierTests
{
    [Theory]
    [InlineData("ignore", "/guide")]
    [InlineData("never", "/guide")]
    [InlineData("always", "/guide/")]
    public void Build_CanonicalMetadataAndPreviewUseTheSameTrailingSlashPolicy(string policy, string route)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-canonical-slash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "content"));
        try
        {
            File.WriteAllText(Path.Combine(root, "content", "guide.md"), "---\ntitle: Guide\n---\nGuide");
            var spec = new SiteSpec
            {
                BaseUrl = "https://example.test", TrailingSlash = Enum.Parse<TrailingSlashMode>(policy, ignoreCase: true),
                Social = new SocialSpec { Enabled = true },
                Collections = [new CollectionSpec { Name = "pages", Input = "content", Output = "/" }]
            };
            var config = Path.Combine(root, "site.json");
            File.WriteAllText(config, "{}");
            var output = Path.Combine(root, "_site");
            WebSiteBuilder.Build(spec, WebSitePlanner.Plan(spec, config), output);
            var url = "https://example.test" + route;
            var html = File.ReadAllText(Path.Combine(output, "guide", "index.html"));
            Assert.Contains("<link rel=\"canonical\" href=\"" + url + "\"", html, StringComparison.Ordinal);
            Assert.Contains("property=\"og:url\" content=\"" + url + "\"", html, StringComparison.Ordinal);
            using var preview = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "_powerforge", "seo-preview.json")));
            Assert.Equal(url, preview.RootElement.GetProperty("pages")[0].GetProperty("canonicalUrl").GetString());
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
