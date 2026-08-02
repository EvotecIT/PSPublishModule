using System.IO.Compression;
using System.Text;
using System.Xml;
using ImageMagick;

namespace PowerForge.Web;

/// <summary>
/// Validates browser-renderable visual-story animation payloads without executing producer code.
/// </summary>
internal static class WebVisualStoryAnimatedArtifactValidator
{
    private readonly record struct SvgElementIdentity(string LocalName, string? Id, string[] Classes);

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
            ValidateSvg(path, displayPath, requireAnimation: true);
            return;
        }
        if (string.Equals(format, "apng", StringComparison.OrdinalIgnoreCase))
        {
            ValidateApng(path, displayPath);
            return;
        }

        ValidateGif(path, displayPath, requireMultipleFrames: true);
    }

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

    internal static void ValidateSvg(string path, string displayPath, bool requireAnimation)
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
            var sawDeclarativeAnimation = false;
            var cssKeyframeNames = new HashSet<string>(StringComparer.Ordinal);
            var cssAnimationNames = new HashSet<string>(StringComparer.Ordinal);
            var cssStyleBlocks = new List<string>();
            var svgElements = new List<SvgElementIdentity> { ReadSvgElementIdentity(reader) };
            var rootInlineStyle = reader.GetAttribute("style");
            ValidateSelfContainedReferences(reader, displayPath);
            AddCssAnimationNames(rootInlineStyle, cssAnimationNames, displayPath);
            var insideStyle = false;
            StringBuilder? currentStyle = null;
            var pendingAnimateMotionDepth = -1;
            while (reader.Read())
            {
                if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA && insideStyle)
                {
                    currentStyle!.Append(reader.Value);
                    continue;
                }
                if (reader.NodeType == XmlNodeType.EndElement &&
                    string.Equals(reader.LocalName, "style", StringComparison.Ordinal) &&
                    string.Equals(reader.NamespaceURI, SvgNamespace, StringComparison.Ordinal))
                {
                    var css = currentStyle?.ToString() ?? string.Empty;
                    AddNames(cssKeyframeNames, WebVisualStoryCssAnimationValidator.GetKeyframeNames(css));
                    if (WebVisualStoryCssAnimationValidator.ContainsExternalResourceReference(css))
                    {
                        throw new InvalidOperationException(
                            $"Visual-story SVG artifacts must be self-contained and cannot reference external resources: {displayPath}");
                    }
                    cssStyleBlocks.Add(css);
                    currentStyle = null;
                    insideStyle = false;
                    continue;
                }
                if (reader.NodeType == XmlNodeType.EndElement &&
                    pendingAnimateMotionDepth == reader.Depth &&
                    string.Equals(reader.LocalName, "animateMotion", StringComparison.Ordinal) &&
                    string.Equals(reader.NamespaceURI, SvgNamespace, StringComparison.Ordinal))
                {
                    pendingAnimateMotionDepth = -1;
                    continue;
                }
                if (reader.NodeType != XmlNodeType.Element ||
                    !string.Equals(reader.NamespaceURI, SvgNamespace, StringComparison.Ordinal))
                    continue;

                ValidateSelfContainedReferences(reader, displayPath);
                svgElements.Add(ReadSvgElementIdentity(reader));
                if (IsDeclarativeAnimationElement(reader))
                    sawDeclarativeAnimation = true;
                else if (string.Equals(reader.LocalName, "animateMotion", StringComparison.Ordinal) &&
                         HasEffectiveSmilTiming(reader) &&
                         !reader.IsEmptyElement)
                    pendingAnimateMotionDepth = reader.Depth;
                else if (pendingAnimateMotionDepth >= 0 &&
                         reader.Depth == pendingAnimateMotionDepth + 1 &&
                         string.Equals(reader.LocalName, "mpath", StringComparison.Ordinal) &&
                         HasLocalMotionPathReference(reader))
                    sawDeclarativeAnimation = true;

                var inlineStyle = reader.GetAttribute("style");
                AddCssAnimationNames(inlineStyle, cssAnimationNames, displayPath);

                insideStyle = string.Equals(reader.LocalName, "style", StringComparison.Ordinal) && !reader.IsEmptyElement;
                if (insideStyle)
                    currentStyle = new StringBuilder();
            }
            foreach (var css in cssStyleBlocks)
            {
                AddNames(
                    cssAnimationNames,
                    WebVisualStoryCssAnimationValidator.GetEffectiveAnimationNamesForMatchingSelectors(
                        css,
                        selector => svgElements.Any(element => MatchesSimpleSelector(selector, element))));
            }
            if (requireAnimation &&
                !sawDeclarativeAnimation &&
                !cssAnimationNames.Overlaps(cssKeyframeNames))
            {
                throw new InvalidOperationException(
                    $"Visual-story animated SVG artifact does not contain a supported animation: {displayPath}");
            }
        }
        catch (XmlException ex)
        {
            throw new InvalidOperationException(
                $"Visual-story animated artifact is not a valid SVG: {displayPath}",
                ex);
        }
    }

    private static bool IsDeclarativeAnimationElement(XmlReader reader)
    {
        switch (reader.LocalName)
        {
            case "set":
                return reader.GetAttribute("attributeName") is { Length: > 0 } &&
                       reader.GetAttribute("to") is { Length: > 0 } &&
                       HasEffectiveSmilTiming(reader);
            case "animate":
            case "animateTransform":
                return reader.GetAttribute("attributeName") is { Length: > 0 } &&
                       HasEffectiveSmilTiming(reader) &&
                       HasAnimationValue(reader);
            case "animateMotion":
                return HasEffectiveSmilTiming(reader) &&
                       (reader.GetAttribute("path") is { Length: > 0 } || HasAnimationValue(reader));
            default:
                return false;
        }
    }

    private static bool HasAnimationValue(XmlReader reader)
        => reader.GetAttribute("values") is { Length: > 0 } ||
           reader.GetAttribute("to") is { Length: > 0 } ||
           reader.GetAttribute("by") is { Length: > 0 };

    private static bool HasLocalMotionPathReference(XmlReader reader)
    {
        var reference = reader.GetAttribute("href") ??
                        reader.GetAttribute("href", "http://www.w3.org/1999/xlink");
        return reference is { Length: > 1 } && reference[0] == '#';
    }

    private static SvgElementIdentity ReadSvgElementIdentity(XmlReader reader)
    {
        var classes = (reader.GetAttribute("class") ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return new SvgElementIdentity(reader.LocalName, reader.GetAttribute("id"), classes);
    }

    private static bool MatchesSimpleSelector(string selector, SvgElementIdentity element)
    {
        var token = selector.Trim();
        if (token.Length == 0 || token.Any(char.IsWhiteSpace))
            return false;

        var index = 0;
        if (token[index] == '*')
            index++;
        else if (token[index] is not ('.' or '#'))
        {
            var start = index;
            while (index < token.Length && IsSimpleCssIdentifierCharacter(token[index]))
                index++;
            if (index == start ||
                !string.Equals(token.Substring(start, index - start), element.LocalName, StringComparison.Ordinal))
                return false;
        }

        while (index < token.Length)
        {
            var prefix = token[index++];
            if (prefix is not ('.' or '#'))
                return false;
            var start = index;
            while (index < token.Length && IsSimpleCssIdentifierCharacter(token[index]))
                index++;
            if (index == start)
                return false;
            var value = token.Substring(start, index - start);
            if (prefix == '#')
            {
                if (!string.Equals(value, element.Id, StringComparison.Ordinal))
                    return false;
            }
            else if (!element.Classes.Contains(value, StringComparer.Ordinal))
                return false;
        }
        return true;
    }

    private static bool IsSimpleCssIdentifierCharacter(char value)
        => char.IsLetterOrDigit(value) || value is '-' or '_';

    private static void AddCssAnimationNames(
        string? css,
        HashSet<string> animationNames,
        string displayPath)
    {
        if (string.IsNullOrWhiteSpace(css))
            return;
        if (WebVisualStoryCssAnimationValidator.ContainsExternalResourceReference(css))
        {
            throw new InvalidOperationException(
                $"Visual-story SVG artifacts must be self-contained and cannot reference external resources: {displayPath}");
        }
        AddNames(animationNames, WebVisualStoryCssAnimationValidator.GetEffectiveAnimationNames(css));
    }

    private static void AddNames(HashSet<string> destination, IReadOnlySet<string> source)
    {
        foreach (var name in source)
            destination.Add(name);
    }

    private static void ValidateSelfContainedReferences(XmlReader reader, string displayPath)
    {
        if (!reader.HasAttributes)
            return;
        if (!reader.MoveToFirstAttribute())
            return;
        do
        {
            var reference = reader.Value.Trim();
            var isDirectReference = string.Equals(reader.LocalName, "href", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(reader.LocalName, "src", StringComparison.OrdinalIgnoreCase);
            if (isDirectReference && reference.Length > 0 && reference[0] != '#' ||
                WebVisualStoryCssAnimationValidator.ContainsExternalResourceReference(reference))
            {
                throw new InvalidOperationException(
                    $"Visual-story SVG artifacts must be self-contained and cannot reference external resources: {displayPath}");
            }
        } while (reader.MoveToNextAttribute());
        reader.MoveToElement();
    }

    private static bool HasEffectiveSmilTiming(XmlReader reader)
        => HasPositiveSmilDuration(reader.GetAttribute("dur")) &&
           HasAutomaticSmilBegin(reader.GetAttribute("begin")) &&
           HasActiveSmilRepeat(reader.GetAttribute("repeatCount"), reader.GetAttribute("repeatDur"));

    private static bool HasAutomaticSmilBegin(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;
        return value.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(static token => token.Trim())
            .Any(static token => token == "0" || TryParseSmilClockValue(token, out var milliseconds) && milliseconds >= 0);
    }

    private static bool HasActiveSmilRepeat(string? repeatCount, string? repeatDuration)
    {
        if (!string.IsNullOrWhiteSpace(repeatCount) &&
            !string.Equals(repeatCount.Trim(), "indefinite", StringComparison.OrdinalIgnoreCase) &&
            (!double.TryParse(
                repeatCount.Trim(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var count) ||
             !double.IsFinite(count) ||
             count <= 0))
            return false;
        return string.IsNullOrWhiteSpace(repeatDuration) ||
               string.Equals(repeatDuration.Trim(), "indefinite", StringComparison.OrdinalIgnoreCase) ||
               HasPositiveSmilDuration(repeatDuration);
    }

    private static bool HasPositiveSmilDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        return TryParseSmilClockValue(value.Trim(), out var milliseconds) && milliseconds > 0;
    }

    private static bool TryParseSmilClockValue(string token, out double milliseconds)
    {
        milliseconds = 0;
        if (token.EndsWith("ms", StringComparison.OrdinalIgnoreCase) &&
            double.TryParse(token[..^2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedMilliseconds))
        {
            milliseconds = parsedMilliseconds;
            return double.IsFinite(milliseconds);
        }
        var units = new[] { (Suffix: "min", Multiplier: 60d), (Suffix: "h", Multiplier: 3600d), (Suffix: "s", Multiplier: 1d) };
        foreach (var unit in units)
        {
            if (!token.EndsWith(unit.Suffix, StringComparison.OrdinalIgnoreCase) ||
                !double.TryParse(token[..^unit.Suffix.Length], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number))
                continue;
            milliseconds = number * unit.Multiplier * 1000d;
            return double.IsFinite(milliseconds);
        }
        if (!TimeSpan.TryParse(token, System.Globalization.CultureInfo.InvariantCulture, out var duration))
            return false;
        milliseconds = duration.TotalMilliseconds;
        return double.IsFinite(milliseconds);
    }

    private static void ValidateApng(string path, string displayPath)
    {
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
        using var defaultImageData = new MemoryStream();
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
                if (frameCount == 0 && sawImageData && defaultImageData.Length > 0)
                {
                    decodedFrameBytes = ReserveApngDecodedBytes(
                        decodedFrameBytes,
                        ComputeDecodedFrameBytes(
                            canvasWidth,
                            canvasHeight,
                            bitDepth,
                            colorType,
                            interlaceMethod),
                        displayPath);
                    ValidateFrameData(
                        defaultImageData,
                        canvasWidth,
                        canvasHeight,
                        bitDepth,
                        colorType,
                        interlaceMethod,
                        displayPath);
                }
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
                if (frameData is null)
                    defaultImageData.Write(bytes, dataOffset, dataLength);
                else
                    frameData.Write(bytes, dataOffset, dataLength);
            }
            else if (first == 'f' && second == 'd' && third == 'A' && fourth == 'T')
            {
                if (frameData is null || currentFrameAllowsIdat || dataLength < 5 ||
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
        ValidatePngEnvelope(path, displayPath);
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
            var filterValidator = new PngScanlineFilterValidator(
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
                filterValidator.Consume(buffer, read, displayPath);
                total += read;
                if (total > expectedBytes)
                    throw new InvalidOperationException($"Visual-story APNG frame expands beyond its dimensions: {displayPath}");
            }
            if (total != expectedBytes)
                throw new InvalidOperationException($"Visual-story APNG frame has incomplete pixel data: {displayPath}");
            filterValidator.EnsureComplete(displayPath);
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
