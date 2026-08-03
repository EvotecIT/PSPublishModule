using ImageMagick;

namespace PowerForge.Web;

internal static partial class WebVisualStoryAnimatedArtifactValidator
{
    internal static void ValidateGif(string path, string displayPath, bool requireMultipleFrames)
    {
        try
        {
            using (var metadata = new MagickImageCollection())
            {
                metadata.Ping(path);
                if (metadata.Count < (requireMultipleFrames ? 2 : 1))
                {
                    throw new InvalidOperationException(
                        requireMultipleFrames
                            ? $"Visual-story animated artifact must contain multiple decodable frames: {displayPath}"
                            : $"Visual-story GIF artifact must contain a decodable frame: {displayPath}");
                }
                if (metadata.Count > MaximumGifFrames)
                {
                    throw new InvalidOperationException(
                        $"Visual-story animated artifact exceeds the {MaximumGifFrames}-frame safety limit: {displayPath}");
                }

                var decodedPixels = 0UL;
                foreach (var frame in metadata)
                {
                    if (frame.Format is not (MagickFormat.Gif or MagickFormat.Gif87) ||
                        frame.Width == 0 ||
                        frame.Height == 0)
                    {
                        throw new InvalidOperationException(
                            $"Visual-story animated artifact does not match its declared format: {displayPath}");
                    }
                    decodedPixels = checked(decodedPixels + (ulong)frame.Width * frame.Height);
                    if (decodedPixels > MaximumGifDecodedPixels)
                    {
                        throw new InvalidOperationException(
                            $"Visual-story animated artifact exceeds the aggregate decoded-pixel safety limit: {displayPath}");
                    }
                }
            }

            using var frames = new MagickImageCollection();
            frames.Read(path);
            frames.Coalesce();
            string? firstFrameSignature = null;
            var sawVisibleFrameChange = false;
            foreach (var frame in frames)
            {
                if (frame.Format is not (MagickFormat.Gif or MagickFormat.Gif87) ||
                    frame.Width == 0 ||
                    frame.Height == 0)
                {
                    throw new InvalidOperationException(
                        $"Visual-story animated artifact does not match its declared format: {displayPath}");
                }
                if ((ulong)frame.Width * frame.Height > 100_000_000UL)
                {
                    throw new InvalidOperationException(
                        $"Visual-story animated artifact exceeds the 100-megapixel frame safety limit: {displayPath}");
                }
                var signature = frame.Signature;
                firstFrameSignature ??= signature;
                if (!string.Equals(signature, firstFrameSignature, StringComparison.Ordinal))
                    sawVisibleFrameChange = true;
            }
            if (requireMultipleFrames && !sawVisibleFrameChange)
            {
                throw new InvalidOperationException(
                    $"Visual-story animated GIF artifact must contain a visible frame change: {displayPath}");
            }
        }
        catch (MagickException ex)
        {
            throw new InvalidOperationException(
                $"Visual-story animated artifact is not decodable: {displayPath}",
                ex);
        }
    }
}
