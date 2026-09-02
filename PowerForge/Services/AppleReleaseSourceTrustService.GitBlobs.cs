namespace PowerForge;

internal sealed partial class AppleReleaseSourceTrustService
{
    private sealed class TrackedRepositoryProof
    {
        internal TrackedRepositoryProof(IEqualityComparer<string> comparer)
        {
            IndexEntries = new Dictionary<string, TrackedIndexEntry>(comparer);
            HeadBlobIds = new Dictionary<string, string>(comparer);
            LoadedFilePaths = new HashSet<string>(comparer);
            LoadedDirectoryPaths = new HashSet<string>(comparer);
            FileProbeCountsByDirectory = new Dictionary<string, int>(comparer);
        }

        internal Dictionary<string, TrackedIndexEntry> IndexEntries { get; }

        internal Dictionary<string, string> HeadBlobIds { get; }

        internal HashSet<string> LoadedFilePaths { get; }

        internal HashSet<string> LoadedDirectoryPaths { get; }

        internal Dictionary<string, int> FileProbeCountsByDirectory { get; }
    }

    private sealed class TrackedIndexEntry
    {
        internal TrackedIndexEntry(string fullPath, string relativePath, string tag)
        {
            FullPath = fullPath;
            RelativePath = relativePath;
            Tag = tag;
        }

        internal string FullPath { get; }

        internal string RelativePath { get; }

        internal string Tag { get; }
    }

    private void EnsureNoCustomGitFilter(string repositoryRoot, string relativePath, string name)
        => EnsureNoCustomGitFilters(repositoryRoot, new[] { relativePath }, name);

    internal void EnsureNoCustomGitFilters(
        string repositoryRoot,
        IReadOnlyCollection<string> relativePaths,
        string name)
    {
        const int maximumPathsPerInvocation = 256;
        var paths = relativePaths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(GetPathComparer())
            .ToArray();
        for (var offset = 0; offset < paths.Length; offset += maximumPathsPerInvocation)
        {
            var batch = paths.Skip(offset).Take(maximumPathsPerInvocation).ToArray();
            var arguments = new List<string> { "check-attr", "-z", "filter", "--" };
            arguments.AddRange(batch);
            var attributes = RunGit(repositoryRoot, arguments.ToArray())
                .StdOut.Split(new[] { '\0' }, StringSplitOptions.None);
            var valueCount = attributes.Length > 0 && attributes[attributes.Length - 1].Length == 0
                ? attributes.Length - 1
                : attributes.Length;
            if (valueCount != batch.Length * 3)
            {
                throw new InvalidOperationException(
                    $"{name} Git filter attributes could not be parsed safely for {batch.Length} exact-source input(s).");
            }

            for (var index = 0; index < valueCount; index += 3)
            {
                var path = attributes[index];
                var attribute = attributes[index + 1];
                var value = attributes[index + 2];
                if (!attribute.Equals("filter", StringComparison.Ordinal) ||
                    !batch.Contains(path, GetPathComparer()))
                {
                    throw new InvalidOperationException(
                        $"{name} Git filter attributes returned an unexpected exact-source path: {path}.");
                }
                if (!value.Equals("unspecified", StringComparison.Ordinal) &&
                    !value.Equals("unset", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"{name} uses custom Git filter '{value}' and cannot be attested to the exact source commit: {path}. " +
                        "Exact Apple source inputs may use Git text/EOL normalization but not repository-configuration-dependent clean or smudge filters.");
                }
            }
        }
    }

    private void TrackGitFilterPath(string repositoryRoot, string relativePath)
        => TrackGitFilterPaths(repositoryRoot, new[] { relativePath });

    private void TrackGitFilterPaths(
        string repositoryRoot,
        IEnumerable<string> relativePaths)
    {
        var root = Path.GetFullPath(repositoryRoot);
        if (!_pendingGitFilterPaths.TryGetValue(root, out var pending))
        {
            pending = new HashSet<string>(GetPathComparer());
            _pendingGitFilterPaths.Add(root, pending);
        }
        pending.UnionWith(relativePaths.Where(static path => !string.IsNullOrWhiteSpace(path)));
    }

    internal void ValidatePendingGitFilters()
    {
        foreach (var repositoryRoot in _pendingGitFilterPaths.Keys.ToArray())
            ValidatePendingGitFilters(repositoryRoot);
    }

    private void ValidatePendingGitFilters(string repositoryRoot)
    {
        var root = Path.GetFullPath(repositoryRoot);
        if (!_pendingGitFilterPaths.TryGetValue(root, out var pending))
            return;
        EnsureNoCustomGitFilters(root, pending.ToArray(), "Apple exact-source input");
        _pendingGitFilterPaths.Remove(root);
    }

    private TrackedRepositoryProof ReadTrackedRepositoryProof(
        string repositoryRoot,
        string requestedPath,
        bool recursive)
    {
        const int directoryExpansionThreshold = 4;
        var root = Path.GetFullPath(repositoryRoot);
        var comparer = GetPathComparer();
        if (!_trackedRepositoryProofs.TryGetValue(root, out var proof))
        {
            proof = new TrackedRepositoryProof(comparer);
            _trackedRepositoryProofs.Add(root, proof);
        }

        var requested = Path.GetFullPath(requestedPath);
        if (proof.LoadedDirectoryPaths.Any(directory => IsPathAtOrWithin(requested, directory)) ||
            (!recursive && proof.LoadedFilePaths.Contains(requested)))
        {
            return proof;
        }

        var scope = requested;
        var loadDirectory = recursive;
        if (!recursive)
        {
            var directory = Path.GetDirectoryName(requested) ?? root;
            proof.FileProbeCountsByDirectory.TryGetValue(directory, out var probes);
            probes++;
            proof.FileProbeCountsByDirectory[directory] = probes;
            // Keep small graphs exact-path scoped, then amortize repeated sibling
            // lookups without ever expanding root-level files to the whole repository.
            if (!comparer.Equals(directory, root) && probes >= directoryExpansionThreshold)
            {
                scope = directory;
                loadDirectory = true;
            }
        }

        var relativeScope = FrameworkCompatibility.GetRelativePath(root, scope).Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(relativeScope))
            relativeScope = ".";
        var stagedEntries = RunGit(root, "--literal-pathspecs", "ls-files", "--stage", "-v", "-z", "--", relativeScope)
            .StdOut.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
        var seenIndexEntries = new HashSet<string>(comparer);
        foreach (var stagedEntry in stagedEntries)
        {
            var tab = stagedEntry.IndexOf('\t');
            var metadata = tab > 2
                ? stagedEntry.Substring(2, tab - 2).Split(
                    new[] { ' ' },
                    StringSplitOptions.RemoveEmptyEntries)
                : Array.Empty<string>();
            if (stagedEntry.Length < 3 || stagedEntry[1] != ' ' ||
                metadata.Length != 3 || !metadata[2].Equals("0", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Tracked Apple source inputs contain an unexpected or unmerged Git index entry.");
            }

            var relativePath = stagedEntry.Substring(tab + 1);
            var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!seenIndexEntries.Add(fullPath))
            {
                throw new InvalidOperationException(
                    $"Tracked Apple source inputs contain duplicate Git index entries for: {relativePath}");
            }
            proof.IndexEntries[fullPath] =
                new TrackedIndexEntry(fullPath, relativePath, stagedEntry.Substring(0, 1));
        }

        var headEntries = RunGit(root, "--literal-pathspecs", "ls-tree", "-r", "-z", "HEAD", "--", relativeScope)
            .StdOut.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var headEntry in headEntries)
        {
            var tab = headEntry.IndexOf('\t');
            if (tab < 0)
                continue;
            var metadata = headEntry.Substring(0, tab).Split(' ');
            if (metadata.Length != 3 || !metadata[1].Equals("blob", StringComparison.Ordinal))
                continue;
            var fullPath = Path.GetFullPath(Path.Combine(root, headEntry.Substring(tab + 1)));
            proof.HeadBlobIds[fullPath] = metadata[2];
        }

        if (loadDirectory)
            proof.LoadedDirectoryPaths.Add(scope);
        else
            proof.LoadedFilePaths.Add(requested);
        return proof;
    }

    private string ComputeRawGitBlobId(string repositoryRoot, string filePath)
    {
        var objectFormat = ReadGitObjectFormat(repositoryRoot);
        using System.Security.Cryptography.HashAlgorithm hash = objectFormat.Equals("sha256", StringComparison.OrdinalIgnoreCase)
            ? System.Security.Cryptography.SHA256.Create()
            : objectFormat.Equals("sha1", StringComparison.OrdinalIgnoreCase)
                ? System.Security.Cryptography.SHA1.Create()
                : throw new InvalidOperationException($"Unsupported Git object format '{objectFormat}'.");
        var length = new FileInfo(filePath).Length;
        var prefix = System.Text.Encoding.ASCII.GetBytes($"blob {length}\0");
        hash.TransformBlock(prefix, 0, prefix.Length, prefix, 0);
        using var stream = File.OpenRead(filePath);
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            hash.TransformBlock(buffer, 0, read, buffer, 0);
        hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return BitConverter.ToString(hash.Hash!).Replace("-", string.Empty).ToLowerInvariant();
    }

    private string ComputeRawGitBlobId(string repositoryRoot, byte[] content)
    {
        var objectFormat = ReadGitObjectFormat(repositoryRoot);
        using System.Security.Cryptography.HashAlgorithm hash = objectFormat.Equals("sha256", StringComparison.OrdinalIgnoreCase)
            ? System.Security.Cryptography.SHA256.Create()
            : objectFormat.Equals("sha1", StringComparison.OrdinalIgnoreCase)
                ? System.Security.Cryptography.SHA1.Create()
                : throw new InvalidOperationException($"Unsupported Git object format '{objectFormat}'.");
        var prefix = System.Text.Encoding.ASCII.GetBytes($"blob {content.LongLength}\0");
        hash.TransformBlock(prefix, 0, prefix.Length, prefix, 0);
        hash.TransformFinalBlock(content, 0, content.Length);
        return BitConverter.ToString(hash.Hash!).Replace("-", string.Empty).ToLowerInvariant();
    }

    private string ComputePathAwareGitBlobId(string repositoryRoot, string filePath, string relativePath)
        => RunGit(repositoryRoot, "hash-object", $"--path={relativePath}", "--", filePath).StdOut.Trim();

    private string ComputePathAwareGitBlobId(string repositoryRoot, byte[] content, string relativePath)
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), ".powerforge-git-filter-" + Guid.NewGuid().ToString("N"));
        var temporaryPath = Path.Combine(temporaryRoot, "captured-input");
        Directory.CreateDirectory(temporaryRoot);
#if NET8_0_OR_GREATER
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(temporaryRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
#endif
        try
        {
            File.WriteAllBytes(temporaryPath, content);
#if NET8_0_OR_GREATER
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temporaryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
#endif
            return RunGit(repositoryRoot, "hash-object", $"--path={relativePath}", "--", temporaryPath).StdOut.Trim();
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
            if (Directory.Exists(temporaryRoot))
                Directory.Delete(temporaryRoot);
        }
    }
}
