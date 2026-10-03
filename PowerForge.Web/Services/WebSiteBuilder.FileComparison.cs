namespace PowerForge.Web;

public static partial class WebSiteBuilder
{
    private static bool FileContentsEqual(string sourcePath, string destinationPath)
    {
        using var source = File.OpenRead(sourcePath);
        using var destination = File.OpenRead(destinationPath);
        Span<byte> sourceBuffer = stackalloc byte[4096];
        Span<byte> destinationBuffer = stackalloc byte[4096];
        while (true)
        {
            var count = source.Read(sourceBuffer);
            if (count == 0) return destination.ReadByte() == -1;
            destination.ReadExactly(destinationBuffer[..count]);
            if (!sourceBuffer[..count].SequenceEqual(destinationBuffer[..count])) return false;
        }
    }
}
