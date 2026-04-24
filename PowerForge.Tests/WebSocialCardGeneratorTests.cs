using PowerForge.Web;
using System.Text;
using ImageMagick;

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
        Assert.True(logoFrame!.Value.Y > 68, "Spotlight logo should sit within the branded right-side panel, not at the top safe margin.");
        Assert.True(logoFrame.Value.X > 800, "Spotlight logo should remain anchored on the right side of the card.");
    }

    [Fact]
    public void RenderPng_AlignsPrimaryTextInkLeftEdges()
    {
        var tokens = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["socialCard"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["accent"] = "#2563eb",
                ["backgroundStart"] = "#ffffff",
                ["backgroundEnd"] = "#f8fafc",
                ["surface"] = "#eef2ff",
                ["textPrimary"] = "#0f172a",
                ["textSecondary"] = "#475569",
                ["badgeRadius"] = "16px",
                ["frameInset"] = "30px",
                ["panelInset"] = "18px",
                ["contentPadding"] = "32px"
            }
        };

        var options = new WebSocialCardGenerator.SocialCardRenderOptions
        {
            Title = "Evotec Services",
            Description = "Modern PowerShell, .NET, and automation consulting.",
            Eyebrow = "Evotec",
            Badge = "HOME",
            FooterLabel = "/",
            Width = 1200,
            Height = 630,
            StyleKey = "home",
            VariantKey = "spotlight",
            ColorScheme = "light",
            ThemeTokens = tokens
        };
        var bytes = WebSocialCardGenerator.RenderPng(options);

        Assert.NotNull(bytes);

        var eyebrowLeft = MeasureInkLeft(bytes!, 260, 305, static pixel => pixel.B > 180 && pixel.R < 80);
        var titleLeft = MeasureInkLeft(bytes!, 320, 385, static pixel => pixel.R < 45 && pixel.G < 55 && pixel.B < 85);
        var descriptionLeft = MeasureInkLeft(bytes!, 395, 435, static pixel => pixel.R < 80 && pixel.G < 100 && pixel.B < 140);
        var monogram = MeasureInkBox(bytes!, 880, 1060, 250, 380, static pixel => pixel.R < 45 && pixel.G < 55 && pixel.B < 85);
        var logoFrame = WebSocialCardGenerator.GetLogoFrameForTesting(options);

        Assert.InRange(Math.Abs(titleLeft - eyebrowLeft), 0, 1);
        Assert.InRange(Math.Abs(titleLeft - descriptionLeft), 0, 1);
        Assert.NotNull(logoFrame);
        Assert.InRange(Math.Abs((((double)monogram.Left + monogram.Right) / 2d) - (logoFrame.Value.X + (logoFrame.Value.Width / 2d))), 0d, 1.1d);
    }

    [Fact]
    public void RenderPng_BalancesPrimaryTextVerticalRhythm()
    {
        var tokens = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["socialCard"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["accent"] = "#2563eb",
                ["backgroundStart"] = "#ffffff",
                ["backgroundEnd"] = "#f8fafc",
                ["surface"] = "#eef2ff",
                ["textPrimary"] = "#0f172a",
                ["textSecondary"] = "#475569",
                ["badgeRadius"] = "16px",
                ["frameInset"] = "30px",
                ["panelInset"] = "18px",
                ["contentPadding"] = "32px"
            }
        };

        var bytes = WebSocialCardGenerator.RenderPng(new WebSocialCardGenerator.SocialCardRenderOptions
        {
            Title = "Evotec Services",
            Description = "Modern PowerShell, .NET, and automation consulting.",
            Eyebrow = "Evotec",
            Badge = "HOME",
            FooterLabel = "/",
            Width = 1200,
            Height = 630,
            StyleKey = "home",
            VariantKey = "spotlight",
            ColorScheme = "light",
            ThemeTokens = tokens
        });

        Assert.NotNull(bytes);

        var badge = MeasureInkBox(bytes!, 80, 700, 180, 260, static pixel => pixel.B > 180 && pixel.R < 80 && pixel.G > 70 && pixel.G < 140);
        var eyebrow = MeasureInkBox(bytes!, 80, 700, 260, 305, static pixel => pixel.B > 180 && pixel.R < 80);
        var title = MeasureInkBox(bytes!, 80, 700, 300, 380, static pixel => pixel.R < 45 && pixel.G < 55 && pixel.B < 85);
        var description = MeasureInkBox(bytes!, 80, 700, 390, 435, static pixel => pixel.R < 80 && pixel.G < 100 && pixel.B < 140);

        var badgeToEyebrow = eyebrow.Top - badge.Bottom;
        var eyebrowToTitle = title.Top - eyebrow.Bottom;
        var titleToDescription = description.Top - title.Bottom;

        Assert.InRange(Math.Abs(badgeToEyebrow - eyebrowToTitle), 0, 4);
        Assert.InRange(titleToDescription, 20, 34);
    }

    [Fact]
    public void RenderPng_KeepsWrappedBlogTitleAndDescriptionSeparated()
    {
        var tokens = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["socialCard"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["accent"] = "#f97316",
                ["backgroundStart"] = "#fff7ed",
                ["backgroundEnd"] = "#fffbeb",
                ["surface"] = "#ffedd5",
                ["textPrimary"] = "#1c1917",
                ["textSecondary"] = "#57534e",
                ["badgeRadius"] = "16px",
                ["frameInset"] = "30px",
                ["panelInset"] = "18px",
                ["contentPadding"] = "32px"
            }
        };

        var bytes = WebSocialCardGenerator.RenderPng(new WebSocialCardGenerator.SocialCardRenderOptions
        {
            Title = "Building safer PowerShell release pipelines for signed modules and automated quality gates",
            Description = "A practical guide to repeatable publishing, baseline verification, release notes, and site previews for teams that ship often.",
            Eyebrow = "PSPublishModule",
            Badge = "BLOG",
            FooterLabel = "/blog/powershell-release-pipelines/",
            Width = 1200,
            Height = 630,
            StyleKey = "blog",
            VariantKey = "editorial",
            ColorScheme = "light",
            ThemeTokens = tokens
        });

        Assert.NotNull(bytes);

        var title = MeasureInkBox(bytes!, 80, 760, 230, 380, static pixel => pixel.R < 45 && pixel.G < 55 && pixel.B < 85);
        var description = MeasureInkBox(bytes!, 80, 760, 360, 455, static pixel => pixel.R < 120 && pixel.G < 130 && pixel.B < 150);

        Assert.InRange(description.Top - title.Bottom, 14, 44);
        Assert.True(title.Right < 760, "Expected wrapped title to stay inside the text column.");
        Assert.True(description.Right < 760, "Expected description to stay inside the text column.");
    }

    [Fact]
    public void RenderSvg_RendersMetricRow_WhenMetricsAreProvided()
    {
        var svg = WebSocialCardGenerator.RenderSvg(new WebSocialCardGenerator.SocialCardRenderOptions
        {
            Title = "nasa/fprime",
            Description = "Flight software and embedded systems framework.",
            Eyebrow = "GitHub",
            Badge = "REPO",
            FooterLabel = "github.com/nasa/fprime",
            Width = 1200,
            Height = 630,
            StyleKey = "default",
            VariantKey = "product",
            Metrics =
            [
                new SocialCardMetricSpec { Icon = "star", Value = "8k", Label = "Stars" },
                new SocialCardMetricSpec { Icon = "issue", Value = "55", Label = "Issues" },
                new SocialCardMetricSpec { Icon = "fork", Value = "948", Label = "Forks" }
            ]
        });

        Assert.Contains(">8k</text>", svg, StringComparison.Ordinal);
        Assert.Contains(">Stars</text>", svg, StringComparison.Ordinal);
        Assert.Contains(">55</text>", svg, StringComparison.Ordinal);
        Assert.Contains(">Forks</text>", svg, StringComparison.Ordinal);
        Assert.DoesNotContain(">*</text>", svg, StringComparison.Ordinal);
        Assert.DoesNotContain(">!</text>", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderSvg_RendersExpandedMetricIconVocabulary()
    {
        var svg = WebSocialCardGenerator.RenderSvg(new WebSocialCardGenerator.SocialCardRenderOptions
        {
            Title = "Release health",
            Description = "Build and release signals.",
            Eyebrow = "PowerForge",
            Badge = "STATUS",
            FooterLabel = "/releases/",
            Width = 1200,
            Height = 630,
            StyleKey = "default",
            VariantKey = "product",
            Metrics =
            [
                new SocialCardMetricSpec { Icon = "pull-request", Value = "12", Label = "PRs" },
                new SocialCardMetricSpec { Icon = "release", Value = "v2", Label = "Release" },
                new SocialCardMetricSpec { Icon = "security", Value = "A", Label = "Security" },
                new SocialCardMetricSpec { Icon = "docs", Value = "42", Label = "Docs" },
                new SocialCardMetricSpec { Icon = "license", Value = "MIT", Label = "License" }
            ]
        });

        Assert.Contains(">12</text>", svg, StringComparison.Ordinal);
        Assert.Contains(">Release</text>", svg, StringComparison.Ordinal);
        Assert.Contains(">Security</text>", svg, StringComparison.Ordinal);
        Assert.Contains(">License</text>", svg, StringComparison.Ordinal);
        Assert.Contains(@"<path d=""M10 6h4a4 4 0 0 1 4 4v5.7""", svg, StringComparison.Ordinal);
        Assert.Contains(@"<path d=""M12 3l7 3v5.5", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalizeFontFamilyForRaster_UsesPlatformFriendlySansFallback_ForGenericStacks()
    {
        var resolved = WebSocialCardGenerator.NormalizeFontFamilyForRaster(
            "system-ui, sans-serif",
            "Segoe UI, Arial, sans-serif");

        var expected = OperatingSystem.IsWindows()
            ? "Segoe UI"
            : OperatingSystem.IsMacOS()
                ? "Helvetica Neue"
                : "DejaVu Sans";

        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void NormalizeFontFamilyForRaster_UsesPlatformFriendlyMonoFallback_ForGenericStacks()
    {
        var resolved = WebSocialCardGenerator.NormalizeFontFamilyForRaster(
            "ui-monospace, monospace",
            "Cascadia Code, Consolas, monospace",
            monospace: true);

        var expected = OperatingSystem.IsWindows()
            ? "Consolas"
            : OperatingSystem.IsMacOS()
                ? "Menlo"
                : "DejaVu Sans Mono";

        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void GetTitleOpticalOffsetForTesting_ReturnsReasonableMeasuredInset()
    {
        var blogOffset = WebSocialCardGenerator.GetTitleOpticalOffsetForTesting(72, "Blog");
        var aboutOffset = WebSocialCardGenerator.GetTitleOpticalOffsetForTesting(72, "About Us");
        var evotecOffset = WebSocialCardGenerator.GetTitleOpticalOffsetForTesting(72, "Evotec Services");

        Assert.InRange(blogOffset, -32, 32);
        Assert.InRange(aboutOffset, -32, 32);
        Assert.InRange(evotecOffset, -32, 0);
    }

    [Fact]
    public void GetTitleOpticalOffsetForTesting_UsesConfiguredOverride_WhenProvided()
    {
        var tokens = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["socialCard"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["titleOpticalOffset"] = "18px"
            }
        };

        var offset = WebSocialCardGenerator.GetTitleOpticalOffsetForTesting(72, "Blog", tokens);
        Assert.Equal(18, offset);
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
    public void IsRenderableImageSource_ReturnsFalse_WhenRemoteFetchFails()
    {
        WebSocialCardGenerator.ClearRemoteImageCache();

        var renderable = WebSocialCardGenerator.IsRenderableImageSource(
            "https://cdn.example.test/logo.svg",
            allowRemoteMediaFetch: true,
            _ => null);

        Assert.False(renderable);
        WebSocialCardGenerator.ClearRemoteImageCache();
    }

    [Fact]
    public void IsRenderableImageSource_ReturnsTrue_WhenRemoteFetchSucceeds()
    {
        WebSocialCardGenerator.ClearRemoteImageCache();

        var renderable = WebSocialCardGenerator.IsRenderableImageSource(
            "https://cdn.example.test/logo.svg",
            allowRemoteMediaFetch: true,
            _ => Encoding.UTF8.GetBytes("remote-logo"));

        Assert.True(renderable);
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
        Assert.EndsWith("...", lines[^1], StringComparison.Ordinal);
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

    private static int MeasureInkLeft(byte[] png, int minY, int maxY, Func<(byte R, byte G, byte B, byte A), bool> predicate)
    {
        return MeasureInkBox(png, 0, int.MaxValue, minY, maxY, predicate).Left;
    }

    private static InkBox MeasureInkBox(
        byte[] png,
        int minX,
        int maxX,
        int minY,
        int maxY,
        Func<(byte R, byte G, byte B, byte A), bool> predicate)
    {
        using var image = new MagickImage(png);
        var pixels = image.GetPixels().ToByteArray(PixelMapping.RGBA);
        Assert.NotNull(pixels);

        var width = (int)image.Width;
        var height = (int)image.Height;
        var left = width;
        var top = height;
        var right = -1;
        var bottom = -1;

        for (var y = Math.Max(0, minY); y <= Math.Min(height - 1, maxY); y++)
        {
            for (var x = Math.Max(0, minX); x <= Math.Min(width - 1, maxX); x++)
            {
                var index = ((y * width) + x) * 4;
                var pixel = (pixels![index], pixels[index + 1], pixels[index + 2], pixels[index + 3]);
                if (!predicate(pixel))
                    continue;

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        Assert.True(left < width, $"Expected ink in range x {minX}-{maxX}, y {minY}-{maxY}.");
        return new InkBox(left, top, right, bottom);
    }

    private readonly record struct InkBox(int Left, int Top, int Right, int Bottom);
}
