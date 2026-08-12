using PowerForge.Web;

namespace PowerForge.Tests;

public partial class WebSiteAuditOptimizeBuildTests
{
    [Fact]
    public void OptimizeDetailed_CacheHeaders_CombinesExplicitAndExactHashedPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-opt-cache-headers-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "assets"));

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"), "<link rel=\"stylesheet\" href=\"/assets/site.css\">");
            File.WriteAllText(Path.Combine(root, "assets", "site.css"), "body { color: red; }");

            var result = WebAssetOptimizer.OptimizeDetailed(new WebAssetOptimizerOptions
            {
                SiteRoot = root,
                HashAssets = true,
                HashExtensions = [".css"],
                AssetPolicy = new AssetPolicySpec
                {
                    CacheHeaders = new CacheHeadersSpec
                    {
                        Enabled = true,
                        ImmutablePaths = ["/app/_framework/*.*.wasm"]
                    }
                }
            });

            Assert.True(result.CacheHeadersWritten);
            var hashedPath = Assert.Single(result.HashedAssets).HashedPath;
            var headers = File.ReadAllText(Path.Combine(root, "_headers"));
            Assert.Contains("/app/_framework/*.*.wasm", headers, StringComparison.Ordinal);
            Assert.Contains(hashedPath, headers, StringComparison.Ordinal);
            Assert.DoesNotContain("/assets/*", headers, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
