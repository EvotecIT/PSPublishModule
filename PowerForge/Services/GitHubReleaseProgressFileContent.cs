using System.Net;
using System.Net.Http;

namespace PowerForge;

internal sealed class GitHubReleaseProgressFileContent : HttpContent
{
    private const int BufferSize = 1024 * 1024;
    private const long MinimumReportedBytes = 4L * 1024 * 1024;
    private static readonly TimeSpan MaximumReportInterval = TimeSpan.FromMilliseconds(250);

    private readonly string _filePath;
    private readonly long _length;
    private readonly Action<long, long>? _reportProgress;

    internal GitHubReleaseProgressFileContent(
        string filePath,
        Action<long, long>? reportProgress)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _length = new FileInfo(filePath).Length;
        _reportProgress = reportProgress;
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        var buffer = new byte[BufferSize];
        long transferred = 0;
        long lastReportedBytes = 0;
        var lastReportedAt = DateTime.UtcNow;

        using var source = File.OpenRead(_filePath);
        while (true)
        {
            var read = await source.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            if (read <= 0)
                break;

            await stream.WriteAsync(buffer, 0, read).ConfigureAwait(false);
            transferred += read;

            var now = DateTime.UtcNow;
            if (transferred == _length ||
                transferred - lastReportedBytes >= MinimumReportedBytes ||
                now - lastReportedAt >= MaximumReportInterval)
            {
                _reportProgress?.Invoke(transferred, _length);
                lastReportedBytes = transferred;
                lastReportedAt = now;
            }
        }

        if (lastReportedBytes != transferred)
            _reportProgress?.Invoke(transferred, _length);
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _length;
        return true;
    }
}
