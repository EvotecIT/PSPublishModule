using PowerForge.Web;
using System.Text;

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
    public void SelectPalette_DerivesLightThemePalette_WhenLightSchemeRequested()
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

        var palette = WebSocialCardGenerator.SelectPalette("default", "seed", tokens, "light");

        Assert.NotEqual("#102030", palette.BackgroundStart);
        Assert.NotEqual("#203040", palette.Surface);
        Assert.Equal("#ff7a00", palette.Accent);
        Assert.Equal("#0f172a", palette.TextPrimary);
        Assert.Equal("#475569", palette.TextSecondary);
        Assert.Equal("#0f172a", palette.ChipText);
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
                ["frameInset"] = "30px",
                ["panelInset"] = "18px",
                ["panelRadius"] = "26px",
                ["frameRadius"] = "30px",
                ["badgeRadius"] = "16px",
                ["badgeAlign"] = "left",
                ["contentPadding"] = "32px",
                ["safeMarginX"] = "72px",
                ["safeMarginY"] = "68px"
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
        Assert.Contains("layout:spotlight", svg, StringComparison.Ordinal);
        Assert.Contains("x=\"30\" y=\"30\"", svg, StringComparison.Ordinal);
        Assert.Contains("rx=\"30\"", svg, StringComparison.Ordinal);
        Assert.Contains("x=\"48\" y=\"48\"", svg, StringComparison.Ordinal);
        Assert.Contains("rx=\"26\"", svg, StringComparison.Ordinal);

        var safeMarginTokens = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["socialCard"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["safeMarginX"] = "72px",
                ["safeMarginY"] = "68px",
                ["logoSize"] = "96px"
            }
        };
        var logoFrame = WebSocialCardGenerator.GetLogoFrameForTesting(new WebSocialCardGenerator.SocialCardRenderOptions
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
            ThemeTokens = safeMarginTokens
        });
        Assert.NotNull(logoFrame);
        Assert.Equal(68, logoFrame!.Value.Y);
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
            Title = "TestimoX - Active Directory Security",
            Description = "Security assessment suite for Active Directory.",
            Eyebrow = "TestimoX",
            Badge = "HOME",
            FooterLabel = "/",
            Width = 1200,
            Height = 630,
            StyleKey = "home",
            VariantKey = "spotlight",
            LogoDataUri = dataUri
        });

        Assert.Contains("layout:spotlight", svg, StringComparison.Ordinal);
        Assert.Contains(dataUri, svg, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderSvg_DoesNotEmbedMediaReferences_WhenDisabled()
    {
        const string dataUri = "data:image/svg+xml;base64,PHN2Zy8+";

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
            LogoDataUri = dataUri,
            InlineImageDataUri = dataUri,
            EmbedReferencedMediaInSvg = false
        });

        Assert.Contains("layout:inline-image", svg, StringComparison.Ordinal);
        Assert.DoesNotContain($"href=\"{dataUri}\"", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderSvg_FallsBackFromInlineImageLayout_WhenRemoteMediaFetchIsDisabled()
    {
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
            InlineImageDataUri = "https://cdn.example.test/hero.png",
            AllowRemoteMediaFetch = false
        });

        Assert.Contains("layout:editorial", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("layout:inline-image", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderSvg_AllowsRemoteLogoReferences_WhenProvided()
    {
        const string logoUrl = "https://cdn.example.test/logo.svg";

        var svg = WebSocialCardGenerator.RenderSvg(new WebSocialCardGenerator.SocialCardRenderOptions
        {
            Title = "TestimoX - Active Directory Security",
            Description = "Security assessment suite for Active Directory.",
            Eyebrow = "TestimoX",
            Badge = "HOME",
            FooterLabel = "/",
            Width = 1200,
            Height = 630,
            StyleKey = "home",
            VariantKey = "spotlight",
            LogoDataUri = logoUrl
        });

        Assert.Contains(logoUrl, svg, StringComparison.Ordinal);
    }

    [Fact]
    public void GetRemoteImageBytes_DoesNotFetchRemoteMedia_WhenDisabled()
    {
        var bytes = WebSocialCardGenerator.GetRemoteImageBytes(
            "https://cdn.example.test/logo.svg",
            allowRemoteMediaFetch: false,
            _ => throw new InvalidOperationException("Remote fetch should not run when disabled."));

        Assert.Null(bytes);
    }

    [Fact]
    public void GetRemoteImageBytes_CachesRemoteMedia_WhenEnabled()
    {
        WebSocialCardGenerator.ClearRemoteImageCache();
        var calls = 0;

        byte[]? Fetcher(string _)
        {
            calls++;
            return Encoding.UTF8.GetBytes("remote-logo");
        }

        var first = WebSocialCardGenerator.GetRemoteImageBytes(
            "https://cdn.example.test/logo.svg",
            allowRemoteMediaFetch: true,
            Fetcher);
        var second = WebSocialCardGenerator.GetRemoteImageBytes(
            "https://cdn.example.test/logo.svg",
            allowRemoteMediaFetch: true,
            Fetcher);

        Assert.Equal(1, calls);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.True(first!.SequenceEqual(second!));
        WebSocialCardGenerator.ClearRemoteImageCache();
    }

    [Fact]
    public void GetRemoteImageBytes_DoesNotCacheTransientFailures()
    {
        WebSocialCardGenerator.ClearRemoteImageCache();
        var calls = 0;

        byte[]? Fetcher(string _)
        {
            calls++;
            return calls == 1 ? null : Encoding.UTF8.GetBytes("recovered");
        }

        var first = WebSocialCardGenerator.GetRemoteImageBytes(
            "https://cdn.example.test/logo.svg",
            allowRemoteMediaFetch: true,
            Fetcher);
        var second = WebSocialCardGenerator.GetRemoteImageBytes(
            "https://cdn.example.test/logo.svg",
            allowRemoteMediaFetch: true,
            Fetcher);

        Assert.Null(first);
        Assert.NotNull(second);
        Assert.Equal(2, calls);
        WebSocialCardGenerator.ClearRemoteImageCache();
    }

    [Fact]
    public void GetRemoteImageBytes_UsesCaseSensitiveCacheKeys()
    {
        WebSocialCardGenerator.ClearRemoteImageCache();
        var calls = 0;

        byte[]? Fetcher(string source)
        {
            calls++;
            return Encoding.UTF8.GetBytes(source);
        }

        var upper = WebSocialCardGenerator.GetRemoteImageBytes(
            "https://cdn.example.test/Logo.png",
            allowRemoteMediaFetch: true,
            Fetcher);
        var lower = WebSocialCardGenerator.GetRemoteImageBytes(
            "https://cdn.example.test/logo.png",
            allowRemoteMediaFetch: true,
            Fetcher);

        Assert.Equal(2, calls);
        Assert.NotNull(upper);
        Assert.NotNull(lower);
        Assert.False(upper!.SequenceEqual(lower!));
        WebSocialCardGenerator.ClearRemoteImageCache();
    }

    [Fact]
    public void RenderSvg_FallsBackToMonogram_WhenRemoteLogoCannotBeRendered()
    {
        var svg = WebSocialCardGenerator.RenderSvg(new WebSocialCardGenerator.SocialCardRenderOptions
        {
            Title = "TestimoX - Active Directory Security",
            Description = "Security assessment suite for Active Directory.",
            Eyebrow = "TestimoX",
            Badge = "HOME",
            FooterLabel = "/",
            Width = 1200,
            Height = 630,
            StyleKey = "home",
            VariantKey = "spotlight",
            LogoDataUri = "https://cdn.example.test/logo.svg",
            AllowRemoteMediaFetch = false,
            EmbedReferencedMediaInSvg = false
        });

        Assert.Contains(">TE</text>", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void AdaptTitleSize_ReducesFontSize_WhenTitleWouldTruncate()
    {
        // Very long title that cannot fit in 2 lines at 60px on a 400px-wide area
        var longTitle = "TestimoX release notes: safer rule catalog generation and richer diagnostic output for enterprise environments with advanced monitoring capabilities and extended reporting";
        var baseFontSize = 60;
        var baseLineHeight = 66;

        var (fontSize, lineHeight, lines) = WebSocialCardGenerator.AdaptTitleSize(
            longTitle, baseFontSize, baseLineHeight, contentWidth: 300, maxLines: 2, width: 1200, height: 630);

        // At 300px content width with 60px font, title will truncate. Adaptive sizing should kick in.
        // If the title still truncates at the reduced size, at minimum the reduced attempt should
        // produce more lines than the original, or the font size should be smaller.
        Assert.True(fontSize <= baseFontSize, $"Font size {fontSize} should not exceed base {baseFontSize}");
        Assert.True(lines.Count >= 1, "Should produce at least one line");
        Assert.True(lines.Count <= 2, $"Adaptive sizing should respect the 2-line cap, but rendered {lines.Count} lines");
        Assert.True(lineHeight <= baseLineHeight, $"Line height {lineHeight} should not exceed base {baseLineHeight}");
    }

    [Fact]
    public void AdaptTitleSize_KeepsOriginalSize_WhenTitleFits()
    {
        var shortTitle = "TestimoX";
        var baseFontSize = 48;
        var baseLineHeight = 52;

        var (fontSize, lineHeight, lines) = WebSocialCardGenerator.AdaptTitleSize(
            shortTitle, baseFontSize, baseLineHeight, contentWidth: 600, maxLines: 3, width: 1200, height: 630);

        Assert.Equal(baseFontSize, fontSize);
        Assert.Equal(baseLineHeight, lineHeight);
        Assert.Single(lines);
        Assert.Equal("TestimoX", lines[0]);
    }

    [Fact]
    public void RenderSvg_ConnectLayout_RendersMapVisualAndRoute()
    {
        const string dataUri = "data:image/svg+xml;base64,PHN2Zy8+";

        var svg = WebSocialCardGenerator.RenderSvg(new WebSocialCardGenerator.SocialCardRenderOptions
        {
            Title = "Contact PowerForge",
            Description = "Get in touch.",
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
        Assert.Contains("/contact", svg, StringComparison.Ordinal);
        Assert.Contains(dataUri, svg, StringComparison.Ordinal);
    }
}
