using HtmlTinkerX;
using ImageMagick;
using PowerForge.Web;

namespace PowerForge.Tests;

public class WebAssetOptimizerPictureTests
{
    [Theory]
    [InlineData("", "/assets/hero.png", "(width > 600px)")]
    [InlineData("<base href='/assets/'>", "hero.png", "(width > 600px)")]
    [InlineData("<base href='../assets/'>", "./hero.png?theme=light&amp;v=1", "(width > 600px)")]
    public void RewritesRealImageAttributesAndPreservesDocumentSemantics(string documentBase, string imageUrl, string media)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-picture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "docs"));
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        try
        {
            using (var image = new MagickImage(MagickColors.DeepSkyBlue, 1200, 600))
                image.Write(Path.Combine(root, "assets", "hero.png"), MagickFormat.Png);
            var page = Path.Combine(root, "docs", "index.html");
            File.WriteAllText(page, $$"""
                <!doctype html><html><head>{{documentBase}}</head><body>
                <!-- <source srcset='/assets/hero.png'> -->
                <script>const template = '<img src="/assets/hero.png">';</script>
                <picture>
                <source data-srcset="keep.png" data-type="editorial" srcset="{{imageUrl}}" media="{{media}}" />
                <img data-src="keep.png" src="{{imageUrl}}" alt="A > B" />
                </picture>
                </body></html>
                """);
            WebAssetOptimizer.OptimizeDetailed(new WebAssetOptimizerOptions
            {
                SiteRoot = root, OptimizeImages = true, ImageExtensions = new[] { ".png" },
                ImageGenerateWebp = true, ImagePreferNextGen = true,
                ResponsiveImageWidths = new[] { 480 }, EnhanceImageTags = true
            });
            var html = File.ReadAllText(page);
            var doc = HtmlParser.ParseWithAngleSharp(html);
            var source = doc.QuerySelector("picture source")!;
            var img = doc.QuerySelector("picture img")!;
            Assert.Equal(media, source.GetAttribute("media"));
            Assert.Equal("keep.png", source.GetAttribute("data-srcset"));
            Assert.Equal("keep.png", img.GetAttribute("data-src"));
            Assert.Equal("A > B", img.GetAttribute("alt"));
            Assert.Equal("100vw", source.GetAttribute("sizes"));
            var baseUri = new Uri(new Uri("https://site.test/docs/index.html"), doc.QuerySelector("base")?.GetAttribute("href") ?? "");
            foreach (var value in new[] { source.GetAttribute("srcset")!, img.GetAttribute("srcset")! })
            {
                Assert.Contains(".w480.", value);
                foreach (var candidate in value.Split(','))
                {
                    var resolved = new Uri(baseUri, candidate.Trim().Split(' ')[0]);
                    Assert.StartsWith("/assets/hero", resolved.AbsolutePath);
                    Assert.True(File.Exists(Path.Combine(root, resolved.AbsolutePath.TrimStart('/'))));
                    if (imageUrl.Contains('?')) Assert.Equal("?theme=light&v=1", resolved.Query);
                }
            }
            Assert.Contains("<!-- <source srcset='/assets/hero.png'> -->", html);
            Assert.Contains("const template = '<img src=\"/assets/hero.png\">';", html);
        }
        finally { Directory.Delete(root, true); }
    }
}
