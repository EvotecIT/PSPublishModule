using PowerForge.Web;

namespace PowerForge.Tests;

public class WebMediaViewerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Build_PublishesViewerAssetsOnlyWhenEnabled(bool enabled)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-media-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "content"));
            File.WriteAllText(Path.Combine(root, "content", "sample.md"), "---\ntitle: Sample\nslug: gallery/sample\n---\n<a data-pf-media href=\"/sample.png\"><img src=\"/sample.png\" alt=\"Sample\"></a>");
            var spec = new SiteSpec
            {
                Name = "Media", BaseUrl = "https://example.test", ContentRoot = "content",
                Collections = [new CollectionSpec { Name = "pages", Input = "content", Output = "/" }],
                MediaViewer = new MediaViewerSpec { Enabled = enabled, Selector = "a[data-pf-media=\"gallery\"]" }
            };
            var output = Path.Combine(root, "_site");
            WebSiteBuilder.Build(spec, WebSitePlanner.Plan(spec, Path.Combine(root, "site.json")), output);
            var html = File.ReadAllText(Path.Combine(output, "gallery", "sample", "index.html"));
            Assert.Equal(enabled, html.Contains("media-viewer.v1.js"));
            Assert.Equal(enabled, File.Exists(Path.Combine(output, "assets", "powerforge", "media-viewer.v1.js")));
            Assert.Equal(enabled, File.Exists(Path.Combine(output, "assets", "powerforge", "media-viewer.v1.css")));
            Assert.Contains("href=\"/sample.png\"", html);
            if (enabled) Assert.Contains("data-pf-media-selector=\"a[data-pf-media=&quot;gallery&quot;]\"", html);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
