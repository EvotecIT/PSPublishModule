using System.Text;

namespace PowerForgeStudio.Orchestrator.Workspace;

/// <summary>Atomically occupies a reviewed missing working-copy path until registration cleanup finishes.</summary>
internal sealed class WorkingCopyPathGuard : IDisposable
{
    private const string GuardMarker = "PowerForge Studio stale-registration guard v1";
    private readonly string _path;
    private FileStream? _stream;

    private WorkingCopyPathGuard(string path, FileStream stream)
    {
        _path = path;
        _stream = stream;
    }

    public static WorkingCopyPathGuard Acquire(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
            throw new IOException("The parent of the reviewed stale worktree path no longer exists.");

        FileStream? stream = null;
        try
        {
            stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 4096,
                FileOptions.WriteThrough);
            var bytes = Encoding.UTF8.GetBytes(GuardMarker);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
            return new WorkingCopyPathGuard(fullPath, stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stream?.Dispose();
            throw new IOException("The reviewed stale worktree path changed before Studio could guard it. No registration was removed.", ex);
        }
    }

    public void Dispose()
    {
        var stream = Interlocked.Exchange(ref _stream, null);
        if (stream is null) return;
        stream.Dispose();
        if (File.Exists(_path)) File.Delete(_path);
    }
}
