namespace PowerForge;

/// <summary>Owns a private xcodebuild archive destination and atomically publishes its verified bytes.</summary>
internal sealed class AppleArchiveBuildSnapshot : IDisposable {
    private bool _disposed;

    private AppleArchiveBuildSnapshot(string rootPath, string archivePath) {
        RootPath = rootPath;
        ArchivePath = archivePath;
    }

    internal string RootPath { get; }

    internal string ArchivePath { get; }

    internal static AppleArchiveBuildSnapshot Create(string destinationArchivePath) {
        var archiveName = Path.GetFileName(Path.GetFullPath(destinationArchivePath));
        if (string.IsNullOrWhiteSpace(archiveName))
            throw new InvalidOperationException($"Apple archive path has no file name: {destinationArchivePath}");

        var root = Path.Combine(Path.GetTempPath(), "PowerForge", "apple-archive-builds", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
#if NET8_0_OR_GREATER
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
#endif
        return new AppleArchiveBuildSnapshot(root, Path.Combine(root, archiveName));
    }

    /// <summary>
    /// Copies the private archive into a user-only same-volume staging directory, verifies it, and
    /// atomically replaces the configured public archive. A missing archive is retained only for
    /// injected process adapters.
    /// </summary>
    internal string? Publish(string destinationArchivePath, string? expectedSourceSha256 = null) {
        if (!Directory.Exists(ArchivePath))
            return null;

        var sourceSha256 = AppleNotarizationService.ComputeArtifactSha256(ArchivePath);
        if (!string.IsNullOrWhiteSpace(expectedSourceSha256) &&
            !sourceSha256.Equals(expectedSourceSha256, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException(
                $"The private Apple archive changed after xcodebuild completed. Expected '{expectedSourceSha256}', received '{sourceSha256}'.");
        }
        var destination = Path.GetFullPath(destinationArchivePath);
        var parent = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException($"Apple archive path has no parent: {destination}");
        Directory.CreateDirectory(parent);
        if (File.Exists(destination) ||
            (Directory.Exists(destination) && (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)) {
            throw new InvalidOperationException($"Apple archive path must be a regular directory: {destination}");
        }

        var name = Path.GetFileName(destination);
        var stageRoot = Path.Combine(parent, $".{name}.powerforge-stage-{Guid.NewGuid():N}");
        var stage = Path.Combine(stageRoot, name);
        var backup = Path.Combine(parent, $".{name}.powerforge-backup-{Guid.NewGuid():N}");
        var movedExisting = false;
        var published = false;
        try {
            Directory.CreateDirectory(stageRoot);
#if NET8_0_OR_GREATER
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(stageRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
#endif
            AppleArtifactCopy.CopyDirectory(ArchivePath, stage);
            var stagedSha256 = AppleNotarizationService.ComputeArtifactSha256(stage);
            if (!stagedSha256.Equals(sourceSha256, StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidOperationException(
                    $"The staged Apple archive changed during publication. Expected '{sourceSha256}', received '{stagedSha256}'.");
            }

            if (Directory.Exists(destination)) {
                Directory.Move(destination, backup);
                movedExisting = true;
            }
            Directory.Move(stage, destination);
            published = true;

            var publishedSha256 = AppleNotarizationService.ComputeArtifactSha256(destination);
            if (!publishedSha256.Equals(sourceSha256, StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidOperationException(
                    $"The published Apple archive changed before release processing. Expected '{sourceSha256}', received '{publishedSha256}'.");
            }
            if (movedExisting && Directory.Exists(backup))
                Directory.Delete(backup, recursive: true);
            return publishedSha256;
        } catch (Exception publicationException) {
            try {
                if (published && Directory.Exists(destination))
                    Directory.Delete(destination, recursive: true);
                if (movedExisting)
                    AppleArtifactCopy.RestoreDirectoryBackup(destination, backup);
            } catch (Exception rollbackException) {
                throw new AggregateException(
                    $"Apple archive publication failed and rollback could not complete. Recovery bytes are retained at '{backup}'.",
                    publicationException,
                    rollbackException);
            }
            throw;
        } finally {
            if (Directory.Exists(stageRoot))
                Directory.Delete(stageRoot, recursive: true);
        }
    }

    public void Dispose() {
        if (_disposed)
            return;
        _disposed = true;
        if (Directory.Exists(RootPath))
            Directory.Delete(RootPath, recursive: true);
    }
}
