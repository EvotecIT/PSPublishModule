namespace PowerForge;

/// <summary>Owns a private Developer ID export and publishes its verified bytes atomically.</summary>
internal sealed class AppleDirectExportSnapshot : IDisposable
{
    private bool _disposed;
    private string? _approvedArtifactPath;
    private string? _approvedArtifactSha256;

    private AppleDirectExportSnapshot(string rootPath, string exportPath)
    {
        RootPath = rootPath;
        ExportPath = exportPath;
    }

    internal string RootPath { get; }

    internal string ExportPath { get; }

    internal static AppleDirectExportSnapshot Create()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge", "apple-direct-exports", Guid.NewGuid().ToString("N"));
        var exportPath = Path.Combine(root, "export");
        Directory.CreateDirectory(exportPath);
#if NET8_0_OR_GREATER
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
#endif
        return new AppleDirectExportSnapshot(root, exportPath);
    }

    internal void BindProducedArtifact(string? producerArtifactPath, string? producerArtifactSha256)
    {
        var artifactPath = PowerForgeReleaseService.ResolveDirectAppleArtifactPath(ExportPath);
        EnsureArtifactWithinExportRoot(artifactPath);
        var artifactSha256 = AppleNotarizationService.ComputeArtifactSha256(artifactPath);
        if (!string.IsNullOrWhiteSpace(producerArtifactPath) &&
            !Path.GetFullPath(producerArtifactPath).Equals(
                artifactPath,
                Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"xcodebuild reported Developer ID artifact '{producerArtifactPath}', but the private export contains '{artifactPath}'.");
        }
        if (!string.IsNullOrWhiteSpace(producerArtifactSha256) &&
            !string.Equals(producerArtifactSha256, artifactSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The private Developer ID export changed after xcodebuild completed. Expected '{producerArtifactSha256}', received '{artifactSha256}'.");
        }

        _approvedArtifactPath = artifactPath;
        _approvedArtifactSha256 = artifactSha256;
    }

    internal ApplePublishedDirectExport Publish(string destinationExportPath)
    {
        if (string.IsNullOrWhiteSpace(_approvedArtifactPath) || string.IsNullOrWhiteSpace(_approvedArtifactSha256))
            throw new InvalidOperationException("The Developer ID export must be bound immediately after xcodebuild completes before it can be published.");
        var sourceArtifact = PowerForgeReleaseService.ResolveDirectAppleArtifactPath(ExportPath);
        EnsureArtifactWithinExportRoot(sourceArtifact);
        var sourceArtifactSha256 = AppleNotarizationService.ComputeArtifactSha256(sourceArtifact);
        if (!sourceArtifact.Equals(
                _approvedArtifactPath,
                Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
            !sourceArtifactSha256.Equals(_approvedArtifactSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The private Developer ID export changed after xcodebuild completed. Expected '{_approvedArtifactSha256}' at '{_approvedArtifactPath}', " +
                $"received '{sourceArtifactSha256}' at '{sourceArtifact}'.");
        }
        var relativeArtifactPath = FrameworkCompatibility.GetRelativePath(ExportPath, sourceArtifact);

        var destination = Path.GetFullPath(destinationExportPath);
        var parent = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException($"Developer ID export path has no parent: {destination}");
        Directory.CreateDirectory(parent);
        if (File.Exists(destination) ||
            (Directory.Exists(destination) && (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0))
        {
            throw new InvalidOperationException($"Developer ID export path must be a regular directory: {destination}");
        }

        var name = Path.GetFileName(destination);
        var stage = Path.Combine(parent, $".{name}.powerforge-stage-{Guid.NewGuid():N}");
        var backup = Path.Combine(parent, $".{name}.powerforge-backup-{Guid.NewGuid():N}");
        var movedExisting = false;
        var published = false;
        try
        {
            AppleArtifactCopy.CopyDirectory(ExportPath, stage);
            var stagedArtifact = Path.Combine(stage, relativeArtifactPath);
            var stagedSha256 = AppleNotarizationService.ComputeArtifactSha256(stagedArtifact);
            if (!stagedSha256.Equals(sourceArtifactSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The staged Developer ID export changed during publication. Expected '{sourceArtifactSha256}', received '{stagedSha256}'.");
            }

            if (Directory.Exists(destination))
            {
                Directory.Move(destination, backup);
                movedExisting = true;
            }
            Directory.Move(stage, destination);
            published = true;

            var publishedArtifact = Path.Combine(destination, relativeArtifactPath);
            var publishedSha256 = AppleNotarizationService.ComputeArtifactSha256(publishedArtifact);
            if (!publishedSha256.Equals(sourceArtifactSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The published Developer ID export changed before notarization. Expected '{sourceArtifactSha256}', received '{publishedSha256}'.");
            }
            if (movedExisting && Directory.Exists(backup))
                Directory.Delete(backup, recursive: true);
            return new ApplePublishedDirectExport(destination, publishedArtifact, publishedSha256);
        }
        catch (Exception publicationException)
        {
            try
            {
                if (published && Directory.Exists(destination))
                    Directory.Delete(destination, recursive: true);
                if (movedExisting)
                    AppleArtifactCopy.RestoreDirectoryBackup(destination, backup);
            }
            catch (Exception rollbackException)
            {
                throw new AggregateException(
                    $"Developer ID export publication failed and rollback could not complete. Recovery bytes are retained at '{backup}'.",
                    publicationException,
                    rollbackException);
            }
            throw;
        }
        finally
        {
            if (Directory.Exists(stage))
                Directory.Delete(stage, recursive: true);
        }
    }

    private void EnsureArtifactWithinExportRoot(string artifactPath)
    {
        var relativeArtifactPath = FrameworkCompatibility.GetRelativePath(ExportPath, artifactPath);
        if (Path.IsPathRooted(relativeArtifactPath) ||
            relativeArtifactPath.Equals("..", StringComparison.Ordinal) ||
            relativeArtifactPath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Direct Apple export artifact escaped its private export root: {artifactPath}");
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

internal sealed class ApplePublishedDirectExport
{
    internal ApplePublishedDirectExport(string exportPath, string artifactPath, string artifactSha256)
    {
        ExportPath = exportPath;
        ArtifactPath = artifactPath;
        ArtifactSha256 = artifactSha256;
    }

    internal string ExportPath { get; }

    internal string ArtifactPath { get; }

    internal string ArtifactSha256 { get; }
}
