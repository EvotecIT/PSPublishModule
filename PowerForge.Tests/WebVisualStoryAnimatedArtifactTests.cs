using System.IO.Compression;
using System.Text.Json;
using ImageMagick;
using PowerForge.Web;

namespace PowerForge.Tests;

public class WebVisualStoryAnimatedArtifactTests
{
    [Fact]
    public void Stage_RequiresRenderableAnimatedArtifactFormats()
    {
        var root = WebVisualStoryStagerTests.CreateBundle();
        try
        {
            var manifest = Path.Combine(root, "source", "story.json");
            var bundle = ReadBundle(manifest);
            var animated = Assert.Single(bundle.Artifacts, artifact => artifact.Role == "animated");
            animated.Format = "png";
            animated.Path = "demo.png";
            WriteBundle(manifest, bundle);

            var error = Assert.Throws<InvalidOperationException>(() =>
                WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
                {
                    ManifestPath = manifest,
                    OutputPath = Path.Combine(root, "published")
                }));

            Assert.Contains("svg, gif, or apng", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("svg", "broken.svg")]
    [InlineData("gif", "broken.gif")]
    [InlineData("apng", "broken.png")]
    public void Stage_RejectsCorruptAnimatedArtifacts(string format, string fileName)
    {
        var root = WebVisualStoryStagerTests.CreateBundle();
        try
        {
            var source = Path.Combine(root, "source");
            File.WriteAllText(Path.Combine(source, fileName), "not an animated image");
            var manifest = Path.Combine(source, "story.json");
            var bundle = ReadBundle(manifest);
            var animated = Assert.Single(bundle.Artifacts, artifact => artifact.Role == "animated");
            animated.Format = format;
            animated.Path = fileName;
            WriteBundle(manifest, bundle);

            var error = Assert.Throws<InvalidOperationException>(() =>
                WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
                {
                    ManifestPath = manifest,
                    OutputPath = Path.Combine(root, "published")
                }));

            Assert.Contains("animated artifact", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_RejectsSvgRootsOutsideTheSvgNamespace()
    {
        var root = WebVisualStoryStagerTests.CreateBundle();
        try
        {
            var source = Path.Combine(root, "source");
            File.WriteAllText(Path.Combine(source, "wrong-namespace.svg"), "<svg xmlns=\"urn:not-svg\"/>");
            var manifest = Path.Combine(source, "story.json");
            var bundle = ReadBundle(manifest);
            var animated = Assert.Single(bundle.Artifacts, artifact => artifact.Role == "animated");
            animated.Format = "svg";
            animated.Path = "wrong-namespace.svg";
            WriteBundle(manifest, bundle);

            var error = Assert.Throws<InvalidOperationException>(() =>
                WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
                {
                    ManifestPath = manifest,
                    OutputPath = Path.Combine(root, "published")
                }));

            Assert.Contains("valid SVG", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_RejectsUppercaseSvgRootElements()
    {
        var root = WebVisualStoryStagerTests.CreateBundle();
        try
        {
            var source = Path.Combine(root, "source");
            File.WriteAllText(
                Path.Combine(source, "uppercase.svg"),
                "<SVG xmlns=\"http://www.w3.org/2000/svg\"/>");
            var manifest = Path.Combine(source, "story.json");
            var bundle = ReadBundle(manifest);
            var animated = Assert.Single(bundle.Artifacts, artifact => artifact.Role == "animated");
            animated.Format = "svg";
            animated.Path = "uppercase.svg";
            WriteBundle(manifest, bundle);

            var error = Assert.Throws<InvalidOperationException>(() =>
                WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
                {
                    ManifestPath = manifest,
                    OutputPath = Path.Combine(root, "published")
                }));

            Assert.Contains("valid SVG", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_RejectsStaticSvgForAnimatedRole()
    {
        var root = WebVisualStoryStagerTests.CreateBundle();
        try
        {
            var source = Path.Combine(root, "source");
            File.WriteAllText(
                Path.Combine(source, "static.svg"),
                "<svg xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"1\" height=\"1\"/></svg>");
            var manifest = Path.Combine(source, "story.json");
            var bundle = ReadBundle(manifest);
            var animated = Assert.Single(bundle.Artifacts, artifact => artifact.Role == "animated");
            animated.Path = "static.svg";
            WriteBundle(manifest, bundle);

            var error = Assert.Throws<InvalidOperationException>(() =>
                WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
                {
                    ManifestPath = manifest,
                    OutputPath = Path.Combine(root, "published")
                }));

            Assert.Contains("supported animation", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("gif", "animated.gif", MagickFormat.Gif)]
    [InlineData("apng", "animated.png", MagickFormat.APng)]
    public void Stage_AcceptsDecodableAnimatedRasterArtifacts(
        string format,
        string fileName,
        MagickFormat magickFormat)
    {
        var root = WebVisualStoryStagerTests.CreateBundle();
        try
        {
            var source = Path.Combine(root, "source");
            var animationPath = Path.Combine(source, fileName);
            if (magickFormat == MagickFormat.APng)
            {
                WriteTinyApng(animationPath);
            }
            else
            {
                using var frames = new MagickImageCollection();
                frames.Add(new MagickImage(MagickColors.DeepSkyBlue, 2, 2) { AnimationDelay = 10 });
                frames.Add(new MagickImage(MagickColors.MediumSeaGreen, 2, 2) { AnimationDelay = 10 });
                frames.Write(animationPath, magickFormat);
            }

            var manifest = Path.Combine(source, "story.json");
            var bundle = ReadBundle(manifest);
            var animated = Assert.Single(bundle.Artifacts, artifact => artifact.Role == "animated");
            animated.Format = format;
            animated.Path = fileName;
            WriteBundle(manifest, bundle);

            var result = WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
            {
                ManifestPath = manifest,
                OutputPath = Path.Combine(root, "published")
            });

            Assert.Contains(
                WebVisualStoryStager.Load(result.ManifestPath).Artifacts,
                artifact => artifact.Role == "animated" && artifact.Format == format);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_RejectsApngFramesWithIncompletePixelData()
    {
        var root = WebVisualStoryStagerTests.CreateBundle();
        try
        {
            var source = Path.Combine(root, "source");
            var animationPath = Path.Combine(source, "animated.png");
            WriteTinyApng(animationPath, completeSecondFrame: false);
            var manifest = Path.Combine(source, "story.json");
            var bundle = ReadBundle(manifest);
            var animated = Assert.Single(bundle.Artifacts, artifact => artifact.Role == "animated");
            animated.Format = "apng";
            animated.Path = "animated.png";
            WriteBundle(manifest, bundle);

            var error = Assert.Throws<InvalidOperationException>(() =>
                WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
                {
                    ManifestPath = manifest,
                    OutputPath = Path.Combine(root, "published")
                }));

            Assert.Contains("incomplete pixel data", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_RejectsInvalidApngFrameControlOperations()
    {
        var root = WebVisualStoryStagerTests.CreateBundle();
        try
        {
            var source = Path.Combine(root, "source");
            var animationPath = Path.Combine(source, "animated.png");
            WriteTinyApng(animationPath, secondFrameDisposal: 3);
            var manifest = Path.Combine(source, "story.json");
            var bundle = ReadBundle(manifest);
            var animated = Assert.Single(bundle.Artifacts, artifact => artifact.Role == "animated");
            animated.Format = "apng";
            animated.Path = "animated.png";
            WriteBundle(manifest, bundle);

            var error = Assert.Throws<InvalidOperationException>(() =>
                WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
                {
                    ManifestPath = manifest,
                    OutputPath = Path.Combine(root, "published")
                }));

            Assert.Contains("frame control", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_RejectsIdatChunksForLaterApngFrames()
    {
        var root = WebVisualStoryStagerTests.CreateBundle();
        try
        {
            var source = Path.Combine(root, "source");
            var animationPath = Path.Combine(source, "animated.png");
            WriteTinyApng(animationPath, useIdatForSecondFrame: true);
            var manifest = Path.Combine(source, "story.json");
            var bundle = ReadBundle(manifest);
            var animated = Assert.Single(bundle.Artifacts, artifact => artifact.Role == "animated");
            animated.Format = "apng";
            animated.Path = "animated.png";
            WriteBundle(manifest, bundle);

            var error = Assert.Throws<InvalidOperationException>(() =>
                WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
                {
                    ManifestPath = manifest,
                    OutputPath = Path.Combine(root, "published")
                }));

            Assert.Contains("later animation frame", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_RejectsFdatForTheFirstIncludedApngFrame()
    {
        var root = WebVisualStoryStagerTests.CreateBundle();
        try
        {
            var source = Path.Combine(root, "source");
            var animationPath = Path.Combine(source, "animated.png");
            WriteApngWithFdatForFirstIncludedFrame(animationPath);
            var manifest = Path.Combine(source, "story.json");
            var bundle = ReadBundle(manifest);
            var animated = Assert.Single(bundle.Artifacts, artifact => artifact.Role == "animated");
            animated.Format = "apng";
            animated.Path = "animated.png";
            WriteBundle(manifest, bundle);

            var error = Assert.Throws<InvalidOperationException>(() =>
                WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
                {
                    ManifestPath = manifest,
                    OutputPath = Path.Combine(root, "published")
                }));

            Assert.Contains("invalid frame data", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_RejectsInvalidApngScanlineFilters()
    {
        var root = WebVisualStoryStagerTests.CreateBundle();
        try
        {
            var source = Path.Combine(root, "source");
            var animationPath = Path.Combine(source, "animated.png");
            WriteTinyApng(animationPath, secondFrameFilter: 5);
            var manifest = Path.Combine(source, "story.json");
            var bundle = ReadBundle(manifest);
            var animated = Assert.Single(bundle.Artifacts, artifact => artifact.Role == "animated");
            animated.Format = "apng";
            animated.Path = "animated.png";
            WriteBundle(manifest, bundle);

            var error = Assert.Throws<InvalidOperationException>(() =>
                WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
                {
                    ManifestPath = manifest,
                    OutputPath = Path.Combine(root, "published")
                }));

            Assert.Contains("scanline filter", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_RejectsInvalidApngDefaultImageScanlineFilters()
    {
        var root = WebVisualStoryStagerTests.CreateBundle();
        try
        {
            var source = Path.Combine(root, "source");
            var animationPath = Path.Combine(source, "animated.png");
            WriteApngWithExcludedDefaultImage(animationPath, defaultImageFilter: 5);
            var manifest = Path.Combine(source, "story.json");
            var bundle = ReadBundle(manifest);
            var animated = Assert.Single(bundle.Artifacts, artifact => artifact.Role == "animated");
            animated.Format = "apng";
            animated.Path = "animated.png";
            WriteBundle(manifest, bundle);

            var error = Assert.Throws<InvalidOperationException>(() =>
                WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
                {
                    ManifestPath = manifest,
                    OutputPath = Path.Combine(root, "published")
                }));

            Assert.Contains("scanline filter", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void PngScanlineFilterValidator_RejectsInvalidAdam7PassFilters()
    {
        var firstTwoPasses = new byte[10];
        firstTwoPasses[5] = 5;
        var validator = new PngScanlineFilterValidator(
            width: 8,
            height: 8,
            bitDepth: 8,
            colorType: 6,
            interlaceMethod: 1);

        var error = Assert.Throws<InvalidOperationException>(() =>
            validator.Consume(firstTwoPasses, firstTwoPasses.Length, "adam7.png"));

        Assert.Contains("scanline filter", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Stage_RejectsGifCollectionsBeyondTheFrameBudget()
    {
        var root = WebVisualStoryStagerTests.CreateBundle();
        try
        {
            var source = Path.Combine(root, "source");
            var animationPath = Path.Combine(source, "too-many.gif");
            using (var frames = new MagickImageCollection())
            {
                for (var index = 0; index < 241; index++)
                {
                    frames.Add(new MagickImage(MagickColors.DeepSkyBlue, 1, 1)
                    {
                        AnimationDelay = 1
                    });
                }
                frames.Write(animationPath, MagickFormat.Gif);
            }
            var manifest = Path.Combine(source, "story.json");
            var bundle = ReadBundle(manifest);
            var animated = Assert.Single(bundle.Artifacts, artifact => artifact.Role == "animated");
            animated.Format = "gif";
            animated.Path = "too-many.gif";
            WriteBundle(manifest, bundle);

            var error = Assert.Throws<InvalidOperationException>(() =>
                WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
                {
                    ManifestPath = manifest,
                    OutputPath = Path.Combine(root, "published")
                }));

            Assert.Contains("frame safety limit", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ApngValidation_RejectsAggregateDecodedBytesBeyondTheBudget()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            WebVisualStoryAnimatedArtifactValidator.ReserveApngDecodedBytes(
                500_000_000,
                20_000_000,
                "oversized.png"));

        Assert.Contains("aggregate decoded-byte safety limit", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static WebVisualStoryBundle ReadBundle(string manifest)
        => JsonSerializer.Deserialize<WebVisualStoryBundle>(File.ReadAllText(manifest), JsonOptions)!;

    private static void WriteBundle(string manifest, WebVisualStoryBundle bundle)
        => File.WriteAllText(manifest, JsonSerializer.Serialize(bundle, JsonOptions));

    internal static void WriteTinyApng(
        string path,
        bool completeSecondFrame = true,
        byte secondFrameDisposal = 0,
        bool useIdatForSecondFrame = false,
        byte secondFrameFilter = 0)
    {
        using var output = new MemoryStream();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        WriteUInt32(header, 0, 1);
        WriteUInt32(header, 4, 1);
        header[8] = 8;
        header[9] = 6;
        WritePngChunk(output, "IHDR", header);

        var animation = new byte[8];
        WriteUInt32(animation, 0, 2);
        WritePngChunk(output, "acTL", animation);
        WritePngChunk(output, "fcTL", FrameControl(sequence: 0));
        WritePngChunk(output, "IDAT", CompressPngPixel(0, 191, 255, 255));
        WritePngChunk(output, "fcTL", FrameControl(sequence: 1, disposal: secondFrameDisposal));
        var compressedFrame = completeSecondFrame
            ? CompressPngBytes(secondFrameFilter, 60, 179, 113, 255)
            : CompressPngBytes(0);
        if (useIdatForSecondFrame)
        {
            WritePngChunk(output, "IDAT", compressedFrame);
        }
        else
        {
            var frameData = new byte[4 + compressedFrame.Length];
            WriteUInt32(frameData, 0, 2);
            compressedFrame.CopyTo(frameData, 4);
            WritePngChunk(output, "fdAT", frameData);
        }
        WritePngChunk(output, "IEND", Array.Empty<byte>());
        File.WriteAllBytes(path, output.ToArray());
    }

    private static void WriteApngWithExcludedDefaultImage(string path, byte defaultImageFilter)
    {
        using var output = new MemoryStream();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        WriteUInt32(header, 0, 1);
        WriteUInt32(header, 4, 1);
        header[8] = 8;
        header[9] = 6;
        WritePngChunk(output, "IHDR", header);

        var animation = new byte[8];
        WriteUInt32(animation, 0, 2);
        WritePngChunk(output, "acTL", animation);
        WritePngChunk(output, "IDAT", CompressPngBytes(defaultImageFilter, 0, 0, 0, 255));

        WritePngChunk(output, "fcTL", FrameControl(sequence: 0));
        WriteFrameData(output, sequence: 1, CompressPngPixel(0, 191, 255, 255));
        WritePngChunk(output, "fcTL", FrameControl(sequence: 2));
        WriteFrameData(output, sequence: 3, CompressPngPixel(60, 179, 113, 255));
        WritePngChunk(output, "IEND", Array.Empty<byte>());
        File.WriteAllBytes(path, output.ToArray());
    }

    private static void WriteApngWithFdatForFirstIncludedFrame(string path)
    {
        using var output = new MemoryStream();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        WriteUInt32(header, 0, 1);
        WriteUInt32(header, 4, 1);
        header[8] = 8;
        header[9] = 6;
        WritePngChunk(output, "IHDR", header);

        var animation = new byte[8];
        WriteUInt32(animation, 0, 2);
        WritePngChunk(output, "acTL", animation);
        WritePngChunk(output, "fcTL", FrameControl(sequence: 0));
        WriteFrameData(output, sequence: 1, CompressPngPixel(0, 191, 255, 255));
        WritePngChunk(output, "fcTL", FrameControl(sequence: 2));
        WriteFrameData(output, sequence: 3, CompressPngPixel(60, 179, 113, 255));
        WritePngChunk(output, "IEND", Array.Empty<byte>());
        File.WriteAllBytes(path, output.ToArray());
    }

    private static void WriteFrameData(Stream output, uint sequence, byte[] compressedFrame)
    {
        var frameData = new byte[4 + compressedFrame.Length];
        WriteUInt32(frameData, 0, sequence);
        compressedFrame.CopyTo(frameData, 4);
        WritePngChunk(output, "fdAT", frameData);
    }

    private static byte[] FrameControl(uint sequence, byte disposal = 0)
    {
        var control = new byte[26];
        WriteUInt32(control, 0, sequence);
        WriteUInt32(control, 4, 1);
        WriteUInt32(control, 8, 1);
        control[21] = 1;
        control[23] = 10;
        control[24] = disposal;
        return control;
    }

    private static byte[] CompressPngPixel(byte red, byte green, byte blue, byte alpha)
        => CompressPngBytes(0, red, green, blue, alpha);

    private static byte[] CompressPngBytes(params byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(bytes);
        }
        return output.ToArray();
    }

    private static void WritePngChunk(Stream output, string type, byte[] data)
    {
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        var length = new byte[4];
        WriteUInt32(length, 0, (uint)data.Length);
        output.Write(length);
        output.Write(typeBytes);
        output.Write(data);
        var crcInput = new byte[typeBytes.Length + data.Length];
        typeBytes.CopyTo(crcInput, 0);
        data.CopyTo(crcInput, typeBytes.Length);
        var crc = new byte[4];
        WriteUInt32(crc, 0, PngCrc32(crcInput));
        output.Write(crc);
    }

    private static uint PngCrc32(byte[] data)
    {
        var crc = uint.MaxValue;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? 0xEDB88320U ^ (crc >> 1) : crc >> 1;
        }
        return ~crc;
    }

    private static void WriteUInt32(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}
