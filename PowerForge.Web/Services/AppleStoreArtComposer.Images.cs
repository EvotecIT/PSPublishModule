using ImageMagick;

namespace PowerForge.Web;

public sealed partial class AppleStoreArtComposer
{
    private static void NormalizePng(string path, AppleStoreArtDisplay size)
    {
        using var image = new MagickImage(path);
        if (image.Format != MagickFormat.Png || image.Width != size.Width || image.Height != size.Height)
            throw new InvalidOperationException($"Rendered PNG must be exactly {size.Width}x{size.Height}.");
        image.BackgroundColor = MagickColors.White;
        image.Alpha(AlphaAction.Remove);
        image.Strip();
        image.Settings.SetDefine(MagickFormat.Png, "exclude-chunks", "all");
        image.Write(path, MagickFormat.Png24);
        using var check = new MagickImage(path);
        if (check.HasAlpha || check.Width != size.Width || check.Height != size.Height)
            throw new InvalidOperationException("Final PNG failed dimensions or no-alpha validation.");
    }
    /// <summary>Computes a versioned 64-bit horizontal difference hash for visual comparison.</summary>
    public static string PerceptualHash(string path)
    {
        using var image = new MagickImage(path);
        image.ColorSpace = ColorSpace.Gray;
        image.Resize(new MagickGeometry(9, 8) { IgnoreAspectRatio = true });
        using var pixels = image.GetPixels();
        ulong bits = 0;
        for (var y = 0; y < 8; y++)
            for (var x = 0; x < 8; x++)
                bits = (bits << 1) | (pixels.GetPixel(x, y).GetChannel(0) > pixels.GetPixel(x + 1, y).GetChannel(0) ? 1UL : 0UL);
        return "dhash-v1:" + bits.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
    }
    private static void ContactSheet(string folder, string output)
    {
        var files = Directory.GetFiles(folder, "*.png").OrderBy(f => f, StringComparer.Ordinal).ToArray();
        const int tileWidth = 264, padding = 16;
        var thumbnails = new List<MagickImage>();
        try
        {
            foreach (var file in files)
            {
                var thumbnail = new MagickImage(file); thumbnail.Resize((uint)tileWidth, 0); thumbnails.Add(thumbnail);
            }
            var columns = Math.Min(5, thumbnails.Count); var rows = (thumbnails.Count + columns - 1) / columns;
            var tileHeight = (int)thumbnails.Max(t => t.Height);
            using var sheet = new MagickImage(MagickColors.Gray, (uint)(columns * (tileWidth + padding) + padding), (uint)(rows * (tileHeight + padding) + padding));
            for (var i = 0; i < thumbnails.Count; i++)
                sheet.Composite(thumbnails[i], padding + i % columns * (tileWidth + padding), padding + i / columns * (tileHeight + padding), CompositeOperator.Over);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            sheet.Alpha(AlphaAction.Remove); sheet.Strip(); sheet.Write(output, MagickFormat.Png24);
        }
        finally { foreach (var thumbnail in thumbnails) thumbnail.Dispose(); }
    }
}
