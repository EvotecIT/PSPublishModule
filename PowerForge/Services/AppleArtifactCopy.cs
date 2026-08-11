namespace PowerForge;

/// <summary>Copies Apple artifacts without following symbolic links outside their owning tree.</summary>
internal static class AppleArtifactCopy
{
    internal sealed class PathIdentity
    {
        internal PathIdentity(bool isDirectory, string sha256)
        {
            IsDirectory = isDirectory;
            Sha256 = sha256;
        }

        internal bool IsDirectory { get; }

        internal string Sha256 { get; }
    }

    internal static void CopyDirectory(string sourceRoot, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);
        var directoryMetadata = new List<(string Source, string Destination)>
        {
            (sourceRoot, destinationRoot)
        };
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
                    directoryMetadata.Add((sourcePath, destinationPath));
                    pending.Push((sourcePath, destinationPath));
                }
                else
                {
                    File.Copy(sourcePath, destinationPath, overwrite: false);
                }

                if (!isLink && !isDirectory)
                    ApplyMetadata(sourcePath, destinationPath, attributes);
            }
        }

        // Directory permissions must be restored only after every descendant has been copied.
        // Applying a source mode such as 0555 at creation time makes the destination unwritable
        // and prevents ordinary release users from materializing the remaining bundle contents.
        for (var index = directoryMetadata.Count - 1; index >= 0; index--)
        {
            var directory = directoryMetadata[index];
            ApplyMetadata(
                directory.Source,
                directory.Destination,
                File.GetAttributes(directory.Source));
        }
    }

    private static void ApplyMetadata(string sourcePath, string destinationPath, FileAttributes attributes)
    {
#if NET8_0_OR_GREATER
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(destinationPath, File.GetUnixFileMode(sourcePath));
#endif
        File.SetAttributes(destinationPath, attributes);
    }

    /// <summary>
    /// Captures the stable content identity of an existing regular artifact path.
    /// Missing paths return <see langword="null"/>; linked paths are never accepted.
    /// </summary>
    internal static PathIdentity? CaptureRegularPathIdentity(
        string path,
        string artifactDescription,
        bool? requireDirectory = null)
    {
        if (!Directory.Exists(path) && !File.Exists(path))
            return null;

        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException($"{artifactDescription} must not be a linked path: {path}");
        var isDirectory = (attributes & FileAttributes.Directory) != 0;
        if (requireDirectory.HasValue && isDirectory != requireDirectory.Value)
        {
            var expected = requireDirectory.Value ? "directory" : "file";
            throw new InvalidOperationException($"{artifactDescription} must be a regular {expected}: {path}");
        }

        return new PathIdentity(isDirectory, AppleNotarizationService.ComputeArtifactSha256(path));
    }

    /// <summary>
    /// Atomically stages the destination as a backup only when it still matches the
    /// identity observed before publication. A concurrently created or replaced path
    /// is left at the destination and causes publication to fail closed.
    /// </summary>
    internal static bool MoveExistingPathToBackupIfUnchanged(
        string destinationPath,
        string backupPath,
        PathIdentity? expectedIdentity,
        string artifactDescription)
    {
        var currentExists = Directory.Exists(destinationPath) || File.Exists(destinationPath);
        if (expectedIdentity is null)
        {
            if (currentExists)
            {
                throw new InvalidOperationException(
                    $"{artifactDescription} destination was created concurrently before publication: {destinationPath}");
            }
            return false;
        }
        if (!currentExists)
        {
            throw new InvalidOperationException(
                $"{artifactDescription} destination disappeared concurrently before publication: {destinationPath}");
        }

        var currentAttributes = File.GetAttributes(destinationPath);
        var currentIsDirectory = (currentAttributes & FileAttributes.Directory) != 0;
        if ((currentAttributes & FileAttributes.ReparsePoint) != 0 || currentIsDirectory != expectedIdentity.IsDirectory)
        {
            throw new InvalidOperationException(
                $"{artifactDescription} destination was replaced concurrently before publication: {destinationPath}");
        }

        CreatePrivateBackupParent(backupPath);
        try
        {
            if (currentIsDirectory)
                Directory.Move(destinationPath, backupPath);
            else
                File.Move(destinationPath, backupPath);
        }
        catch
        {
            TryDeleteOwnedBackupParent(backupPath);
            throw;
        }

        var backupIdentity = CaptureRegularPathIdentity(backupPath, artifactDescription);
        if (backupIdentity is null ||
            backupIdentity.IsDirectory != expectedIdentity.IsDirectory ||
            !backupIdentity.Sha256.Equals(expectedIdentity.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            RestorePathBackup(destinationPath, backupPath);
            throw new InvalidOperationException(
                $"{artifactDescription} destination changed while it was being staged for publication: {destinationPath}");
        }
        return true;
    }

    /// <summary>Deletes a retained backup only while it still matches the pre-publication identity.</summary>
    internal static void RemoveBackupIfUnchanged(
        string backupPath,
        string quarantinePath,
        PathIdentity expectedIdentity,
        string artifactDescription)
    {
        var current = CaptureRegularPathIdentity(backupPath, artifactDescription);
        if (current is null ||
            current.IsDirectory != expectedIdentity.IsDirectory ||
            !current.Sha256.Equals(expectedIdentity.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{artifactDescription} backup was replaced concurrently and has been retained: {backupPath}");
        }
        RemovePublishedPathIfUnchanged(
            backupPath,
            quarantinePath,
            expectedIdentity.Sha256,
            artifactDescription);
        TryDeleteOwnedBackupParent(backupPath);
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
        TryDeleteOwnedBackupParent(backupPath);
    }

    /// <summary>
    /// Quarantines a published directory and deletes it only when its complete artifact hash still
    /// matches the bytes owned by the current publication. Unknown, unreadable, or linked replacement
    /// bytes are restored to the destination when possible and are never recursively traversed or deleted.
    /// </summary>
    internal static void RemovePublishedDirectoryIfUnchanged(
        string destinationPath,
        string quarantinePath,
        string expectedSha256,
        string artifactDescription)
        => RemovePublishedPathIfUnchanged(destinationPath, quarantinePath, expectedSha256, artifactDescription);

    /// <summary>
    /// Quarantines a published file or directory and deletes it only when its exact artifact hash
    /// still matches the bytes owned by the current publication. Concurrent or linked replacements
    /// are restored to their observed pathname when possible and are never deleted.
    /// </summary>
    internal static void RemovePublishedPathIfUnchanged(
        string destinationPath,
        string quarantinePath,
        string expectedSha256,
        string artifactDescription)
    {
        var attributes = File.GetAttributes(destinationPath);
        var isDirectory = (attributes & FileAttributes.Directory) != 0;
        var quarantinedArtifactPath = Path.Combine(quarantinePath, Path.GetFileName(destinationPath));
        try
        {
            Directory.CreateDirectory(quarantinePath);
#if NET8_0_OR_GREATER
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(quarantinePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
#endif
            if (isDirectory)
                Directory.Move(destinationPath, quarantinedArtifactPath);
            else
                File.Move(destinationPath, quarantinedArtifactPath);
        }
        catch
        {
            if (Directory.Exists(quarantinePath) && !Directory.EnumerateFileSystemEntries(quarantinePath).Any())
                Directory.Delete(quarantinePath);
            throw;
        }
        try
        {
            var quarantinedAttributes = File.GetAttributes(quarantinedArtifactPath);
            if ((quarantinedAttributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"{artifactDescription} rollback found a linked replacement at '{destinationPath}'.");
            }

            var observedSha256 = AppleNotarizationService.ComputeArtifactSha256(quarantinedArtifactPath);
            if (!observedSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"{artifactDescription} rollback found replacement bytes at '{destinationPath}'.");
            }

            if (isDirectory)
            {
                PrepareOwnedDirectoryForDeletion(quarantinedArtifactPath);
                Directory.Delete(quarantinedArtifactPath, recursive: true);
                Directory.Delete(quarantinePath);
            }
            else
            {
                File.Delete(quarantinedArtifactPath);
                Directory.Delete(quarantinePath);
            }
        }
        catch
        {
            if (!Directory.Exists(destinationPath) && !File.Exists(destinationPath))
            {
                if (isDirectory)
                {
                    Directory.Move(quarantinedArtifactPath, destinationPath);
                    Directory.Delete(quarantinePath);
                }
                else
                {
                    File.Move(quarantinedArtifactPath, destinationPath);
                    Directory.Delete(quarantinePath);
                }
            }
            throw;
        }
    }

    /// <summary>
    /// Makes a verified private directory tree deletable without traversing symbolic links.
    /// This completes before recursive deletion starts so read-only bundle metadata cannot leave
    /// a partially deleted backup that a publication rollback could mistake for the original.
    /// </summary>
    private static void PrepareOwnedDirectoryForDeletion(string directoryPath)
    {
        var pending = new Stack<string>();
        pending.Push(directoryPath);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            var attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                continue;

            var isDirectory = (attributes & FileAttributes.Directory) != 0;
#if NET8_0_OR_GREATER
            if (isDirectory && !OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(current);
                File.SetUnixFileMode(
                    current,
                    mode | UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            else
#endif
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(current, attributes & ~FileAttributes.ReadOnly);
            }

            if (!isDirectory)
                continue;
            foreach (var child in Directory.EnumerateFileSystemEntries(current))
                pending.Push(child);
        }
    }

    /// <summary>Restores a retained file or directory backup only when the destination is vacant.</summary>
    internal static void RestorePathBackup(string destinationPath, string backupPath)
    {
        if (!Directory.Exists(backupPath) && !File.Exists(backupPath))
            return;
        if (Directory.Exists(destinationPath) || File.Exists(destinationPath))
        {
            throw new InvalidOperationException(
                $"Apple artifact rollback could not restore '{destinationPath}' because the destination was recreated. " +
                $"The previous artifact is retained at '{backupPath}'.");
        }
        if (Directory.Exists(backupPath))
            Directory.Move(backupPath, destinationPath);
        else
            File.Move(backupPath, destinationPath);
        TryDeleteOwnedBackupParent(backupPath);
    }

    private static void CreatePrivateBackupParent(string backupPath)
    {
        var parent = Path.GetDirectoryName(backupPath)
            ?? throw new InvalidOperationException($"Apple artifact backup path has no parent: {backupPath}");
        Directory.CreateDirectory(parent);
#if NET8_0_OR_GREATER
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(parent, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
#endif
    }

    private static void TryDeleteOwnedBackupParent(string backupPath)
    {
        var parent = Path.GetDirectoryName(backupPath);
        if (string.IsNullOrWhiteSpace(parent) ||
            !Path.GetFileName(parent).Contains(".powerforge-backup-", StringComparison.Ordinal) ||
            !Directory.Exists(parent) ||
            Directory.EnumerateFileSystemEntries(parent).Any())
        {
            return;
        }
        Directory.Delete(parent);
    }
}
