using System.Globalization;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ImageMagick;

namespace PowerForge.Web;

internal static partial class WebSocialCardGenerator
{
    private static readonly TimeSpan SocialRegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex WhitespaceRegex = new(
        "\\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        SocialRegexTimeout);

    private static readonly SocialPalette[] Palettes =
    [
        new(
            "#070f25",
            "#102b57",
            "#0b1732",
            "#0b1732",
            "#2b4b7f",
            "#38bdf8",
            "#67e8f9",
            "#bae6fd",
            "#f8fafc",
            "#cbd5e1",
            "#0b1f3f",
            "#2b4b7f",
            "#dbeafe"),
        new(
            "#0d142f",
            "#1f305e",
            "#111b3d",
            "#111b3d",
            "#3e4f7a",
            "#a78bfa",
            "#93c5fd",
            "#ddd6fe",
            "#f8fafc",
            "#d1d5db",
            "#1d1f47",
            "#51558c",
            "#e0e7ff"),
        new(
            "#071f29",
            "#0f3f5a",
            "#082b36",
            "#082b36",
            "#2d6070",
            "#22d3ee",
            "#5eead4",
            "#99f6e4",
            "#f8fafc",
            "#d1fae5",
            "#09343c",
            "#23606d",
            "#ccfbf1"),
        new(
            "#171327",
            "#2f2752",
            "#1d1538",
            "#1d1538",
            "#4a3c73",
            "#f59e0b",
            "#f97316",
            "#fde68a",
            "#f8fafc",
            "#e5e7eb",
            "#3a2146",
            "#75495f",
            "#fef3c7")
    ];

    private static readonly SocialPalette[] LightPalettes =
    [
        // Blue accent (home/default)
        new(
            "#ffffff",
            "#f8fafc",
            "#f1f5f9",
            "#f1f5f9",
            "#e2e8f0",
            "#2563eb",
            "#3b82f6",
            "#1d4ed8",
            "#0f172a",
            "#475569",
            "#e0e7ff",
            "#c7d2fe",
            "#1e3a5f"),
        // Violet accent (contact)
        new(
            "#ffffff",
            "#faf5ff",
            "#f3e8ff",
            "#f3e8ff",
            "#e9d5ff",
            "#7c3aed",
            "#8b5cf6",
            "#6d28d9",
            "#1e1b4b",
            "#6b7280",
            "#ede9fe",
            "#ddd6fe",
            "#4c1d95"),
        // Teal accent (api/docs)
        new(
            "#ffffff",
            "#f0fdfa",
            "#ccfbf1",
            "#ecfdf5",
            "#d1fae5",
            "#0d9488",
            "#14b8a6",
            "#0f766e",
            "#0f172a",
            "#475569",
            "#ccfbf1",
            "#99f6e4",
            "#134e4a"),
        // Amber accent (blog/editorial)
        new(
            "#ffffff",
            "#fffbeb",
            "#fef3c7",
            "#fefce8",
            "#fde68a",
            "#d97706",
            "#f59e0b",
            "#b45309",
            "#1c1917",
            "#57534e",
            "#fef3c7",
            "#fde68a",
            "#78350f")
    ];

    internal static byte[]? RenderPng(
        string? title,
        string? description,
        string? eyebrow,
        string? badge,
        string? footerLabel,
        int width,
        int height,
        string? styleKey = null,
        string? variantKey = null,
        IReadOnlyDictionary<string, object?>? themeTokens = null)
    {
        var svg = RenderSvg(title, description, eyebrow, badge, footerLabel, width, height, styleKey, variantKey, themeTokens);
        if (string.IsNullOrWhiteSpace(svg))
            return null;

        try
        {
            var svgBytes = Encoding.UTF8.GetBytes(svg);
            var settings = new MagickReadSettings
            {
                Width = (uint)Math.Clamp(width, 600, 2400),
                Height = (uint)Math.Clamp(height, 315, 1400),
                Format = MagickFormat.Svg
            };
            using var image = new MagickImage(svgBytes, settings);
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

    internal static string RenderSvg(
        string? title,
        string? description,
        string? eyebrow,
        string? badge,
        string? footerLabel,
        int width,
        int height,
        string? styleKey = null,
        string? variantKey = null,
        IReadOnlyDictionary<string, object?>? themeTokens = null)
    {
        width = Math.Clamp(width, 600, 2400);
        height = Math.Clamp(height, 315, 1400);

        var primaryTitle = string.IsNullOrWhiteSpace(title)
            ? "PowerForge Web"
            : title!;
        var primaryDescription = string.IsNullOrWhiteSpace(description)
            ? "Static content with docs, API references, and editorial pages."
            : description!;
        var primaryEyebrow = string.IsNullOrWhiteSpace(eyebrow)
            ? "PowerForge"
            : eyebrow!;
        var primaryBadge = string.IsNullOrWhiteSpace(badge)
            ? "PAGE"
            : badge!;
        var primaryFooterLabel = string.IsNullOrWhiteSpace(footerLabel)
            ? "/"
            : footerLabel!;

        var normalizedStyle = NormalizeStyle(styleKey) ?? ClassifyStyle(primaryBadge, primaryFooterLabel);
        var normalizedVariant = NormalizeVariant(variantKey) ?? ClassifyVariant(normalizedStyle, primaryBadge, primaryFooterLabel);
        var isHero = string.Equals(normalizedVariant, "hero", StringComparison.Ordinal);
        var isCompact = string.Equals(normalizedVariant, "compact", StringComparison.Ordinal);
        var normalizedBadge = NormalizeBadgeLabel(primaryBadge, primaryFooterLabel, normalizedStyle, normalizedVariant);
        var normalizedFooterLabel = NormalizeFooterLabel(primaryFooterLabel, normalizedBadge, normalizedStyle);

        var safeEyebrow = EscapeXml(TrimSingleLine(primaryEyebrow, 56));
        var safeBadgeUpper = EscapeXml(TrimSingleLine(normalizedBadge.ToUpperInvariant(), 20));
        var safeFooter = EscapeXml(TrimSingleLine(normalizedFooterLabel, isHero ? 42 : (isCompact ? 40 : 44)));
        var seed = string.Join("|", normalizedStyle, normalizedVariant, primaryTitle, primaryDescription, primaryEyebrow, normalizedBadge, normalizedFooterLabel, width, height);
        var palette = SelectPalette(normalizedStyle, seed, themeTokens);
        var type = ResolveTypography(themeTokens);
        var frameInset = ResolveTokenPixels(themeTokens, width, height, defaultBasePixels: 36, minimum: 22, "socialCard", "frameInset");
        var panelInset = ResolveTokenPixels(themeTokens, width, height, defaultBasePixels: 48, minimum: 30, "socialCard", "panelInset");
        var panelWidth = width - (panelInset * 2);
        var panelHeight = height - (panelInset * 2);
        var contentPadding = ResolveTokenPixels(themeTokens, width, height, defaultBasePixels: 24, minimum: 16, "socialCard", "contentPadding");
        var contentLeft = panelInset + contentPadding;
        var contentRight = panelInset + panelWidth - contentPadding;
        var contentWidth = Math.Max(120, contentRight - contentLeft);
        var safeMarginX = Math.Max(contentPadding, ResolveTokenPixels(themeTokens, width, height, defaultBasePixels: 96, minimum: 48, "socialCard", "safeMarginX"));
        var safeMarginY = Math.Max(
            ResolveTokenPixels(themeTokens, width, height, defaultBasePixels: 26, minimum: 16, "socialCard", "safeMarginY"),
            (int)Math.Round(height * 0.09));
        var safeLeft = Math.Max(contentLeft, safeMarginX);
        var safeRight = Math.Min(contentRight, width - safeMarginX);
        var safeTop = Math.Max(panelInset + contentPadding, safeMarginY);
        var safeBottom = Math.Min(panelInset + panelHeight - contentPadding, height - safeMarginY);
        if (safeRight - safeLeft < GetScaledPixels(width, height, basePixels: 260, minimum: 200))
        {
            safeLeft = contentLeft;
            safeRight = contentRight;
        }

        if (safeBottom - safeTop < GetScaledPixels(width, height, basePixels: 180, minimum: 132))
        {
            safeTop = panelInset + contentPadding;
            safeBottom = panelInset + panelHeight - contentPadding;
        }

        var safeWidth = Math.Max(120, safeRight - safeLeft);
        var topBandHeight = ResolveTokenPixels(themeTokens, width, height, defaultBasePixels: isHero ? 11 : (isCompact ? 8 : 9), minimum: 5, "socialCard", "topBandHeight");
        var topBandRadius = ResolveRadiusPixels(themeTokens, width, height, defaultBasePixels: 4, minimum: 2, "topBandRadius");
        var eyebrowFontSize = ResolveTokenPixels(themeTokens, width, height, defaultBasePixels: isHero ? 26 : (isCompact ? 22 : 24), minimum: 14, "socialCard", "eyebrowFontSize");
        var titleFontSize = ResolveTokenPixels(themeTokens, width, height, defaultBasePixels: isHero ? 70 : (isCompact ? 52 : 60), minimum: isCompact ? 28 : 32, "socialCard", "titleFontSize");
        var descriptionFontSize = ResolveTokenPixels(themeTokens, width, height, defaultBasePixels: isHero ? 34 : (isCompact ? 28 : 32), minimum: isCompact ? 16 : 18, "socialCard", "descriptionFontSize");
        var footerFontSize = ResolveTokenPixels(themeTokens, width, height, defaultBasePixels: isHero ? 24 : (isCompact ? 20 : 22), minimum: 13, "socialCard", "footerFontSize");
        var footerRectHeight = ResolveTokenPixels(themeTokens, width, height, defaultBasePixels: isHero ? 50 : (isCompact ? 42 : 48), minimum: 30, "socialCard", "footerHeight");
        var footerRectRadius = ResolveRadiusPixels(themeTokens, width, height, defaultBasePixels: 14, minimum: 8, "footerRadius");
        var footerRectY = safeBottom - footerRectHeight;
        var footerRectX = safeLeft;
        var footerTextInsetX = ResolveTokenPixels(themeTokens, width, height, defaultBasePixels: 20, minimum: 12, "socialCard", "footerPaddingX");
        var footerTextWidth = EstimateTextWidth(TrimSingleLine(normalizedFooterLabel, 64), footerFontSize, glyphFactor: 0.52);
        var footerRectMinWidth = ResolveTokenPixels(themeTokens, width, height, defaultBasePixels: 180, minimum: 120, "socialCard", "footerMinWidth");
        var footerRectMaxWidth = Math.Min(
            safeWidth,
            ResolveTokenPixels(themeTokens, width, height, defaultBasePixels: isHero ? 440 : (isCompact ? 460 : 520), minimum: 220, "socialCard", "footerMaxWidth"));
        var footerRectWidth = Math.Clamp(footerTextWidth + (footerTextInsetX * 2), footerRectMinWidth, footerRectMaxWidth);
        var footerTextY = footerRectY + (footerRectHeight / 2);
        var pillPaddingX = ResolveTokenPixels(themeTokens, width, height, defaultBasePixels: 14, minimum: 8, "socialCard", "badgePaddingX");
        var pillHeight = ResolveTokenPixels(themeTokens, width, height, defaultBasePixels: isHero ? 42 : (isCompact ? 36 : 40), minimum: 26, "socialCard", "badgeHeight");
        var pillRadius = ResolveRadiusPixels(themeTokens, width, height, defaultBasePixels: 20, minimum: 13, "badgeRadius");
        var pillFontSize = ResolveTokenPixels(themeTokens, width, height, defaultBasePixels: isHero ? 19 : (isCompact ? 16 : 18), minimum: 12, "socialCard", "badgeFontSize");
        var pillMaxWidth = Math.Min(safeWidth, ResolveTokenPixels(themeTokens, width, height, defaultBasePixels: 320, minimum: 192, "socialCard", "badgeMaxWidth"));
        var pillMinWidth = ResolveTokenPixels(themeTokens, width, height, defaultBasePixels: 148, minimum: 112, "socialCard", "badgeMinWidth");
        var pillTextWidth = EstimateTextWidth(TrimSingleLine(normalizedBadge.ToUpperInvariant(), 24), pillFontSize);
        var pillWidth = Math.Clamp(pillTextWidth + (pillPaddingX * 2), pillMinWidth, pillMaxWidth);
        var pillX = ResolveBadgeX(themeTokens, safeLeft, safeRight, pillWidth);
        var pillY = safeTop;
        var pillTextX = pillX + (pillWidth / 2);
        var pillTextY = pillY + (pillHeight / 2);
        var glowRadiusLarge = GetScaledPixels(width, height, basePixels: 240, minimum: 140);
        var glowRadiusSmall = GetScaledPixels(width, height, basePixels: 150, minimum: 88);
        var rightGlowX = width - GetScaledPixels(width, height, basePixels: 130, minimum: 84);
        var rightGlowY = GetScaledPixels(width, height, basePixels: 126, minimum: 76);
        var leftGlowX = GetScaledPixels(width, height, basePixels: 180, minimum: 108);
        var leftGlowY = height - GetScaledPixels(width, height, basePixels: 104, minimum: 66);
        var badgeGap = ResolveTokenPixels(themeTokens, width, height, defaultBasePixels: 20, minimum: 10, "socialCard", "badgeGap");
        var eyebrowX = safeLeft;
        var eyebrowY = safeTop + GetScaledPixels(width, height, basePixels: 26, minimum: 18);
        if (string.Equals(ReadThemeToken(themeTokens, "socialCard", "badgeAlign"), "left", StringComparison.OrdinalIgnoreCase))
        {
            var shiftedEyebrowX = pillX + pillWidth + badgeGap;
            var remainingWidth = safeRight - shiftedEyebrowX;
            if (remainingWidth >= Math.Max(160, safeWidth / 3))
            {
                eyebrowX = shiftedEyebrowX;
            }
            else
            {
                eyebrowY = pillY + pillHeight + badgeGap;
            }
        }

        var titleY = eyebrowY + GetScaledPixels(width, height, basePixels: isHero ? 106 : (isCompact ? 82 : 94), minimum: 56);
        var titleLineHeight = GetScaledPixels(width, height, basePixels: isHero ? 66 : (isCompact ? 54 : 62), minimum: 30);
        var descriptionLineHeight = GetScaledPixels(width, height, basePixels: isHero ? 36 : (isCompact ? 31 : 34), minimum: 20);
        var titleMaxLines = isHero ? 2 : 3;
        var descriptionMaxLines = isHero ? 2 : (isCompact ? 4 : 3);
        var titleLines = WrapText(primaryTitle, maxChars: GetTitleWrapWidth(safeWidth, titleFontSize), maxLines: titleMaxLines);
        var descriptionY = titleY + (titleLines.Count * titleLineHeight) + GetScaledPixels(width, height, basePixels: 24, minimum: 16);
        var descriptionBottomY = footerRectY - GetScaledPixels(width, height, basePixels: 24, minimum: 16);
        var maxDescriptionLines = Math.Max(
            1,
            (descriptionBottomY - descriptionY) / descriptionLineHeight);
        var descriptionLines = WrapText(
            primaryDescription,
            maxChars: GetDescriptionWrapWidth(safeWidth, descriptionFontSize),
            maxLines: Math.Min(descriptionMaxLines, maxDescriptionLines));
        var accentLineY = footerRectY - GetScaledPixels(width, height, basePixels: isHero ? 42 : (isCompact ? 32 : 36), minimum: 22);
        var accentLineX = safeLeft;
        var accentLineWidth = Math.Max(GetScaledPixels(width, height, basePixels: 160, minimum: 120), safeWidth);
        var frameRadius = ResolveRadiusPixels(themeTokens, width, height, defaultBasePixels: 24, minimum: 12, "frameRadius");
        var panelRadius = ResolveRadiusPixels(themeTokens, width, height, defaultBasePixels: 20, minimum: 10, "panelRadius");

        var svg = new StringBuilder();
        svg.AppendLine($@"<svg xmlns=""http://www.w3.org/2000/svg"" width=""{width}"" height=""{height}"" viewBox=""0 0 {width} {height}"">");
        svg.AppendLine(@"  <defs>");
        svg.AppendLine(@"    <linearGradient id=""bg"" x1=""0%"" y1=""0%"" x2=""100%"" y2=""100%"">");
        svg.AppendLine($@"      <stop offset=""0%"" stop-color=""{palette.BackgroundStart}""/>");
        svg.AppendLine($@"      <stop offset=""58%"" stop-color=""{palette.BackgroundMid}""/>");
        svg.AppendLine($@"      <stop offset=""100%"" stop-color=""{palette.BackgroundEnd}""/>");
        svg.AppendLine(@"    </linearGradient>");
        svg.AppendLine(@"    <radialGradient id=""orbA"" cx=""50%"" cy=""50%"" r=""50%"">");
        svg.AppendLine($@"      <stop offset=""0%"" stop-color=""{palette.AccentSoft}"" stop-opacity=""0.28""/>");
        svg.AppendLine($@"      <stop offset=""100%"" stop-color=""{palette.Accent}"" stop-opacity=""0""/>");
        svg.AppendLine(@"    </radialGradient>");
        svg.AppendLine(@"    <radialGradient id=""orbB"" cx=""50%"" cy=""50%"" r=""50%"">");
        svg.AppendLine($@"      <stop offset=""0%"" stop-color=""{palette.Accent}"" stop-opacity=""0.18""/>");
        svg.AppendLine($@"      <stop offset=""100%"" stop-color=""{palette.AccentStrong}"" stop-opacity=""0""/>");
        svg.AppendLine(@"    </radialGradient>");
        svg.AppendLine(@"    <linearGradient id=""topBand"" x1=""0%"" y1=""0%"" x2=""100%"" y2=""0%"">");
        svg.AppendLine($@"      <stop offset=""0%"" stop-color=""{palette.AccentSoft}"" stop-opacity=""0.42""/>");
        svg.AppendLine($@"      <stop offset=""100%"" stop-color=""{palette.Accent}"" stop-opacity=""0.2""/>");
        svg.AppendLine(@"    </linearGradient>");
        svg.AppendLine(@"    <linearGradient id=""accentLine"" x1=""0%"" y1=""0%"" x2=""100%"" y2=""0%"">");
        svg.AppendLine($@"      <stop offset=""0%"" stop-color=""{palette.Accent}"" stop-opacity=""0.62""/>");
        svg.AppendLine($@"      <stop offset=""100%"" stop-color=""{palette.AccentStrong}"" stop-opacity=""0.2""/>");
        svg.AppendLine(@"    </linearGradient>");
        svg.AppendLine(@"  </defs>");
        svg.AppendLine($@"  <rect x=""0"" y=""0"" width=""{width}"" height=""{height}"" fill=""url(#bg)""/>");
        svg.AppendLine($@"  <circle cx=""{rightGlowX}"" cy=""{rightGlowY}"" r=""{glowRadiusLarge}"" fill=""url(#orbA)"" />");
        svg.AppendLine($@"  <circle cx=""{leftGlowX}"" cy=""{leftGlowY}"" r=""{glowRadiusSmall}"" fill=""url(#orbB)"" />");
        svg.AppendLine($@"  <rect x=""{frameInset}"" y=""{frameInset}"" width=""{width - (frameInset * 2)}"" height=""{height - (frameInset * 2)}"" rx=""{frameRadius}"" fill=""rgba(7,12,26,0.32)"" stroke=""rgba(148,163,184,0.26)""/>");
        svg.AppendLine($@"  <rect x=""{panelInset}"" y=""{panelInset}"" width=""{panelWidth}"" height=""{panelHeight}"" rx=""{panelRadius}"" fill=""{palette.Surface}"" fill-opacity=""0.44"" stroke=""{palette.SurfaceStroke}"" stroke-opacity=""0.36""/>");
        svg.AppendLine($@"  <rect x=""{panelInset}"" y=""{panelInset}"" width=""{panelWidth}"" height=""{topBandHeight}"" rx=""{topBandRadius}"" fill=""url(#topBand)""/>");
        svg.AppendLine($@"  <rect x=""{accentLineX}"" y=""{accentLineY}"" width=""{accentLineWidth}"" height=""2"" rx=""1"" fill=""url(#accentLine)""/>");

        svg.AppendLine($@"  <rect x=""{pillX}"" y=""{pillY}"" width=""{pillWidth}"" height=""{pillHeight}"" rx=""{pillRadius}"" fill=""{palette.ChipBackground}"" fill-opacity=""0.78"" stroke=""{palette.ChipBorder}"" stroke-opacity=""0.76""/>");
        svg.AppendLine($@"  <text x=""{pillTextX}"" y=""{pillTextY}"" fill=""{palette.ChipText}"" font-size=""{pillFontSize}"" font-family=""{EscapeXml(type.BadgeFontFamily)}"" font-weight=""700"" dominant-baseline=""middle"" text-anchor=""middle"">{safeBadgeUpper}</text>");

        svg.AppendLine($@"  <text x=""{eyebrowX}"" y=""{eyebrowY}"" fill=""{palette.AccentSoft}"" font-size=""{eyebrowFontSize}"" font-family=""{EscapeXml(type.EyebrowFontFamily)}"" font-weight=""700"">");
        svg.AppendLine($"    {safeEyebrow}");
        svg.AppendLine(@"  </text>");

        for (var i = 0; i < titleLines.Count; i++)
        {
            var y = titleY + (i * titleLineHeight);
            svg.AppendLine($@"  <text x=""{safeLeft}"" y=""{y}"" fill=""{palette.TextPrimary}"" font-size=""{titleFontSize}"" font-family=""{EscapeXml(type.TitleFontFamily)}"" font-weight=""800"">{EscapeXml(titleLines[i])}</text>");
        }

        for (var i = 0; i < descriptionLines.Count; i++)
        {
            var y = descriptionY + (i * descriptionLineHeight);
            svg.AppendLine($@"  <text x=""{safeLeft}"" y=""{y}"" fill=""{palette.TextSecondary}"" font-size=""{descriptionFontSize}"" font-family=""{EscapeXml(type.BodyFontFamily)}"" font-weight=""500"">{EscapeXml(descriptionLines[i])}</text>");
        }

        svg.AppendLine($@"  <rect x=""{footerRectX}"" y=""{footerRectY}"" rx=""{footerRectRadius}"" ry=""{footerRectRadius}"" width=""{footerRectWidth}"" height=""{footerRectHeight}"" fill=""{palette.ChipBackground}"" fill-opacity=""0.68"" stroke=""{palette.ChipBorder}"" stroke-opacity=""0.58""/>");
        svg.AppendLine($@"  <text x=""{footerRectX + (footerRectWidth / 2)}"" y=""{footerTextY}"" fill=""{palette.AccentSoft}"" font-size=""{footerFontSize}"" font-family=""{EscapeXml(type.FooterFontFamily)}"" font-weight=""700"" dominant-baseline=""middle"" text-anchor=""middle"">{safeFooter}</text>");
        svg.AppendLine(@"</svg>");
        return svg.ToString();
    }

    private static List<string> WrapText(string? value, int maxChars, int maxLines)
    {
        maxChars = Math.Max(8, maxChars);
        maxLines = Math.Max(1, maxLines);

        var input = NormalizeWrapInput(value);
        if (string.IsNullOrWhiteSpace(input))
            return new List<string> { string.Empty };

        var words = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = new StringBuilder();

        foreach (var word in words)
        {
            foreach (var token in SplitToken(word, maxChars))
            {
                if (current.Length == 0)
                {
                    current.Append(token);
                    continue;
                }

                if (current.Length + 1 + token.Length <= maxChars)
                {
                    current.Append(' ').Append(token);
                    continue;
                }

                lines.Add(current.ToString());
                if (lines.Count >= maxLines)
                    return ClampLineCount(lines, maxLines);
                current.Clear();
                current.Append(token);
            }
        }

        if (current.Length > 0)
            lines.Add(current.ToString());

        return ClampLineCount(lines, maxLines);
    }

    private static List<string> ClampLineCount(List<string> lines, int maxLines)
    {
        if (lines.Count <= maxLines)
            return lines;

        var clamped = lines.Take(maxLines).ToList();
        if (clamped[^1].Length > 3)
            clamped[^1] = clamped[^1][..^3] + "...";
        else
            clamped[^1] += "...";
        return clamped;
    }

    private static IEnumerable<string> SplitToken(string token, int maxChars)
    {
        if (string.IsNullOrEmpty(token))
            yield break;

        if (token.Length <= maxChars)
        {
            yield return token;
            yield break;
        }

        var remaining = token;
        var chunkSize = Math.Max(3, maxChars);
        while (remaining.Length > maxChars)
        {
            yield return remaining[..chunkSize];
            remaining = remaining[chunkSize..];
        }

        if (!string.IsNullOrEmpty(remaining))
            yield return remaining;
    }

    private static int GetScaledPixels(int width, int height, int basePixels, int minimum)
    {
        var scale = Math.Min(width / 1200d, height / 630d);
        return Math.Max(minimum, (int)Math.Round(basePixels * scale));
    }

    internal static (int FontSize, int LineHeight, List<string> Lines) AdaptTitleSize(
        string title,
        int baseFontSize,
        int baseLineHeight,
        int contentWidth,
        int maxLines,
        int width,
        int height)
    {
        var minFontSize = Math.Max(24, (int)Math.Round(baseFontSize * 0.75));
        var fontSize = baseFontSize;
        var lineHeight = baseLineHeight;
        var lines = WrapText(title, GetTitleWrapWidth(contentWidth, fontSize), maxLines);

        if (lines.Count > 0 && lines[^1].EndsWith("...", StringComparison.Ordinal))
        {
            var reducedFontSize = Math.Max(minFontSize, (int)Math.Round(baseFontSize * 0.82));
            var reducedLineHeight = Math.Max(24, (int)Math.Round(baseLineHeight * 0.82));
            var reducedLines = WrapText(title, GetTitleWrapWidth(contentWidth, reducedFontSize), maxLines);
            if (reducedLines.Count > 0)
            {
                var currentVisibleChars = CountVisibleCharacters(lines);
                var reducedVisibleChars = CountVisibleCharacters(reducedLines);
                var reducedStillTruncates = reducedLines[^1].EndsWith("...", StringComparison.Ordinal);
                if (!reducedStillTruncates || reducedVisibleChars > currentVisibleChars)
                {
                    fontSize = reducedFontSize;
                    lineHeight = reducedLineHeight;
                    lines = reducedLines;
                }
            }
        }

        return (fontSize, lineHeight, lines);
    }

    private static int CountVisibleCharacters(IReadOnlyList<string> lines)
    {
        var count = 0;
        foreach (var line in lines)
        {
            if (string.IsNullOrEmpty(line))
                continue;

            count += line.EndsWith("...", StringComparison.Ordinal)
                ? Math.Max(0, line.Length - 3)
                : line.Length;
        }

        return count;
    }

    private static int GetTitleWrapWidth(int safeWidth, int fontSize)
    {
        return EstimateCharsFromWidth(safeWidth, fontSize, minChars: 20, maxChars: 42);
    }

    private static int GetDescriptionWrapWidth(int safeWidth, int fontSize)
    {
        return EstimateCharsFromWidth(safeWidth, fontSize, minChars: 34, maxChars: 74);
    }

    private static int EstimateCharsFromWidth(int pixelWidth, int fontSize, int minChars, int maxChars)
    {
        if (pixelWidth <= 0 || fontSize <= 0)
            return minChars;

        var estimated = (int)Math.Floor(pixelWidth / Math.Max(1d, fontSize * 0.56));
        return Math.Clamp(estimated, minChars, maxChars);
    }

    private static int EstimateTextWidth(string text, int fontSize, double glyphFactor = 0.56)
    {
        if (string.IsNullOrWhiteSpace(text) || fontSize <= 0)
            return 0;
        return (int)Math.Ceiling(text.Length * fontSize * glyphFactor);
    }

    internal static string NormalizeDisplayTextForWrap(string? value)
    {
        var input = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        return WhitespaceRegex.Replace(input, " ").Trim();
    }

    private static string NormalizeWrapInput(string? value)
    {
        return NormalizeDisplayTextForWrap(value);
    }

    internal static SocialPalette SelectPalette(string styleKey, string seed, IReadOnlyDictionary<string, object?>? themeTokens = null, string? colorScheme = null)
    {
        if (TryResolveThemePalette(themeTokens, colorScheme, out var themed))
            return themed;

        var isLight = string.Equals(colorScheme, "light", StringComparison.OrdinalIgnoreCase);
        var pool = isLight ? LightPalettes : Palettes;

        if (pool.Length == 0)
            return Palettes[0];

        var input = Encoding.UTF8.GetBytes(seed ?? string.Empty);
        var hash = SHA256.HashData(input);
        var candidates = styleKey switch
        {
            "api" => new[] { isLight ? 2 : 0, isLight ? 2 : 2 },
            "docs" => new[] { isLight ? 2 : 2, isLight ? 2 : 0 },
            "blog" => new[] { isLight ? 3 : 3, isLight ? 3 : 1 },
            "contact" => new[] { isLight ? 1 : 3, isLight ? 1 : 0 },
            "home" => new[] { isLight ? 0 : 1, isLight ? 0 : 0 },
            _ => new[] { isLight ? 0 : 1, isLight ? 0 : 0 }
        };
        var candidate = candidates[hash[0] % candidates.Length];
        return pool[candidate % pool.Length];
    }

    private static bool TryResolveThemePalette(IReadOnlyDictionary<string, object?>? themeTokens, string? colorScheme, out SocialPalette palette)
    {
        palette = default!;
        if (themeTokens is null || themeTokens.Count == 0)
            return false;

        var accent = ReadThemeToken(themeTokens, "socialCard", "accent") ??
                     ReadThemeToken(themeTokens, "color", "accent");
        var backgroundStart = ReadThemeToken(themeTokens, "socialCard", "backgroundStart") ??
                              ReadThemeToken(themeTokens, "color", "bg");
        var surface = ReadThemeToken(themeTokens, "socialCard", "surface") ??
                      ReadThemeToken(themeTokens, "color", "panel");
        var textPrimary = ReadThemeToken(themeTokens, "socialCard", "textPrimary") ??
                          ReadThemeToken(themeTokens, "color", "ink");
        var textSecondary = ReadThemeToken(themeTokens, "socialCard", "textSecondary") ??
                            ReadThemeToken(themeTokens, "color", "muted");
        var surfaceStroke = ReadThemeToken(themeTokens, "socialCard", "surfaceStroke") ??
                            ReadThemeToken(themeTokens, "color", "border");

        if (string.IsNullOrWhiteSpace(accent) ||
            string.IsNullOrWhiteSpace(backgroundStart) ||
            string.IsNullOrWhiteSpace(surface) ||
            string.IsNullOrWhiteSpace(textPrimary))
        {
            return false;
        }

        var backgroundMid = ReadThemeToken(themeTokens, "socialCard", "backgroundMid") ?? surface;
        var backgroundEnd = ReadThemeToken(themeTokens, "socialCard", "backgroundEnd") ?? backgroundStart;
        var accentSoft = ReadThemeToken(themeTokens, "socialCard", "accentSoft") ?? accent;
        var accentStrong = ReadThemeToken(themeTokens, "socialCard", "accentStrong") ?? textPrimary;
        var resolvedTextSecondary = string.IsNullOrWhiteSpace(textSecondary) ? textPrimary : textSecondary;
        var resolvedSurfaceStroke = string.IsNullOrWhiteSpace(surfaceStroke) ? accent : surfaceStroke;
        var chipBackground = ReadThemeToken(themeTokens, "socialCard", "chipBackground") ?? surface;
        var chipBorder = ReadThemeToken(themeTokens, "socialCard", "chipBorder") ?? resolvedSurfaceStroke;
        var chipText = ReadThemeToken(themeTokens, "socialCard", "chipText") ?? textPrimary;

        var basePalette = new SocialPalette(
            backgroundStart,
            backgroundMid,
            backgroundEnd,
            surface,
            resolvedSurfaceStroke,
            accent,
            accentSoft,
            accentStrong,
            textPrimary,
            resolvedTextSecondary,
            chipBackground,
            chipBorder,
            chipText);

        palette = string.Equals(colorScheme, "light", StringComparison.OrdinalIgnoreCase)
            ? BuildLightThemePalette(themeTokens, basePalette)
            : basePalette;
        return true;
    }

    private static SocialPalette BuildLightThemePalette(IReadOnlyDictionary<string, object?>? themeTokens, SocialPalette basePalette)
    {
        return new SocialPalette(
            ReadThemeToken(themeTokens, "socialCardLight", "backgroundStart") ?? BlendHexColor(basePalette.BackgroundStart, "#ffffff", 0.92),
            ReadThemeToken(themeTokens, "socialCardLight", "backgroundMid") ?? BlendHexColor(basePalette.BackgroundMid, "#ffffff", 0.88),
            ReadThemeToken(themeTokens, "socialCardLight", "backgroundEnd") ?? BlendHexColor(basePalette.BackgroundEnd, "#ffffff", 0.84),
            ReadThemeToken(themeTokens, "socialCardLight", "surface") ?? BlendHexColor(basePalette.Surface, "#ffffff", 0.82),
            ReadThemeToken(themeTokens, "socialCardLight", "surfaceStroke") ?? BlendHexColor(basePalette.SurfaceStroke, "#cbd5e1", 0.4),
            ReadThemeToken(themeTokens, "socialCardLight", "accent") ?? basePalette.Accent,
            ReadThemeToken(themeTokens, "socialCardLight", "accentSoft") ?? BlendHexColor(basePalette.AccentSoft, "#ffffff", 0.32),
            ReadThemeToken(themeTokens, "socialCardLight", "accentStrong") ?? BlendHexColor(basePalette.AccentStrong, "#0f172a", 0.18),
            ReadThemeToken(themeTokens, "socialCardLight", "textPrimary") ?? "#0f172a",
            ReadThemeToken(themeTokens, "socialCardLight", "textSecondary") ?? "#475569",
            ReadThemeToken(themeTokens, "socialCardLight", "chipBackground") ?? BlendHexColor(basePalette.ChipBackground, "#ffffff", 0.7),
            ReadThemeToken(themeTokens, "socialCardLight", "chipBorder") ?? BlendHexColor(basePalette.ChipBorder, "#cbd5e1", 0.28),
            ReadThemeToken(themeTokens, "socialCardLight", "chipText") ?? "#0f172a");
    }

    private static string BlendHexColor(string source, string target, double amount)
    {
        if (!TryParseHexColor(source, out var sourceRed, out var sourceGreen, out var sourceBlue) ||
            !TryParseHexColor(target, out var targetRed, out var targetGreen, out var targetBlue))
        {
            return source;
        }

        var mix = Math.Clamp(amount, 0d, 1d);
        var red = (int)Math.Round(sourceRed + ((targetRed - sourceRed) * mix));
        var green = (int)Math.Round(sourceGreen + ((targetGreen - sourceGreen) * mix));
        var blue = (int)Math.Round(sourceBlue + ((targetBlue - sourceBlue) * mix));
        return $"#{red:X2}{green:X2}{blue:X2}";
    }

    private static bool TryParseHexColor(string? value, out int red, out int green, out int blue)
    {
        red = 0;
        green = 0;
        blue = 0;

        var candidate = (value ?? string.Empty).Trim();
        if (candidate.StartsWith('#'))
            candidate = candidate[1..];

        if (candidate.Length == 3)
        {
            candidate = string.Concat(candidate.Select(static ch => new string(ch, 2)));
        }

        if (candidate.Length != 6)
            return false;

        if (!int.TryParse(candidate[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out red) ||
            !int.TryParse(candidate.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out green) ||
            !int.TryParse(candidate.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out blue))
        {
            red = 0;
            green = 0;
            blue = 0;
            return false;
        }

        return true;
    }

    private static string? ReadThemeToken(IReadOnlyDictionary<string, object?>? map, params string[] path)
    {
        if (map is null || path is null || path.Length == 0)
            return null;

        IReadOnlyDictionary<string, object?>? currentMap = map;
        object? currentValue = null;
        for (var i = 0; i < path.Length; i++)
        {
            if (currentMap is null || !currentMap.TryGetValue(path[i], out currentValue))
                return null;

            if (i == path.Length - 1)
                return currentValue?.ToString();

            currentMap = currentValue as IReadOnlyDictionary<string, object?>;
        }

        return currentValue?.ToString();
    }

    private static SocialCardTypography ResolveTypography(IReadOnlyDictionary<string, object?>? themeTokens)
    {
        var display = NormalizeFontFamily(
            ReadThemeToken(themeTokens, "socialCard", "fontDisplay") ??
            ReadThemeToken(themeTokens, "font", "display"),
            "Segoe UI, Arial, sans-serif");
        var body = NormalizeFontFamily(
            ReadThemeToken(themeTokens, "socialCard", "fontBody") ??
            ReadThemeToken(themeTokens, "font", "body"),
            "Segoe UI, Arial, sans-serif");
        var mono = NormalizeFontFamily(
            ReadThemeToken(themeTokens, "socialCard", "fontMono") ??
            ReadThemeToken(themeTokens, "font", "mono"),
            "Cascadia Code, Consolas, monospace");
        var eyebrow = NormalizeFontFamily(ReadThemeToken(themeTokens, "socialCard", "fontEyebrow"), display);
        var badge = NormalizeFontFamily(ReadThemeToken(themeTokens, "socialCard", "fontBadge"), body);
        var footer = NormalizeFontFamily(ReadThemeToken(themeTokens, "socialCard", "fontFooter"), body);
        return new SocialCardTypography(display, body, mono, eyebrow, badge, footer);
    }

    private static string NormalizeFontFamily(string? value, string fallback)
    {
        var candidate = (value ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(candidate) ? fallback : candidate;
    }

    private static int ResolveBadgeX(IReadOnlyDictionary<string, object?>? themeTokens, int safeLeft, int safeRight, int pillWidth)
    {
        var align = ReadThemeToken(themeTokens, "socialCard", "badgeAlign");
        if (string.Equals(align, "left", StringComparison.OrdinalIgnoreCase))
            return safeLeft;

        return safeRight - pillWidth;
    }

    private static int ResolveRadiusPixels(
        IReadOnlyDictionary<string, object?>? themeTokens,
        int width,
        int height,
        int defaultBasePixels,
        int minimum,
        string socialCardKey)
    {
        var raw = ReadThemeToken(themeTokens, "socialCard", socialCardKey);
        if (TryParsePixelishInt(raw, out var value))
            return Math.Max(minimum, value);

        var genericRadiusKey = socialCardKey switch
        {
            "panelRadius" => "base",
            "frameRadius" => "base",
            "footerRadius" => "sm",
            "badgeRadius" => "sm",
            "topBandRadius" => "sm",
            _ => string.Empty
        };

        if (!string.IsNullOrWhiteSpace(genericRadiusKey) &&
            TryParsePixelishInt(ReadThemeToken(themeTokens, "radius", genericRadiusKey), out value))
        {
            return Math.Max(minimum, value);
        }

        return GetScaledPixels(width, height, defaultBasePixels, minimum);
    }

    private static int ResolveTokenPixels(
        IReadOnlyDictionary<string, object?>? themeTokens,
        int width,
        int height,
        int defaultBasePixels,
        int minimum,
        params string[] path)
    {
        if (TryParsePixelishInt(ReadThemeToken(themeTokens, path), out var value))
            return Math.Max(minimum, value);

        return GetScaledPixels(width, height, defaultBasePixels, minimum);
    }

    private static bool TryParsePixelishInt(string? raw, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var candidate = raw.Trim();
        if (candidate.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            candidate = candidate[..^2].Trim();

        if (int.TryParse(candidate, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            return true;

        if (double.TryParse(candidate, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            value = (int)Math.Round(number);
            return true;
        }

        return false;
    }

    private static string ClassifyStyle(string badge, string footerLabel)
    {
        var combined = string.Concat(badge ?? string.Empty, " ", footerLabel ?? string.Empty).ToLowerInvariant();
        if (combined.Contains("home", StringComparison.Ordinal))
            return "home";
        if (combined.Contains("api", StringComparison.Ordinal))
            return "api";
        if (combined.Contains("doc", StringComparison.Ordinal))
            return "docs";
        if (combined.Contains("contact", StringComparison.Ordinal) ||
            combined.Contains("support", StringComparison.Ordinal))
            return "contact";
        if (combined.Contains("blog", StringComparison.Ordinal) ||
            combined.Contains("post", StringComparison.Ordinal) ||
            combined.Contains("news", StringComparison.Ordinal) ||
            combined.Contains("article", StringComparison.Ordinal))
            return "blog";
        return "default";
    }

    private static string ClassifyVariant(string styleKey, string badge, string footerLabel)
    {
        var normalizedBadge = (badge ?? string.Empty).Trim();
        var normalizedFooter = (footerLabel ?? string.Empty).Trim();
        if (normalizedBadge.Equals("HOME", StringComparison.OrdinalIgnoreCase) ||
            normalizedFooter.Equals("/", StringComparison.OrdinalIgnoreCase))
            return "spotlight";

        return styleKey switch
        {
            "home" => "spotlight",
            "docs" => "shelf",
            "api" => "reference",
            "blog" => "editorial",
            "contact" => "connect",
            _ => "product"
        };
    }

    private static string? NormalizeStyle(string? style)
    {
        if (string.IsNullOrWhiteSpace(style))
            return null;

        return style.Trim().ToLowerInvariant() switch
        {
            "default" => "default",
            "platform" => "default",
            "product" => "default",
            "home" => "home",
            "landing" => "home",
            "docs" => "docs",
            "documentation" => "docs",
            "knowledge" => "docs",
            "api" => "api",
            "reference" => "api",
            "editorial" => "blog",
            "blog" => "blog",
            "news" => "blog",
            "article" => "blog",
            "marketing" => "blog",
            "contact" => "contact",
            "contacts" => "contact",
            "support" => "contact",
            _ => null
        };
    }

    private static string? NormalizeVariant(string? variant)
    {
        if (string.IsNullOrWhiteSpace(variant))
            return null;

        return variant.Trim().ToLowerInvariant() switch
        {
            "standard" => "product",
            "default" => "product",
            "product" => "product",
            "compact" => "shelf",
            "shelf" => "shelf",
            "docs" => "shelf",
            "hero" => "spotlight",
            "featured" => "spotlight",
            "spotlight" => "spotlight",
            "home" => "spotlight",
            "reference" => "reference",
            "api" => "reference",
            "editorial" => "editorial",
            "imageinline" => "inline-image",
            "image-inline" => "inline-image",
            "inline-image" => "inline-image",
            "connect" => "connect",
            "contact" => "connect",
            _ => null
        };
    }

    private static string NormalizeBadgeLabel(string badge, string footerLabel, string styleKey, string variantKey)
    {
        var candidate = TrimSingleLine(badge, 80).Trim();
        if (string.IsNullOrWhiteSpace(candidate))
            candidate = DefaultBadgeForStyle(styleKey, variantKey);

        if (IsRouteLike(candidate))
            candidate = DefaultBadgeForStyle(styleKey, variantKey);

        var footer = TrimSingleLine(footerLabel, 120).Trim();
        if (candidate.Length > 16)
        {
            if (LooksLikeApiReferenceLabel(candidate) || LooksLikeApiReferenceLabel(footer))
                candidate = "API";
            else if (LooksLikeDocsLabel(candidate) || LooksLikeDocsLabel(footer))
                candidate = "DOCS";
            else if (LooksLikeContactLabel(candidate) || LooksLikeContactLabel(footer))
                candidate = "CONTACT";
            else if (LooksLikeBlogLabel(candidate) || LooksLikeBlogLabel(footer))
                candidate = "BLOG";
            else
                candidate = DefaultBadgeForStyle(styleKey, variantKey);
        }

        return string.IsNullOrWhiteSpace(candidate)
            ? DefaultBadgeForStyle(styleKey, variantKey)
            : candidate;
    }

    private static string NormalizeFooterLabel(string footerLabel, string badge, string styleKey)
    {
        var candidate = TrimSingleLine(footerLabel, 180).Trim();
        if (string.IsNullOrWhiteSpace(candidate))
            return DefaultRouteForStyle(styleKey);

        if (IsRouteLike(candidate))
            return AbbreviateRouteLabel(NormalizeRouteLabel(candidate));

        if (string.Equals(candidate, badge, StringComparison.OrdinalIgnoreCase))
            return DefaultRouteForStyle(styleKey);

        if (LooksLikeApiReferenceLabel(candidate))
            return "/api";
        if (LooksLikeDocsLabel(candidate))
            return "/docs";
        if (LooksLikeContactLabel(candidate))
            return "/contact";
        if (LooksLikeBlogLabel(candidate))
            return "/blog";
        if (string.Equals(candidate, "home", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate, "index", StringComparison.OrdinalIgnoreCase))
            return "/";

        var slug = SlugifyRouteLabel(candidate);
        if (string.IsNullOrWhiteSpace(slug))
            return DefaultRouteForStyle(styleKey);
        return AbbreviateRouteLabel("/" + slug);
    }

    private static string DefaultBadgeForStyle(string styleKey, string variantKey)
    {
        if (string.Equals(variantKey, "spotlight", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(styleKey, "home", StringComparison.OrdinalIgnoreCase))
            return "HOME";

        return styleKey switch
        {
            "api" => "API",
            "docs" => "DOCS",
            "blog" => "BLOG",
            "contact" => "CONTACT",
            _ => "PAGE"
        };
    }

    private static string DefaultRouteForStyle(string styleKey)
    {
        return styleKey switch
        {
            "api" => "/api",
            "docs" => "/docs",
            "blog" => "/blog",
            "contact" => "/contact",
            _ => "/"
        };
    }

    private static bool IsRouteLike(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        return trimmed.StartsWith("/", StringComparison.Ordinal) ||
               trimmed.Contains('/', StringComparison.Ordinal) ||
               trimmed.Contains('\\', StringComparison.Ordinal);
    }

    private static string NormalizeRouteLabel(string value)
    {
        var trimmed = value.Replace('\\', '/').Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return "/";
        if (!trimmed.StartsWith("/", StringComparison.Ordinal))
            trimmed = "/" + trimmed;
        return trimmed;
    }

    private static string AbbreviateRouteLabel(string route)
    {
        if (string.IsNullOrWhiteSpace(route))
            return "/";

        var normalized = NormalizeRouteLabel(route);
        if (normalized.Length <= 40)
            return normalized;

        var segments = normalized
            .Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length <= 1)
            return normalized[..37] + "...";

        var first = segments[0];
        var last = segments[^1];
        if (segments.Length >= 3 &&
            string.Equals(first, "api", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(segments[1], "powershell", StringComparison.OrdinalIgnoreCase))
            return $"/api/powershell/.../{last}";

        return $"/{first}/.../{last}";
    }

    private static bool LooksLikeApiReferenceLabel(string value)
    {
        return ContainsToken(value, "api") && ContainsToken(value, "reference");
    }

    private static bool LooksLikeDocsLabel(string value)
    {
        return ContainsToken(value, "docs") || ContainsToken(value, "documentation");
    }

    private static bool LooksLikeBlogLabel(string value)
    {
        return ContainsToken(value, "blog") || ContainsToken(value, "post") || ContainsToken(value, "news");
    }

    private static bool LooksLikeContactLabel(string value)
    {
        return ContainsToken(value, "contact") || ContainsToken(value, "support");
    }

    private static bool ContainsToken(string value, string token)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(token))
            return false;
        return value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string SlugifyRouteLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var sb = new StringBuilder(value.Length);
        var lastDash = false;
        foreach (var ch in value.Trim())
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
                lastDash = false;
                continue;
            }

            if (lastDash)
                continue;

            sb.Append('-');
            lastDash = true;
        }

        return sb
            .ToString()
            .Trim('-');
    }

    private static string EscapeXml(string value)
    {
        return SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;
    }

    private static string TrimSingleLine(string? value, int maxLength)
    {
        var input = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        if (input.Length <= maxLength)
            return input;
        return input[..Math.Max(0, maxLength - 3)] + "...";
    }

    internal sealed class SocialPalette
    {
        public SocialPalette(
            string backgroundStart,
            string backgroundMid,
            string backgroundEnd,
            string surface,
            string surfaceStroke,
            string accent,
            string accentSoft,
            string accentStrong,
            string textPrimary,
            string textSecondary,
            string chipBackground,
            string chipBorder,
            string chipText)
        {
            BackgroundStart = backgroundStart;
            BackgroundMid = backgroundMid;
            BackgroundEnd = backgroundEnd;
            Surface = surface;
            SurfaceStroke = surfaceStroke;
            Accent = accent;
            AccentSoft = accentSoft;
            AccentStrong = accentStrong;
            TextPrimary = textPrimary;
            TextSecondary = textSecondary;
            ChipBackground = chipBackground;
            ChipBorder = chipBorder;
            ChipText = chipText;
        }

        public string BackgroundStart { get; }
        public string BackgroundMid { get; }
        public string BackgroundEnd { get; }
        public string Surface { get; }
        public string SurfaceStroke { get; }
        public string Accent { get; }
        public string AccentSoft { get; }
        public string AccentStrong { get; }
        public string TextPrimary { get; }
        public string TextSecondary { get; }
        public string ChipBackground { get; }
        public string ChipBorder { get; }
        public string ChipText { get; }
    }

    private sealed class SocialCardTypography
    {
        public SocialCardTypography(
            string titleFontFamily,
            string bodyFontFamily,
            string monoFontFamily,
            string eyebrowFontFamily,
            string badgeFontFamily,
            string footerFontFamily)
        {
            TitleFontFamily = titleFontFamily;
            BodyFontFamily = bodyFontFamily;
            MonoFontFamily = monoFontFamily;
            EyebrowFontFamily = eyebrowFontFamily;
            BadgeFontFamily = badgeFontFamily;
            FooterFontFamily = footerFontFamily;
        }

        public string TitleFontFamily { get; }
        public string BodyFontFamily { get; }
        public string MonoFontFamily { get; }
        public string EyebrowFontFamily { get; }
        public string BadgeFontFamily { get; }
        public string FooterFontFamily { get; }
    }
}
