namespace PowerForge;

/// <summary>Owns a private Developer ID export and publishes its verified bytes atomically.</summary>
internal sealed class AppleDirectExportSnapshot : IDisposable
{
    private bool _disposed;

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

    internal ApplePublishedDirectExport Publish(string destinationExportPath)
    {
        var sourceArtifact = PowerForgeReleaseService.ResolveDirectAppleArtifactPath(ExportPath);
        var sourceArtifactSha256 = AppleNotarizationService.ComputeArtifactSha256(sourceArtifact);
        var relativeArtifactPath = FrameworkCompatibility.GetRelativePath(ExportPath, sourceArtifact);
        if (Path.IsPathRooted(relativeArtifactPath) ||
            relativeArtifactPath.Equals("..", StringComparison.Ordinal) ||
            relativeArtifactPath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Direct Apple export artifact escaped its private export root: {sourceArtifact}");
        }

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
        catch
        {
            if (published && Directory.Exists(destination))
                Directory.Delete(destination, recursive: true);
            if (movedExisting && Directory.Exists(backup))
                Directory.Move(backup, destination);
            throw;
        }
        finally
        {
            if (Directory.Exists(stage))
                Directory.Delete(stage, recursive: true);
            if (Directory.Exists(backup))
                Directory.Delete(backup, recursive: true);
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
