using PowerForge.Web;

namespace PowerForge.Tests;

public partial class WebSiteAuditOptimizeBuildTests
{
    [Theory]
    [InlineData("\"")]
    [InlineData("'")]
    public void OptimizeDetailed_Hashing_RewritesEveryViewerSource(string quote)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-media-hash-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "gallery"));
            Directory.CreateDirectory(Path.Combine(root, "images"));
            File.WriteAllText(Path.Combine(root, "images", "sample.png"), "fixture bytes for asset fingerprinting");
            var attributes = new[] { "src", "light", "dark", "mobile", "desktop" };
            var markup = string.Join(" ", attributes.Select(attribute =>
                $"data-pf-media-{attribute} = {quote}../images/sample.png?download=1&amp;x=2#preview{quote}"));
            var path = Path.Combine(root, "gallery", "index.html");
            var preserved = "<pre><code>src = '../images/sample.png'</code></pre>" +
                """<script>const src = '../images/sample.png'; const html = `<img src="../images/sample.png">`;</script>""" +
                "<!-- <img src='../images/sample.png'> -->" +
                "<textarea><img src='../images/sample.png'></textarea>" +
                "<style>/* src = '../images/sample.png' */</style>" +
                """<div title="src = '../images/sample.png'">Example</div>""";
            File.WriteAllText(path, preserved + $"<a {markup} data-unrelated-src={quote}../images/sample.png{quote} href={quote}../images/sample.png{quote}>Preview</a>");

            var result = WebAssetOptimizer.OptimizeDetailed(new WebAssetOptimizerOptions
            {
                SiteRoot = root, HashAssets = true, HashExtensions = [".png"]
            });
            var hashed = Assert.Single(result.HashedAssets);
            var fileName = Path.GetFileName(hashed.HashedPath);
            Assert.True(File.Exists(Path.Combine(root, "images", fileName)));
            Assert.False(File.Exists(Path.Combine(root, "images", "sample.png")));
            var html = File.ReadAllText(path);
            Assert.Contains(preserved, html);
            foreach (var attribute in attributes)
                Assert.Contains($"data-pf-media-{attribute} = {quote}../images/{fileName}?download=1&amp;x=2#preview{quote}", html);
            Assert.Contains($"href={quote}../images/{fileName}{quote}", html);
            Assert.Contains($"data-unrelated-src={quote}../images/sample.png{quote}", html);
            File.WriteAllText(path, preserved + $"<a data-pf-media-src={quote}../images/sample.png{quote}>Preview</a>");
            WebAssetOptimizer.OptimizeDetailed(new WebAssetOptimizerOptions
            {
                SiteRoot = root,
                AssetPolicy = new AssetPolicySpec
                {
                    Rewrites = [new AssetRewriteSpec { Match = "../images/", Replace = "../media/", MatchType = "prefix" }]
                }
            });
            html = File.ReadAllText(path);
            Assert.Contains(preserved, html);
            Assert.Contains($"data-pf-media-src={quote}../media/sample.png{quote}", html);

        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
