using ImageMagick;
using PowerForge.Web;

namespace PowerForge.Tests;

public partial class WebSiteAuditOptimizeBuildTests
{
    [Fact]
    public void OptimizeDetailed_ImageHints_PreserveUnquotedAttributeValuesEndingInSlash()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-opt-image-unquoted-slash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var nonBreakingSpace = '\u00A0';
            File.WriteAllText(Path.Combine(root, "index.html"),
                $$"""
                <!doctype html>
                <html>
                  <body>
                    <img src="/hero.png" data-marker=/>
                    <img src="/hero.png" data-marker=x{{nonBreakingSpace}}/>
                    <img src="/hero.png"/>
                  </body>
                </html>
                """);

            using (var image = new MagickImage(MagickColors.DeepSkyBlue, 640, 320))
                image.Write(Path.Combine(root, "hero.png"), MagickFormat.Png);

            var result = WebAssetOptimizer.OptimizeDetailed(new WebAssetOptimizerOptions
            {
                SiteRoot = root,
                OptimizeImages = true,
                ImageExtensions = new[] { ".png" },
                EnhanceImageTags = true
            });

            var html = File.ReadAllText(Path.Combine(root, "index.html"));
            Assert.Equal(1, result.ImageHtmlRewriteCount);
            Assert.Contains("data-marker=/ width=\"640\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain("data-marker= width=", html, StringComparison.Ordinal);
            Assert.Contains($"data-marker=x{nonBreakingSpace}/ width=\"640\"", html, StringComparison.Ordinal);
            Assert.Contains("decoding=\"async\" />", html, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
