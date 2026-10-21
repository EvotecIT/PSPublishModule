using ImageMagick;
using PowerForge.Web;
using System.Xml.Linq;

namespace PowerForge.Tests;

public sealed class WebSocialCardRenderingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pf-social-render-" + Guid.NewGuid().ToString("N"));

    public WebSocialCardRenderingTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ConfiguredFonts_RenderExactFileWithoutInstallation_AndRejectUnavailableFamilies()
    {
        var fontPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "SocialCards", "SpaceGrotesk-Bold.ttf");
        File.Copy(fontPath, Path.Combine(_root, "display.ttf"));
        var fonts = WebSocialCardGenerator.ResolveFontFiles(new[] { new SocialCardFontSpec { Family = "Fixture Display", Path = "display.ttf", Weight = "100 800" } }, _root);
        var options = Options();
        options.VariantKey = "code-led";
        options.Code = "public static class Terminal";
        options.SiteAddress = "example.test";
        options.ThemeTokens = Tokens(("fontDisplay", "Fixture Display"), ("fontBody", "Fixture Display"));
        options.Fonts = fonts;
        var svg = WebSocialCardGenerator.RenderSvg(options);
        Assert.Contains("Fixture Display", svg, StringComparison.Ordinal);
        if (WebSocialCardGenerator.IsPngRenderingAvailable())
        {
            var exactFile = WebSocialCardGenerator.RenderPng(options);
            Assert.NotNull(exactFile);
            using var rendered = new MagickImage(exactFile);
            using var pixels = rendered.GetPixels();
            int LightPixels(int left, int top, int right, int bottom)
            {
                var count = 0;
                for (var y = top; y < bottom; y++)
                    for (var x = left; x < right; x++)
                    {
                        var color = pixels.GetPixel(x, y).ToColor()!;
                        if (color.R > 160 && color.G > 160 && color.B > 160)
                            count++;
                    }
                return count;
            }
            Assert.True(LightPixels(115, 50, 400, 110) > 80, "The configured font must retain the brand identity row.");
            Assert.True(LightPixels(60, 550, 400, 590) > 50, "The configured font must retain the footer.");
            options.Fonts = null;
            var fallback = WebSocialCardGenerator.RenderPng(options);
            Assert.NotNull(fallback);
            Assert.False(exactFile.AsSpan().SequenceEqual(fallback), "The declared uninstalled font must change the raster output.");
        }
        options.Fonts = fonts;
        options.ThemeTokens = Tokens(("fontDisplay", "Unavailable Fixture Font, sans-serif"));
        Assert.Contains("Unavailable Fixture Font", Assert.Throws<InvalidOperationException>(() => WebSocialCardGenerator.RenderPng(options)).Message);
        options.ThemeTokens = new Dictionary<string, object?> { ["font"] = new Dictionary<string, object?> { ["display"] = "Unavailable Theme Font, sans-serif" } };
        Assert.Contains("Unavailable Theme Font", Assert.Throws<InvalidOperationException>(() => WebSocialCardGenerator.RenderPng(options)).Message);
    }

    [Fact]
    public void ConfiguredFonts_MissingInvalidOrUnsupportedFiles_FailLoudly_AndContentChangesInvalidateCard()
    {
        var declaration = new SocialCardFontSpec { Family = "Fixture Display", Path = "missing.ttf", Weight = 700 };
        Assert.Contains("not found", Assert.Throws<InvalidOperationException>(() => WebSocialCardGenerator.ResolveFontFiles(new[] { declaration }, _root)).Message);
        File.WriteAllText(Path.Combine(_root, "invalid.ttf"), "not a font");
        declaration.Path = "invalid.ttf";
        var options = Options();
        options.Fonts = WebSocialCardGenerator.ResolveFontFiles(new[] { declaration }, _root);
        options.ThemeTokens = Tokens(("fontDisplay", "Fixture Display"));
        Assert.Contains("could not be rendered", Assert.Throws<InvalidOperationException>(() => WebSocialCardGenerator.RenderPng(options)).Message);
        var first = options.Fonts[0].Fingerprint;
        File.WriteAllText(Path.Combine(_root, "invalid.ttf"), "different font bytes");
        Assert.NotEqual(first, WebSocialCardGenerator.ResolveFontFiles(new[] { declaration }, _root)[0].Fingerprint);
        declaration.Path = "../outside.ttf";
        Assert.Contains("site root", Assert.Throws<InvalidOperationException>(() => WebSocialCardGenerator.ResolveFontFiles(new[] { declaration }, _root)).Message);
        File.WriteAllText(Path.Combine(_root, "web.woff2"), "web font");
        declaration.Path = "web.woff2";
        Assert.Contains("TTF or OTF", Assert.Throws<InvalidOperationException>(() => WebSocialCardGenerator.ResolveFontFiles(new[] { declaration }, _root)).Message);
    }

    [Theory]
    [InlineData("cover", "left top", "xMinYMin slice")]
    [InlineData("contain", "right bottom", "xMaxYMax meet")]
    public void SplitLayout_UsesFullBleedColumnAndConfiguredFitAnchor(string fit, string anchor, string expected)
    {
        var options = Options();
        options.VariantKey = "split";
        options.InlineImageDataUri = TestImage();
        options.ThemeTokens = Tokens(("imageShare", 0.48), ("imageFit", fit), ("imageAnchor", anchor), ("imagePadding", 0));
        var svg = XDocument.Parse(WebSocialCardGenerator.RenderSvg(options));
        var media = svg.Descendants().Single(element => element.Name.LocalName == "image" && element.Attribute("clip-path")?.Value == "url(#splitMediaClip)");
        Assert.Equal("624", media.Attribute("x")!.Value);
        Assert.Equal("0", media.Attribute("y")!.Value);
        Assert.Equal("576", media.Attribute("width")!.Value);
        Assert.Equal("630", media.Attribute("height")!.Value);
        Assert.Equal(expected, media.Attribute("preserveAspectRatio")!.Value);
        if (WebSocialCardGenerator.IsPngRenderingAvailable())
        {
            using var image = new MagickImage(WebSocialCardGenerator.RenderPng(options)!);
            Assert.Equal(1200u, image.Width);
            Assert.Equal(630u, image.Height);
            // Top-left cover keeps the red half; bottom-right contain aligns the image to the bottom edge.
            var pixels = image.GetPixels();
            var corner = pixels.GetPixel(630, fit == "cover" ? 3 : 620).ToColor()!;
            Assert.True(corner.R > corner.B, "The anchored red half should be visible at the expected image edge.");
        }
    }

    [Fact]
    public void ConciseText_TruncatesAtWordBoundary_AndFitsTitleBeforeExceedingTwoLines()
    {
        Assert.Equal(new[] { "arrays and…" }, WebSocialCardGenerator.WrapWords("arrays and groups with PowerShell", 14, 1));
        var options = Options();
        options.Title = "Build readable console tables and live dashboards";
        options.InlineImageDataUri = TestImage();
        options.Fonts = WebSocialCardGenerator.ResolveFontFiles(new[] { new SocialCardFontSpec
        {
            Family = "Fixture Display", Path = "Fixtures/SocialCards/SpaceGrotesk-Bold.ttf", Weight = 700
        } }, AppContext.BaseDirectory);
        options.ThemeTokens = Tokens(("fontDisplay", "Fixture Display"), ("panelTitleFontSize", 64));
        options.TextFit = true;
        var svg = XDocument.Parse(WebSocialCardGenerator.RenderSvg(options));
        var titleLines = svg.Descendants().Where(element => element.Name.LocalName == "text" && element.Attribute("font-weight")?.Value == "800" &&
            int.TryParse(element.Attribute("y")?.Value, out var y) && y > 180).ToArray();
        Assert.InRange(titleLines.Length, 1, 2);
        Assert.All(titleLines, line => Assert.DoesNotContain("…", line.Value, StringComparison.Ordinal));
        Assert.True(int.Parse(titleLines[0].Attribute("font-size")!.Value, System.Globalization.CultureInfo.InvariantCulture) < 64);
    }

    [Fact]
    public void CodeLedLayout_RendersSignatureWithMonoTheme_AndNoReferenceWatermark()
    {
        var options = Options();
        options.Title = "Terminal";
        options.VariantKey = "code-led";
        options.Code = "public static class Terminal";
        options.ThemeTokens = Tokens(("fontMono", "Fixture Mono"), ("accent", "#123456"), ("accentSoft", "#123456"),
            ("backgroundStart", "#101010"), ("surface", "#202020"), ("textPrimary", "#ffffff"));
        var svg = XDocument.Parse(WebSocialCardGenerator.RenderSvg(options));
        var code = svg.Descendants().Where(element => element.Name.LocalName == "text" && element.Attribute("font-family")?.Value == "Fixture Mono").ToArray();
        Assert.Contains(code, element => element.Value.Contains("public static class", StringComparison.Ordinal));
        Assert.Contains(code, element => element.Value.Contains("Terminal", StringComparison.Ordinal));
        Assert.Contains(code, element => element.Attribute("fill")?.Value == "#123456");
        Assert.DoesNotContain(svg.Descendants(), element => element.Name.LocalName == "text" && element.Attribute("font-size")?.Value == "120");
    }

    private static WebSocialCardGenerator.SocialCardRenderOptions Options() => new()
    {
        Title = "Terminal", Description = "Build readable console output.", Eyebrow = "Example", Badge = "DOCS", VariantKey = "panel"
    };

    private static Dictionary<string, object?> Tokens(params (string Key, object? Value)[] pairs)
        => new() { ["socialCard"] = pairs.ToDictionary(pair => pair.Key, pair => pair.Value) };

    private static string TestImage()
    {
        const string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1000\" height=\"200\"><rect width=\"500\" height=\"200\" fill=\"red\"/><rect x=\"500\" width=\"500\" height=\"200\" fill=\"blue\"/></svg>";
        return "data:image/svg+xml;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(svg));
    }
}
