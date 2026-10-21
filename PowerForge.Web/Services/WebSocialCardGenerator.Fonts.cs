using System.Globalization;
using System.Security.Cryptography;
using System.Xml.Linq;
using ImageMagick;
using ImageMagick.Drawing;

namespace PowerForge.Web;

internal static partial class WebSocialCardGenerator
{
    internal sealed record CardFontFile(string Family, int MinWeight, int MaxWeight, string Path, string Fingerprint);

    private sealed class ConfiguredCardFontException(string message, Exception innerException)
        : InvalidOperationException(message, innerException) { }

    internal static IReadOnlyList<CardFontFile> ResolveFontFiles(IReadOnlyList<SocialCardFontSpec>? fonts, string? siteRoot)
    {
        if (fonts is not { Count: > 0 })
            return Array.Empty<CardFontFile>();
        if (string.IsNullOrWhiteSpace(siteRoot))
            throw new InvalidOperationException("GeneratedCardFonts requires a site root.");
        var root = System.IO.Path.GetFullPath(siteRoot);
        var resolved = new List<CardFontFile>();
        foreach (var font in fonts)
        {
            if (font is null || string.IsNullOrWhiteSpace(font.Family) || string.IsNullOrWhiteSpace(font.Path))
                throw new InvalidOperationException("Each GeneratedCardFonts entry requires Family and Path.");
            var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, font.Path));
            var relative = System.IO.Path.GetRelativePath(root, path);
            if (relative == ".." || relative.StartsWith(".." + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal) || System.IO.Path.IsPathRooted(relative))
                throw new InvalidOperationException($"Social card font '{font.Family}' must be inside the site root: '{font.Path}'.");
            if (!File.Exists(path))
                throw new InvalidOperationException($"Social card font '{font.Family}' was not found: '{path}'.");
            if (System.IO.Path.GetExtension(path).ToLowerInvariant() is not (".ttf" or ".otf"))
                throw new InvalidOperationException($"Social card font '{font.Family}' requires a TTF or OTF file. Convert '{font.Path}' before building.");
            var weights = (font.Weight?.ToString() ?? "400").Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (weights.Length is < 1 or > 2 || !int.TryParse(weights[0], out var min) || min is < 1 or > 1000 ||
                !int.TryParse(weights[^1], out var max) || max < min || max > 1000)
                throw new InvalidOperationException($"Invalid social card font weight for '{font.Family}': '{font.Weight}'.");
            var fingerprint = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            resolved.Add(new CardFontFile(font.Family.Trim(), min, max, path, fingerprint));
        }
        return resolved;
    }

    private static void ValidateConfiguredFontFamilies(SocialCardRenderOptions options)
    {
        if (options.Fonts is not { Count: > 0 })
            return;
        foreach (var key in new[] { "fontDisplay", "fontBody", "fontMono", "fontEyebrow", "fontBadge", "fontFooter" })
        {
            var baseKey = key switch { "fontDisplay" => "display", "fontBody" => "body", "fontMono" => "mono", _ => null };
            var stack = ReadThemeToken(options.ThemeTokens, "socialCard", key) ??
                        (baseKey is null ? null : ReadThemeToken(options.ThemeTokens, "font", baseKey));
            if (string.IsNullOrWhiteSpace(stack))
                continue;
            var family = ParseFontFamilyStack(stack).FirstOrDefault();
            if (family is null || options.Fonts.Any(font => font.Family.Equals(family, StringComparison.OrdinalIgnoreCase)) ||
                TryMapGenericFontFamily(family, SocialCardFontIntent.Body, out _) || IsFontFamilyAvailable(family))
                continue;
            throw new InvalidOperationException($"Social card font family '{family}' ({key}) is unavailable. Declare its TTF/OTF file in GeneratedCardFonts.");
        }
    }

    // Keep declared text out of SVG font resolution. Draw it directly onto the finished raster with FreeType,
    // using the file selected by family and weight; no process-wide font registration or SVG font fallback is needed.
    private static string PrepareConfiguredFontText(string svg, SocialCardRenderOptions options, out IReadOnlyList<XElement> textNodes)
    {
        textNodes = Array.Empty<XElement>();
        if (options.Fonts is not { Count: > 0 })
            return svg;
        var document = XDocument.Parse(svg);
        var nodes = new List<XElement>();
        foreach (var text in document.Descendants().Where(element => element.Name.LocalName == "text").ToArray())
        {
            if (SelectFontFile(text, options.Fonts) is null)
                continue;
            nodes.Add(new XElement(text));
            text.Remove();
        }
        textNodes = nodes;
        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static CardFontFile? SelectFontFile(XElement text, IReadOnlyList<CardFontFile> fonts)
    {
        var family = ParseFontFamilyStack((string?)text.Attribute("font-family") ?? string.Empty).FirstOrDefault();
        var requestedWeight = int.TryParse((string?)text.Attribute("font-weight"), out var weight) ? weight : 400;
        return SelectFontFile(family, requestedWeight, fonts);
    }

    private static CardFontFile? SelectFontFile(string? family, int requestedWeight, IReadOnlyList<CardFontFile> fonts)
    {
        return fonts.Where(font => font.Family.Equals(family, StringComparison.OrdinalIgnoreCase))
            .OrderBy(font => requestedWeight < font.MinWeight ? font.MinWeight - requestedWeight : Math.Max(0, requestedWeight - font.MaxWeight))
            .FirstOrDefault();
    }

    private static void DrawConfiguredFontText(MagickImage image, IReadOnlyList<XElement> textNodes, SocialCardRenderOptions options)
    {
        foreach (var text in textNodes)
        {
            var font = SelectFontFile(text, options.Fonts!)!;
            try
            {
                if (string.IsNullOrWhiteSpace(text.Value))
                    continue;
                double Number(string name, double fallback = 0) => double.TryParse((string?)text.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
                var size = Number("font-size", 24);
                var requestedWeight = Math.Clamp((int)Number("font-weight", 400), font.MinWeight, font.MaxWeight);
                var x = Number("x");
                var y = Number("y");
                var align = (string?)text.Attribute("text-anchor") switch { "middle" => TextAlignment.Center, "end" => TextAlignment.Right, _ => TextAlignment.Left };
                if ((string?)text.Attribute("dominant-baseline") == "middle")
                    y += size * 0.35;
                new Drawables().Font(font.Path, FontStyleType.Normal, (FontWeight)requestedWeight, FontStretch.Normal).FontPointSize(size)
                    .FillColor(new MagickColor((string?)text.Attribute("fill") ?? "#ffffff"))
                    .FillOpacity(new Percentage(Math.Clamp(Number("fill-opacity", 1) * Number("opacity", 1), 0, 1) * 100))
                    .TextAlignment(align).TextKerning(Number("letter-spacing")).Text(x, y, text.Value).Draw(image);
            }
            catch (Exception ex)
            {
                throw new ConfiguredCardFontException($"Social card font '{font.Family}' could not be rendered from '{font.Path}': {ex.Message}", ex);
            }
        }
    }
}
