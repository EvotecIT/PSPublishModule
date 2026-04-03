using System.Net.Http;
using System.Collections.Concurrent;
using System.Threading;
using System.Text;
using ImageMagick;

namespace PowerForge.Web;

internal static partial class WebSocialCardGenerator
{
    private static readonly HttpClient SocialImageHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };
    private static readonly ConcurrentDictionary<string, Lazy<byte[]>> RemoteImageByteCache = new(StringComparer.OrdinalIgnoreCase);

    internal static byte[]? RenderPng(SocialCardRenderOptions options)
    {
        var rasterOptions = CloneRenderOptions(options, embedReferencedMediaInSvg: false);
        var state = CreateState(rasterOptions);
        var svg = RenderSvg(rasterOptions);
        if (string.IsNullOrWhiteSpace(svg))
            return null;

        try
        {
            var settings = new MagickReadSettings
            {
                Width = (uint)Math.Clamp(options.Width, 600, 2400),
                Height = (uint)Math.Clamp(options.Height, 315, 1400),
                Format = MagickFormat.Svg
            };

            using var image = new MagickImage(Encoding.UTF8.GetBytes(svg), settings);
            CompositeMedia(image, state);
            image.Format = MagickFormat.Png;
            image.Strip();
            using var stream = new MemoryStream();
            image.Write(stream);
            return stream.ToArray();
        }
        catch
        {
            return null;
        }
    }

    internal static string RenderSvg(SocialCardRenderOptions options)
    {
        var state = CreateState(options);
        var svg = new StringBuilder();
        svg.AppendLine($@"<svg xmlns=""http://www.w3.org/2000/svg"" xmlns:xlink=""http://www.w3.org/1999/xlink"" width=""{state.Width}"" height=""{state.Height}"" viewBox=""0 0 {state.Width} {state.Height}"">");
        svg.AppendLine($@"  <!-- layout:{state.LayoutKey} style:{state.StyleKey} variant:{state.VariantKey} -->");
        svg.AppendLine(@"  <defs>");
        AppendDefs(svg, state);
        if (string.Equals(state.LayoutKey, "inline-image", StringComparison.OrdinalIgnoreCase))
        {
            var media = GetInlineMediaFrame(state);
            svg.AppendLine(@"    <clipPath id=""mediaClip"">");
            svg.AppendLine($@"      <rect x=""{media.X}"" y=""{media.Y}"" width=""{media.Width}"" height=""{media.Height}"" rx=""{media.Radius}"" ry=""{media.Radius}""/>");
            svg.AppendLine(@"    </clipPath>");
        }
        svg.AppendLine(@"  </defs>");
        AppendBackground(svg, state);

        switch (state.LayoutKey)
        {
            case "spotlight":
                AppendSpotlightLayout(svg, state);
                break;
            case "shelf":
                AppendShelfLayout(svg, state);
                break;
            case "reference":
                AppendReferenceLayout(svg, state);
                break;
            case "inline-image":
                AppendInlineImageLayout(svg, state);
                break;
            case "connect":
                AppendConnectLayout(svg, state);
                break;
            default:
                AppendEditorialOrProductLayout(svg, state);
                break;
        }

        svg.AppendLine(@"</svg>");
        return svg.ToString();
    }

    private static SocialCardRenderState CreateState(SocialCardRenderOptions options)
    {
        var width = Math.Clamp(options.Width, 600, 2400);
        var height = Math.Clamp(options.Height, 315, 1400);
        var title = string.IsNullOrWhiteSpace(options.Title) ? "PowerForge Web" : options.Title!;
        var description = string.IsNullOrWhiteSpace(options.Description)
            ? "Static content with docs, API references, editorial streams, and project landing pages."
            : options.Description!;
        var eyebrow = string.IsNullOrWhiteSpace(options.Eyebrow) ? "PowerForge" : options.Eyebrow!;
        var badge = string.IsNullOrWhiteSpace(options.Badge) ? "PAGE" : options.Badge!;
        var footerLabel = string.IsNullOrWhiteSpace(options.FooterLabel) ? "/" : options.FooterLabel!;
        var styleKey = NormalizeStyle(options.StyleKey) ?? ClassifyStyle(badge, footerLabel);
        var variantKey = NormalizeVariant(options.VariantKey) ?? ClassifyVariant(styleKey, badge, footerLabel);
        var normalizedBadge = NormalizeBadgeLabel(badge, footerLabel, styleKey, variantKey);
        var normalizedFooter = NormalizeFooterLabel(footerLabel, normalizedBadge, styleKey);
        var hasRenderableInlineImage = IsRenderableImageSource(options.InlineImageDataUri, options.AllowRemoteMediaFetch);
        var layoutKey = ResolveLayoutKey(styleKey, variantKey, hasRenderableInlineImage);
        var frameInset = ResolveTokenPixels(options.ThemeTokens, width, height, 0, 0, "socialCard", "frameInset");
        var panelInset = ResolveTokenPixels(options.ThemeTokens, width, height, 0, 0, "socialCard", "panelInset");
        var contentPadding = ResolveTokenPixels(options.ThemeTokens, width, height, 28, 18, "socialCard", "contentPadding");
        var safeMarginX = Math.Max(
            frameInset + panelInset + contentPadding,
            ResolveTokenPixels(options.ThemeTokens, width, height, 72, 40, "socialCard", "safeMarginX"));
        var safeMarginY = Math.Max(
            frameInset + panelInset + contentPadding,
            ResolveTokenPixels(options.ThemeTokens, width, height, 72, 40, "socialCard", "safeMarginY"));

        return new SocialCardRenderState
        {
            Width = width,
            Height = height,
            Title = title,
            Description = description,
            Eyebrow = eyebrow,
            Badge = normalizedBadge,
            FooterLabel = normalizedFooter,
            StyleKey = styleKey,
            VariantKey = variantKey,
            LayoutKey = layoutKey,
            Palette = SelectPalette(styleKey, string.Join("|", styleKey, variantKey, title, description, eyebrow, normalizedBadge, normalizedFooter, width, height), options.ThemeTokens, options.ColorScheme),
            Typography = ResolveTypography(options.ThemeTokens),
            ThemeTokens = options.ThemeTokens,
            LogoDataUri = options.LogoDataUri,
            InlineImageDataUri = options.InlineImageDataUri,
            AllowRemoteMediaFetch = options.AllowRemoteMediaFetch,
            EmbedReferencedMediaInSvg = options.EmbedReferencedMediaInSvg,
            CtaLabel = ResolveCtaLabel(styleKey, normalizedBadge),
            FrameInset = frameInset,
            PanelInset = panelInset,
            ContentPadding = contentPadding,
            FrameRadius = ResolveRadiusPixels(options.ThemeTokens, width, height, 24, 0, "frameRadius"),
            PanelRadius = ResolveRadiusPixels(options.ThemeTokens, width, height, 16, 8, "panelRadius"),
            SafeMarginX = safeMarginX,
            SafeMarginY = safeMarginY
        };
    }

    private static string ResolveLayoutKey(string styleKey, string variantKey, bool hasInlineImage)
    {
        if (string.Equals(variantKey, "spotlight", StringComparison.OrdinalIgnoreCase))
            return "spotlight";
        if (string.Equals(variantKey, "shelf", StringComparison.OrdinalIgnoreCase))
            return "shelf";
        if (string.Equals(variantKey, "reference", StringComparison.OrdinalIgnoreCase))
            return "reference";
        if (string.Equals(variantKey, "connect", StringComparison.OrdinalIgnoreCase))
            return "connect";
        if (string.Equals(variantKey, "inline-image", StringComparison.OrdinalIgnoreCase))
            return hasInlineImage ? "inline-image" : "editorial";

        return styleKey switch
        {
            "home" => "spotlight",
            "docs" => "shelf",
            "api" => "reference",
            "contact" => "connect",
            "blog" when hasInlineImage => "inline-image",
            "blog" => "editorial",
            _ => "product"
        };
    }

    internal static bool IsRenderableImageSource(string? source, bool allowRemoteMediaFetch)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;

        if (source.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            return true;

        if (IsRemoteMediaSource(source))
            return allowRemoteMediaFetch;

        if (Uri.TryCreate(source, UriKind.Absolute, out var absoluteUri) && absoluteUri.IsFile)
            return File.Exists(absoluteUri.LocalPath);

        return File.Exists(source);
    }

    private static string ResolveCtaLabel(string styleKey, string badge)
    {
        if (string.Equals(styleKey, "contact", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(badge, "CONTACT", StringComparison.OrdinalIgnoreCase))
            return "Get in Touch";
        if (string.Equals(styleKey, "api", StringComparison.OrdinalIgnoreCase))
            return "Explore API";
        if (string.Equals(styleKey, "docs", StringComparison.OrdinalIgnoreCase))
            return "Read Docs";
        return "Learn More";
    }

    private static SocialCardRenderOptions CloneRenderOptions(SocialCardRenderOptions options, bool embedReferencedMediaInSvg)
    {
        return new SocialCardRenderOptions
        {
            Title = options.Title,
            Description = options.Description,
            Eyebrow = options.Eyebrow,
            Badge = options.Badge,
            FooterLabel = options.FooterLabel,
            Width = options.Width,
            Height = options.Height,
            StyleKey = options.StyleKey,
            VariantKey = options.VariantKey,
            ThemeTokens = options.ThemeTokens,
            LogoDataUri = options.LogoDataUri,
            InlineImageDataUri = options.InlineImageDataUri,
            ColorScheme = options.ColorScheme,
            AllowRemoteMediaFetch = options.AllowRemoteMediaFetch,
            EmbedReferencedMediaInSvg = embedReferencedMediaInSvg
        };
    }

    // ── Defs & Background ─────────────────────────────────────────────

    private static void AppendDefs(StringBuilder svg, SocialCardRenderState state)
    {
        svg.AppendLine(@"    <linearGradient id=""bg"" x1=""0%"" y1=""0%"" x2=""100%"" y2=""100%"">");
        svg.AppendLine($@"      <stop offset=""0%"" stop-color=""{state.Palette.BackgroundStart}""/>");
        svg.AppendLine($@"      <stop offset=""100%"" stop-color=""{state.Palette.BackgroundEnd}""/>");
        svg.AppendLine(@"    </linearGradient>");
    }

    private static void AppendBackground(StringBuilder svg, SocialCardRenderState state)
    {
        var accentBarHeight = GetScaledPixels(state.Width, state.Height, 6, 4);
        svg.AppendLine($@"  <rect width=""{state.Width}"" height=""{state.Height}"" fill=""url(#bg)""/>");

        if (state.FrameInset > 0)
        {
            var frameWidth = Math.Max(1, state.Width - (state.FrameInset * 2));
            var frameHeight = Math.Max(1, state.Height - (state.FrameInset * 2));
            svg.AppendLine($@"  <rect x=""{state.FrameInset}"" y=""{state.FrameInset}"" width=""{frameWidth}"" height=""{frameHeight}"" rx=""{state.FrameRadius}"" fill=""{state.Palette.Surface}"" fill-opacity=""0.06"" stroke=""{state.Palette.SurfaceStroke}"" stroke-opacity=""0.16""/>");
        }

        if (state.FrameInset > 0 || state.PanelInset > 0)
        {
            var panel = GetPanelRect(state);
            svg.AppendLine($@"  <rect x=""{panel.X}"" y=""{panel.Y}"" width=""{panel.Width}"" height=""{panel.Height}"" rx=""{panel.Radius}"" fill=""{state.Palette.Surface}"" fill-opacity=""0.12"" stroke=""{state.Palette.SurfaceStroke}"" stroke-opacity=""0.12""/>");
        }

        // Bottom accent bar — full width, solid accent color
        svg.AppendLine($@"  <rect x=""0"" y=""{state.Height - accentBarHeight}"" width=""{state.Width}"" height=""{accentBarHeight}"" fill=""{state.Palette.Accent}""/>");
    }

    // ── Shared layout helpers ──────────────────────────────────────────

    private static void AppendBadge(StringBuilder svg, SocialCardRenderState state, int x, int y)
    {
        var badgeText = state.Badge.ToUpperInvariant();
        var fontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 15, 11, "socialCard", "badgeFontSize");
        var height = GetScaledPixels(state.Width, state.Height, 32, 22);
        var paddingX = GetScaledPixels(state.Width, state.Height, 16, 10);
        var textWidth = EstimateTextWidth(badgeText, fontSize, glyphFactor: 0.62);
        var width = textWidth + (paddingX * 2);
        var radius = GetScaledPixels(state.Width, state.Height, 4, 3);
        svg.AppendLine($@"  <rect x=""{x}"" y=""{y}"" width=""{width}"" height=""{height}"" rx=""{radius}"" fill=""{state.Palette.Accent}""/>");
        svg.AppendLine($@"  <text x=""{x + (width / 2)}"" y=""{y + (height / 2)}"" fill=""{state.Palette.BackgroundStart}"" font-size=""{fontSize}"" font-family=""{EscapeXml(state.Typography.BadgeFontFamily)}"" font-weight=""800"" letter-spacing=""0.8"" dominant-baseline=""central"" text-anchor=""middle"">{EscapeXml(badgeText)}</text>");
    }

    private static void AppendEyebrowText(StringBuilder svg, SocialCardRenderState state, int x, int y)
    {
        var fontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 22, 14, "socialCard", "eyebrowFontSize");
        svg.AppendLine($@"  <text x=""{x}"" y=""{y}"" fill=""{state.Palette.Accent}"" font-size=""{fontSize}"" font-family=""{EscapeXml(state.Typography.EyebrowFontFamily)}"" font-weight=""700"">{EscapeXml(TrimSingleLine(state.Eyebrow, 56))}</text>");
    }

    private static void AppendTitle(StringBuilder svg, SocialCardRenderState state, IReadOnlyList<string> lines, int x, int y, int fontSize, int lineHeight)
    {
        for (var i = 0; i < lines.Count; i++)
            svg.AppendLine($@"  <text x=""{x}"" y=""{y + (i * lineHeight)}"" fill=""{state.Palette.TextPrimary}"" font-size=""{fontSize}"" font-family=""{EscapeXml(state.Typography.TitleFontFamily)}"" font-weight=""800"">{EscapeXml(lines[i])}</text>");
    }

    private static void AppendDescription(StringBuilder svg, SocialCardRenderState state, IReadOnlyList<string> lines, int x, int y, int fontSize, int lineHeight)
    {
        for (var i = 0; i < lines.Count; i++)
            svg.AppendLine($@"  <text x=""{x}"" y=""{y + (i * lineHeight)}"" fill=""{state.Palette.TextSecondary}"" font-size=""{fontSize}"" font-family=""{EscapeXml(state.Typography.BodyFontFamily)}"" font-weight=""500"">{EscapeXml(lines[i])}</text>");
    }

    private static void AppendFooterRoute(StringBuilder svg, SocialCardRenderState state, int x, int y)
    {
        var label = TrimSingleLine(state.FooterLabel, 64).Trim();
        if (string.IsNullOrWhiteSpace(label) || label == "/")
            return;
        var fontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 16, 11, "socialCard", "footerFontSize");
        svg.AppendLine($@"  <text x=""{x}"" y=""{y}"" fill=""{state.Palette.TextSecondary}"" fill-opacity=""0.6"" font-size=""{fontSize}"" font-family=""{EscapeXml(state.Typography.FooterFontFamily)}"" font-weight=""500"">{EscapeXml(label)}</text>");
    }

    private static void AppendLogo(StringBuilder svg, SocialCardRenderState state, int x, int y, int size)
    {
        var radius = GetScaledPixels(state.Width, state.Height, 20, 10);
        svg.AppendLine($@"  <rect x=""{x}"" y=""{y}"" width=""{size}"" height=""{size}"" rx=""{radius}"" fill=""{state.Palette.Surface}""/>");
        if (!string.IsNullOrWhiteSpace(state.LogoDataUri) && state.EmbedReferencedMediaInSvg)
        {
            var inset = Math.Max(6, size / 8);
            svg.AppendLine($@"  <image href=""{EscapeXml(state.LogoDataUri)}"" xlink:href=""{EscapeXml(state.LogoDataUri)}"" x=""{x + inset}"" y=""{y + inset}"" width=""{Math.Max(12, size - (inset * 2))}"" height=""{Math.Max(12, size - (inset * 2))}"" preserveAspectRatio=""xMidYMid meet""/>");
            return;
        }

        if (!string.IsNullOrWhiteSpace(state.LogoDataUri))
            return;

        var monogram = BuildMonogram(state.Eyebrow, state.Badge);
        svg.AppendLine($@"  <text x=""{x + (size / 2)}"" y=""{y + (size / 2)}"" fill=""{state.Palette.TextPrimary}"" font-size=""{Math.Max(18, size * 2 / 5)}"" font-family=""{EscapeXml(state.Typography.TitleFontFamily)}"" font-weight=""800"" dominant-baseline=""central"" text-anchor=""middle"">{EscapeXml(monogram)}</text>");
    }

    private static void AppendSeparator(StringBuilder svg, SocialCardRenderState state, int x, int y, int width)
    {
        svg.AppendLine($@"  <rect x=""{x}"" y=""{y}"" width=""{width}"" height=""1"" fill=""{state.Palette.SurfaceStroke}"" fill-opacity=""0.3""/>");
    }

    // ── Layout: Spotlight (Home) ───────────────────────────────────────

    private static void AppendSpotlightLayout(StringBuilder svg, SocialCardRenderState state)
    {
        var pad = state.SafeMarginX;
        var contentWidth = Math.Max(320, (int)Math.Round(state.Width * 0.58));
        var x = pad;

        var baseTitleFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 62, 32, "socialCard", "titleFontSize");
        var baseTitleLineHeight = GetScaledPixels(state.Width, state.Height, 68, 36);
        var descFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 22, 14, "socialCard", "descriptionFontSize");
        var descLineHeight = GetScaledPixels(state.Width, state.Height, 30, 19);

        // Badge
        var badgeY = pad;
        AppendBadge(svg, state, x, badgeY);
        var badgeHeight = GetScaledPixels(state.Width, state.Height, 32, 22);

        // Eyebrow
        var eyebrowY = badgeY + badgeHeight + GetScaledPixels(state.Width, state.Height, 40, 24);
        AppendEyebrowText(svg, state, x, eyebrowY);

        // Title
        var titleY = eyebrowY + GetScaledPixels(state.Width, state.Height, 50, 30);
        var (titleFontSize, titleLineHeight, titleLines) = AdaptTitleSize(state.Title, baseTitleFontSize, baseTitleLineHeight, contentWidth, 3, state.Width, state.Height);
        AppendTitle(svg, state, titleLines, x, titleY, titleFontSize, titleLineHeight);

        // Description
        var descY = titleY + (titleLines.Count * titleLineHeight) + GetScaledPixels(state.Width, state.Height, 28, 16);
        var descLines = WrapText(state.Description, GetDescriptionWrapWidth(contentWidth, descFontSize), 3);
        AppendDescription(svg, state, descLines, x, descY, descFontSize, descLineHeight);

        // Footer route
        var footerY = state.Height - pad - GetScaledPixels(state.Width, state.Height, 6, 4);
        AppendFooterRoute(svg, state, x, footerY);

        // Logo on right
        var logoSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 160, 90, "socialCard", "logoSize");
        var logoX = state.Width - pad - logoSize;
        var logoY = pad;
        AppendLogo(svg, state, logoX, logoY, logoSize);
    }

    // ── Layout: Shelf (Docs) ───────────────────────────────────────────

    private static void AppendShelfLayout(StringBuilder svg, SocialCardRenderState state)
    {
        var pad = state.SafeMarginX;
        var x = pad;
        var contentWidth = state.Width - (pad * 2);

        var baseTitleFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 48, 26, "socialCard", "titleFontSize");
        var baseTitleLineHeight = GetScaledPixels(state.Width, state.Height, 54, 30);
        var descFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 20, 14, "socialCard", "descriptionFontSize");
        var descLineHeight = GetScaledPixels(state.Width, state.Height, 28, 18);

        // Badge
        var badgeY = pad;
        AppendBadge(svg, state, x, badgeY);
        var badgeHeight = GetScaledPixels(state.Width, state.Height, 32, 22);

        // Route label on the right
        var routeLabel = TrimSingleLine(state.FooterLabel, 40).Trim();
        if (!string.IsNullOrWhiteSpace(routeLabel) && routeLabel != "/")
        {
            var routeFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 16, 11, "socialCard", "footerFontSize");
            svg.AppendLine($@"  <text x=""{state.Width - pad}"" y=""{badgeY + (badgeHeight / 2)}"" fill=""{state.Palette.TextSecondary}"" fill-opacity=""0.5"" font-size=""{routeFontSize}"" font-family=""{EscapeXml(state.Typography.FooterFontFamily)}"" font-weight=""500"" dominant-baseline=""central"" text-anchor=""end"">{EscapeXml(routeLabel)}</text>");
        }

        // Eyebrow
        var eyebrowY = badgeY + badgeHeight + GetScaledPixels(state.Width, state.Height, 40, 24);
        AppendEyebrowText(svg, state, x, eyebrowY);

        // Title
        var titleY = eyebrowY + GetScaledPixels(state.Width, state.Height, 50, 30);
        var (titleFontSize, titleLineHeight, titleLines) = AdaptTitleSize(state.Title, baseTitleFontSize, baseTitleLineHeight, contentWidth, 3, state.Width, state.Height);
        AppendTitle(svg, state, titleLines, x, titleY, titleFontSize, titleLineHeight);

        // Description
        var descY = titleY + (titleLines.Count * titleLineHeight) + GetScaledPixels(state.Width, state.Height, 24, 14);
        var descLines = WrapText(state.Description, GetDescriptionWrapWidth(contentWidth, descFontSize), 3);
        AppendDescription(svg, state, descLines, x, descY, descFontSize, descLineHeight);
    }

    // ── Layout: Reference (API) ────────────────────────────────────────

    private static void AppendReferenceLayout(StringBuilder svg, SocialCardRenderState state)
    {
        var pad = state.SafeMarginX;
        var codeWidth = Math.Max(GetScaledPixels(state.Width, state.Height, 340, 220), (int)Math.Round(state.Width * 0.35));
        var codeX = state.Width - pad - codeWidth;
        var gapX = GetScaledPixels(state.Width, state.Height, 32, 18);
        var x = pad;
        var contentWidth = Math.Max(240, codeX - x - gapX);

        var baseTitleFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 44, 26, "socialCard", "titleFontSize");
        var baseTitleLineHeight = GetScaledPixels(state.Width, state.Height, 50, 28);
        var descFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 20, 14, "socialCard", "descriptionFontSize");
        var descLineHeight = GetScaledPixels(state.Width, state.Height, 27, 18);

        // Badge
        var badgeY = pad;
        AppendBadge(svg, state, x, badgeY);
        var badgeHeight = GetScaledPixels(state.Width, state.Height, 32, 22);

        // Eyebrow
        var eyebrowY = badgeY + badgeHeight + GetScaledPixels(state.Width, state.Height, 40, 24);
        AppendEyebrowText(svg, state, x, eyebrowY);

        // Title
        var titleY = eyebrowY + GetScaledPixels(state.Width, state.Height, 50, 30);
        var (titleFontSize, titleLineHeight, titleLines) = AdaptTitleSize(state.Title, baseTitleFontSize, baseTitleLineHeight, contentWidth, 3, state.Width, state.Height);
        AppendTitle(svg, state, titleLines, x, titleY, titleFontSize, titleLineHeight);

        // Description
        var descY = titleY + (titleLines.Count * titleLineHeight) + GetScaledPixels(state.Width, state.Height, 24, 14);
        var descLines = WrapText(state.Description, GetDescriptionWrapWidth(contentWidth, descFontSize), 3);
        AppendDescription(svg, state, descLines, x, descY, descFontSize, descLineHeight);

        // Footer route
        var footerY = state.Height - pad - GetScaledPixels(state.Width, state.Height, 6, 4);
        AppendFooterRoute(svg, state, x, footerY);

        // Code pane on right
        AppendCodePane(svg, state, codeX, pad, codeWidth, state.Height - (pad * 2) - GetScaledPixels(state.Width, state.Height, 6, 4));
    }

    // ── Layout: Inline Image (Blog with image) ────────────────────────

    private static void AppendInlineImageLayout(StringBuilder svg, SocialCardRenderState state)
    {
        var pad = state.SafeMarginX;
        var media = GetInlineMediaFrame(state);
        var gapX = GetScaledPixels(state.Width, state.Height, 28, 16);
        var x = pad;
        var contentWidth = Math.Max(240, media.X - x - gapX);

        var baseTitleFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 44, 26, "socialCard", "titleFontSize");
        var baseTitleLineHeight = GetScaledPixels(state.Width, state.Height, 50, 28);
        var descFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 20, 14, "socialCard", "descriptionFontSize");
        var descLineHeight = GetScaledPixels(state.Width, state.Height, 27, 18);

        // Badge
        var badgeY = pad;
        AppendBadge(svg, state, x, badgeY);
        var badgeHeight = GetScaledPixels(state.Width, state.Height, 32, 22);

        // Eyebrow
        var eyebrowY = badgeY + badgeHeight + GetScaledPixels(state.Width, state.Height, 40, 24);
        AppendEyebrowText(svg, state, x, eyebrowY);

        // Title
        var titleY = eyebrowY + GetScaledPixels(state.Width, state.Height, 50, 30);
        var (titleFontSize, titleLineHeight, titleLines) = AdaptTitleSize(state.Title, baseTitleFontSize, baseTitleLineHeight, contentWidth, 3, state.Width, state.Height);
        AppendTitle(svg, state, titleLines, x, titleY, titleFontSize, titleLineHeight);

        // Description
        var descY = titleY + (titleLines.Count * titleLineHeight) + GetScaledPixels(state.Width, state.Height, 24, 14);
        var descLines = WrapText(state.Description, GetDescriptionWrapWidth(contentWidth, descFontSize), 4);
        AppendDescription(svg, state, descLines, x, descY, descFontSize, descLineHeight);

        // Footer route
        var footerY = state.Height - pad - GetScaledPixels(state.Width, state.Height, 6, 4);
        AppendFooterRoute(svg, state, x, footerY);

        // Inline image on right
        var imgRadius = GetScaledPixels(state.Width, state.Height, 12, 6);
        svg.AppendLine($@"  <rect x=""{media.X}"" y=""{media.Y}"" width=""{media.Width}"" height=""{media.Height}"" rx=""{imgRadius}"" fill=""{state.Palette.Surface}""/>");
        if (state.EmbedReferencedMediaInSvg)
            svg.AppendLine($@"  <image href=""{EscapeXml(state.InlineImageDataUri ?? string.Empty)}"" xlink:href=""{EscapeXml(state.InlineImageDataUri ?? string.Empty)}"" x=""{media.X}"" y=""{media.Y}"" width=""{media.Width}"" height=""{media.Height}"" preserveAspectRatio=""xMidYMid slice"" clip-path=""url(#mediaClip)""/>");
    }

    // ── Layout: Connect (Contact) ──────────────────────────────────────

    private static void AppendConnectLayout(StringBuilder svg, SocialCardRenderState state)
    {
        var pad = state.SafeMarginX;
        var mapWidth = Math.Max(GetScaledPixels(state.Width, state.Height, 380, 220), (int)Math.Round(state.Width * 0.38));
        var mapX = state.Width - mapWidth;
        var gapX = GetScaledPixels(state.Width, state.Height, 28, 16);
        var x = pad;
        var contentWidth = Math.Max(240, mapX - x - gapX);

        var baseTitleFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 48, 28, "socialCard", "titleFontSize");
        var baseTitleLineHeight = GetScaledPixels(state.Width, state.Height, 54, 30);
        var descFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 20, 14, "socialCard", "descriptionFontSize");
        var descLineHeight = GetScaledPixels(state.Width, state.Height, 27, 18);

        // Stylized map on the right side (behind content)
        AppendMapVisual(svg, state, mapX, 0, mapWidth, state.Height);

        // Badge
        var badgeY = pad;
        AppendBadge(svg, state, x, badgeY);
        var badgeHeight = GetScaledPixels(state.Width, state.Height, 32, 22);

        // Eyebrow
        var eyebrowY = badgeY + badgeHeight + GetScaledPixels(state.Width, state.Height, 40, 24);
        AppendEyebrowText(svg, state, x, eyebrowY);

        // Title
        var titleY = eyebrowY + GetScaledPixels(state.Width, state.Height, 50, 30);
        var (titleFontSize, titleLineHeight, titleLines) = AdaptTitleSize(state.Title, baseTitleFontSize, baseTitleLineHeight, contentWidth, 3, state.Width, state.Height);
        AppendTitle(svg, state, titleLines, x, titleY, titleFontSize, titleLineHeight);

        // Description
        var descY = titleY + (titleLines.Count * titleLineHeight) + GetScaledPixels(state.Width, state.Height, 24, 14);
        var descLines = WrapText(state.Description, GetDescriptionWrapWidth(contentWidth, descFontSize), 4);
        AppendDescription(svg, state, descLines, x, descY, descFontSize, descLineHeight);

        // Footer route
        var footerY = state.Height - pad - GetScaledPixels(state.Width, state.Height, 6, 4);
        AppendFooterRoute(svg, state, x, footerY);

        if (!string.IsNullOrWhiteSpace(state.LogoDataUri))
        {
            var frame = ResolveLogoFrame(state);
            if (frame is not null)
                AppendLogo(svg, state, frame.X, frame.Y, frame.Width);
        }
    }

    private static void AppendMapVisual(StringBuilder svg, SocialCardRenderState state, int x, int y, int width, int height)
    {
        // Subtle grid pattern resembling a map
        var gridSpacing = GetScaledPixels(state.Width, state.Height, 48, 30);
        var lineOpacity = "0.06";
        var accentLineOpacity = "0.12";

        // Vertical grid lines
        for (var gx = x + gridSpacing; gx < x + width; gx += gridSpacing)
            svg.AppendLine($@"  <rect x=""{gx}"" y=""{y}"" width=""1"" height=""{height}"" fill=""{state.Palette.Accent}"" fill-opacity=""{lineOpacity}""/>");

        // Horizontal grid lines
        for (var gy = y + gridSpacing; gy < y + height; gy += gridSpacing)
            svg.AppendLine($@"  <rect x=""{x}"" y=""{gy}"" width=""{width}"" height=""1"" fill=""{state.Palette.Accent}"" fill-opacity=""{lineOpacity}""/>");

        // Accent "roads" - a few thicker diagonal/crossing lines
        var roadWidth = GetScaledPixels(state.Width, state.Height, 3, 2);
        var cx = x + (width / 2);
        var cy = y + (height / 2);
        svg.AppendLine($@"  <rect x=""{x}"" y=""{cy - (roadWidth / 2)}"" width=""{width}"" height=""{roadWidth}"" fill=""{state.Palette.Accent}"" fill-opacity=""{accentLineOpacity}""/>");
        svg.AppendLine($@"  <rect x=""{cx - (roadWidth / 2)}"" y=""{y}"" width=""{roadWidth}"" height=""{height}"" fill=""{state.Palette.Accent}"" fill-opacity=""{accentLineOpacity}""/>");
        // Diagonal
        svg.AppendLine($@"  <line x1=""{x}"" y1=""{y + height}"" x2=""{x + width}"" y2=""{y}"" stroke=""{state.Palette.Accent}"" stroke-opacity=""{accentLineOpacity}"" stroke-width=""{roadWidth}""/>");

        // Location pin
        var pinSize = GetScaledPixels(state.Width, state.Height, 36, 22);
        var pinX = cx + GetScaledPixels(state.Width, state.Height, 20, 12);
        var pinY = cy - GetScaledPixels(state.Width, state.Height, 40, 24);
        // Pin circle
        svg.AppendLine($@"  <circle cx=""{pinX}"" cy=""{pinY}"" r=""{pinSize}"" fill=""{state.Palette.Accent}"" fill-opacity=""0.22""/>");
        svg.AppendLine($@"  <circle cx=""{pinX}"" cy=""{pinY}"" r=""{pinSize * 2 / 3}"" fill=""{state.Palette.Accent}"" fill-opacity=""0.4""/>");
        svg.AppendLine($@"  <circle cx=""{pinX}"" cy=""{pinY}"" r=""{Math.Max(4, pinSize / 3)}"" fill=""{state.Palette.Accent}""/>");
    }

    // ── Layout: Editorial / Product (Default) ──────────────────────────

    private static void AppendEditorialOrProductLayout(StringBuilder svg, SocialCardRenderState state)
    {
        var pad = state.SafeMarginX;
        var x = pad;
        var contentWidth = state.Width - (pad * 2);

        var baseTitleFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 50, 28, "socialCard", "titleFontSize");
        var baseTitleLineHeight = GetScaledPixels(state.Width, state.Height, 56, 32);
        var descFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 20, 14, "socialCard", "descriptionFontSize");
        var descLineHeight = GetScaledPixels(state.Width, state.Height, 28, 18);

        // Badge
        var badgeY = pad;
        AppendBadge(svg, state, x, badgeY);
        var badgeHeight = GetScaledPixels(state.Width, state.Height, 32, 22);

        // Eyebrow
        var eyebrowY = badgeY + badgeHeight + GetScaledPixels(state.Width, state.Height, 40, 24);
        AppendEyebrowText(svg, state, x, eyebrowY);

        // Title
        var titleY = eyebrowY + GetScaledPixels(state.Width, state.Height, 50, 30);
        var (titleFontSize, titleLineHeight, titleLines) = AdaptTitleSize(state.Title, baseTitleFontSize, baseTitleLineHeight, contentWidth, 3, state.Width, state.Height);
        AppendTitle(svg, state, titleLines, x, titleY, titleFontSize, titleLineHeight);

        // Description
        var descY = titleY + (titleLines.Count * titleLineHeight) + GetScaledPixels(state.Width, state.Height, 24, 14);
        var descLines = WrapText(state.Description, GetDescriptionWrapWidth(contentWidth, descFontSize), 4);
        AppendDescription(svg, state, descLines, x, descY, descFontSize, descLineHeight);

        // Footer route
        var footerY = state.Height - pad - GetScaledPixels(state.Width, state.Height, 6, 4);
        AppendFooterRoute(svg, state, x, footerY);

        // Small logo top-right if available
        if (!string.IsNullOrWhiteSpace(state.LogoDataUri))
        {
            var logoSize = GetScaledPixels(state.Width, state.Height, 48, 30);
            AppendLogo(svg, state, state.Width - pad - logoSize, pad, logoSize);
        }
    }

    // ── Code pane (API reference) ──────────────────────────────────────

    private static void AppendCodePane(StringBuilder svg, SocialCardRenderState state, int x, int y, int width, int height)
    {
        var radius = GetScaledPixels(state.Width, state.Height, 16, 8);
        svg.AppendLine($@"  <rect x=""{x}"" y=""{y}"" width=""{width}"" height=""{height}"" rx=""{radius}"" fill=""{state.Palette.Surface}""/>");

        var lang = ResolveLanguageMark(state.FooterLabel, state.Title);
        var cx = x + (width / 2);
        var cy = y + (height / 2);

        // Large language mark - centered, bold, modern
        var markFontSize = Math.Min(width, height) * 2 / 5;
        svg.AppendLine($@"  <text x=""{cx}"" y=""{cy}"" fill=""{state.Palette.Accent}"" fill-opacity=""0.18"" font-size=""{markFontSize}"" font-family=""{EscapeXml(state.Typography.TitleFontFamily)}"" font-weight=""800"" dominant-baseline=""central"" text-anchor=""middle"">{EscapeXml(lang)}</text>");

        // Smaller label below the mark
        var labelFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 16, 11, "socialCard", "codeFontSize");
        var labelText = ResolveLanguageLabel(state.FooterLabel, state.Title);
        svg.AppendLine($@"  <text x=""{cx}"" y=""{cy + (markFontSize / 2) + GetScaledPixels(state.Width, state.Height, 24, 14)}"" fill=""{state.Palette.TextSecondary}"" fill-opacity=""0.5"" font-size=""{labelFontSize}"" font-family=""{EscapeXml(state.Typography.BodyFontFamily)}"" font-weight=""600"" text-anchor=""middle"">{EscapeXml(labelText)}</text>");
    }

    private static string ResolveLanguageMark(string footerLabel, string title)
    {
        var combined = string.Concat(footerLabel ?? "", " ", title ?? "").ToLowerInvariant();
        if (combined.Contains("powershell", StringComparison.Ordinal) || combined.Contains("/ps/", StringComparison.Ordinal))
            return "PS";
        if (combined.Contains("csharp", StringComparison.Ordinal) || combined.Contains("c#", StringComparison.Ordinal) || combined.Contains("/dotnet/", StringComparison.Ordinal))
            return "C#";
        if (combined.Contains("python", StringComparison.Ordinal) || combined.Contains("/py/", StringComparison.Ordinal))
            return "PY";
        if (combined.Contains("javascript", StringComparison.Ordinal) || combined.Contains("/js/", StringComparison.Ordinal))
            return "JS";
        if (combined.Contains("typescript", StringComparison.Ordinal) || combined.Contains("/ts/", StringComparison.Ordinal))
            return "TS";
        if (combined.Contains("rust", StringComparison.Ordinal))
            return "RS";
        if (combined.Contains("golang", StringComparison.Ordinal) || combined.Contains("/go/", StringComparison.Ordinal))
            return "GO";
        return "API";
    }

    private static string ResolveLanguageLabel(string footerLabel, string title)
    {
        var mark = ResolveLanguageMark(footerLabel, title);
        return mark switch
        {
            "PS" => "PowerShell",
            "C#" => "C# / .NET",
            "PY" => "Python",
            "JS" => "JavaScript",
            "TS" => "TypeScript",
            "RS" => "Rust",
            "GO" => "Go",
            _ => "API Reference"
        };
    }

    // ── Geometry helpers ────────────────────────────────────────────────

    private static SocialRect GetInlineMediaFrame(SocialCardRenderState state)
    {
        var pad = state.SafeMarginX;
        var width = Math.Max(GetScaledPixels(state.Width, state.Height, 340, 220), (int)Math.Round(state.Width * 0.35));
        var imgHeight = state.Height - (pad * 2) - GetScaledPixels(state.Width, state.Height, 6, 4);
        return new SocialRect(
            state.Width - pad - width,
            pad,
            width,
            Math.Max(160, imgHeight),
            GetScaledPixels(state.Width, state.Height, 12, 6));
    }

    private static List<string> BuildReferenceLines(SocialCardRenderState state, int maxChars = 32)
    {
        var routeMaxChars = Math.Max(12, maxChars - "namespace ".Length - 1);
        var route = TrimSingleLine(state.FooterLabel.Trim('/').Replace('/', '.'), routeMaxChars);
        if (string.IsNullOrWhiteSpace(route))
            route = "api.reference";

        var titleTokens = NormalizeDisplayTextForWrap(state.Title)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(4)
            .ToArray();
        var signatureMaxChars = Math.Max(12, maxChars - "public sealed class ".Length);
        var signature = titleTokens.Length == 0
            ? "Invoke.Reference()"
            : string.Concat(titleTokens.Select(static token => char.ToUpperInvariant(token[0]) + token[1..])) + "()";

        var commentMaxChars = Math.Max(8, maxChars - 2);
        return
        [
            TrimSingleLine($"namespace {route};", maxChars),
            TrimSingleLine($"public sealed class {TrimSingleLine(signature, signatureMaxChars)}", maxChars),
            "{",
            TrimSingleLine("  // docs, examples, and parameters", commentMaxChars),
            "}"
        ];
    }

    private static string BuildMonogram(string eyebrow, string badge)
    {
        var value = NormalizeDisplayTextForWrap(eyebrow);
        if (string.IsNullOrWhiteSpace(value))
            value = NormalizeDisplayTextForWrap(badge);
        if (string.IsNullOrWhiteSpace(value))
            return "PF";

        var tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length >= 2)
            return string.Concat(tokens[0][0], tokens[1][0]).ToUpperInvariant();

        return tokens[0].Length >= 2 ? tokens[0][..2].ToUpperInvariant() : tokens[0].ToUpperInvariant();
    }

    private static int MeasurePillWidth(string text, int fontSize, int minimumWidth, int maximumWidth)
    {
        return Math.Clamp(
            EstimateTextWidth(TrimSingleLine(text, 56), fontSize, glyphFactor: 0.58) + GetScaledPixels(1200, 630, 34, 22),
            minimumWidth,
            maximumWidth);
    }

    // ── Image compositing (PNG pass) ───────────────────────────────────

    private static void CompositeMedia(MagickImage canvas, SocialCardRenderState state)
    {
        if (!string.IsNullOrWhiteSpace(state.InlineImageDataUri) &&
            string.Equals(state.LayoutKey, "inline-image", StringComparison.OrdinalIgnoreCase))
        {
            var media = GetInlineMediaFrame(state);
            using var image = TryLoadImageSource(state.InlineImageDataUri, media.Width, media.Height, state.AllowRemoteMediaFetch);
            if (image is not null)
            {
                image.Resize(new MagickGeometry((uint)media.Width, (uint)media.Height) { FillArea = true });
                image.Crop((uint)media.Width, (uint)media.Height, Gravity.Center);
                canvas.Composite(image, media.X, media.Y, CompositeOperator.Over);
            }
        }

        if (string.IsNullOrWhiteSpace(state.LogoDataUri))
            return;

        var frame = ResolveLogoFrame(state);
        if (frame is null)
            return;

        using var logo = TryLoadImageSource(state.LogoDataUri, frame.Width, frame.Height, state.AllowRemoteMediaFetch);
        if (logo is null)
            return;

        var inset = Math.Max(6, frame.Width / 8);
        var targetWidth = Math.Max(12, frame.Width - (inset * 2));
        var targetHeight = Math.Max(12, frame.Height - (inset * 2));
        logo.Resize(new MagickGeometry((uint)targetWidth, (uint)targetHeight));
        var offsetX = frame.X + ((frame.Width - (int)logo.Width) / 2);
        var offsetY = frame.Y + ((frame.Height - (int)logo.Height) / 2);
        canvas.Composite(logo, offsetX, offsetY, CompositeOperator.Over);
    }

    private static SocialRect? ResolveLogoFrame(SocialCardRenderState state)
    {
        var pad = state.SafeMarginX;
        var radius = GetScaledPixels(state.Width, state.Height, 20, 10);

        if (string.Equals(state.LayoutKey, "spotlight", StringComparison.OrdinalIgnoreCase))
        {
            var size = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 160, 90, "socialCard", "logoSize");
            return new SocialRect(state.Width - pad - size, pad, size, size, radius);
        }

        if (string.Equals(state.LayoutKey, "connect", StringComparison.OrdinalIgnoreCase))
        {
            var mapWidth = Math.Max(GetScaledPixels(state.Width, state.Height, 380, 220), (int)Math.Round(state.Width * 0.38));
            var maxSize = Math.Max(48, mapWidth - GetScaledPixels(state.Width, state.Height, 64, 40));
            var size = Math.Min(
                ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 96, 56, "socialCard", "logoSize"),
                maxSize);
            return new SocialRect(state.Width - pad - size, pad, size, size, radius);
        }

        if (string.Equals(state.LayoutKey, "product", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(state.LayoutKey, "editorial", StringComparison.OrdinalIgnoreCase))
        {
            var size = GetScaledPixels(state.Width, state.Height, 48, 30);
            return new SocialRect(state.Width - pad - size, pad, size, size, radius);
        }

        return null;
    }

    internal static void ClearRemoteImageCache()
    {
        RemoteImageByteCache.Clear();
    }

    internal static byte[]? GetRemoteImageBytes(string source, bool allowRemoteMediaFetch, Func<string, byte[]?>? remoteFetcher = null)
    {
        if (!allowRemoteMediaFetch || !IsRemoteMediaSource(source))
            return null;

        var fetch = remoteFetcher ?? FetchRemoteImageBytes;
        var lazy = RemoteImageByteCache.GetOrAdd(
            source,
            key => new Lazy<byte[]>(
                () => fetch(key) ?? Array.Empty<byte>(),
                LazyThreadSafetyMode.ExecutionAndPublication));

        var bytes = lazy.Value;
        return bytes.Length == 0 ? null : bytes;
    }

    private static bool IsRemoteMediaSource(string source)
    {
        return source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    private static byte[]? FetchRemoteImageBytes(string source)
    {
        using var response = SocialImageHttpClient.GetAsync(source).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
            return null;

        return response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
    }

    private static MagickImage? TryLoadImageSource(string source, int widthHint, int heightHint, bool allowRemoteMediaFetch)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(source))
                return null;

            if (IsRemoteMediaSource(source))
            {
                var remoteBytes = GetRemoteImageBytes(source, allowRemoteMediaFetch);
                if (remoteBytes is null || remoteBytes.Length == 0)
                    return null;

                return CreateMagickImage(remoteBytes, source, widthHint, heightHint);
            }

            var commaIndex = source.IndexOf(',', StringComparison.Ordinal);
            if (commaIndex > 0 && source.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                var metadata = source[..commaIndex];
                var payload = source[(commaIndex + 1)..];
                var isBase64 = metadata.EndsWith(";base64", StringComparison.OrdinalIgnoreCase);
                var bytes = isBase64
                    ? Convert.FromBase64String(payload)
                    : Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));

                return CreateMagickImage(bytes, metadata, widthHint, heightHint);
            }

            if (Uri.TryCreate(source, UriKind.Absolute, out var absoluteUri) &&
                absoluteUri.IsFile &&
                File.Exists(absoluteUri.LocalPath))
            {
                return new MagickImage(absoluteUri.LocalPath);
            }

            if (File.Exists(source))
                return new MagickImage(source);

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static MagickImage CreateMagickImage(byte[] bytes, string sourceHint, int widthHint, int heightHint)
    {
        if (sourceHint.Contains("image/svg+xml", StringComparison.OrdinalIgnoreCase) ||
            sourceHint.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            return new MagickImage(bytes, new MagickReadSettings
            {
                Width = (uint)Math.Max(1, widthHint),
                Height = (uint)Math.Max(1, heightHint),
                Format = MagickFormat.Svg
            });
        }

        return new MagickImage(bytes);
    }

    // ── Types ──────────────────────────────────────────────────────────

    internal sealed class SocialCardRenderOptions
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Eyebrow { get; set; }
        public string? Badge { get; set; }
        public string? FooterLabel { get; set; }
        public int Width { get; set; } = 1200;
        public int Height { get; set; } = 630;
        public string? StyleKey { get; set; }
        public string? VariantKey { get; set; }
        public IReadOnlyDictionary<string, object?>? ThemeTokens { get; set; }
        public string? LogoDataUri { get; set; }
        public string? InlineImageDataUri { get; set; }
        /// <summary>"light", "dark", or null (auto = dark).</summary>
        public string? ColorScheme { get; set; }
        public bool AllowRemoteMediaFetch { get; set; }
        public bool EmbedReferencedMediaInSvg { get; set; } = true;
    }

    private sealed class SocialCardRenderState
    {
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required string Title { get; init; }
        public required string Description { get; init; }
        public required string Eyebrow { get; init; }
        public required string Badge { get; init; }
        public required string FooterLabel { get; init; }
        public required string StyleKey { get; init; }
        public required string VariantKey { get; init; }
        public required string LayoutKey { get; init; }
        public required SocialPalette Palette { get; init; }
        public required SocialCardTypography Typography { get; init; }
        public required int FrameInset { get; init; }
        public required int PanelInset { get; init; }
        public required int ContentPadding { get; init; }
        public required int FrameRadius { get; init; }
        public required int PanelRadius { get; init; }
        public required int SafeMarginX { get; init; }
        public required int SafeMarginY { get; init; }
        public required string CtaLabel { get; init; }
        public required bool AllowRemoteMediaFetch { get; init; }
        public required bool EmbedReferencedMediaInSvg { get; init; }
        public IReadOnlyDictionary<string, object?>? ThemeTokens { get; init; }
        public string? LogoDataUri { get; init; }
        public string? InlineImageDataUri { get; init; }
    }

    private sealed record SocialRect(int X, int Y, int Width, int Height, int Radius);

    // ── Compatibility shims for old Append* names used in tests ────────

    private static SocialRect GetPanelRect(SocialCardRenderState state)
    {
        var inset = Math.Max(0, state.FrameInset + state.PanelInset);
        return new(
            inset,
            inset,
            Math.Max(1, state.Width - (inset * 2)),
            Math.Max(1, state.Height - (inset * 2)),
            state.PanelRadius);
    }
}
