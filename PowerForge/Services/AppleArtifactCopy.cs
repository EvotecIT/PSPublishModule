namespace PowerForge;

/// <summary>Copies Apple artifacts without following symbolic links outside their owning tree.</summary>
internal static class AppleArtifactCopy
{
    internal static void CopyDirectory(string sourceRoot, string destinationRoot)
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
                        throw new InvalidOperationException($"Unable to preserve Apple artifact symbolic link: {sourcePath}");
                    if (Path.IsPathRooted(linkTarget))
                        throw new InvalidOperationException($"Apple artifact symbolic links must remain inside the archive or artifact: {sourcePath}");
                    var resolvedTarget = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, linkTarget));
                    var relativeTarget = FrameworkCompatibility.GetRelativePath(sourceRoot, resolvedTarget);
                    if (Path.IsPathRooted(relativeTarget) ||
                        relativeTarget.Equals("..", StringComparison.Ordinal) ||
                        relativeTarget.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException($"Apple artifact symbolic links must remain inside the archive or artifact: {sourcePath}");
                    }
                    if (isDirectory)
                        Directory.CreateSymbolicLink(destinationPath, linkTarget!);
                    else
                        File.CreateSymbolicLink(destinationPath, linkTarget!);
#else
                    throw new PlatformNotSupportedException("Apple artifact symbolic-link copies require .NET 8 or newer.");
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

    /// <summary>
    /// Restores a retained directory backup only when the destination is still vacant.
    /// A concurrently recreated destination wins and the backup remains available for recovery.
    /// </summary>
    internal static void RestoreDirectoryBackup(string destinationPath, string backupPath)
    {
        if (!Directory.Exists(backupPath))
            return;
        if (Directory.Exists(destinationPath) || File.Exists(destinationPath))
        {
            throw new InvalidOperationException(
                $"Apple artifact rollback could not restore '{destinationPath}' because the destination was recreated. " +
                $"The previous artifact is retained at '{backupPath}'.");
        }
        Directory.Move(backupPath, destinationPath);
    }
}
