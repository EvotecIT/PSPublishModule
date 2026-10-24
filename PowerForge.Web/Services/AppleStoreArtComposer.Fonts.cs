using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

public sealed partial class AppleStoreArtComposer
{
    private static readonly Lazy<Dictionary<string, HashSet<int>>> FontGlyphs = new(() =>
        new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Inter"] = ReadFontGlyphs(Assets.Value["Fonts.Inter.ttf"]),
            ["Hanken Grotesk"] = ReadFontGlyphs(Assets.Value["Fonts.HankenGrotesk.ttf"]),
            ["JetBrains Mono"] = ReadFontGlyphs(Assets.Value["Fonts.JetBrainsMono.ttf"]),
            ["Noto Sans CJK"] = ReadFontGlyphs(Assets.Value["Fonts.NotoSansCJK.ttf"])
        });

    private static Dictionary<string, string[]> CaptionFonts(string themeStyle)
    {
        var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(themeStyle, @"(--font-(?:display|body|eyebrow))\s*:\s*([^;}]+)"))
            result[match.Groups[1].Value] = match.Groups[2].Value.Split(',')
                .Select(name => name.Trim().Trim('\'', '"')).ToArray();
        return result;
    }

    private static void ValidateCaptionFont(string text, string[] families, string locale, string frame)
    {
        var offline = families.Where(name => name is not ("sans-serif" or "serif" or "monospace")).ToArray();
        foreach (var family in offline)
            if (!FontGlyphs.Value.ContainsKey(family))
                throw new InvalidOperationException($"Locale '{locale}', frame '{frame}': missing offline font '{family}'. Only bundled font families are supported.");
        foreach (var rune in text.EnumerateRunes())
        {
            // These affect layout/shaping without requiring their own visible glyph.
            if (Rune.IsWhiteSpace(rune) || Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format ||
                rune.Value is >= 0xfe00 and <= 0xfe0f or >= 0xe0100 and <= 0xe01ef) continue;
            if (offline.Any(family => FontGlyphs.Value[family].Contains(rune.Value))) continue;
            var missing = rune.Value switch
            {
                >= 0x0590 and <= 0x05ff or >= 0xfb1d and <= 0xfb4f => "Noto Sans Hebrew",
                >= 0x0600 and <= 0x06ff or >= 0x0750 and <= 0x077f or >= 0x0870 and <= 0x08ff or
                    >= 0xfb50 and <= 0xfdff or >= 0xfe70 and <= 0xfeff => "Noto Sans Arabic",
                >= 0x0900 and <= 0x097f or >= 0xa8e0 and <= 0xa8ff => "Noto Sans Devanagari",
                >= 0x0e00 and <= 0x0e7f => "Noto Sans Thai",
                _ => string.Join(", ", families)
            };
            throw new InvalidOperationException($"Locale '{locale}', frame '{frame}': missing offline font '{missing}' for U+{rune.Value:X4} ('{rune}'). Unsupported caption characters cannot use system fallback fonts.");
        }
    }

    // Read only the Unicode cmap formats used by our pinned, embedded TrueType assets.
    // A nonzero glyph id is required: a mapped .notdef entry still renders tofu.
    // https://learn.microsoft.com/typography/opentype/spec/cmap
    private static HashSet<int> ReadFontGlyphs(byte[] font)
    {
        int U16(int offset) => BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(offset, 2));
        int U32(int offset) => checked((int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(offset, 4)));
        var cmap = -1;
        for (var table = 0; table < U16(4); table++)
        {
            var entry = 12 + table * 16;
            if (Encoding.ASCII.GetString(font, entry, 4) == "cmap") cmap = U32(entry + 8);
        }
        if (cmap < 0) throw new InvalidOperationException("Bundled font has no Unicode cmap.");
        var points = new HashSet<int>();
        var seen = new HashSet<int>();
        for (var record = 0; record < U16(cmap + 2); record++)
        {
            var entry = cmap + 4 + record * 8;
            var platform = U16(entry); var encoding = U16(entry + 2);
            if (platform != 0 && !(platform == 3 && encoding is 1 or 10)) continue;
            var subtable = cmap + U32(entry + 4);
            if (!seen.Add(subtable)) continue;
            switch (U16(subtable))
            {
                case 4:
                    var segments = U16(subtable + 6) / 2;
                    var ends = subtable + 14;
                    var starts = ends + segments * 2 + 2;
                    var deltas = starts + segments * 2;
                    var offsets = deltas + segments * 2;
                    for (var segment = 0; segment < segments; segment++)
                    {
                        var delta = U16(deltas + segment * 2);
                        var range = U16(offsets + segment * 2);
                        var start = U16(starts + segment * 2);
                        for (var point = start; point <= U16(ends + segment * 2); point++)
                        {
                            var glyph = range == 0 ? (point + delta) & 0xffff :
                                U16(offsets + segment * 2 + range + (point - start) * 2);
                            if (range != 0 && glyph != 0) glyph = (glyph + delta) & 0xffff;
                            if (glyph != 0) points.Add(point);
                        }
                    }
                    break;
                case 12:
                    for (var group = 0; group < U32(subtable + 12); group++)
                    {
                        var start = subtable + 16 + group * 12;
                        for (var point = U32(start); point <= U32(start + 4); point++)
                            if (U32(start + 8) + point - U32(start) != 0) points.Add(point);
                    }
                    break;
            }
        }
        if (points.Count == 0) throw new InvalidOperationException("Bundled font has no supported Unicode glyph mappings.");
        return points;
    }
}
