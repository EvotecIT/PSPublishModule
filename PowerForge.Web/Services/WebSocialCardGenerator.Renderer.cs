using System.Text;
using ImageMagick;

namespace PowerForge.Web;

internal static partial class WebSocialCardGenerator
{
    internal static byte[]? RenderPng(SocialCardRenderOptions options)
    {
        var state = CreateState(options);
        var svg = RenderSvg(options);
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
        AppendCommonDefs(svg, state);
        if (string.Equals(state.LayoutKey, "inline-image", StringComparison.OrdinalIgnoreCase))
        {
            var media = GetInlineMediaFrame(state);
            svg.AppendLine(@"    <clipPath id=""mediaClip"">");
            svg.AppendLine($@"      <rect x=""{media.X}"" y=""{media.Y}"" width=""{media.Width}"" height=""{media.Height}"" rx=""{media.Radius}"" ry=""{media.Radius}""/>");
            svg.AppendLine(@"    </clipPath>");
        }
        svg.AppendLine(@"  </defs>");
        AppendCommonBackground(svg, state);

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
        var layoutKey = ResolveLayoutKey(styleKey, variantKey, !string.IsNullOrWhiteSpace(options.InlineImageDataUri));

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
            Palette = SelectPalette(styleKey, string.Join("|", styleKey, variantKey, title, description, eyebrow, normalizedBadge, normalizedFooter, width, height), options.ThemeTokens),
            Typography = ResolveTypography(options.ThemeTokens),
            ThemeTokens = options.ThemeTokens,
            LogoDataUri = options.LogoDataUri,
            InlineImageDataUri = options.InlineImageDataUri,
            FrameInset = ResolveTokenPixels(options.ThemeTokens, width, height, 30, 18, "socialCard", "frameInset"),
            PanelInset = ResolveTokenPixels(options.ThemeTokens, width, height, 42, 24, "socialCard", "panelInset"),
            ContentPadding = ResolveTokenPixels(options.ThemeTokens, width, height, 28, 16, "socialCard", "contentPadding"),
            FrameRadius = ResolveRadiusPixels(options.ThemeTokens, width, height, 28, 14, "frameRadius"),
            PanelRadius = ResolveRadiusPixels(options.ThemeTokens, width, height, 24, 12, "panelRadius"),
            SafeMarginX = ResolveTokenPixels(options.ThemeTokens, width, height, 84, 44, "socialCard", "safeMarginX"),
            SafeMarginY = ResolveTokenPixels(options.ThemeTokens, width, height, 44, 24, "socialCard", "safeMarginY")
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

    private static void AppendCommonDefs(StringBuilder svg, SocialCardRenderState state)
    {
        svg.AppendLine(@"    <linearGradient id=""bg"" x1=""0%"" y1=""0%"" x2=""100%"" y2=""100%"">");
        svg.AppendLine($@"      <stop offset=""0%"" stop-color=""{state.Palette.BackgroundStart}""/>");
        svg.AppendLine($@"      <stop offset=""56%"" stop-color=""{state.Palette.BackgroundMid}""/>");
        svg.AppendLine($@"      <stop offset=""100%"" stop-color=""{state.Palette.BackgroundEnd}""/>");
        svg.AppendLine(@"    </linearGradient>");
        svg.AppendLine(@"    <radialGradient id=""orbA"" cx=""50%"" cy=""50%"" r=""50%"">");
        svg.AppendLine($@"      <stop offset=""0%"" stop-color=""{state.Palette.AccentSoft}"" stop-opacity=""0.34""/>");
        svg.AppendLine($@"      <stop offset=""100%"" stop-color=""{state.Palette.Accent}"" stop-opacity=""0""/>");
        svg.AppendLine(@"    </radialGradient>");
        svg.AppendLine(@"    <radialGradient id=""orbB"" cx=""50%"" cy=""50%"" r=""50%"">");
        svg.AppendLine($@"      <stop offset=""0%"" stop-color=""{state.Palette.AccentStrong}"" stop-opacity=""0.18""/>");
        svg.AppendLine($@"      <stop offset=""100%"" stop-color=""{state.Palette.AccentStrong}"" stop-opacity=""0""/>");
        svg.AppendLine(@"    </radialGradient>");
        svg.AppendLine(@"    <linearGradient id=""accentBand"" x1=""0%"" y1=""0%"" x2=""100%"" y2=""0%"">");
        svg.AppendLine($@"      <stop offset=""0%"" stop-color=""{state.Palette.AccentSoft}"" stop-opacity=""0.86""/>");
        svg.AppendLine($@"      <stop offset=""100%"" stop-color=""{state.Palette.Accent}"" stop-opacity=""0.22""/>");
        svg.AppendLine(@"    </linearGradient>");
    }

    private static void AppendCommonBackground(StringBuilder svg, SocialCardRenderState state)
    {
        var glowRadiusLarge = GetScaledPixels(state.Width, state.Height, 320, 180);
        var glowRadiusSmall = GetScaledPixels(state.Width, state.Height, 180, 96);
        svg.AppendLine($@"  <rect x=""0"" y=""0"" width=""{state.Width}"" height=""{state.Height}"" fill=""url(#bg)""/>");
        svg.AppendLine($@"  <circle cx=""{state.Width - GetScaledPixels(state.Width, state.Height, 160, 94)}"" cy=""{GetScaledPixels(state.Width, state.Height, 116, 78)}"" r=""{glowRadiusLarge}"" fill=""url(#orbA)"" />");
        svg.AppendLine($@"  <circle cx=""{GetScaledPixels(state.Width, state.Height, 180, 110)}"" cy=""{state.Height - GetScaledPixels(state.Width, state.Height, 110, 72)}"" r=""{glowRadiusSmall}"" fill=""url(#orbB)"" />");
        svg.AppendLine($@"  <rect x=""{state.FrameInset}"" y=""{state.FrameInset}"" width=""{state.Width - (state.FrameInset * 2)}"" height=""{state.Height - (state.FrameInset * 2)}"" rx=""{state.FrameRadius}"" fill=""#050b1a"" fill-opacity=""0.18"" stroke=""{state.Palette.SurfaceStroke}"" stroke-opacity=""0.32""/>");
    }

    private static void AppendSpotlightLayout(StringBuilder svg, SocialCardRenderState state)
    {
        var badgeHeight = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 38, 26, "socialCard", "badgeHeight");
        var badgeFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 18, 12, "socialCard", "badgeFontSize");
        var badgeWidth = MeasurePillWidth(state.Badge.ToUpperInvariant(), badgeFontSize, 118, 240);
        var titleFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 66, 34, "socialCard", "titleFontSize");
        var titleLineHeight = GetScaledPixels(state.Width, state.Height, 66, 34);
        var descriptionFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 28, 17, "socialCard", "descriptionFontSize");
        var descriptionLineHeight = GetScaledPixels(state.Width, state.Height, 32, 20);
        var contentWidth = Math.Max(320, (int)Math.Round(state.Width * 0.54));
        var titleX = state.SafeMarginX;
        var titleLines = WrapText(state.Title, GetTitleWrapWidth(contentWidth, titleFontSize), 2);
        var badgeY = state.SafeMarginY;
        var eyebrowY = badgeY + badgeHeight + GetScaledPixels(state.Width, state.Height, 28, 16);
        var titleY = eyebrowY + GetScaledPixels(state.Width, state.Height, 72, 42);
        var descriptionY = titleY + (titleLines.Count * titleLineHeight) + GetScaledPixels(state.Width, state.Height, 24, 16);
        var descriptionLines = WrapText(state.Description, GetDescriptionWrapWidth(contentWidth, descriptionFontSize), 3);
        var footerY = state.Height - state.SafeMarginY;
        var markSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 228, 132, "socialCard", "logoSize");
        var markX = state.Width - state.SafeMarginX - markSize;
        var markY = state.SafeMarginY + GetScaledPixels(state.Width, state.Height, 42, 22);

        AppendPill(svg, state, titleX, badgeY, badgeWidth, badgeHeight, state.Badge.ToUpperInvariant(), true);
        AppendEyebrow(svg, state, titleX, eyebrowY, ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 26, 15, "socialCard", "eyebrowFontSize"));
        AppendTitleLines(svg, state, titleLines, titleX, titleY, titleFontSize, titleLineHeight);
        AppendDescriptionLines(svg, state, descriptionLines, titleX, descriptionY, descriptionFontSize, descriptionLineHeight);
        svg.AppendLine($@"  <rect x=""{titleX}"" y=""{footerY - GetScaledPixels(state.Width, state.Height, 30, 20)}"" width=""{Math.Max(220, contentWidth)}"" height=""2"" rx=""1"" fill=""url(#accentBand)"" fill-opacity=""0.56""/>");
        svg.AppendLine($@"  <text x=""{titleX}"" y=""{footerY}"" fill=""{state.Palette.TextSecondary}"" font-size=""{ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 22, 13, "socialCard", "footerFontSize")}"" font-family=""{EscapeXml(state.Typography.FooterFontFamily)}"" font-weight=""600"">{EscapeXml(TrimSingleLine(state.FooterLabel, 56))}</text>");
        AppendLogoMark(svg, state, markX, markY, markSize);
    }

    private static void AppendShelfLayout(StringBuilder svg, SocialCardRenderState state)
    {
        var panel = GetPanelRect(state);
        var badgeHeight = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 32, 24, "socialCard", "badgeHeight");
        var badgeFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 16, 12, "socialCard", "badgeFontSize");
        var badgeWidth = MeasurePillWidth(state.Badge.ToUpperInvariant(), badgeFontSize, 108, 220);
        var routeWidth = MeasurePillWidth(state.FooterLabel, badgeFontSize, 136, 280);
        var badgeX = panel.X + state.ContentPadding;
        var badgeY = panel.Y + state.ContentPadding;
        var titleX = badgeX + GetScaledPixels(state.Width, state.Height, 34, 22);
        var titleWidth = Math.Max(260, panel.Width - (state.ContentPadding * 2) - GetScaledPixels(state.Width, state.Height, 40, 24));
        var titleFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 48, 28, "socialCard", "titleFontSize");
        var titleLineHeight = GetScaledPixels(state.Width, state.Height, 52, 30);
        var descriptionFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 24, 16, "socialCard", "descriptionFontSize");
        var descriptionLineHeight = GetScaledPixels(state.Width, state.Height, 30, 20);
        var eyebrowY = badgeY + badgeHeight + GetScaledPixels(state.Width, state.Height, 26, 16);
        var titleY = eyebrowY + GetScaledPixels(state.Width, state.Height, 58, 38);
        var titleLines = WrapText(state.Title, GetTitleWrapWidth(titleWidth, titleFontSize), 3);
        var descriptionY = titleY + (titleLines.Count * titleLineHeight) + GetScaledPixels(state.Width, state.Height, 20, 14);
        var descriptionLines = WrapText(state.Description, GetDescriptionWrapWidth(titleWidth, descriptionFontSize), 4);

        AppendSurfacePanel(svg, state, panel, true);
        AppendPill(svg, state, badgeX, badgeY, badgeWidth, badgeHeight, state.Badge.ToUpperInvariant(), true);
        AppendPill(svg, state, panel.X + panel.Width - state.ContentPadding - routeWidth, badgeY, routeWidth, badgeHeight, state.FooterLabel, false);
        svg.AppendLine($@"  <rect x=""{badgeX}"" y=""{eyebrowY - GetScaledPixels(state.Width, state.Height, 14, 10)}"" width=""6"" height=""{panel.Height - (state.ContentPadding * 2) - badgeHeight}"" rx=""3"" fill=""url(#accentBand)""/>");
        AppendEyebrow(svg, state, titleX, eyebrowY, ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 22, 14, "socialCard", "eyebrowFontSize"));
        AppendTitleLines(svg, state, titleLines, titleX, titleY, titleFontSize, titleLineHeight);
        AppendDescriptionLines(svg, state, descriptionLines, titleX, descriptionY, descriptionFontSize, descriptionLineHeight);
    }

    private static void AppendReferenceLayout(StringBuilder svg, SocialCardRenderState state)
    {
        var panel = GetPanelRect(state);
        var codeWidth = Math.Max(GetScaledPixels(state.Width, state.Height, 320, 210), (int)Math.Round(panel.Width * 0.32));
        var codeX = panel.X + panel.Width - state.ContentPadding - codeWidth;
        var badgeHeight = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 32, 24, "socialCard", "badgeHeight");
        var badgeFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 16, 12, "socialCard", "badgeFontSize");
        var badgeWidth = MeasurePillWidth(state.Badge.ToUpperInvariant(), badgeFontSize, 102, 204);
        var titleX = panel.X + state.ContentPadding;
        var titleWidth = Math.Max(240, codeX - titleX - GetScaledPixels(state.Width, state.Height, 30, 18));
        var titleFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 46, 28, "socialCard", "titleFontSize");
        var titleLineHeight = GetScaledPixels(state.Width, state.Height, 50, 30);
        var descriptionFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 24, 15, "socialCard", "descriptionFontSize");
        var descriptionLineHeight = GetScaledPixels(state.Width, state.Height, 29, 19);
        var badgeY = panel.Y + state.ContentPadding;
        var eyebrowY = badgeY + badgeHeight + GetScaledPixels(state.Width, state.Height, 26, 16);
        var titleY = eyebrowY + GetScaledPixels(state.Width, state.Height, 60, 36);
        var titleLines = WrapText(state.Title, GetTitleWrapWidth(titleWidth, titleFontSize), 3);
        var descriptionY = titleY + (titleLines.Count * titleLineHeight) + GetScaledPixels(state.Width, state.Height, 18, 12);
        var descriptionLines = WrapText(state.Description, GetDescriptionWrapWidth(titleWidth, descriptionFontSize), 3);

        AppendSurfacePanel(svg, state, panel, true);
        AppendPill(svg, state, titleX, badgeY, badgeWidth, badgeHeight, state.Badge.ToUpperInvariant(), true);
        AppendEyebrow(svg, state, titleX, eyebrowY, ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 22, 14, "socialCard", "eyebrowFontSize"));
        AppendTitleLines(svg, state, titleLines, titleX, titleY, titleFontSize, titleLineHeight);
        AppendDescriptionLines(svg, state, descriptionLines, titleX, descriptionY, descriptionFontSize, descriptionLineHeight);
        AppendPill(svg, state, titleX, panel.Y + panel.Height - state.ContentPadding - badgeHeight, MeasurePillWidth(state.FooterLabel, badgeFontSize, 144, 280), badgeHeight, state.FooterLabel, false);
        AppendCodePane(svg, state, codeX, panel.Y + state.ContentPadding + GetScaledPixels(state.Width, state.Height, 16, 10), codeWidth, panel.Height - (state.ContentPadding * 2) - GetScaledPixels(state.Width, state.Height, 16, 10));
    }

    private static void AppendInlineImageLayout(StringBuilder svg, SocialCardRenderState state)
    {
        var panel = GetPanelRect(state);
        var media = GetInlineMediaFrame(state);
        var badgeHeight = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 32, 24, "socialCard", "badgeHeight");
        var badgeFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 16, 12, "socialCard", "badgeFontSize");
        var titleX = panel.X + state.ContentPadding;
        var titleWidth = Math.Max(240, media.X - titleX - GetScaledPixels(state.Width, state.Height, 30, 18));
        var titleFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 48, 28, "socialCard", "titleFontSize");
        var titleLineHeight = GetScaledPixels(state.Width, state.Height, 52, 30);
        var descriptionFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 24, 16, "socialCard", "descriptionFontSize");
        var descriptionLineHeight = GetScaledPixels(state.Width, state.Height, 29, 19);
        var badgeY = panel.Y + state.ContentPadding;
        var eyebrowY = badgeY + badgeHeight + GetScaledPixels(state.Width, state.Height, 28, 18);
        var titleY = eyebrowY + GetScaledPixels(state.Width, state.Height, 56, 36);
        var titleLines = WrapText(state.Title, GetTitleWrapWidth(titleWidth, titleFontSize), 3);
        var descriptionY = titleY + (titleLines.Count * titleLineHeight) + GetScaledPixels(state.Width, state.Height, 18, 12);
        var descriptionLines = WrapText(state.Description, GetDescriptionWrapWidth(titleWidth, descriptionFontSize), 4);

        AppendSurfacePanel(svg, state, panel, false);
        AppendPill(svg, state, titleX, badgeY, MeasurePillWidth(state.Badge.ToUpperInvariant(), badgeFontSize, 108, 220), badgeHeight, state.Badge.ToUpperInvariant(), true);
        AppendEyebrow(svg, state, titleX, eyebrowY, ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 22, 14, "socialCard", "eyebrowFontSize"));
        AppendTitleLines(svg, state, titleLines, titleX, titleY, titleFontSize, titleLineHeight);
        AppendDescriptionLines(svg, state, descriptionLines, titleX, descriptionY, descriptionFontSize, descriptionLineHeight);
        AppendPill(svg, state, titleX, panel.Y + panel.Height - state.ContentPadding - badgeHeight, MeasurePillWidth(state.FooterLabel, badgeFontSize, 144, 280), badgeHeight, state.FooterLabel, false);
        svg.AppendLine($@"  <rect x=""{media.X}"" y=""{media.Y}"" width=""{media.Width}"" height=""{media.Height}"" rx=""{media.Radius}"" ry=""{media.Radius}"" fill=""{state.Palette.Surface}"" fill-opacity=""0.28"" stroke=""{state.Palette.SurfaceStroke}"" stroke-opacity=""0.28""/>");
        svg.AppendLine($@"  <image href=""{EscapeXml(state.InlineImageDataUri ?? string.Empty)}"" xlink:href=""{EscapeXml(state.InlineImageDataUri ?? string.Empty)}"" x=""{media.X}"" y=""{media.Y}"" width=""{media.Width}"" height=""{media.Height}"" preserveAspectRatio=""xMidYMid slice"" clip-path=""url(#mediaClip)""/>");
    }

    private static void AppendConnectLayout(StringBuilder svg, SocialCardRenderState state)
    {
        var panel = GetPanelRect(state);
        var railWidth = Math.Max(GetScaledPixels(state.Width, state.Height, 260, 180), (int)Math.Round(panel.Width * 0.28));
        var railX = panel.X + panel.Width - railWidth;
        var badgeHeight = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 34, 24, "socialCard", "badgeHeight");
        var badgeFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 16, 12, "socialCard", "badgeFontSize");
        var titleX = panel.X + state.ContentPadding;
        var titleWidth = Math.Max(240, railX - titleX - GetScaledPixels(state.Width, state.Height, 36, 22));
        var titleFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 52, 30, "socialCard", "titleFontSize");
        var titleLineHeight = GetScaledPixels(state.Width, state.Height, 56, 32);
        var descriptionFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 24, 16, "socialCard", "descriptionFontSize");
        var descriptionLineHeight = GetScaledPixels(state.Width, state.Height, 30, 19);
        var badgeY = panel.Y + state.ContentPadding;
        var eyebrowY = badgeY + badgeHeight + GetScaledPixels(state.Width, state.Height, 30, 18);
        var titleY = eyebrowY + GetScaledPixels(state.Width, state.Height, 60, 38);
        var titleLines = WrapText(state.Title, GetTitleWrapWidth(titleWidth, titleFontSize), 3);
        var descriptionY = titleY + (titleLines.Count * titleLineHeight) + GetScaledPixels(state.Width, state.Height, 18, 12);
        var descriptionLines = WrapText(state.Description, GetDescriptionWrapWidth(titleWidth, descriptionFontSize), 4);

        AppendSurfacePanel(svg, state, panel, true);
        AppendPill(svg, state, titleX, badgeY, MeasurePillWidth(state.Badge.ToUpperInvariant(), badgeFontSize, 118, 220), badgeHeight, state.Badge.ToUpperInvariant(), true);
        AppendEyebrow(svg, state, titleX, eyebrowY, ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 22, 14, "socialCard", "eyebrowFontSize"));
        AppendTitleLines(svg, state, titleLines, titleX, titleY, titleFontSize, titleLineHeight);
        AppendDescriptionLines(svg, state, descriptionLines, titleX, descriptionY, descriptionFontSize, descriptionLineHeight);
        AppendPill(svg, state, titleX, panel.Y + panel.Height - state.ContentPadding - badgeHeight, MeasurePillWidth(state.FooterLabel, badgeFontSize, 150, 300), badgeHeight, state.FooterLabel, false);
        AppendRailMark(svg, state, railX, panel.Y, railWidth, panel.Height);
    }

    private static void AppendEditorialOrProductLayout(StringBuilder svg, SocialCardRenderState state)
    {
        var panel = GetPanelRect(state);
        var badgeHeight = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 34, 24, "socialCard", "badgeHeight");
        var badgeFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 17, 12, "socialCard", "badgeFontSize");
        var titleX = panel.X + state.ContentPadding;
        var titleWidth = panel.Width - (state.ContentPadding * 2);
        var titleFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, string.Equals(state.LayoutKey, "editorial", StringComparison.OrdinalIgnoreCase) ? 52 : 54, string.Equals(state.LayoutKey, "editorial", StringComparison.OrdinalIgnoreCase) ? 30 : 32, "socialCard", "titleFontSize");
        var titleLineHeight = GetScaledPixels(state.Width, state.Height, string.Equals(state.LayoutKey, "editorial", StringComparison.OrdinalIgnoreCase) ? 56 : 58, string.Equals(state.LayoutKey, "editorial", StringComparison.OrdinalIgnoreCase) ? 32 : 34);
        var descriptionFontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 25, 16, "socialCard", "descriptionFontSize");
        var descriptionLineHeight = GetScaledPixels(state.Width, state.Height, 30, 19);
        var badgeY = panel.Y + state.ContentPadding;
        var eyebrowY = badgeY + badgeHeight + GetScaledPixels(state.Width, state.Height, 28, 18);
        var titleY = eyebrowY + GetScaledPixels(state.Width, state.Height, 58, 38);
        var titleLines = WrapText(state.Title, GetTitleWrapWidth(titleWidth, titleFontSize), 3);
        var descriptionY = titleY + (titleLines.Count * titleLineHeight) + GetScaledPixels(state.Width, state.Height, 18, 12);
        var descriptionLines = WrapText(state.Description, GetDescriptionWrapWidth(titleWidth, descriptionFontSize), 4);

        AppendSurfacePanel(svg, state, panel, !string.Equals(state.LayoutKey, "editorial", StringComparison.OrdinalIgnoreCase));
        AppendPill(svg, state, titleX, badgeY, MeasurePillWidth(state.Badge.ToUpperInvariant(), badgeFontSize, 112, 220), badgeHeight, state.Badge.ToUpperInvariant(), true);
        AppendEyebrow(svg, state, titleX, eyebrowY, ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 22, 14, "socialCard", "eyebrowFontSize"));
        AppendTitleLines(svg, state, titleLines, titleX, titleY, titleFontSize, titleLineHeight);
        AppendDescriptionLines(svg, state, descriptionLines, titleX, descriptionY, descriptionFontSize, descriptionLineHeight);
        AppendPill(svg, state, titleX, panel.Y + panel.Height - state.ContentPadding - badgeHeight, MeasurePillWidth(state.FooterLabel, badgeFontSize, 146, 300), badgeHeight, state.FooterLabel, false);
        if (!string.IsNullOrWhiteSpace(state.LogoDataUri))
            AppendLogoMark(svg, state, panel.X + panel.Width - state.ContentPadding - ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 52, 34, "socialCard", "logoSize"), badgeY - 2, ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 52, 34, "socialCard", "logoSize"));
    }

    private static void AppendSurfacePanel(StringBuilder svg, SocialCardRenderState state, SocialRect panel, bool showTopBand)
    {
        svg.AppendLine($@"  <rect x=""{panel.X}"" y=""{panel.Y}"" width=""{panel.Width}"" height=""{panel.Height}"" rx=""{panel.Radius}"" fill=""{state.Palette.Surface}"" fill-opacity=""0.48"" stroke=""{state.Palette.SurfaceStroke}"" stroke-opacity=""0.34""/>");
        if (showTopBand)
            svg.AppendLine($@"  <rect x=""{panel.X}"" y=""{panel.Y}"" width=""{panel.Width}"" height=""{ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 8, 4, "socialCard", "topBandHeight")}"" rx=""3"" fill=""url(#accentBand)"" fill-opacity=""0.74""/>");
    }

    private static void AppendPill(StringBuilder svg, SocialCardRenderState state, int x, int y, int width, int height, string text, bool filled)
    {
        svg.AppendLine($@"  <rect x=""{x}"" y=""{y}"" width=""{width}"" height=""{height}"" rx=""{ResolveRadiusPixels(state.ThemeTokens, state.Width, state.Height, 18, 10, "badgeRadius")}"" fill=""{(filled ? state.Palette.ChipBackground : state.Palette.Surface)}"" fill-opacity=""{(filled ? "0.86" : "0.38")}"" stroke=""{state.Palette.ChipBorder}"" stroke-opacity=""{(filled ? "0.74" : "0.42")}""/>");
        svg.AppendLine($@"  <text x=""{x + (width / 2)}"" y=""{y + (height / 2)}"" fill=""{state.Palette.ChipText}"" font-size=""{ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 16, 12, "socialCard", "badgeFontSize")}"" font-family=""{EscapeXml(state.Typography.BadgeFontFamily)}"" font-weight=""700"" dominant-baseline=""middle"" text-anchor=""middle"">{EscapeXml(TrimSingleLine(text, 48))}</text>");
    }

    private static void AppendEyebrow(StringBuilder svg, SocialCardRenderState state, int x, int y, int fontSize)
        => svg.AppendLine($@"  <text x=""{x}"" y=""{y}"" fill=""{state.Palette.AccentSoft}"" font-size=""{fontSize}"" font-family=""{EscapeXml(state.Typography.EyebrowFontFamily)}"" font-weight=""700"">{EscapeXml(TrimSingleLine(state.Eyebrow, 56))}</text>");

    private static void AppendTitleLines(StringBuilder svg, SocialCardRenderState state, IReadOnlyList<string> lines, int x, int y, int fontSize, int lineHeight)
    {
        for (var i = 0; i < lines.Count; i++)
            svg.AppendLine($@"  <text x=""{x}"" y=""{y + (i * lineHeight)}"" fill=""{state.Palette.TextPrimary}"" font-size=""{fontSize}"" font-family=""{EscapeXml(state.Typography.TitleFontFamily)}"" font-weight=""800"">{EscapeXml(lines[i])}</text>");
    }

    private static void AppendDescriptionLines(StringBuilder svg, SocialCardRenderState state, IReadOnlyList<string> lines, int x, int y, int fontSize, int lineHeight)
    {
        for (var i = 0; i < lines.Count; i++)
            svg.AppendLine($@"  <text x=""{x}"" y=""{y + (i * lineHeight)}"" fill=""{state.Palette.TextSecondary}"" font-size=""{fontSize}"" font-family=""{EscapeXml(state.Typography.BodyFontFamily)}"" font-weight=""500"">{EscapeXml(lines[i])}</text>");
    }

    private static void AppendLogoMark(StringBuilder svg, SocialCardRenderState state, int x, int y, int size)
    {
        svg.AppendLine($@"  <rect x=""{x}"" y=""{y}"" width=""{size}"" height=""{size}"" rx=""{ResolveRadiusPixels(state.ThemeTokens, state.Width, state.Height, 26, 12, "panelRadius")}"" fill=""{state.Palette.Surface}"" fill-opacity=""0.46"" stroke=""{state.Palette.SurfaceStroke}"" stroke-opacity=""0.34""/>");
        if (!string.IsNullOrWhiteSpace(state.LogoDataUri))
        {
            var inset = Math.Max(8, size / 7);
            svg.AppendLine($@"  <image href=""{EscapeXml(state.LogoDataUri)}"" xlink:href=""{EscapeXml(state.LogoDataUri)}"" x=""{x + inset}"" y=""{y + inset}"" width=""{Math.Max(12, size - (inset * 2))}"" height=""{Math.Max(12, size - (inset * 2))}"" preserveAspectRatio=""xMidYMid meet""/>");
            return;
        }

        var monogram = BuildMonogram(state.Eyebrow, state.Badge);
        svg.AppendLine($@"  <text x=""{x + (size / 2)}"" y=""{y + (size / 2)}"" fill=""{state.Palette.TextPrimary}"" font-size=""{Math.Max(20, size / 2)}"" font-family=""{EscapeXml(state.Typography.TitleFontFamily)}"" font-weight=""800"" dominant-baseline=""middle"" text-anchor=""middle"">{EscapeXml(monogram)}</text>");
    }

    private static void AppendRailMark(StringBuilder svg, SocialCardRenderState state, int x, int y, int width, int height)
    {
        svg.AppendLine($@"  <rect x=""{x}"" y=""{y}"" width=""{width}"" height=""{height}"" rx=""{ResolveRadiusPixels(state.ThemeTokens, state.Width, state.Height, 24, 12, "panelRadius")}"" fill=""{state.Palette.Surface}"" fill-opacity=""0.56"" stroke=""{state.Palette.SurfaceStroke}"" stroke-opacity=""0.42""/>");
        svg.AppendLine($@"  <rect x=""{x}"" y=""{y}"" width=""{width}"" height=""6"" rx=""3"" fill=""url(#accentBand)""/>");
        var markSize = Math.Min(width - GetScaledPixels(state.Width, state.Height, 56, 36), GetScaledPixels(state.Width, state.Height, 160, 110));
        var markX = x + ((width - markSize) / 2);
        var markY = y + state.ContentPadding + GetScaledPixels(state.Width, state.Height, 44, 28);
        AppendLogoMark(svg, state, markX, markY, markSize);
        svg.AppendLine($@"  <text x=""{x + (width / 2)}"" y=""{markY + markSize + GetScaledPixels(state.Width, state.Height, 52, 32)}"" fill=""{state.Palette.AccentSoft}"" font-size=""{ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 22, 14, "socialCard", "eyebrowFontSize")}"" font-family=""{EscapeXml(state.Typography.EyebrowFontFamily)}"" font-weight=""700"" text-anchor=""middle"">Reach Out</text>");
    }

    private static void AppendCodePane(StringBuilder svg, SocialCardRenderState state, int x, int y, int width, int height)
    {
        svg.AppendLine($@"  <rect x=""{x}"" y=""{y}"" width=""{width}"" height=""{height}"" rx=""{ResolveRadiusPixels(state.ThemeTokens, state.Width, state.Height, 18, 10, "panelRadius")}"" fill=""{state.Palette.Surface}"" fill-opacity=""0.62"" stroke=""{state.Palette.SurfaceStroke}"" stroke-opacity=""0.46""/>");
        svg.AppendLine($@"  <rect x=""{x}"" y=""{y}"" width=""{width}"" height=""6"" rx=""3"" fill=""url(#accentBand)""/>");
        var lines = BuildReferenceLines(state);
        var fontSize = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 20, 13, "socialCard", "codeFontSize");
        var lineHeight = GetScaledPixels(state.Width, state.Height, 34, 21);
        for (var i = 0; i < lines.Count; i++)
        {
            var color = i == 0 ? state.Palette.AccentSoft : state.Palette.TextSecondary;
            svg.AppendLine($@"  <text x=""{x + GetScaledPixels(state.Width, state.Height, 20, 14)}"" y=""{y + GetScaledPixels(state.Width, state.Height, 54, 34) + (i * lineHeight)}"" fill=""{color}"" font-size=""{fontSize}"" font-family=""{EscapeXml(state.Typography.MonoFontFamily)}"" font-weight=""{(i == 0 ? "700" : "500")}"">{EscapeXml(lines[i])}</text>");
        }
    }

    private static SocialRect GetPanelRect(SocialCardRenderState state)
        => new(state.PanelInset, state.PanelInset, state.Width - (state.PanelInset * 2), state.Height - (state.PanelInset * 2), state.PanelRadius);

    private static SocialRect GetInlineMediaFrame(SocialCardRenderState state)
    {
        var panel = GetPanelRect(state);
        var width = Math.Max(GetScaledPixels(state.Width, state.Height, 320, 220), (int)Math.Round(panel.Width * 0.34));
        return new SocialRect(
            panel.X + panel.Width - state.ContentPadding - width,
            panel.Y + state.ContentPadding,
            width,
            Math.Max(160, panel.Height - (state.ContentPadding * 2)),
            ResolveRadiusPixels(state.ThemeTokens, state.Width, state.Height, 22, 12, "panelRadius"));
    }

    private static List<string> BuildReferenceLines(SocialCardRenderState state)
    {
        var route = TrimSingleLine(state.FooterLabel.Trim('/').Replace('/', '.'), 32);
        if (string.IsNullOrWhiteSpace(route))
            route = "api.reference";

        var titleTokens = NormalizeDisplayTextForWrap(state.Title)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(4)
            .ToArray();
        var signature = titleTokens.Length == 0
            ? "Invoke.Reference()"
            : string.Concat(titleTokens.Select(static token => char.ToUpperInvariant(token[0]) + token[1..])) + "()";

        return
        [
            $"namespace {route};",
            $"public sealed class {TrimSingleLine(signature, 28)}",
            "{",
            "  // docs, examples, and parameters",
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

    private static void CompositeMedia(MagickImage canvas, SocialCardRenderState state)
    {
        if (!string.IsNullOrWhiteSpace(state.InlineImageDataUri) &&
            string.Equals(state.LayoutKey, "inline-image", StringComparison.OrdinalIgnoreCase))
        {
            var media = GetInlineMediaFrame(state);
            using var image = TryLoadDataUriImage(state.InlineImageDataUri, media.Width, media.Height);
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

        using var logo = TryLoadDataUriImage(state.LogoDataUri, frame.Width, frame.Height);
        if (logo is null)
            return;

        var inset = Math.Max(8, frame.Width / 7);
        var targetWidth = Math.Max(12, frame.Width - (inset * 2));
        var targetHeight = Math.Max(12, frame.Height - (inset * 2));
        logo.Resize(new MagickGeometry((uint)targetWidth, (uint)targetHeight));
        var offsetX = frame.X + ((frame.Width - (int)logo.Width) / 2);
        var offsetY = frame.Y + ((frame.Height - (int)logo.Height) / 2);
        canvas.Composite(logo, offsetX, offsetY, CompositeOperator.Over);
    }

    private static SocialRect? ResolveLogoFrame(SocialCardRenderState state)
    {
        if (string.Equals(state.LayoutKey, "spotlight", StringComparison.OrdinalIgnoreCase))
        {
            var size = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 228, 132, "socialCard", "logoSize");
            return new SocialRect(
                state.Width - state.SafeMarginX - size,
                state.SafeMarginY + GetScaledPixels(state.Width, state.Height, 42, 22),
                size,
                size,
                ResolveRadiusPixels(state.ThemeTokens, state.Width, state.Height, 26, 12, "panelRadius"));
        }

        if (string.Equals(state.LayoutKey, "connect", StringComparison.OrdinalIgnoreCase))
        {
            var panel = GetPanelRect(state);
            var railWidth = Math.Max(GetScaledPixels(state.Width, state.Height, 260, 180), (int)Math.Round(panel.Width * 0.28));
            var markSize = Math.Min(railWidth - GetScaledPixels(state.Width, state.Height, 56, 36), GetScaledPixels(state.Width, state.Height, 160, 110));
            return new SocialRect(
                panel.X + panel.Width - railWidth + ((railWidth - markSize) / 2),
                panel.Y + state.ContentPadding + GetScaledPixels(state.Width, state.Height, 44, 28),
                markSize,
                markSize,
                ResolveRadiusPixels(state.ThemeTokens, state.Width, state.Height, 26, 12, "panelRadius"));
        }

        if (string.Equals(state.LayoutKey, "product", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(state.LayoutKey, "editorial", StringComparison.OrdinalIgnoreCase))
        {
            var panel = GetPanelRect(state);
            var size = ResolveTokenPixels(state.ThemeTokens, state.Width, state.Height, 52, 34, "socialCard", "logoSize");
            return new SocialRect(
                panel.X + panel.Width - state.ContentPadding - size,
                panel.Y + state.ContentPadding - 2,
                size,
                size,
                ResolveRadiusPixels(state.ThemeTokens, state.Width, state.Height, 18, 10, "panelRadius"));
        }

        return null;
    }

    private static MagickImage? TryLoadDataUriImage(string dataUri, int widthHint, int heightHint)
    {
        try
        {
            var commaIndex = dataUri.IndexOf(',', StringComparison.Ordinal);
            if (commaIndex <= 0)
                return null;

            var metadata = dataUri[..commaIndex];
            var payload = dataUri[(commaIndex + 1)..];
            var isBase64 = metadata.EndsWith(";base64", StringComparison.OrdinalIgnoreCase);
            var bytes = isBase64
                ? Convert.FromBase64String(payload)
                : Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));

            if (metadata.Contains("image/svg+xml", StringComparison.OrdinalIgnoreCase))
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
        catch
        {
            return null;
        }
    }

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
        public IReadOnlyDictionary<string, object?>? ThemeTokens { get; init; }
        public string? LogoDataUri { get; init; }
        public string? InlineImageDataUri { get; init; }
    }

    private sealed record SocialRect(int X, int Y, int Width, int Height, int Radius);
}
