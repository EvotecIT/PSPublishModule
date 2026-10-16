namespace PowerForge;

internal sealed partial class AppleReleaseSourceSnapshot
{
    /// <summary>
    /// Prepares the package and tracked-workspace containers Xcode normally creates before the
    /// source observer starts. Files and later mutations remain protected by the observer.
    /// </summary>
    private void PreparePackageWorkingDirectories()
    {
        var trackedPaths = Run(
                _git,
                RootPath,
                new[] { "ls-files", "-z" },
                "enumerate local Swift package roots")
            .StdOut.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var relativePath in trackedPaths.Where(path =>
                     Path.GetFileName(path).Equals("Package.swift", StringComparison.Ordinal)))
        {
            var packageManifest = Path.GetFullPath(Path.Combine(
                RootPath,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            EnsureContained(RootPath, packageManifest, "Apple snapshot Swift package manifest");
            var metadataRoot = Path.Combine(Path.GetDirectoryName(packageManifest)!, ".swiftpm");
            PrepareSwiftPackageMetadata(metadataRoot, "configuration", "xcode");
        }

        foreach (var relativePath in trackedPaths.Where(path =>
                     Path.GetFileName(path).Equals("contents.xcworkspacedata", StringComparison.Ordinal) &&
                     string.Equals(Path.GetExtension(Path.GetDirectoryName(path)), ".xcworkspace", StringComparison.OrdinalIgnoreCase)))
        {
            var workspace = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(
                RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar))))!;
            EnsureRealDirectory(workspace, "Apple snapshot Xcode workspace");
            foreach (var directory in AppleXcodeWorkspaceWorkingDirectories.GetMetadataDirectories(workspace, isWorkspace: true))
                EnsureRealDirectory(directory, "Apple snapshot Xcode workspace metadata directory", createIfMissing: true);
            PrepareSwiftPackageMetadata(Path.Combine(workspace, "xcshareddata", "swiftpm"), "configuration");
        }
    }

    private void PrepareSwiftPackageMetadata(string metadataRoot, params string[] directoryNames)
    {
        EnsureRealDirectory(metadataRoot, "Apple snapshot Swift package metadata root", createIfMissing: true);
        foreach (var name in directoryNames)
            EnsureRealDirectory(Path.Combine(metadataRoot, name), $"Apple snapshot Swift package {name} directory", createIfMissing: true);
        ValidateSwiftPackageMetadataTree(metadataRoot, directoryNames);
        _preparedSwiftPackageMetadata[metadataRoot] = new PreparedSwiftPackageMetadata(
            directoryNames,
            AppleArchiveUploadSnapshot.CaptureCompleteIdentity(metadataRoot, "prepared Apple snapshot Swift package metadata"));
    }

    private void ValidatePreparedSwiftPackageWorkingDirectories()
    {
        foreach (var pair in _preparedSwiftPackageMetadata)
        {
            var metadataRoot = pair.Key;
            EnsureRealDirectory(metadataRoot, "Apple snapshot Swift package metadata root");
            foreach (var name in pair.Value.DirectoryNames)
            {
                EnsureRealDirectory(
                    Path.Combine(metadataRoot, name),
                    $"Apple snapshot Swift package {name} directory");
            }
            ValidateSwiftPackageMetadataTree(metadataRoot, pair.Value.DirectoryNames);
            var current = AppleArchiveUploadSnapshot.CaptureCompleteIdentity(
                metadataRoot,
                "prepared Apple snapshot Swift package metadata");
            if (!pair.Value.Identity.Equals(current))
            {
                throw new InvalidOperationException(
                    $"Apple snapshot Swift package metadata changed before the Apple build started: {metadataRoot}");
            }
        }
    }

    private void ValidateSwiftPackageMetadataTree(string metadataRoot, IReadOnlyList<string> directoryNames)
    {
        var relativeRoot = FrameworkCompatibility.GetRelativePath(RootPath, metadataRoot).Replace('\\', '/');
        var trackedFiles = Run(
                _git,
                RootPath,
                new[] { "ls-files", "-z", "--", relativeRoot + "/" },
                "enumerate tracked Swift package metadata")
            .StdOut.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => Path.GetFullPath(Path.Combine(RootPath, path.Replace('/', Path.DirectorySeparatorChar))))
            .ToHashSet(GetPathComparer());
        var allowedDirectories = new HashSet<string>(GetPathComparer())
        {
            metadataRoot
        };
        foreach (var name in directoryNames)
            allowedDirectories.Add(Path.Combine(metadataRoot, name));
        foreach (var trackedFile in trackedFiles)
        {
            EnsureContained(metadataRoot, trackedFile, "tracked Apple snapshot Swift package metadata");
            for (var directory = Path.GetDirectoryName(trackedFile);
                 directory is not null && !directory.Equals(metadataRoot, GetPathComparison());
                 directory = Path.GetDirectoryName(directory))
            {
                allowedDirectories.Add(directory);
            }
        }
        var pending = new Stack<string>();
        pending.Push(metadataRoot);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        $"Apple snapshot Swift package metadata must not contain a symbolic link or reparse point: {entry}");
                }
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (!allowedDirectories.Contains(Path.GetFullPath(entry)))
                    {
                        throw new InvalidOperationException(
                            $"Apple snapshot Swift package metadata contains an untracked directory: {entry}");
                    }
                    pending.Push(entry);
                    continue;
                }
                var fullPath = Path.GetFullPath(entry);
                if (!trackedFiles.Contains(fullPath))
                {
                    throw new InvalidOperationException(
                        $"Apple snapshot Swift package metadata contains untracked state: {entry}");
                }
            }
        }
    }

    private void EnsureRealDirectory(string directory, string name, bool createIfMissing = false)
    {
        EnsureContained(RootPath, directory, name);
        for (var current = Path.GetFullPath(directory); current is not null; current = Path.GetDirectoryName(current))
        {
            if ((Directory.Exists(current) || File.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException($"{name} must not traverse a symbolic link or reparse point: {current}");
            if (current.Equals(RootPath, GetPathComparison())) break;
        }
        if (!Directory.Exists(directory))
        {
            if (!createIfMissing || File.Exists(directory))
                throw new InvalidOperationException($"{name} must be an ordinary directory: {directory}");
            Directory.CreateDirectory(directory);
            return;
        }
    }

    private sealed class PreparedSwiftPackageMetadata
    {
        internal PreparedSwiftPackageMetadata(string[] directoryNames, AppleArchiveUploadSnapshot.SnapshotIdentity identity)
        {
            DirectoryNames = directoryNames;
            Identity = identity;
        }

        internal string[] DirectoryNames { get; }
        internal AppleArchiveUploadSnapshot.SnapshotIdentity Identity { get; }
    }
}
