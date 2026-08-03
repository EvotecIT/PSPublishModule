using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ImageMagick;
using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public partial class WebSiteAuditOptimizeBuildTests {
    [Theory]
    [InlineData(null, null, WebImageMetadataPolicy.Preserve)]
    [InlineData("preserve", null, WebImageMetadataPolicy.Preserve)]
    [InlineData("strip-all", null, WebImageMetadataPolicy.StripAll)]
    [InlineData(null, true, WebImageMetadataPolicy.StripAll)]
    [InlineData(null, false, WebImageMetadataPolicy.Preserve)]
    public void ParseImageMetadataPolicy_MapsNamedAndLegacyValues(
        string? value,
        bool? legacyStripMetadata,
        WebImageMetadataPolicy expected) {
        Assert.Equal(expected, WebCliHelpers.ParseImageMetadataPolicy(value, legacyStripMetadata));
    }

    [Fact]
    public void ParseImageMetadataPolicy_RejectsConflictingLegacyValue() {
        Assert.Throws<ArgumentException>(() =>
            WebCliHelpers.ParseImageMetadataPolicy("preserve", legacyStripMetadata: true));
    }

    [Fact]
    public void WebPublishSpec_DeserializesNamedMetadataPolicy() {
        const string json = """
            {
              "optimize": {
                "imageMetadataPolicy": "stripAll"
              }
            }
            """;

        var spec = JsonSerializer.Deserialize<WebPublishSpec>(json, WebCliJson.Options);

        Assert.NotNull(spec);
        Assert.NotNull(spec.Optimize);
        Assert.Equal(WebImageMetadataPolicy.StripAll, spec.Optimize.ImageMetadataPolicy);
    }

    [Fact]
    public void WebPublishSchema_DeclaresNamedMetadataPolicy() {
        var schemaPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "Schemas",
            "powerforge.web.publishspec.schema.json"));
        var schema = JsonNode.Parse(File.ReadAllText(schemaPath));
        var policy = schema?["$defs"]?["OptimizeSpec"]?["properties"]?["ImageMetadataPolicy"];

        Assert.NotNull(policy);
        Assert.Equal("string", policy["type"]?.GetValue<string>());
        Assert.Equal("preserve", policy["enum"]?[0]?.GetValue<string>());
        Assert.Equal("stripAll", policy["enum"]?[1]?.GetValue<string>());
    }

    [Fact]
    public void OptimizeDetailed_DefaultMetadataPolicy_PreservesOriginalBytes() {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-metadata-preserve-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try {
            var imagePath = Path.Combine(root, "pixel.png");
            var original = CreateMinimalPngWithComment("rights");
            File.WriteAllBytes(imagePath, original);

            var result = WebAssetOptimizer.OptimizeDetailed(new WebAssetOptimizerOptions {
                SiteRoot = root,
                OptimizeImages = true,
                ImageExtensions = [".png"]
            });

            Assert.Equal(original, File.ReadAllBytes(imagePath));
            Assert.Equal(WebImageMetadataPolicy.Preserve, result.ImageMetadataPolicy);
            Assert.Equal(0, result.ImageMetadataPolicyAppliedCount);
            Assert.Equal(0, result.ImageOptimizedCount);
            Assert.DoesNotContain(result.UpdatedFiles, path => path.Equals("pixel.png", StringComparison.OrdinalIgnoreCase));
        } finally {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void OptimizeDetailed_StripAll_RewritesOriginalEvenWhenOutputIsLarger() {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-metadata-strip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try {
            var imagePath = Path.Combine(root, "photo.jpg");
            using (var source = new MagickImage(MagickColors.SlateBlue, 128, 128)) {
                source.AddNoise(NoiseType.Random);
                source.Quality = 10;
                source.Comment = "x";
                source.Write(imagePath, MagickFormat.Jpeg);
            }
            var original = File.ReadAllBytes(imagePath);

            var result = WebAssetOptimizer.OptimizeDetailed(new WebAssetOptimizerOptions {
                SiteRoot = root,
                OptimizeImages = true,
                ImageExtensions = [".jpg"],
                ImageQuality = 100,
                ImageMetadataPolicy = WebImageMetadataPolicy.StripAll
            });

            using var rewritten = new MagickImage(imagePath);
            var rewrittenLength = new FileInfo(imagePath).Length;
            Assert.True(
                rewrittenLength > original.LongLength,
                $"Expected stripped output ({rewrittenLength}) to exceed the compact source ({original.LongLength}).");
            Assert.True(string.IsNullOrEmpty(rewritten.Comment));
            Assert.Equal(WebImageMetadataPolicy.StripAll, result.ImageMetadataPolicy);
            Assert.Equal(1, result.ImageMetadataPolicyAppliedCount);
            Assert.Equal(1, result.ImageOptimizedCount);
            Assert.Contains(result.UpdatedFiles, path => path.Equals("photo.jpg", StringComparison.OrdinalIgnoreCase));
        } finally {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void OptimizeDetailed_LegacyStripSwitch_MapsToStripAll() {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-metadata-legacy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try {
            var imagePath = Path.Combine(root, "pixel.png");
            File.WriteAllBytes(imagePath, CreateMinimalPngWithComment("legacy"));

            var result = WebAssetOptimizer.OptimizeDetailed(new WebAssetOptimizerOptions {
                SiteRoot = root,
                OptimizeImages = true,
                ImageExtensions = [".png"],
                ImageStripMetadata = true
            });

            Assert.Equal(WebImageMetadataPolicy.StripAll, result.ImageMetadataPolicy);
            Assert.Equal(1, result.ImageMetadataPolicyAppliedCount);
        } finally {
            Directory.Delete(root, true);
        }
    }

    private static byte[] CreateMinimalPngWithComment(string comment) {
        byte[] png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        byte[] payload = Encoding.ASCII.GetBytes("Comment\0" + comment);
        byte[] chunk = CreatePngChunkForMetadataPolicy("tEXt", payload);
        const int insertOffset = 33;
        byte[] result = new byte[png.Length + chunk.Length];
        Buffer.BlockCopy(png, 0, result, 0, insertOffset);
        Buffer.BlockCopy(chunk, 0, result, insertOffset, chunk.Length);
        Buffer.BlockCopy(png, insertOffset, result, insertOffset + chunk.Length, png.Length - insertOffset);
        return result;
    }

    private static byte[] CreatePngChunkForMetadataPolicy(string type, byte[] payload) {
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        byte[] chunk = new byte[12 + payload.Length];
        WritePngUInt32(chunk, 0, (uint)payload.Length);
        Buffer.BlockCopy(typeBytes, 0, chunk, 4, typeBytes.Length);
        Buffer.BlockCopy(payload, 0, chunk, 8, payload.Length);

        byte[] crcInput = new byte[typeBytes.Length + payload.Length];
        Buffer.BlockCopy(typeBytes, 0, crcInput, 0, typeBytes.Length);
        Buffer.BlockCopy(payload, 0, crcInput, typeBytes.Length, payload.Length);
        WritePngUInt32(chunk, 8 + payload.Length, ComputePngCrcForMetadataPolicy(crcInput));
        return chunk;
    }

    private static uint ComputePngCrcForMetadataPolicy(byte[] data) {
        uint crc = 0xFFFFFFFF;
        foreach (byte value in data) {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }

        return ~crc;
    }

    private static void WritePngUInt32(byte[] value, int offset, uint number) {
        value[offset] = (byte)(number >> 24);
        value[offset + 1] = (byte)(number >> 16);
        value[offset + 2] = (byte)(number >> 8);
        value[offset + 3] = (byte)number;
    }
}
