namespace PowerForge;

/// <summary>Copies one approved archive into a private upload input so exporters cannot observe transient source changes.</summary>
internal sealed class AppleArchiveUploadSnapshot : IDisposable
{
    private bool _disposed;

    private AppleArchiveUploadSnapshot(string rootPath, string archivePath)
    {
        RootPath = rootPath;
        ArchivePath = archivePath;
    }

    internal string RootPath { get; }

    internal string ArchivePath { get; }

    internal static AppleArchiveUploadSnapshot Create(string archivePath, string expectedSha256)
    {
        var source = Path.GetFullPath(archivePath);
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"Approved Apple archive was not found: {source}");

        var root = Path.Combine(Path.GetTempPath(), "PowerForge", "apple-upload-snapshots", Guid.NewGuid().ToString("N"));
        var snapshotPath = Path.Combine(root, Path.GetFileName(source));
        Directory.CreateDirectory(root);
#if NET8_0_OR_GREATER
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
#endif
        try
        {
            AppleArtifactCopy.CopyDirectory(source, snapshotPath);
            var actual = AppleNotarizationService.ComputeArtifactSha256(snapshotPath);
            if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The private Apple upload snapshot does not match the approved archive. Expected '{expectedSha256}', received '{actual}'.");
            }
            return new AppleArchiveUploadSnapshot(root, snapshotPath);
        }
        catch
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
            throw;
        }
    }

    internal void ValidateUnchanged(string expectedSha256)
    {
        var actual = AppleNotarizationService.ComputeArtifactSha256(ArchivePath);
        if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The private Apple upload snapshot changed while xcodebuild was reading it. Expected '{expectedSha256}', received '{actual}'. Discard the upload/export result and inspect remote state before retrying.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (Directory.Exists(RootPath))
            Directory.Delete(RootPath, recursive: true);
    }

}
