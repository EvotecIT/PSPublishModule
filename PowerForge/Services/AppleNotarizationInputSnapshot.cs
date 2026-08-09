namespace PowerForge;

/// <summary>Provides a private, hash-verified notarization submission input.</summary>
internal sealed class AppleNotarizationInputSnapshot : IDisposable
{
    private readonly AppleArchiveUploadSnapshot? _directorySnapshot;
    private bool _disposed;

    private AppleNotarizationInputSnapshot(
        string rootPath,
        string artifactPath,
        AppleArchiveUploadSnapshot? directorySnapshot)
    {
        RootPath = rootPath;
        ArtifactPath = artifactPath;
        _directorySnapshot = directorySnapshot;
    }

    internal string RootPath { get; }

    internal string ArtifactPath { get; }

    internal static AppleNotarizationInputSnapshot Create(string artifactPath, string expectedSha256)
    {
        var source = Path.GetFullPath(artifactPath);
        if (Directory.Exists(source))
        {
            var directorySnapshot = AppleArchiveUploadSnapshot.Create(source, expectedSha256);
            return new AppleNotarizationInputSnapshot(
                directorySnapshot.RootPath,
                directorySnapshot.ArchivePath,
                directorySnapshot);
        }
        if (!File.Exists(source))
            throw new FileNotFoundException("Apple notarization artifact was not found.", source);

        var root = Path.Combine(Path.GetTempPath(), "PowerForge", "apple-notarization-inputs", Guid.NewGuid().ToString("N"));
        var snapshotPath = Path.Combine(root, Path.GetFileName(source));
        Directory.CreateDirectory(root);
#if NET8_0_OR_GREATER
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
#endif
        try
        {
            File.Copy(source, snapshotPath, overwrite: false);
#if NET8_0_OR_GREATER
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(snapshotPath, File.GetUnixFileMode(source));
#endif
            File.SetAttributes(snapshotPath, File.GetAttributes(source));
            var actual = AppleNotarizationService.ComputeArtifactSha256(snapshotPath);
            if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The private Apple notarization input does not match the approved artifact. Expected '{expectedSha256}', received '{actual}'.");
            }
            return new AppleNotarizationInputSnapshot(root, snapshotPath, directorySnapshot: null);
        }
        catch
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_directorySnapshot is not null)
        {
            _directorySnapshot.Dispose();
            return;
        }
        if (Directory.Exists(RootPath))
            Directory.Delete(RootPath, recursive: true);
    }
}
