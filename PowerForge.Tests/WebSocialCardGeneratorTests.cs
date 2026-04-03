using PowerForge.Web;

namespace PowerForge.Tests;

public class WebSocialCardGeneratorTests
{
    [Theory]
    [InlineData("TestimoX - Active Directory Security", "TestimoX - Active Directory Security")]
    [InlineData("OfficeIMO", "OfficeIMO")]
    [InlineData("OfficeIMO.Word", "OfficeIMO.Word")]
    [InlineData("  TestimoX\r\nActive   Directory  ", "TestimoX Active Directory")]
    public void NormalizeDisplayTextForWrap_PreservesBrandNames(string input, string expected)
    {
        var normalized = WebSocialCardGenerator.NormalizeDisplayTextForWrap(input);
        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void SelectPalette_UsesGenericThemeColorTokens_WhenAvailable()
    {
        var tokens = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["color"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["bg"] = "#102030",
                ["panel"] = "#203040",
                ["ink"] = "#f2f4f8",
                ["muted"] = "#aab4c0",
                ["accent"] = "#ff7a00",
                ["border"] = "#2f4050"
            }
        };

        var palette = WebSocialCardGenerator.SelectPalette("default", "seed", tokens);

        Assert.Equal("#102030", palette.BackgroundStart);
        Assert.Equal("#203040", palette.BackgroundMid);
        Assert.Equal("#102030", palette.BackgroundEnd);
        Assert.Equal("#203040", palette.Surface);
        Assert.Equal("#2f4050", palette.SurfaceStroke);
        Assert.Equal("#ff7a00", palette.Accent);
        Assert.Equal("#ff7a00", palette.AccentSoft);
        Assert.Equal("#f2f4f8", palette.AccentStrong);
        Assert.Equal("#f2f4f8", palette.TextPrimary);
        Assert.Equal("#aab4c0", palette.TextSecondary);
        Assert.Equal("#203040", palette.ChipBackground);
        Assert.Equal("#2f4050", palette.ChipBorder);
        Assert.Equal("#f2f4f8", palette.ChipText);
    }

    [Fact]
    public void SelectPalette_PrefersDedicatedSocialCardTokens_OverGenericThemeColors()
    {
        var tokens = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["color"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["bg"] = "#102030",
                ["panel"] = "#203040",
                ["ink"] = "#f2f4f8",
                ["muted"] = "#aab4c0",
                ["accent"] = "#ff7a00",
                ["border"] = "#2f4050"
            },
            ["socialCard"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["backgroundStart"] = "#010203",
                ["backgroundMid"] = "#111213",
                ["backgroundEnd"] = "#212223",
                ["surface"] = "#313233",
                ["surfaceStroke"] = "#414243",
                ["accent"] = "#515253",
                ["accentSoft"] = "#616263",
                ["accentStrong"] = "#717273",
                ["textPrimary"] = "#818283",
                ["textSecondary"] = "#919293",
                ["chipBackground"] = "#a1a2a3",
                ["chipBorder"] = "#b1b2b3",
                ["chipText"] = "#c1c2c3"
            }
        };

        var palette = WebSocialCardGenerator.SelectPalette("default", "seed", tokens);

        Assert.Equal("#010203", palette.BackgroundStart);
        Assert.Equal("#111213", palette.BackgroundMid);
        Assert.Equal("#212223", palette.BackgroundEnd);
        Assert.Equal("#313233", palette.Surface);
        Assert.Equal("#414243", palette.SurfaceStroke);
        Assert.Equal("#515253", palette.Accent);
        Assert.Equal("#616263", palette.AccentSoft);
        Assert.Equal("#717273", palette.AccentStrong);
        Assert.Equal("#818283", palette.TextPrimary);
        Assert.Equal("#919293", palette.TextSecondary);
        Assert.Equal("#a1a2a3", palette.ChipBackground);
        Assert.Equal("#b1b2b3", palette.ChipBorder);
        Assert.Equal("#c1c2c3", palette.ChipText);
    }

    [Fact]
    public void RenderSvg_UsesThemeFontsAndLayoutTokens_WhenAvailable()
    {
        var tokens = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["color"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["bg"] = "#102030",
                ["panel"] = "#203040",
                ["ink"] = "#f2f4f8",
                ["muted"] = "#aab4c0",
                ["accent"] = "#ff7a00",
                ["border"] = "#2f4050"
            },
            ["radius"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["base"] = "18px",
                ["sm"] = "12px"
            },
            ["font"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["body"] = "Inter, Segoe UI, sans-serif",
                ["display"] = "Space Grotesk, Segoe UI, sans-serif"
            },
            ["socialCard"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["fontBadge"] = "IBM Plex Sans, Segoe UI, sans-serif",
                ["fontFooter"] = "IBM Plex Sans, Segoe UI, sans-serif",
                ["panelRadius"] = "26px",
                ["frameRadius"] = "30px",
                ["badgeRadius"] = "16px",
                ["badgeAlign"] = "left",
                ["contentPadding"] = "32px"
            }
        };

        var svg = WebSocialCardGenerator.RenderSvg(new WebSocialCardGenerator.SocialCardRenderOptions
        {
            Title = "TestimoX - Active Directory Security",
            Description = "Theme aware social card",
            Eyebrow = "TestimoX",
            Badge = "HOME",
            FooterLabel = "/",
            Width = 1200,
            Height = 630,
            StyleKey = "home",
            VariantKey = "spotlight",
            ThemeTokens = tokens
        });

        Assert.Contains("font-family=\"Space Grotesk, Segoe UI, sans-serif\"", svg, StringComparison.Ordinal);
        Assert.Contains("font-family=\"Inter, Segoe UI, sans-serif\"", svg, StringComparison.Ordinal);
        Assert.Contains("font-family=\"IBM Plex Sans, Segoe UI, sans-serif\"", svg, StringComparison.Ordinal);
        Assert.Contains("rx=\"30\"", svg, StringComparison.Ordinal);
        Assert.Contains("rx=\"26\"", svg, StringComparison.Ordinal);
        Assert.Contains("rx=\"16\"", svg, StringComparison.Ordinal);
        Assert.Contains("layout:spotlight", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderSvg_UsesInlineImageLayout_WhenInlineImageProvided()
    {
        const string dataUri = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO8VhXQAAAAASUVORK5CYII=";

        var svg = WebSocialCardGenerator.RenderSvg(new WebSocialCardGenerator.SocialCardRenderOptions
        {
            Title = "OfficeIMO docs walkthrough",
            Description = "Generated docs card with inline hero media.",
            Eyebrow = "OfficeIMO",
            Badge = "BLOG",
            FooterLabel = "/blog/officeimo-docs",
            Width = 1200,
            Height = 630,
            StyleKey = "blog",
            VariantKey = "inline-image",
            InlineImageDataUri = dataUri
        });

        Assert.Contains("layout:inline-image", svg, StringComparison.Ordinal);
        Assert.Contains("clip-path=\"url(#mediaClip)\"", svg, StringComparison.Ordinal);
        Assert.Contains(dataUri, svg, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderSvg_RendersLogoMark_WhenLogoProvided()
    {
        const string dataUri = "data:image/svg+xml;base64,PHN2Zy8+";

        var svg = WebSocialCardGenerator.RenderSvg(new WebSocialCardGenerator.SocialCardRenderOptions
        {
            Title = "Contact PowerForge",
            Description = "Get in touch about docs, modules, and releases.",
            Eyebrow = "PowerForge",
            Badge = "CONTACT",
            FooterLabel = "/contact",
            Width = 1200,
            Height = 630,
            StyleKey = "contact",
            VariantKey = "connect",
            LogoDataUri = dataUri
        });

        Assert.Contains("layout:connect", svg, StringComparison.Ordinal);
        Assert.Contains(dataUri, svg, StringComparison.Ordinal);
    }
}
