using System.Text;

namespace PowerForge;

/// <summary>
/// Owns an exclusive file lease for one mutating Apple release operation.
/// </summary>
internal sealed class AppleReleaseOperationLock : IDisposable
{
    private readonly FileStream _stream;

    private AppleReleaseOperationLock(FileStream stream)
    {
        _stream = stream;
    }

    internal static AppleReleaseOperationLock Acquire(string lockPath, PowerForgeAppleReleaseAction action)
    {
        var directory = Path.GetDirectoryName(lockPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        try
        {
            var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            stream.SetLength(0);
            var processId = System.Diagnostics.Process.GetCurrentProcess().Id;
            var payload = Encoding.UTF8.GetBytes($"pid={processId}\naction={action}\nstartedAt={DateTimeOffset.UtcNow:O}\n");
            stream.Write(payload, 0, payload.Length);
            stream.Flush(flushToDisk: true);
            return new AppleReleaseOperationLock(stream);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException(
                $"Another Apple release operation owns '{lockPath}'. Wait for it to finish or verify that no release process is active before removing a stale lock file.",
                exception);
        }
    }

    public void Dispose()
    {
        _stream.Dispose();
    }
}
