namespace PowerForge;

/// <summary>Provides a private, hash-verified notarization submission input.</summary>
internal sealed class AppleNotarizationInputSnapshot : IDisposable
{
    private readonly AppleArchiveUploadSnapshot? _directorySnapshot;
    private readonly string? _fileMutationIdentity;
    private AppleReleaseSourceMutationMonitor? _fileSnapshotMonitor;
    private bool _disposed;

    private AppleNotarizationInputSnapshot(
        string rootPath,
        string artifactPath,
        AppleArchiveUploadSnapshot? directorySnapshot,
        AppleReleaseSourceMutationMonitor? fileSnapshotMonitor = null,
        string? fileMutationIdentity = null)
    {
        RootPath = rootPath;
        ArtifactPath = artifactPath;
        _directorySnapshot = directorySnapshot;
        _fileSnapshotMonitor = fileSnapshotMonitor;
        _fileMutationIdentity = fileMutationIdentity;
    }

    internal string RootPath { get; }

    internal string ArtifactPath { get; }

    internal void CompleteSubmissionCapture(string expectedSha256)
    {
        if (_directorySnapshot is not null)
        {
            _directorySnapshot.ValidateUnchanged(expectedSha256);
            return;
        }
        if (_fileSnapshotMonitor is null)
            return;
        try
        {
            _fileSnapshotMonitor.ValidateNoChanges();
            var actual = AppleNotarizationService.ComputeArtifactSha256(ArtifactPath);
            if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The private Apple notarization file changed while its submission bytes were captured. Expected '{expectedSha256}', received '{actual}'.");
            }
            var currentMutationIdentity = ExistingFilePathIdentityResolver.CapturePrivateFileMutationIdentity(
                ArtifactPath,
                "private Apple notarization file snapshot");
            if (!string.Equals(_fileMutationIdentity, currentMutationIdentity, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The private Apple notarization file snapshot identity changed while its submission bytes were captured. " +
                    "A transient write or hard-link alias invalidates the approved artifact.");
            }
        }
        finally
        {
            _fileSnapshotMonitor.Dispose();
            _fileSnapshotMonitor = null;
        }
    }

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
        var existingDestination = AppleArtifactCopy.CaptureRegularPathIdentity(
            destination,
            "Apple notarization artifact path");
        var name = Path.GetFileName(destination);
        var stageRoot = Path.Combine(parent, $".powerforge-stage-{Guid.NewGuid():N}");
        var stage = Path.Combine(stageRoot, name);
        var backupRoot = Path.Combine(parent, $".{name}.powerforge-backup-{Guid.NewGuid():N}");
        var backup = Path.Combine(backupRoot, name);
        var backupDeletionCandidate = Path.Combine(parent, $".{name}.powerforge-backup-deletion-{Guid.NewGuid():N}");
        var rollbackCandidate = Path.Combine(parent, $".{name}.powerforge-failed-publication-{Guid.NewGuid():N}");
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

            movedExisting = AppleArtifactCopy.MoveExistingPathToBackupIfUnchanged(
                destination,
                backup,
                existingDestination,
                "Apple notarization artifact");

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

            if (movedExisting)
                AppleArtifactCopy.RemoveBackupIfUnchanged(
                    backup,
                    backupDeletionCandidate,
                    existingDestination!,
                    "Previous Apple notarization artifact");
            return publishedSha256;
        }
        catch (Exception publicationException)
        {
            try
            {
                RollbackPublication(
                    destination,
                    backup,
                    rollbackCandidate,
                    sourceSha256,
                    published,
                    movedExisting);
            }
            catch (Exception rollbackException)
            {
                throw new AggregateException(
                    $"Apple notarization artifact publication failed and rollback could not complete. Recovery bytes are retained at '{backup}'.",
                    publicationException,
                    rollbackException);
            }
            throw;
        }
        finally
        {
            TryDeletePath(stageRoot);
        }
    }

    internal static void RollbackPublication(
        string destination,
        string backup,
        string rollbackCandidate,
        string publishedSha256,
        bool published,
        bool movedExisting)
    {
        if (published && (Directory.Exists(destination) || File.Exists(destination)))
        {
            AppleArtifactCopy.RemovePublishedPathIfUnchanged(
                destination,
                rollbackCandidate,
                publishedSha256,
                "Apple notarization artifact");
        }
        if (movedExisting)
            AppleArtifactCopy.RestorePathBackup(destination, backup);
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
            var monitor = new AppleReleaseSourceMutationMonitor(
                root,
                "private Apple notarization file snapshot",
                "submission hashing",
                "Discard the snapshot and recreate it from the approved artifact.");
            return new AppleNotarizationInputSnapshot(
                root,
                snapshotPath,
                directorySnapshot: null,
                fileSnapshotMonitor: monitor,
                fileMutationIdentity: ExistingFilePathIdentityResolver.CapturePrivateFileMutationIdentity(
                    snapshotPath,
                    "private Apple notarization file snapshot"));
        }
        catch
        {
            try { AppleArtifactCopy.DeleteOwnedDirectory(root); } catch { /* best effort private cleanup */ }
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _fileSnapshotMonitor?.Dispose();
        _fileSnapshotMonitor = null;
        if (_directorySnapshot is not null)
        {
            _directorySnapshot.Dispose();
            return;
        }
        try { AppleArtifactCopy.DeleteOwnedDirectory(RootPath); } catch { /* best effort after notarization */ }
    }

    private static void DeletePath(string path)
    {
        if (Directory.Exists(path))
            AppleArtifactCopy.DeleteOwnedDirectory(path);
        else if (File.Exists(path))
            File.Delete(path);
    }

    private static void TryDeletePath(string path)
    {
        try { DeletePath(path); }
        catch { /* retain recovery bytes rather than masking publication or rollback */ }
    }
}
