using PowerForge.Web;

namespace PowerForge.Tests;

public partial class WebSiteAuditOptimizeBuildTests
{
    [Fact]
    public void OptimizeDetailed_Hashing_PreservesCanonicalWebMcpRuntime()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-opt-webmcp-" + Guid.NewGuid().ToString("N"));
        var powerForgeAssets = Path.Combine(root, "assets", "powerforge");
        var cssAssets = Path.Combine(root, "css");
        var searchRoot = Path.Combine(root, "search");
        Directory.CreateDirectory(powerForgeAssets);
        Directory.CreateDirectory(cssAssets);
        Directory.CreateDirectory(searchRoot);

        try
        {
            var htmlPath = Path.Combine(searchRoot, "index.html");
            var runtimePath = Path.Combine(powerForgeAssets, "webmcp-site-search.v1.js");
            var ordinaryScriptPath = Path.Combine(root, "site.js");
            var ordinaryStylePath = Path.Combine(cssAssets, "app.css");
            var replacementPath = Path.Combine(root, "replacement.txt");
            var runtime = WebSiteBuilder.GetWebMcpSiteSearchAssetContent();
            File.WriteAllText(htmlPath,
                """
                <!doctype html>
                <html><head><link rel="stylesheet" href="../css/app.css"></head><body>
                  <script src="../site.js"></script>
                  <script src="../assets/powerforge/webmcp-site-search.v1.js" data-powerforge-webmcp></script>
                </body></html>
                """);
            File.WriteAllText(runtimePath, runtime);
            File.WriteAllText(replacementPath, "window.replaced = true;");
            File.WriteAllText(ordinaryScriptPath,
                """
                function greet() {
                    console.log("ordinary");
                }
                greet();
                """);
            File.WriteAllText(ordinaryStylePath,
                """
                body {
                  color: green;
                  margin: 0;
                }
                """);

            var result = WebAssetOptimizer.OptimizeDetailed(new WebAssetOptimizerOptions
            {
                SiteRoot = root,
                MinifyCss = true,
                MinifyJs = true,
                HashAssets = true,
                HashExtensions = new[] { ".css", ".js" },
                AssetPolicy = new AssetPolicySpec
                {
                    Rewrites =
                    [
                        new AssetRewriteSpec
                        {
                            Match = "assets/",
                            Replace = "cdn/assets/",
                            MatchType = "contains"
                        },
                        new AssetRewriteSpec
                        {
                            Match = "/unused.js",
                            Replace = "/assets/powerforge/webmcp-site-search.v1.js",
                            Source = replacementPath,
                            Destination = "assets/powerforge/webmcp-site-search.v1.js"
                        }
                    ]
                }
            });

            Assert.Equal(2, result.HashedAssetCount);
            var hashedScript = Assert.Single(result.HashedAssets, asset => asset.OriginalPath == "/site.js");
            var hashedStyle = Assert.Single(result.HashedAssets, asset => asset.OriginalPath == "/css/app.css");
            Assert.True(File.Exists(runtimePath));
            Assert.Equal(runtime, File.ReadAllText(runtimePath));
            Assert.Equal(1, result.JsMinifiedCount);
            AssertFinalHashMatches(root, hashedScript);
            AssertFinalHashMatches(root, hashedStyle);
            var html = File.ReadAllText(htmlPath);
            Assert.Contains("src=\"../assets/powerforge/webmcp-site-search.v1.js\"", html, StringComparison.Ordinal);
            Assert.Contains($"src=\"../{hashedScript.HashedPath.TrimStart('/')}\"", html, StringComparison.Ordinal);
            Assert.Contains($"href=\"../{hashedStyle.HashedPath.TrimStart('/')}\"", html, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("/", "site.js")]
    [InlineData("/assets/", "site.js")]
    [InlineData("../assets/", "site.js")]
    public void OptimizeDetailed_Hashing_HonorsHtmlBaseHref(string baseHref, string assetReference)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-opt-hash-base-" + Guid.NewGuid().ToString("N"));
        var searchRoot = Path.Combine(root, "search");
        var assetRoot = baseHref == "/" ? root : Path.Combine(root, "assets");
        Directory.CreateDirectory(searchRoot);
        Directory.CreateDirectory(assetRoot);

        try
        {
            var htmlPath = Path.Combine(searchRoot, "index.html");
            File.WriteAllText(htmlPath, $"<base href=\"{baseHref}\"><script src=\"{assetReference}\"></script>");
            File.WriteAllText(Path.Combine(assetRoot, "site.js"), "console.log('base');");

            var result = WebAssetOptimizer.OptimizeDetailed(new WebAssetOptimizerOptions
            {
                SiteRoot = root,
                HashAssets = true,
                HashExtensions = new[] { ".js" }
            });

            var hashedAsset = Assert.Single(result.HashedAssets);
            var html = File.ReadAllText(htmlPath);
            Assert.Contains(
                $"src=\"{Path.GetFileName(hashedAsset.HashedPath)}\"",
                html,
                StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void OptimizeDetailed_Hashing_PreservesEscapedReservedPathCharacters()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-opt-hash-escaped-" + Guid.NewGuid().ToString("N"));
        var searchRoot = Path.Combine(root, "search");
        var assetRoot = Path.Combine(root, "assets");
        Directory.CreateDirectory(searchRoot);
        Directory.CreateDirectory(assetRoot);

        try
        {
            var htmlPath = Path.Combine(searchRoot, "index.html");
            File.WriteAllText(htmlPath, "<link rel=\"stylesheet\" href=\"../assets/theme%23dark.css\">");
            File.WriteAllText(Path.Combine(assetRoot, "theme#dark.css"), "body { color: navy; }");

            var result = WebAssetOptimizer.OptimizeDetailed(new WebAssetOptimizerOptions
            {
                SiteRoot = root,
                HashAssets = true,
                HashExtensions = new[] { ".css" }
            });

            var hashedAsset = Assert.Single(result.HashedAssets);
            var expectedUrl = "../" + string.Join('/', hashedAsset.HashedPath.TrimStart('/').Split('/').Select(Uri.EscapeDataString));
            var html = File.ReadAllText(htmlPath);
            Assert.Contains($"href=\"{expectedUrl}\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain("theme#dark", html, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void OptimizeDetailed_Hashing_StabilizesCssAfterDependencyRewrite()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-opt-hash-css-dependency-" + Guid.NewGuid().ToString("N"));
        var cssRoot = Path.Combine(root, "css");
        var imageRoot = Path.Combine(root, "images");
        Directory.CreateDirectory(cssRoot);
        Directory.CreateDirectory(imageRoot);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"), "<link rel=\"stylesheet\" href=\"/css/app.css\">");
            File.WriteAllText(Path.Combine(cssRoot, "app.css"), "body { background: url('../images/hero.png'); }");
            File.WriteAllBytes(Path.Combine(imageRoot, "hero.png"), new byte[] { 1, 2, 3, 4, 5 });

            var result = WebAssetOptimizer.OptimizeDetailed(new WebAssetOptimizerOptions
            {
                SiteRoot = root,
                MinifyCss = true,
                HashAssets = true,
                HashExtensions = new[] { ".css", ".png" }
            });

            var hashedCss = Assert.Single(result.HashedAssets, asset => asset.OriginalPath == "/css/app.css");
            var hashedImage = Assert.Single(result.HashedAssets, asset => asset.OriginalPath == "/images/hero.png");
            var hashedCssPath = Path.Combine(root, hashedCss.HashedPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            var css = File.ReadAllText(hashedCssPath);
            Assert.Contains("../images/" + Path.GetFileName(hashedImage.HashedPath), css, StringComparison.Ordinal);
            AssertFinalHashMatches(root, hashedCss);
            Assert.Equal(1, result.HtmlHashRewriteCount);
            Assert.Equal(1, result.CssHashRewriteCount);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void OptimizeDetailed_Hashing_RewritesQuotedAndUrlCssImports()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-opt-hash-css-imports-" + Guid.NewGuid().ToString("N"));
        var cssRoot = Path.Combine(root, "css");
        Directory.CreateDirectory(cssRoot);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"), "<link rel=\"stylesheet\" href=\"/css/app.css\">");
            File.WriteAllText(Path.Combine(cssRoot, "app.css"),
                "@import \"./theme.css\"; .notice::before { content: '@import \"./theme.css\" url(./base.css)'; } /* @import \"./theme.css\"; url(./base.css) */");
            File.WriteAllText(Path.Combine(cssRoot, "theme.css"), "@import url('./base.css'); h1 { color: teal; }");
            File.WriteAllText(Path.Combine(cssRoot, "base.css"), "html { background: white; }");

            var result = WebAssetOptimizer.OptimizeDetailed(new WebAssetOptimizerOptions
            {
                SiteRoot = root,
                HashAssets = true,
                HashExtensions = new[] { ".css" }
            });

            var hashedApp = Assert.Single(result.HashedAssets, asset => asset.OriginalPath == "/css/app.css");
            var hashedTheme = Assert.Single(result.HashedAssets, asset => asset.OriginalPath == "/css/theme.css");
            var hashedBase = Assert.Single(result.HashedAssets, asset => asset.OriginalPath == "/css/base.css");
            var appCss = File.ReadAllText(GetHashedPath(root, hashedApp));
            var themeCss = File.ReadAllText(GetHashedPath(root, hashedTheme));
            Assert.Contains($"@import \"./{Path.GetFileName(hashedTheme.HashedPath)}\"", appCss, StringComparison.Ordinal);
            Assert.Contains($"@import url('./{Path.GetFileName(hashedBase.HashedPath)}')", themeCss, StringComparison.Ordinal);
            Assert.Contains("content: '@import \"./theme.css\" url(./base.css)'", appCss, StringComparison.Ordinal);
            Assert.Contains("/* @import \"./theme.css\"; url(./base.css) */", appCss, StringComparison.Ordinal);
            AssertFinalHashMatches(root, hashedApp);
            AssertFinalHashMatches(root, hashedTheme);
            AssertFinalHashMatches(root, hashedBase);
            Assert.Equal(1, result.HtmlHashRewriteCount);
            Assert.Equal(2, result.CssHashRewriteCount);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void OptimizeDetailed_Hashing_RejectsCyclicCssImports()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-opt-hash-css-cycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "a.css"), "@import \"./b.css\";");
            File.WriteAllText(Path.Combine(root, "b.css"), "@import url('./a.css');");

            var exception = Assert.Throws<InvalidOperationException>(() =>
                WebAssetOptimizer.OptimizeDetailed(new WebAssetOptimizerOptions
                {
                    SiteRoot = root,
                    HashAssets = true,
                    HashExtensions = new[] { ".css" }
                }));

            Assert.Contains("cyclic references", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void OptimizeDetailed_Hashing_RejectsExternalHtmlBaseBeforeMovingAssets()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-opt-hash-external-base-" + Guid.NewGuid().ToString("N"));
        var assetRoot = Path.Combine(root, "assets");
        Directory.CreateDirectory(assetRoot);

        try
        {
            var originalAsset = Path.Combine(assetRoot, "site.js");
            File.WriteAllText(Path.Combine(root, "index.html"),
                "<base href=\"https://docs.example.com/assets/\"><script src=\"site.js\"></script>");
            File.WriteAllText(originalAsset, "console.log('external base');");

            var exception = Assert.Throws<InvalidOperationException>(() =>
                WebAssetOptimizer.OptimizeDetailed(new WebAssetOptimizerOptions
                {
                    SiteRoot = root,
                    HashAssets = true,
                    HashExtensions = new[] { ".js" }
                }));

            Assert.Contains("another origin", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(originalAsset));
            Assert.Empty(Directory.EnumerateFiles(assetRoot, "site.*.js"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static void AssertFinalHashMatches(string root, WebOptimizeHashedAssetEntry asset)
    {
        var path = GetHashedPath(root, asset);
        var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))[..8]
            .ToLowerInvariant();
        var actual = Path.GetFileNameWithoutExtension(asset.HashedPath).Split('.').Last();
        Assert.Equal(expected, actual);
    }

    private static string GetHashedPath(string root, WebOptimizeHashedAssetEntry asset) =>
        Path.Combine(root, asset.HashedPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
}
