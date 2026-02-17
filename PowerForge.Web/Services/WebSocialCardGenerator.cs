using System.Security;
using System.Text;
using ImageMagick;

namespace PowerForge.Web;

internal static class WebSocialCardGenerator
{
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

        var titleLines = WrapText(title, maxChars: 34, maxLines: 3);
        var descriptionLines = WrapText(description, maxChars: 58, maxLines: 3);
        var safeEyebrow = EscapeXml(TrimSingleLine(eyebrow, 56));
        var safeBadge = EscapeXml(TrimSingleLine(badge, 48));

        var titleY = 190;
        var descriptionY = titleY + (titleLines.Count * 70) + 26;
        var footerY = height - 64;

        var svg = new StringBuilder();
        svg.AppendLine($@"<svg xmlns=""http://www.w3.org/2000/svg"" width=""{width}"" height=""{height}"" viewBox=""0 0 {width} {height}"">");
        svg.AppendLine(@"  <defs>");
        svg.AppendLine(@"    <linearGradient id=""bg"" x1=""0%"" y1=""0%"" x2=""100%"" y2=""100%"">");
        svg.AppendLine(@"      <stop offset=""0%"" stop-color=""#0b132b""/>");
        svg.AppendLine(@"      <stop offset=""55%"" stop-color=""#132247""/>");
        svg.AppendLine(@"      <stop offset=""100%"" stop-color=""#1b3a73""/>");
        svg.AppendLine(@"    </linearGradient>");
        svg.AppendLine(@"    <linearGradient id=""glow"" x1=""0%"" y1=""0%"" x2=""100%"" y2=""0%"">");
        svg.AppendLine(@"      <stop offset=""0%"" stop-color=""#5eead4"" stop-opacity=""0.24""/>");
        svg.AppendLine(@"      <stop offset=""100%"" stop-color=""#38bdf8"" stop-opacity=""0.14""/>");
        svg.AppendLine(@"    </linearGradient>");
        svg.AppendLine(@"  </defs>");
        svg.AppendLine($@"  <rect x=""0"" y=""0"" width=""{width}"" height=""{height}"" fill=""url(#bg)""/>");
        svg.AppendLine($@"  <rect x=""36"" y=""36"" width=""{width - 72}"" height=""{height - 72}"" rx=""22"" fill=""rgba(10,15,30,0.46)"" stroke=""rgba(148,163,184,0.28)""/>");
        svg.AppendLine($@"  <rect x=""48"" y=""48"" width=""{width - 96}"" height=""8"" rx=""4"" fill=""url(#glow)""/>");
        svg.AppendLine(@"  <text x=""72"" y=""108"" fill=""#93c5fd"" font-size=""24"" font-family=""Arial, Segoe UI, sans-serif"" font-weight=""700"">");
        svg.AppendLine($"    {safeEyebrow}");
        svg.AppendLine(@"  </text>");

        for (var i = 0; i < titleLines.Count; i++)
        {
            var y = titleY + (i * 70);
            svg.AppendLine($@"  <text x=""72"" y=""{y}"" fill=""#f8fafc"" font-size=""58"" font-family=""Arial, Segoe UI, sans-serif"" font-weight=""800"">{EscapeXml(titleLines[i])}</text>");
        }

        for (var i = 0; i < descriptionLines.Count; i++)
        {
            var y = descriptionY + (i * 42);
            svg.AppendLine($@"  <text x=""72"" y=""{y}"" fill=""#cbd5e1"" font-size=""32"" font-family=""Arial, Segoe UI, sans-serif"" font-weight=""500"">{EscapeXml(descriptionLines[i])}</text>");
        }

        svg.AppendLine($@"  <rect x=""72"" y=""{height - 122}"" rx=""14"" ry=""14"" width=""{Math.Max(220, Math.Min(width - 144, 760))}"" height=""48"" fill=""rgba(94,234,212,0.14)"" stroke=""rgba(94,234,212,0.36)""/>");
        svg.AppendLine($@"  <text x=""92"" y=""{footerY}"" fill=""#99f6e4"" font-size=""24"" font-family=""Arial, Segoe UI, sans-serif"" font-weight=""700"">{safeBadge}</text>");
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
        var input = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        if (string.IsNullOrWhiteSpace(input))
            return new List<string> { string.Empty };

        var words = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = new StringBuilder();

        foreach (var word in words)
        {
            if (current.Length == 0)
            {
                current.Append(word);
                continue;
            }

            if (current.Length + 1 + word.Length <= maxChars)
            {
                current.Append(' ').Append(word);
                continue;
            }

            lines.Add(current.ToString());
            if (lines.Count >= maxLines)
                return ClampLineCount(lines, maxLines);
            current.Clear();
            current.Append(word);
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
}
