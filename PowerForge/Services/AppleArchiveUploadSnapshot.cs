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
        try
        {
            CopyTree(source, snapshotPath);
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

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (Directory.Exists(RootPath))
            Directory.Delete(RootPath, recursive: true);
    }

    private static void CopyTree(string sourceRoot, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);
        var pending = new Stack<(string Source, string Destination)>();
        pending.Push((sourceRoot, destinationRoot));
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var sourcePath in Directory.EnumerateFileSystemEntries(current.Source))
            {
                var destinationPath = Path.Combine(current.Destination, Path.GetFileName(sourcePath));
                var attributes = File.GetAttributes(sourcePath);
                var isDirectory = (attributes & FileAttributes.Directory) != 0;
                var isLink = (attributes & FileAttributes.ReparsePoint) != 0;
                if (isLink)
                {
#if NET8_0_OR_GREATER
                    var linkTarget = isDirectory
                        ? new DirectoryInfo(sourcePath).LinkTarget
                        : new FileInfo(sourcePath).LinkTarget;
                    if (string.IsNullOrWhiteSpace(linkTarget))
                        throw new InvalidOperationException($"Unable to preserve Apple archive symbolic link: {sourcePath}");
                    if (Path.IsPathRooted(linkTarget))
                        throw new InvalidOperationException($"Apple archive symbolic links must remain inside the archive: {sourcePath}");
                    var resolvedTarget = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, linkTarget));
                    var relativeTarget = FrameworkCompatibility.GetRelativePath(sourceRoot, resolvedTarget);
                    if (Path.IsPathRooted(relativeTarget) ||
                        relativeTarget.Equals("..", StringComparison.Ordinal) ||
                        relativeTarget.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException($"Apple archive symbolic links must remain inside the archive: {sourcePath}");
                    }
                    if (isDirectory)
                        Directory.CreateSymbolicLink(destinationPath, linkTarget!);
                    else
                        File.CreateSymbolicLink(destinationPath, linkTarget!);
#else
                    throw new PlatformNotSupportedException("Apple archive symbolic-link snapshots require .NET 8 or newer.");
#endif
                }
                else if (isDirectory)
                {
                    Directory.CreateDirectory(destinationPath);
                    pending.Push((sourcePath, destinationPath));
                }
                else
                {
                    File.Copy(sourcePath, destinationPath, overwrite: false);
                }

                if (!isLink)
                {
#if NET8_0_OR_GREATER
                    if (!OperatingSystem.IsWindows())
                        File.SetUnixFileMode(destinationPath, File.GetUnixFileMode(sourcePath));
#endif
                    File.SetAttributes(destinationPath, attributes);
                }
            }
        }

#if NET8_0_OR_GREATER
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(destinationRoot, File.GetUnixFileMode(sourceRoot));
#endif
        File.SetAttributes(destinationRoot, File.GetAttributes(sourceRoot));
    }
}
