namespace PowerForge;

internal sealed class ManagedModuleExtractedPackageCacheLock : IDisposable
{
    private readonly FileStream _stream;

    private ManagedModuleExtractedPackageCacheLock(FileStream stream)
    {
        _stream = stream;
    }

    public static ManagedModuleExtractedPackageCacheLock Acquire(
        string packageCacheDirectory,
        string packageSha256,
        CancellationToken cancellationToken)
    {
        var lockDirectory = Path.Combine(Path.GetFullPath(packageCacheDirectory), ".x", "l");
        Directory.CreateDirectory(lockDirectory);
        var lockPath = Path.Combine(lockDirectory, packageSha256.Substring(0, 32) + ".lock");

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return new ManagedModuleExtractedPackageCacheLock(stream);
            }
            catch (IOException)
            {
                Thread.Sleep(50);
            }
        }
    }

    public void Dispose()
    {
        _stream.Dispose();
    }
}
