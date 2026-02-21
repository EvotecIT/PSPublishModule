using System.Security;
using System.Security.Cryptography;
using System.Text;
using ImageMagick;

namespace PowerForge.Web;

internal static class WebSocialCardGenerator
{
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

    internal static byte[]? RenderPng(
        string? title,
        string? description,
        string? eyebrow,
        string? badge,
        string? footerLabel,
        int width,
        int height)
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

        var safeEyebrow = EscapeXml(TrimSingleLine(primaryEyebrow, 56));
        var safeBadgeUpper = EscapeXml(TrimSingleLine(primaryBadge.ToUpperInvariant(), 24));
        var safeFooter = EscapeXml(TrimSingleLine(primaryFooterLabel, 64));
        var styleKey = ClassifyStyle(primaryBadge, primaryFooterLabel);
        var seed = string.Join("|", styleKey, primaryTitle, primaryDescription, primaryEyebrow, primaryBadge, primaryFooterLabel, width, height);
        var palette = SelectPalette(styleKey, seed);
        var frameInset = GetScaledPixels(width, height, basePixels: 36, minimum: 22);
        var panelInset = GetScaledPixels(width, height, basePixels: 48, minimum: 30);
        var panelWidth = width - (panelInset * 2);
        var panelHeight = height - (panelInset * 2);
        var contentPadding = GetScaledPixels(width, height, basePixels: 24, minimum: 16);
        var contentLeft = panelInset + contentPadding;
        var contentRight = panelInset + panelWidth - contentPadding;
        var contentWidth = Math.Max(120, contentRight - contentLeft);
        var safeMarginX = Math.Max(contentPadding, (int)Math.Round(width * 0.08));
        var safeMarginY = Math.Max(GetScaledPixels(width, height, basePixels: 26, minimum: 16), (int)Math.Round(height * 0.09));
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
        var topBandHeight = GetScaledPixels(width, height, basePixels: 9, minimum: 5);
        var eyebrowFontSize = GetScaledPixels(width, height, basePixels: 24, minimum: 14);
        var titleFontSize = GetScaledPixels(width, height, basePixels: 60, minimum: 32);
        var descriptionFontSize = GetScaledPixels(width, height, basePixels: 32, minimum: 18);
        var footerFontSize = GetScaledPixels(width, height, basePixels: 22, minimum: 13);
        var footerRectHeight = GetScaledPixels(width, height, basePixels: 48, minimum: 30);
        var footerRectRadius = GetScaledPixels(width, height, basePixels: 14, minimum: 8);
        var footerRectY = safeBottom - footerRectHeight;
        var footerRectX = safeLeft;
        var footerTextInsetX = GetScaledPixels(width, height, basePixels: 20, minimum: 12);
        var footerTextWidth = EstimateTextWidth(TrimSingleLine(primaryFooterLabel, 64), footerFontSize, glyphFactor: 0.52);
        var footerRectMinWidth = GetScaledPixels(width, height, basePixels: 180, minimum: 120);
        var footerRectWidth = Math.Clamp(footerTextWidth + (footerTextInsetX * 2), footerRectMinWidth, safeWidth);
        var footerTextY = footerRectY + (footerRectHeight / 2);
        var pillPaddingX = GetScaledPixels(width, height, basePixels: 14, minimum: 8);
        var pillHeight = GetScaledPixels(width, height, basePixels: 40, minimum: 26);
        var pillRadius = GetScaledPixels(width, height, basePixels: 20, minimum: 13);
        var pillFontSize = GetScaledPixels(width, height, basePixels: 18, minimum: 12);
        var pillMaxWidth = Math.Min(safeWidth, GetScaledPixels(width, height, basePixels: 320, minimum: 192));
        var pillMinWidth = GetScaledPixels(width, height, basePixels: 148, minimum: 112);
        var pillTextWidth = EstimateTextWidth(TrimSingleLine(primaryBadge.ToUpperInvariant(), 24), pillFontSize);
        var pillWidth = Math.Clamp(pillTextWidth + (pillPaddingX * 2), pillMinWidth, pillMaxWidth);
        var pillX = safeRight - pillWidth;
        var pillY = safeTop;
        var pillTextX = pillX + pillPaddingX;
        var pillTextY = pillY + (pillHeight / 2);
        var glowRadiusLarge = GetScaledPixels(width, height, basePixels: 240, minimum: 140);
        var glowRadiusSmall = GetScaledPixels(width, height, basePixels: 150, minimum: 88);
        var rightGlowX = width - GetScaledPixels(width, height, basePixels: 130, minimum: 84);
        var rightGlowY = GetScaledPixels(width, height, basePixels: 126, minimum: 76);
        var leftGlowX = GetScaledPixels(width, height, basePixels: 180, minimum: 108);
        var leftGlowY = height - GetScaledPixels(width, height, basePixels: 104, minimum: 66);
        var eyebrowY = safeTop + GetScaledPixels(width, height, basePixels: 26, minimum: 18);
        var titleY = eyebrowY + GetScaledPixels(width, height, basePixels: 94, minimum: 56);
        var titleLineHeight = GetScaledPixels(width, height, basePixels: 62, minimum: 30);
        var descriptionLineHeight = GetScaledPixels(width, height, basePixels: 34, minimum: 20);
        var titleLines = WrapText(primaryTitle, maxChars: GetTitleWrapWidth(safeWidth, titleFontSize), maxLines: 3);
        var descriptionY = titleY + (titleLines.Count * titleLineHeight) + GetScaledPixels(width, height, basePixels: 24, minimum: 16);
        var descriptionBottomY = footerRectY - GetScaledPixels(width, height, basePixels: 24, minimum: 16);
        var maxDescriptionLines = Math.Max(
            1,
            (descriptionBottomY - descriptionY) / descriptionLineHeight);
        var descriptionLines = WrapText(
            primaryDescription,
            maxChars: GetDescriptionWrapWidth(safeWidth, descriptionFontSize),
            maxLines: Math.Min(3, maxDescriptionLines));
        var accentLineY = footerRectY - GetScaledPixels(width, height, basePixels: 36, minimum: 22);
        var accentLineX = safeLeft;
        var accentLineWidth = Math.Max(GetScaledPixels(width, height, basePixels: 160, minimum: 120), safeWidth);

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
        svg.AppendLine($@"  <rect x=""{frameInset}"" y=""{frameInset}"" width=""{width - (frameInset * 2)}"" height=""{height - (frameInset * 2)}"" rx=""24"" fill=""rgba(7,12,26,0.32)"" stroke=""rgba(148,163,184,0.26)""/>");
        svg.AppendLine($@"  <rect x=""{panelInset}"" y=""{panelInset}"" width=""{panelWidth}"" height=""{panelHeight}"" rx=""20"" fill=""{palette.Surface}"" fill-opacity=""0.44"" stroke=""{palette.SurfaceStroke}"" stroke-opacity=""0.36""/>");
        svg.AppendLine($@"  <rect x=""{panelInset}"" y=""{panelInset}"" width=""{panelWidth}"" height=""{topBandHeight}"" rx=""4"" fill=""url(#topBand)""/>");
        svg.AppendLine($@"  <rect x=""{accentLineX}"" y=""{accentLineY}"" width=""{accentLineWidth}"" height=""2"" rx=""1"" fill=""url(#accentLine)""/>");

        svg.AppendLine($@"  <rect x=""{pillX}"" y=""{pillY}"" width=""{pillWidth}"" height=""{pillHeight}"" rx=""{pillRadius}"" fill=""{palette.ChipBackground}"" fill-opacity=""0.78"" stroke=""{palette.ChipBorder}"" stroke-opacity=""0.76""/>");
        svg.AppendLine($@"  <text x=""{pillTextX}"" y=""{pillTextY}"" fill=""{palette.ChipText}"" font-size=""{pillFontSize}"" font-family=""Segoe UI, Arial, sans-serif"" font-weight=""700"" dominant-baseline=""middle"" alignment-baseline=""middle"">{safeBadgeUpper}</text>");

        svg.AppendLine($@"  <text x=""{safeLeft}"" y=""{eyebrowY}"" fill=""{palette.AccentSoft}"" font-size=""{eyebrowFontSize}"" font-family=""Segoe UI, Arial, sans-serif"" font-weight=""700"">");
        svg.AppendLine($"    {safeEyebrow}");
        svg.AppendLine(@"  </text>");

        for (var i = 0; i < titleLines.Count; i++)
        {
            var y = titleY + (i * titleLineHeight);
            svg.AppendLine($@"  <text x=""{safeLeft}"" y=""{y}"" fill=""{palette.TextPrimary}"" font-size=""{titleFontSize}"" font-family=""Segoe UI, Arial, sans-serif"" font-weight=""800"">{EscapeXml(titleLines[i])}</text>");
        }

        for (var i = 0; i < descriptionLines.Count; i++)
        {
            var y = descriptionY + (i * descriptionLineHeight);
            svg.AppendLine($@"  <text x=""{safeLeft}"" y=""{y}"" fill=""{palette.TextSecondary}"" font-size=""{descriptionFontSize}"" font-family=""Segoe UI, Arial, sans-serif"" font-weight=""500"">{EscapeXml(descriptionLines[i])}</text>");
        }

        svg.AppendLine($@"  <rect x=""{footerRectX}"" y=""{footerRectY}"" rx=""{footerRectRadius}"" ry=""{footerRectRadius}"" width=""{footerRectWidth}"" height=""{footerRectHeight}"" fill=""{palette.ChipBackground}"" fill-opacity=""0.68"" stroke=""{palette.ChipBorder}"" stroke-opacity=""0.58""/>");
        svg.AppendLine($@"  <text x=""{footerRectX + footerTextInsetX}"" y=""{footerTextY}"" fill=""{palette.AccentSoft}"" font-size=""{footerFontSize}"" font-family=""Segoe UI, Arial, sans-serif"" font-weight=""700"" dominant-baseline=""middle"" alignment-baseline=""middle"">{safeFooter}</text>");
        svg.AppendLine(@"</svg>");

        try
        {
            var svgBytes = Encoding.UTF8.GetBytes(svg.ToString());
            var settings = new MagickReadSettings
            {
                Width = (uint)width,
                Height = (uint)height,
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

    private static List<string> WrapText(string? value, int maxChars, int maxLines)
    {
        maxChars = Math.Max(8, maxChars);
        maxLines = Math.Max(1, maxLines);

        var input = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
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
        var chunkSize = Math.Max(2, maxChars - 1);
        while (remaining.Length > maxChars)
        {
            yield return remaining[..chunkSize] + "-";
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

    private static SocialPalette SelectPalette(string styleKey, string seed)
    {
        if (Palettes.Length == 0)
            return new SocialPalette(
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
                "#dbeafe");

        var input = Encoding.UTF8.GetBytes(seed ?? string.Empty);
        var hash = SHA256.HashData(input);
        var candidates = styleKey switch
        {
            "api" => new[] { 0, 2 },
            "docs" => new[] { 2, 0 },
            "editorial" => new[] { 3, 1 },
            _ => new[] { 1, 0 }
        };
        var candidate = candidates[hash[0] % candidates.Length];
        return Palettes[candidate % Palettes.Length];
    }

    private static string ClassifyStyle(string badge, string footerLabel)
    {
        var combined = string.Concat(badge ?? string.Empty, " ", footerLabel ?? string.Empty).ToLowerInvariant();
        if (combined.Contains("api", StringComparison.Ordinal))
            return "api";
        if (combined.Contains("doc", StringComparison.Ordinal))
            return "docs";
        if (combined.Contains("blog", StringComparison.Ordinal) ||
            combined.Contains("post", StringComparison.Ordinal) ||
            combined.Contains("news", StringComparison.Ordinal) ||
            combined.Contains("article", StringComparison.Ordinal))
            return "editorial";
        return "default";
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

    private sealed class SocialPalette
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
}
