using System.Globalization;
using System.Text;
using System.Xml.Linq;
using ImageMagick;

namespace PowerForge.Web;

internal static partial class WebSocialCardGenerator
{
    private static MagickImage CreateSplitImageCanvas(SocialCardRenderState state)
    {
        var media = GetSplitImageRect(state);
        var source = state.InlineImageDataUri!;
        using var image = LoadSplitImage(source, media.Width, media.Height, SplitImageCovers(state), state.AllowRemoteMediaFetch)
            ?? throw new InvalidOperationException("Social card split artwork could not be rendered.");
        var cover = SplitImageCovers(state);
        var (align, gravity) = GetSplitAnchor(state);
        image.Resize(new MagickGeometry((uint)media.Width, (uint)media.Height) { FillArea = cover });
        if (cover)
        {
            image.Crop((uint)media.Width, (uint)media.Height, gravity);
            image.Page = new MagickGeometry(0, 0, 0, 0);
        }
        var offsetX = align.StartsWith("xMin", StringComparison.Ordinal) ? 0 : align.StartsWith("xMax", StringComparison.Ordinal) ? media.Width - (int)image.Width : (media.Width - (int)image.Width) / 2;
        var offsetY = align.EndsWith("YMin", StringComparison.Ordinal) ? 0 : align.EndsWith("YMax", StringComparison.Ordinal) ? media.Height - (int)image.Height : (media.Height - (int)image.Height) / 2;
        var canvas = new MagickImage(MagickColors.Transparent, (uint)media.Width, (uint)media.Height);
        canvas.Composite(image, offsetX, offsetY, CompositeOperator.Over);
        return canvas;
    }

    private static MagickImage? LoadSplitImage(string source, int width, int height, bool cover, bool allowRemoteMediaFetch)
    {
        byte[]? bytes = null;
        if (source.StartsWith("data:image/svg+xml", StringComparison.OrdinalIgnoreCase))
        {
            var comma = source.IndexOf(',');
            bytes = source[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase)
                ? Convert.FromBase64String(source[(comma + 1)..])
                : Encoding.UTF8.GetBytes(Uri.UnescapeDataString(source[(comma + 1)..]));
        }
        else if (source.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            bytes = IsRemoteMediaSource(source) ? GetRemoteImageBytes(source, allowRemoteMediaFetch) : File.Exists(source) ? File.ReadAllBytes(source) : null;
        }
        if (bytes is null)
            return TryLoadImageSource(source, width, height, allowRemoteMediaFetch);
        var sanitized = SanitizeSvgBytes(bytes);
        if (sanitized.Length == 0)
            return null;
        var document = XDocument.Parse(Encoding.UTF8.GetString(sanitized));
        var root = document.Root!;
        var viewBox = ((string?)root.Attribute("viewBox"))?.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        double Length(string? value) => double.TryParse(value?.Trim().TrimEnd('p', 'x'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : 0;
        var naturalWidth = viewBox is { Length: 4 } ? Length(viewBox[2]) : Length((string?)root.Attribute("width"));
        var naturalHeight = viewBox is { Length: 4 } ? Length(viewBox[3]) : Length((string?)root.Attribute("height"));
        if (naturalWidth <= 0 || naturalHeight <= 0 || !double.IsFinite(naturalWidth) || !double.IsFinite(naturalHeight))
            throw new InvalidOperationException("Social card SVG artwork requires a viewBox or explicit positive width and height.");
        var scale = cover ? Math.Max(width / naturalWidth, height / naturalHeight) : Math.Min(width / naturalWidth, height / naturalHeight);
        var renderWidth = Math.Max(1, (int)Math.Ceiling(naturalWidth * scale));
        var renderHeight = Math.Max(1, (int)Math.Ceiling(naturalHeight * scale));
        if (renderWidth > 8192 || renderHeight > 8192 || (long)renderWidth * renderHeight > 16_000_000)
            throw new InvalidOperationException("Social card artwork aspect ratio requires an excessive cover render. Use contain or a less extreme image.");
        root.SetAttributeValue("width", renderWidth);
        root.SetAttributeValue("height", renderHeight);
        var svg = document.ToString(SaveOptions.DisableFormatting);
        if (OperatingSystem.IsLinux())
        {
            var png = TryRenderPngWithExternalImageMagick(svg, renderWidth, renderHeight);
            return png is { Length: > 0 } ? new MagickImage(png) : null;
        }
        return CreateMagickImage(Encoding.UTF8.GetBytes(svg), "image/svg+xml", renderWidth, renderHeight);
    }

    private static string EmbedSplitImage(SocialCardRenderState state)
    {
        using var image = CreateSplitImageCanvas(state);
        image.Format = MagickFormat.Png;
        image.Strip();
        return "data:image/png;base64," + Convert.ToBase64String(image.ToByteArray());
    }
}
