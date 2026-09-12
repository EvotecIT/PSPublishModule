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
            File.WriteAllText(path, $"<a {markup} data-unrelated-src={quote}../images/sample.png{quote} href={quote}../images/sample.png{quote}>Preview</a>");

            var result = WebAssetOptimizer.OptimizeDetailed(new WebAssetOptimizerOptions
            {
                SiteRoot = root, HashAssets = true, HashExtensions = [".png"]
            });
            var hashed = Assert.Single(result.HashedAssets);
            var fileName = Path.GetFileName(hashed.HashedPath);
            Assert.True(File.Exists(Path.Combine(root, "images", fileName)));
            Assert.False(File.Exists(Path.Combine(root, "images", "sample.png")));
            var html = File.ReadAllText(path);
            foreach (var attribute in attributes)
                Assert.Contains($"data-pf-media-{attribute} = {quote}../images/{fileName}?download=1&amp;x=2#preview{quote}", html);
            Assert.Contains($"href={quote}../images/{fileName}{quote}", html);
            Assert.Contains($"data-unrelated-src={quote}../images/sample.png{quote}", html);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
