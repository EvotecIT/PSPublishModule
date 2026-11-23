using System.IO.Compression;

namespace PowerForge;

/// <summary>Reads an iPhone Duo display screenshot: its size, and whether the display is live.</summary>
/// <remarks>
/// simctl captures the display that isn't live as solid black, so a display counts as live when enough sampled
/// pixels are lighter than near-black. Supports the 8-bit RGB/RGBA, non-interlaced PNGs simctl writes.
/// </remarks>
internal static class AppleDuoScreenshotProbe
{
    private const int DarkChannelLimit = 24;
    private const int SampleStep = 4;
    private const double LitFraction = 0.002;

    internal readonly struct Reading
    {
        public Reading(int width, int height, bool live) { Width = width; Height = height; Live = live; }
        public int Width { get; }
        public int Height { get; }
        public bool Live { get; }
    }

    internal static Reading Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    internal static Reading Read(Stream stream)
    {
        var signature = ReadExactly(stream, 8);
        if (signature[0] != 0x89 || signature[1] != (byte)'P' || signature[2] != (byte)'N' || signature[3] != (byte)'G')
            throw new InvalidDataException("The screenshot is not a PNG.");
        int width = 0, height = 0, channels = 0;
        using var compressed = new MemoryStream();
        while (true)
        {
            var length = ReadBigEndian(ReadExactly(stream, 4), 0);
            var type = System.Text.Encoding.ASCII.GetString(ReadExactly(stream, 4));
            if (length < 0) throw new InvalidDataException("The screenshot has an invalid PNG chunk.");
            var data = ReadExactly(stream, length);
            ReadExactly(stream, 4); // CRC
            if (type == "IHDR")
            {
                width = ReadBigEndian(data, 0);
                height = ReadBigEndian(data, 4);
                if (data[8] != 8 || data[12] != 0)
                    throw new InvalidDataException("Only 8-bit, non-interlaced screenshots are supported.");
                channels = data[9] switch { 2 => 3, 6 => 4, _ => throw new InvalidDataException("Only RGB and RGBA screenshots are supported.") };
            }
            else if (type == "IDAT") compressed.Write(data, 0, data.Length);
            else if (type == "IEND") break;
        }
        if (width <= 0 || height <= 0 || channels == 0) throw new InvalidDataException("The screenshot has no image header.");
        return new Reading(width, height, IsLive(compressed, width, height, channels));
    }

    private static bool IsLive(MemoryStream compressed, int width, int height, int channels)
    {
        compressed.Position = 2; // zlib header; DeflateStream reads the raw stream that follows.
        using var inflater = new DeflateStream(compressed, CompressionMode.Decompress);
        var stride = width * channels;
        var previous = new byte[stride];
        var current = new byte[stride];
        long samples = 0, lit = 0;
        for (var row = 0; row < height; row++)
        {
            var filter = inflater.ReadByte();
            if (filter < 0) throw new InvalidDataException("The screenshot's image data is truncated.");
            FillExactly(inflater, current);
            Unfilter(filter, current, previous, channels);
            if (row % SampleStep == 0)
            {
                for (var x = 0; x < width; x += SampleStep)
                {
                    var offset = x * channels;
                    samples++;
                    if (current[offset] > DarkChannelLimit || current[offset + 1] > DarkChannelLimit || current[offset + 2] > DarkChannelLimit) lit++;
                }
            }
            (previous, current) = (current, previous);
        }
        return samples > 0 && lit >= samples * LitFraction;
    }

    private static void Unfilter(int filter, byte[] row, byte[] above, int bpp)
    {
        switch (filter)
        {
            case 0: return;
            case 1: for (var i = bpp; i < row.Length; i++) row[i] += row[i - bpp]; return;
            case 2: for (var i = 0; i < row.Length; i++) row[i] += above[i]; return;
            case 3:
                for (var i = 0; i < row.Length; i++) row[i] += (byte)(((i >= bpp ? row[i - bpp] : 0) + above[i]) / 2);
                return;
            case 4:
                for (var i = 0; i < row.Length; i++)
                    row[i] += Paeth(i >= bpp ? row[i - bpp] : (byte)0, above[i], i >= bpp ? above[i - bpp] : (byte)0);
                return;
            default: throw new InvalidDataException($"The screenshot uses unknown PNG filter {filter}.");
        }
    }

    private static byte Paeth(byte left, byte up, byte upLeft)
    {
        var estimate = left + up - upLeft;
        var toLeft = Math.Abs(estimate - left);
        var toUp = Math.Abs(estimate - up);
        var toUpLeft = Math.Abs(estimate - upLeft);
        return toLeft <= toUp && toLeft <= toUpLeft ? left : toUp <= toUpLeft ? up : upLeft;
    }

    private static int ReadBigEndian(byte[] data, int offset)
        => (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];

    private static byte[] ReadExactly(Stream stream, int count)
    {
        var buffer = new byte[count];
        FillExactly(stream, buffer);
        return buffer;
    }

    private static void FillExactly(Stream stream, byte[] buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var chunk = stream.Read(buffer, read, buffer.Length - read);
            if (chunk == 0) throw new InvalidDataException("The screenshot is truncated.");
            read += chunk;
        }
    }
}
