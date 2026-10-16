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
        var metadataRoots = new Dictionary<string, string[]>(GetPathComparer());
        foreach (var relativePath in trackedPaths.Where(path =>
                     Path.GetFileName(path).Equals("Package.swift", StringComparison.Ordinal)))
        {
            var packageManifest = Path.GetFullPath(Path.Combine(
                RootPath,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            EnsureContained(RootPath, packageManifest, "Apple snapshot Swift package manifest");
            var metadataRoot = Path.Combine(Path.GetDirectoryName(packageManifest)!, ".swiftpm");
            metadataRoots[metadataRoot] = new[] { "configuration", "xcode" };
        }

        var workspaces = new HashSet<string>(GetPathComparer());
        foreach (var relativePath in trackedPaths.Where(path =>
                     Path.GetFileName(path).Equals("contents.xcworkspacedata", StringComparison.Ordinal) &&
                     string.Equals(Path.GetExtension(Path.GetDirectoryName(path)), ".xcworkspace", StringComparison.OrdinalIgnoreCase)))
        {
            workspaces.Add(Path.GetDirectoryName(Path.GetFullPath(Path.Combine(
                RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar))))!);
        }
        foreach (var relativePath in trackedPaths.Where(path =>
                     Path.GetFileName(path).Equals("project.pbxproj", StringComparison.Ordinal) &&
                     string.Equals(Path.GetExtension(Path.GetDirectoryName(path)), ".xcodeproj", StringComparison.OrdinalIgnoreCase)))
        {
            var project = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(
                RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar))))!;
            workspaces.Add(Path.Combine(project, "project.xcworkspace"));
        }
        foreach (var workspace in workspaces)
        {
            EnsureRealDirectory(workspace, "Apple snapshot Xcode workspace", createIfMissing: true);
            metadataRoots[AppleXcodeWorkspaceWorkingDirectories.GetPackageMetadataRoot(workspace, isWorkspace: true)] =
                new[] { "configuration" };
        }

        // A tracked workspace can live inside another package's .swiftpm tree.
        // Finish every ordinary directory before capturing any overlapping tree.
        var preparedDirectories = metadataRoots.SelectMany(pair =>
                new[] { pair.Key }.Concat(pair.Value.Select(name => Path.Combine(pair.Key, name))))
            .Distinct(GetPathComparer())
            .ToArray();
        foreach (var directory in preparedDirectories)
            EnsureRealDirectory(directory, "Apple snapshot package working directory", createIfMissing: true);
        foreach (var metadataRoot in metadataRoots.Keys)
        {
            var prefix = metadataRoot + Path.DirectorySeparatorChar;
            var descendants = preparedDirectories.Where(directory =>
                    directory.Equals(metadataRoot, GetPathComparison()) ||
                    directory.StartsWith(prefix, GetPathComparison()))
                .ToArray();
            PrepareSwiftPackageMetadata(metadataRoot, descendants);
        }
    }

    private void PrepareSwiftPackageMetadata(string metadataRoot, string[] preparedDirectories)
    {
        EnsureRealDirectory(metadataRoot, "Apple snapshot Swift package metadata root");
        ValidateSwiftPackageMetadataTree(metadataRoot, preparedDirectories);
        _preparedSwiftPackageMetadata[metadataRoot] = new PreparedSwiftPackageMetadata(
            preparedDirectories,
            AppleArchiveUploadSnapshot.CaptureCompleteIdentity(metadataRoot, "prepared Apple snapshot Swift package metadata"));
    }

    private void ValidatePreparedSwiftPackageWorkingDirectories()
    {
        foreach (var pair in _preparedSwiftPackageMetadata)
        {
            var metadataRoot = pair.Key;
            EnsureRealDirectory(metadataRoot, "Apple snapshot Swift package metadata root");
            foreach (var directory in pair.Value.Directories)
            {
                EnsureRealDirectory(
                    directory,
                    "Apple snapshot package working directory");
            }
            ValidateSwiftPackageMetadataTree(metadataRoot, pair.Value.Directories);
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

    private void ValidateSwiftPackageMetadataTree(string metadataRoot, IReadOnlyList<string> preparedDirectories)
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
        foreach (var preparedDirectory in preparedDirectories)
        {
            EnsureContained(metadataRoot, preparedDirectory, "prepared Apple snapshot package directory");
            allowedDirectories.Add(preparedDirectory);
        }
        foreach (var trackedFile in trackedFiles.Concat(preparedDirectories).Where(path =>
                     !path.Equals(metadataRoot, GetPathComparison())))
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
        internal PreparedSwiftPackageMetadata(string[] directories, AppleArchiveUploadSnapshot.SnapshotIdentity identity)
        {
            Directories = directories;
            Identity = identity;
        }

        internal string[] Directories { get; }
        internal AppleArchiveUploadSnapshot.SnapshotIdentity Identity { get; }
    }
}
