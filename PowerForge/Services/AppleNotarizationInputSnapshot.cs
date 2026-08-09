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

    internal string PublishTo(string destinationPath, string expectedSha256)
    {
        var destination = Path.GetFullPath(destinationPath);
        var sourceSha256 = AppleNotarizationService.ComputeArtifactSha256(ArtifactPath);
        if (!sourceSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The private notarized Apple artifact changed before publication. Expected '{expectedSha256}', received '{sourceSha256}'.");
        }

        var parent = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException($"Apple notarization artifact path has no parent: {destination}");
        Directory.CreateDirectory(parent);
        var name = Path.GetFileName(destination);
        var stageRoot = Path.Combine(parent, $".powerforge-stage-{Guid.NewGuid():N}");
        var stage = Path.Combine(stageRoot, name);
        var backup = Path.Combine(parent, $".{name}.powerforge-backup-{Guid.NewGuid():N}");
        var sourceIsDirectory = Directory.Exists(ArtifactPath);
        var movedExisting = false;
        var published = false;
        try
        {
            Directory.CreateDirectory(stageRoot);
            if (sourceIsDirectory)
            {
                AppleArtifactCopy.CopyDirectory(ArtifactPath, stage);
            }
            else
            {
                File.Copy(ArtifactPath, stage, overwrite: false);
#if NET8_0_OR_GREATER
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(stage, File.GetUnixFileMode(ArtifactPath));
#endif
                File.SetAttributes(stage, File.GetAttributes(ArtifactPath));
            }

            var stagedSha256 = AppleNotarizationService.ComputeArtifactSha256(stage);
            if (!stagedSha256.Equals(sourceSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The staged notarized Apple artifact changed during publication. Expected '{sourceSha256}', received '{stagedSha256}'.");
            }

            if (Directory.Exists(destination))
            {
                Directory.Move(destination, backup);
                movedExisting = true;
            }
            else if (File.Exists(destination))
            {
                File.Move(destination, backup);
                movedExisting = true;
            }

            if (sourceIsDirectory)
                Directory.Move(stage, destination);
            else
                File.Move(stage, destination);
            published = true;

            var publishedSha256 = AppleNotarizationService.ComputeArtifactSha256(destination);
            if (!publishedSha256.Equals(sourceSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The published notarized Apple artifact changed during publication. Expected '{sourceSha256}', received '{publishedSha256}'.");
            }

            TryDeletePath(backup);
            return publishedSha256;
        }
        catch
        {
            if (published)
                DeletePath(destination);
            if (movedExisting)
            {
                if (Directory.Exists(backup))
                    Directory.Move(backup, destination);
                else if (File.Exists(backup))
                    File.Move(backup, destination);
            }
            throw;
        }
        finally
        {
            TryDeletePath(stageRoot);
        }
    }

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

    private static void DeletePath(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        else if (File.Exists(path))
            File.Delete(path);
    }

    private static void TryDeletePath(string path)
    {
        try { DeletePath(path); }
        catch { /* retain recovery bytes rather than masking publication or rollback */ }
    }
}
