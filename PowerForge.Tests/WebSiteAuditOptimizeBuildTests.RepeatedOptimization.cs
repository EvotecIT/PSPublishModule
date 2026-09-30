using PowerForge.Web;

namespace PowerForge.Tests;

public partial class WebSiteAuditOptimizeBuildTests
{
    [Fact]
    public void OptimizeDetailed_Hashing_RewritesUnquotedReferencesAfterHtmlMinification()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-opt-repeat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "css"));
        Directory.CreateDirectory(Path.Combine(root, "docs"));
        try
        {
            File.WriteAllText(Path.Combine(root, "css", "app.css"), "body { color: teal; }");
            File.WriteAllText(Path.Combine(root, "site.js"), "window.ready = true;");
            var htmlPath = Path.Combine(root, "docs", "index.html");
            File.WriteAllText(htmlPath,
                "<html><head><link rel=stylesheet href=../css/app.css?theme=dark#style></head>" +
                "<body><script src=../site.js></script><script>const sample = 'src=../site.js';</script></body></html>");
            var options = new WebAssetOptimizerOptions
            {
                SiteRoot = root,
                HashAssets = true,
                MinifyHtml = true,
                MinifyCss = true,
                MinifyJs = true
            };

            for (var pass = 0; pass < 3; pass++)
            {
                var result = WebAssetOptimizer.OptimizeDetailed(options);
                var html = File.ReadAllText(htmlPath);
                Assert.Equal(2, result.HashedAssets.Length);
                foreach (var asset in result.HashedAssets)
                {
                    Assert.True(File.Exists(GetHashedPath(root, asset)));
                    Assert.Contains("../" + asset.HashedPath.TrimStart('/'), html, StringComparison.Ordinal);
                    AssertFinalHashMatches(root, asset);
                }
                Assert.Contains("?theme=dark#style", html, StringComparison.Ordinal);
                Assert.Contains("src=../site.js", html, StringComparison.Ordinal);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
