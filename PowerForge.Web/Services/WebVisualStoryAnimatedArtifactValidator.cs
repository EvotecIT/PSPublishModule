using System.IO.Compression;
using System.Xml;
using ImageMagick;

namespace PowerForge.Web;

/// <summary>
/// Validates browser-renderable visual-story animation payloads without executing producer code.
/// </summary>
internal static class WebVisualStoryAnimatedArtifactValidator
{
    private const int MaximumGifFrames = 240;
    private const ulong MaximumGifDecodedPixels = 128_000_000UL;
    private const int MaximumApngFrames = 240;
    private const long MaximumApngDecodedBytes = 512_000_000L;
    private const string SvgNamespace = "http://www.w3.org/2000/svg";

    /// <summary>
    /// Validates that an animated artifact matches its declared format and contains decodable content.
    /// </summary>
    /// <param name="path">Resolved local artifact path.</param>
    /// <param name="displayPath">Manifest path used in validation errors.</param>
    /// <param name="format">Normalized animated format.</param>
    internal static void Validate(string path, string displayPath, string format)
    {
        if (string.Equals(format, "svg", StringComparison.OrdinalIgnoreCase))
        {
            ValidateSvg(path, displayPath);
            return;
        }
        if (string.Equals(format, "apng", StringComparison.OrdinalIgnoreCase))
        {
            ValidateApng(path, displayPath);
            return;
        }

        ValidateGif(path, displayPath);
    }

    private static void ValidateGif(string path, string displayPath)
    {
        try
        {
            using (var metadata = new MagickImageCollection())
            {
                metadata.Ping(path);
                if (metadata.Count < 2)
                {
                    throw new InvalidOperationException(
                        $"Visual-story animated artifact must contain multiple decodable frames: {displayPath}");
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
            }
        }
        catch (MagickException ex)
        {
            throw new InvalidOperationException(
                $"Visual-story animated artifact is not decodable: {displayPath}",
                ex);
        }
    }

    private static void ValidateSvg(string path, string displayPath)
    {
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 64L * 1024 * 1024
            };
            using var stream = File.OpenRead(path);
            using var reader = XmlReader.Create(stream, settings);
            reader.MoveToContent();
            if (!string.Equals(reader.LocalName, "svg", StringComparison.Ordinal) ||
                !string.Equals(reader.NamespaceURI, SvgNamespace, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Visual-story animated artifact is not a valid SVG: {displayPath}");
            }
            while (reader.Read())
            {
            }
        }
        catch (XmlException ex)
        {
            throw new InvalidOperationException(
                $"Visual-story animated artifact is not a valid SVG: {displayPath}",
                ex);
        }
    }

    private static void ValidateApng(string path, string displayPath)
    {
        ValidatePngEnvelope(path, displayPath);
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 45 ||
            bytes[0] != 137 || bytes[1] != 80 || bytes[2] != 78 || bytes[3] != 71 ||
            bytes[4] != 13 || bytes[5] != 10 || bytes[6] != 26 || bytes[7] != 10)
        {
            throw new InvalidOperationException(
                $"Visual-story animated artifact is not a valid APNG: {displayPath}");
        }

        var offset = 8;
        var sawHeader = false;
        var sawAnimationControl = false;
        var sawImageData = false;
        var sawEnd = false;
        var declaredFrames = 0U;
        var frameCount = 0U;
        var expectedSequence = 0U;
        var canvasWidth = 0U;
        var canvasHeight = 0U;
        byte bitDepth = 0;
        byte colorType = 0;
        byte interlaceMethod = 0;
        MemoryStream? frameData = null;
        uint frameWidth = 0;
        uint frameHeight = 0;
        var decodedFrameBytes = 0L;
        var currentFrameAllowsIdat = false;
        while (offset + 12 <= bytes.Length)
        {
            var length = ReadUInt32(bytes, offset);
            if (length > int.MaxValue || length > bytes.Length - offset - 12)
            {
                throw new InvalidOperationException(
                    $"Visual-story animated artifact contains an invalid PNG chunk: {displayPath}");
            }
            var dataOffset = offset + 8;
            var dataLength = (int)length;
            ValidateChunkCrc(bytes, offset + 4, dataOffset, dataLength, displayPath);
            var first = bytes[offset + 4];
            var second = bytes[offset + 5];
            var third = bytes[offset + 6];
            var fourth = bytes[offset + 7];
            if (!sawHeader &&
                !(first == 'I' && second == 'H' && third == 'D' && fourth == 'R'))
            {
                throw new InvalidOperationException($"Visual-story APNG must begin with an image header: {displayPath}");
            }

            if (first == 'I' && second == 'H' && third == 'D' && fourth == 'R')
            {
                if (sawHeader || dataLength != 13)
                    throw new InvalidOperationException($"Visual-story APNG has an invalid header: {displayPath}");
                canvasWidth = ReadUInt32(bytes, dataOffset);
                canvasHeight = ReadUInt32(bytes, dataOffset + 4);
                if (canvasWidth == 0 || canvasHeight == 0 ||
                    (ulong)canvasWidth * canvasHeight > 100_000_000UL)
                {
                    throw new InvalidOperationException($"Visual-story APNG has invalid dimensions: {displayPath}");
                }
                bitDepth = bytes[dataOffset + 8];
                colorType = bytes[dataOffset + 9];
                interlaceMethod = bytes[dataOffset + 12];
                ValidatePngPixelFormat(bitDepth, colorType, interlaceMethod, displayPath);
                sawHeader = true;
            }
            else if (first == 'a' && second == 'c' && third == 'T' && fourth == 'L')
            {
                if (!sawHeader || sawAnimationControl || sawImageData || dataLength != 8)
                    throw new InvalidOperationException($"Visual-story APNG has invalid animation control: {displayPath}");
                declaredFrames = ReadUInt32(bytes, dataOffset);
                if (declaredFrames < 2)
                    throw new InvalidOperationException($"Visual-story APNG must contain multiple frames: {displayPath}");
                if (declaredFrames > MaximumApngFrames)
                    throw new InvalidOperationException(
                        $"Visual-story animated artifact exceeds the {MaximumApngFrames}-frame safety limit: {displayPath}");
                sawAnimationControl = true;
            }
            else if (first == 'f' && second == 'c' && third == 'T' && fourth == 'L')
            {
                ValidateFrameData(
                    frameData,
                    frameWidth,
                    frameHeight,
                    bitDepth,
                    colorType,
                    interlaceMethod,
                    displayPath);
                if (!sawAnimationControl || dataLength != 26 ||
                    ReadUInt32(bytes, dataOffset) != expectedSequence++)
                {
                    throw new InvalidOperationException($"Visual-story APNG has an invalid frame sequence: {displayPath}");
                }
                frameWidth = ReadUInt32(bytes, dataOffset + 4);
                frameHeight = ReadUInt32(bytes, dataOffset + 8);
                var x = ReadUInt32(bytes, dataOffset + 12);
                var y = ReadUInt32(bytes, dataOffset + 16);
                if (frameWidth == 0 || frameHeight == 0 ||
                    x + (ulong)frameWidth > canvasWidth ||
                    y + (ulong)frameHeight > canvasHeight ||
                    bytes[dataOffset + 24] > 2 ||
                    bytes[dataOffset + 25] > 1)
                {
                    throw new InvalidOperationException($"Visual-story APNG has invalid frame control: {displayPath}");
                }
                decodedFrameBytes = ReserveApngDecodedBytes(
                    decodedFrameBytes,
                    ComputeDecodedFrameBytes(
                        frameWidth,
                        frameHeight,
                        bitDepth,
                        colorType,
                        interlaceMethod),
                    displayPath);
                frameData?.Dispose();
                frameData = new MemoryStream();
                currentFrameAllowsIdat = frameCount == 0 && !sawImageData;
                frameCount++;
            }
            else if (first == 'I' && second == 'D' && third == 'A' && fourth == 'T')
            {
                if (frameData is not null && !currentFrameAllowsIdat)
                {
                    throw new InvalidOperationException(
                        $"Visual-story APNG uses image data for a later animation frame: {displayPath}");
                }
                sawImageData = true;
                frameData?.Write(bytes, dataOffset, dataLength);
            }
            else if (first == 'f' && second == 'd' && third == 'A' && fourth == 'T')
            {
                if (frameData is null || dataLength < 5 ||
                    ReadUInt32(bytes, dataOffset) != expectedSequence++)
                {
                    throw new InvalidOperationException($"Visual-story APNG has invalid frame data: {displayPath}");
                }
                frameData.Write(bytes, dataOffset + 4, dataLength - 4);
            }
            else if (first == 'I' && second == 'E' && third == 'N' && fourth == 'D')
            {
                if (dataLength != 0)
                    throw new InvalidOperationException($"Visual-story APNG has an invalid end chunk: {displayPath}");
                ValidateFrameData(
                    frameData,
                    frameWidth,
                    frameHeight,
                    bitDepth,
                    colorType,
                    interlaceMethod,
                    displayPath);
                sawEnd = true;
                offset += dataLength + 12;
                break;
            }
            offset += dataLength + 12;
        }
        frameData?.Dispose();
        if (!sawHeader || !sawAnimationControl || !sawEnd ||
            frameCount != declaredFrames ||
            offset != bytes.Length)
        {
            throw new InvalidOperationException(
                $"Visual-story animated artifact is not a complete APNG: {displayPath}");
        }
    }

    private static void ValidatePngEnvelope(string path, string displayPath)
    {
        try
        {
            var info = new MagickImageInfo(path);
            if (info.Format is not (MagickFormat.APng or MagickFormat.Png) ||
                info.Width == 0 ||
                info.Height == 0)
            {
                throw new InvalidOperationException(
                    $"Visual-story animated artifact is not a decodable APNG: {displayPath}");
            }
        }
        catch (MagickException ex)
        {
            throw new InvalidOperationException(
                $"Visual-story animated artifact is not a decodable APNG: {displayPath}",
                ex);
        }
    }

    private static void ValidateFrameData(
        MemoryStream? compressed,
        uint width,
        uint height,
        byte bitDepth,
        byte colorType,
        byte interlaceMethod,
        string displayPath)
    {
        if (compressed is null) return;
        if (compressed.Length == 0)
            throw new InvalidOperationException($"Visual-story APNG frame has no image data: {displayPath}");
        compressed.Position = 0;
        try
        {
            using var zlib = new ZLibStream(compressed, CompressionMode.Decompress, leaveOpen: true);
            var buffer = new byte[8192];
            var expectedBytes = ComputeDecodedFrameBytes(
                width,
                height,
                bitDepth,
                colorType,
                interlaceMethod);
            var total = 0L;
            while (true)
            {
                var read = zlib.Read(buffer, 0, buffer.Length);
                if (read == 0) break;
                total += read;
                if (total > expectedBytes)
                    throw new InvalidOperationException($"Visual-story APNG frame expands beyond its dimensions: {displayPath}");
            }
            if (total != expectedBytes)
                throw new InvalidOperationException($"Visual-story APNG frame has incomplete pixel data: {displayPath}");
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidOperationException(
                $"Visual-story APNG frame is not decodable: {displayPath}",
                ex);
        }
    }

    private static void ValidatePngPixelFormat(
        byte bitDepth,
        byte colorType,
        byte interlaceMethod,
        string displayPath)
    {
        var valid = colorType switch
        {
            0 => bitDepth is 1 or 2 or 4 or 8 or 16,
            2 => bitDepth is 8 or 16,
            3 => bitDepth is 1 or 2 or 4 or 8,
            4 => bitDepth is 8 or 16,
            6 => bitDepth is 8 or 16,
            _ => false
        };
        if (!valid || interlaceMethod > 1)
            throw new InvalidOperationException($"Visual-story APNG has an unsupported pixel format: {displayPath}");
    }

    private static long ComputeDecodedFrameBytes(
        uint width,
        uint height,
        byte bitDepth,
        byte colorType,
        byte interlaceMethod)
    {
        var channels = colorType switch
        {
            0 or 3 => 1,
            2 => 3,
            4 => 2,
            6 => 4,
            _ => throw new InvalidOperationException("Unsupported PNG color type.")
        };
        if (interlaceMethod == 0)
            return ComputePassBytes(width, height, channels, bitDepth);

        ReadOnlySpan<(uint X, uint Y, uint Dx, uint Dy)> passes =
        [
            (0, 0, 8, 8),
            (4, 0, 8, 8),
            (0, 4, 4, 8),
            (2, 0, 4, 4),
            (0, 2, 2, 4),
            (1, 0, 2, 2),
            (0, 1, 1, 2)
        ];
        var total = 0L;
        foreach (var pass in passes)
        {
            var passWidth = PassLength(width, pass.X, pass.Dx);
            var passHeight = PassLength(height, pass.Y, pass.Dy);
            total = checked(total + ComputePassBytes(passWidth, passHeight, channels, bitDepth));
        }
        return total;
    }

    internal static long ReserveApngDecodedBytes(long currentBytes, long frameBytes, string displayPath)
    {
        if (currentBytes < 0) throw new ArgumentOutOfRangeException(nameof(currentBytes));
        if (frameBytes < 0) throw new ArgumentOutOfRangeException(nameof(frameBytes));
        var total = checked(currentBytes + frameBytes);
        if (total > MaximumApngDecodedBytes)
        {
            throw new InvalidOperationException(
                $"Visual-story animated artifact exceeds the aggregate decoded-byte safety limit: {displayPath}");
        }
        return total;
    }

    private static long ComputePassBytes(uint width, uint height, int channels, byte bitDepth)
    {
        if (width == 0 || height == 0) return 0;
        var rowBytes = checked(((long)width * channels * bitDepth + 7) / 8);
        return checked((rowBytes + 1) * height);
    }

    private static uint PassLength(uint length, uint start, uint step)
        => length <= start ? 0 : (length - start + step - 1) / step;

    private static void ValidateChunkCrc(
        byte[] bytes,
        int typeOffset,
        int dataOffset,
        int dataLength,
        string displayPath)
    {
        var expected = ReadUInt32(bytes, dataOffset + dataLength);
        var crc = uint.MaxValue;
        for (var index = typeOffset; index < dataOffset + dataLength; index++)
        {
            crc ^= bytes[index];
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? 0xEDB88320U ^ (crc >> 1) : crc >> 1;
        }
        if (~crc != expected)
            throw new InvalidOperationException($"Visual-story APNG contains a corrupt PNG chunk: {displayPath}");
    }

    private static uint ReadUInt32(byte[] bytes, int offset)
    {
        return ((uint)bytes[offset] << 24) |
               ((uint)bytes[offset + 1] << 16) |
               ((uint)bytes[offset + 2] << 8) |
               bytes[offset + 3];
    }
}
