namespace PowerForge;

internal sealed class ManagedModuleInstallLock : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(15);

    private readonly FileStream _stream;

    private ManagedModuleInstallLock(string path, FileStream stream)
    {
        Path = path;
        _stream = stream;
    }

    public string Path { get; }

    public static ManagedModuleInstallLock Acquire(string moduleRoot, string moduleName, CancellationToken cancellationToken)
    {
        var lockDirectory = System.IO.Path.Combine(moduleRoot, ".powerforge", "locks");
        Directory.CreateDirectory(lockDirectory);
        var lockPath = System.IO.Path.Combine(lockDirectory, Sanitize(moduleName) + ".lock");
        var deadline = DateTimeOffset.UtcNow.Add(Timeout);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                stream.SetLength(0);
                var marker = System.Text.Encoding.UTF8.GetBytes(
                    System.Diagnostics.Process.GetCurrentProcess().Id + Environment.NewLine +
                    DateTimeOffset.UtcNow.ToString("O") + Environment.NewLine);
                stream.Write(marker, 0, marker.Length);
                stream.Position = 0;
                return new ManagedModuleInstallLock(lockPath, stream);
            }
            catch (IOException) when (DateTimeOffset.UtcNow < deadline)
            {
                Thread.Sleep(RetryDelay);
            }
        }
    }

    public void Dispose()
    {
        _stream.Dispose();
    }

    private static string Sanitize(string value)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var chars = value.Trim().Select(character => invalid.Contains(character) ? '_' : character).ToArray();
        return new string(chars);
    }
}
