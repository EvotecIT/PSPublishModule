using System.Security.Cryptography;
using ImageMagick;

namespace PowerForge.Web;

/// <summary>Decodes APNG frame payloads and compares their fully composed RGBA canvases.</summary>
internal sealed class ApngRenderedFrameAccumulator
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private readonly byte[] _header;
    private readonly IReadOnlyList<(byte[] Type, byte[] Data)> _sharedChunks;
    private readonly uint _canvasWidth;
    private readonly uint _canvasHeight;
    private readonly byte[] _canvas;
    private readonly HashSet<string> _signatures = new(StringComparer.Ordinal);

    internal ApngRenderedFrameAccumulator(
        byte[] header,
        IReadOnlyList<(byte[] Type, byte[] Data)> sharedChunks,
        uint canvasWidth,
        uint canvasHeight)
    {
        _header = header;
        _sharedChunks = sharedChunks;
        _canvasWidth = canvasWidth;
        _canvasHeight = canvasHeight;
        _canvas = new byte[checked((int)((ulong)canvasWidth * canvasHeight * 4UL))];
    }

    internal int VisibleStateCount => _signatures.Count;

    internal void AddFrame(
        MemoryStream? compressed,
        uint width,
        uint height,
        uint x,
        uint y,
        byte disposal,
        byte blend,
        string displayPath)
    {
        if (compressed is null)
            return;

        var frameBytes = DecodeFrame(compressed, width, height, displayPath);
        var previous = disposal == 2 ? (byte[])_canvas.Clone() : null;
        Composite(frameBytes, width, height, x, y, blend);
        _signatures.Add(Convert.ToBase64String(SHA256.HashData(_canvas)));

        if (disposal == 1)
            ClearRectangle(width, height, x, y);
        else if (disposal == 2)
            Buffer.BlockCopy(previous!, 0, _canvas, 0, _canvas.Length);
    }

    private byte[] DecodeFrame(MemoryStream compressed, uint width, uint height, string displayPath)
    {
        using var png = new MemoryStream();
        png.Write(PngSignature);
        var header = (byte[])_header.Clone();
        WriteUInt32(header, 0, width);
        WriteUInt32(header, 4, height);
        WriteChunk(png, "IHDR", header);
        foreach (var chunk in _sharedChunks)
            WriteChunk(png, chunk.Type, chunk.Data);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", Array.Empty<byte>());
        png.Position = 0;

        try
        {
            using var image = new MagickImage(png);
            image.ColorSpace = ColorSpace.sRGB;
            image.Alpha(AlphaOption.Activate);
            var pixels = image.GetPixels().ToByteArray(PixelMapping.RGBA);
            var expected = checked((int)((ulong)width * height * 4UL));
            if (pixels is null || pixels.Length != expected)
                throw new InvalidOperationException($"Visual-story APNG frame has an unexpected decoded pixel size: {displayPath}");
            return pixels;
        }
        catch (MagickException ex)
        {
            throw new InvalidOperationException($"Visual-story APNG frame is not decodable: {displayPath}", ex);
        }
    }

    private void Composite(byte[] source, uint width, uint height, uint x, uint y, byte blend)
    {
        for (var row = 0U; row < height; row++)
        {
            for (var column = 0U; column < width; column++)
            {
                var sourceIndex = checked((int)(((ulong)row * width + column) * 4UL));
                var destinationIndex = checked((int)((((ulong)y + row) * _canvasWidth + x + column) * 4UL));
                if (blend == 0)
                {
                    Buffer.BlockCopy(source, sourceIndex, _canvas, destinationIndex, 4);
                    continue;
                }
                BlendOver(source, sourceIndex, _canvas, destinationIndex);
            }
        }
    }

    private static void BlendOver(byte[] source, int sourceIndex, byte[] destination, int destinationIndex)
    {
        var sourceAlpha = source[sourceIndex + 3];
        if (sourceAlpha == 255)
        {
            Buffer.BlockCopy(source, sourceIndex, destination, destinationIndex, 4);
            return;
        }
        if (sourceAlpha == 0)
            return;

        var destinationAlpha = destination[destinationIndex + 3];
        var inverseSourceAlpha = 255 - sourceAlpha;
        var outputAlpha = sourceAlpha + (destinationAlpha * inverseSourceAlpha + 127) / 255;
        for (var channel = 0; channel < 3; channel++)
        {
            var premultiplied = source[sourceIndex + channel] * sourceAlpha * 255 +
                                destination[destinationIndex + channel] * destinationAlpha * inverseSourceAlpha;
            destination[destinationIndex + channel] = outputAlpha == 0
                ? (byte)0
                : (byte)((premultiplied + outputAlpha * 127) / (outputAlpha * 255));
        }
        destination[destinationIndex + 3] = (byte)outputAlpha;
    }

    private void ClearRectangle(uint width, uint height, uint x, uint y)
    {
        for (var row = 0U; row < height; row++)
        {
            var start = checked((int)((((ulong)y + row) * _canvasWidth + x) * 4UL));
            Array.Clear(_canvas, start, checked((int)((ulong)width * 4UL)));
        }
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
        => WriteChunk(output, System.Text.Encoding.ASCII.GetBytes(type), data);

    private static void WriteChunk(Stream output, byte[] type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        WriteUInt32(length, 0, checked((uint)data.Length));
        output.Write(length);
        output.Write(type);
        output.Write(data);
        var crc = uint.MaxValue;
        foreach (var value in type.Concat(data))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? 0xEDB88320U ^ (crc >> 1) : crc >> 1;
        }
        Span<byte> checksum = stackalloc byte[4];
        WriteUInt32(checksum, 0, ~crc);
        output.Write(checksum);
    }

    private static void WriteUInt32(Span<byte> bytes, int offset, uint value)
    {
        bytes[offset] = (byte)(value >> 24);
        bytes[offset + 1] = (byte)(value >> 16);
        bytes[offset + 2] = (byte)(value >> 8);
        bytes[offset + 3] = (byte)value;
    }
}
