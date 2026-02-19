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

        var titleLines = WrapText(primaryTitle, maxChars: GetTitleWrapWidth(width), maxLines: 3);
        var titleLineHeight = GetScaledPixels(width, height, basePixels: 62, minimum: 30);
        var descriptionLineHeight = GetScaledPixels(width, height, basePixels: 34, minimum: 20);
        var titleY = GetScaledPixels(width, height, basePixels: 220, minimum: 120);
        var descriptionY = titleY + (titleLines.Count * titleLineHeight) + GetScaledPixels(width, height, basePixels: 24, minimum: 16);
        var footerY = height - GetScaledPixels(width, height, basePixels: 70, minimum: 38);
        var maxDescriptionLines = Math.Max(
            1,
            (footerY - descriptionY - GetScaledPixels(width, height, basePixels: 16, minimum: 10)) / descriptionLineHeight);
        var descriptionLines = WrapText(
            primaryDescription,
            maxChars: GetDescriptionWrapWidth(width),
            maxLines: Math.Min(3, maxDescriptionLines));

        var safeEyebrow = EscapeXml(TrimSingleLine(primaryEyebrow, 56));
        var safeBadge = EscapeXml(TrimSingleLine(primaryBadge, 48));
        var safeBadgeUpper = safeBadge.ToUpperInvariant();
        var seed = string.Join("|", primaryTitle, primaryDescription, primaryEyebrow, primaryBadge, width, height);
        var palette = SelectPalette(seed);
        var frameInset = GetScaledPixels(width, height, basePixels: 36, minimum: 22);
        var panelInset = GetScaledPixels(width, height, basePixels: 48, minimum: 30);
        var panelWidth = width - (panelInset * 2);
        var panelHeight = height - (panelInset * 2);
        var topBandHeight = GetScaledPixels(width, height, basePixels: 9, minimum: 5);
        var eyebrowFontSize = GetScaledPixels(width, height, basePixels: 24, minimum: 14);
        var titleFontSize = GetScaledPixels(width, height, basePixels: 60, minimum: 32);
        var descriptionFontSize = GetScaledPixels(width, height, basePixels: 32, minimum: 18);
        var badgeFontSize = GetScaledPixels(width, height, basePixels: 24, minimum: 14);
        var badgeRectY = height - GetScaledPixels(width, height, basePixels: 122, minimum: 74);
        var badgeRectHeight = GetScaledPixels(width, height, basePixels: 48, minimum: 30);
        var badgeRectRadius = GetScaledPixels(width, height, basePixels: 14, minimum: 8);
        var badgeTextY = footerY;
        var badgeRectWidth = Math.Max(
            GetScaledPixels(width, height, basePixels: 240, minimum: 168),
            Math.Min(panelWidth - GetScaledPixels(width, height, basePixels: 64, minimum: 44), GetScaledPixels(width, height, basePixels: 760, minimum: 420)));
        var pillPadding = GetScaledPixels(width, height, basePixels: 14, minimum: 8);
        var pillHeight = GetScaledPixels(width, height, basePixels: 40, minimum: 26);
        var pillRadius = GetScaledPixels(width, height, basePixels: 20, minimum: 13);
        var glowRadiusLarge = GetScaledPixels(width, height, basePixels: 240, minimum: 140);
        var glowRadiusSmall = GetScaledPixels(width, height, basePixels: 150, minimum: 88);
        var rightGlowX = width - GetScaledPixels(width, height, basePixels: 130, minimum: 84);
        var rightGlowY = GetScaledPixels(width, height, basePixels: 126, minimum: 76);
        var leftGlowX = GetScaledPixels(width, height, basePixels: 180, minimum: 108);
        var leftGlowY = height - GetScaledPixels(width, height, basePixels: 104, minimum: 66);
        var pillTextX = width - GetScaledPixels(width, height, basePixels: 404, minimum: 258);
        var pillTextY = GetScaledPixels(width, height, basePixels: 107, minimum: 66);
        var pillWidth = GetScaledPixels(width, height, basePixels: 320, minimum: 192);
        var accentLineY = height - GetScaledPixels(width, height, basePixels: 159, minimum: 98);

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
        svg.AppendLine($@"  <rect x=""72"" y=""{accentLineY}"" width=""{Math.Max(140, panelWidth - 180)}"" height=""2"" rx=""1"" fill=""url(#accentLine)""/>");

        svg.AppendLine($@"  <rect x=""{pillTextX - pillPadding}"" y=""{GetScaledPixels(width, height, basePixels: 78, minimum: 50)}"" width=""{pillWidth}"" height=""{pillHeight}"" rx=""{pillRadius}"" fill=""{palette.ChipBackground}"" fill-opacity=""0.78"" stroke=""{palette.ChipBorder}"" stroke-opacity=""0.76""/>");
        svg.AppendLine($@"  <text x=""{pillTextX}"" y=""{pillTextY}"" fill=""{palette.ChipText}"" font-size=""{GetScaledPixels(width, height, basePixels: 18, minimum: 12)}"" font-family=""Segoe UI, Arial, sans-serif"" font-weight=""700"">{safeBadgeUpper}</text>");

        svg.AppendLine($@"  <text x=""72"" y=""{GetScaledPixels(width, height, basePixels: 108, minimum: 68)}"" fill=""{palette.AccentSoft}"" font-size=""{eyebrowFontSize}"" font-family=""Segoe UI, Arial, sans-serif"" font-weight=""700"">");
        svg.AppendLine($"    {safeEyebrow}");
        svg.AppendLine(@"  </text>");

        for (var i = 0; i < titleLines.Count; i++)
        {
            var y = titleY + (i * titleLineHeight);
            svg.AppendLine($@"  <text x=""72"" y=""{y}"" fill=""{palette.TextPrimary}"" font-size=""{titleFontSize}"" font-family=""Segoe UI, Arial, sans-serif"" font-weight=""800"">{EscapeXml(titleLines[i])}</text>");
        }

        for (var i = 0; i < descriptionLines.Count; i++)
        {
            var y = descriptionY + (i * descriptionLineHeight);
            svg.AppendLine($@"  <text x=""72"" y=""{y}"" fill=""{palette.TextSecondary}"" font-size=""{descriptionFontSize}"" font-family=""Segoe UI, Arial, sans-serif"" font-weight=""500"">{EscapeXml(descriptionLines[i])}</text>");
        }

        svg.AppendLine($@"  <rect x=""72"" y=""{badgeRectY}"" rx=""{badgeRectRadius}"" ry=""{badgeRectRadius}"" width=""{badgeRectWidth}"" height=""{badgeRectHeight}"" fill=""{palette.ChipBackground}"" fill-opacity=""0.68"" stroke=""{palette.ChipBorder}"" stroke-opacity=""0.58""/>");
        svg.AppendLine($@"  <text x=""92"" y=""{badgeTextY}"" fill=""{palette.AccentSoft}"" font-size=""{badgeFontSize}"" font-family=""Segoe UI, Arial, sans-serif"" font-weight=""700"">{safeBadge}</text>");
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

    private static int GetTitleWrapWidth(int width)
    {
        var dynamicWidth = width / 33;
        return Math.Clamp(dynamicWidth, 24, 42);
    }

    private static int GetDescriptionWrapWidth(int width)
    {
        var dynamicWidth = width / 20;
        return Math.Clamp(dynamicWidth, 46, 74);
    }

    private static SocialPalette SelectPalette(string seed)
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
        var index = hash[0] % Palettes.Length;
        return Palettes[index];
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
