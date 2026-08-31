namespace PowerForge;

/// <summary>
/// Resolves and binds source provenance to Apple build settings without
/// requiring product repositories to run their own Git scripts.
/// </summary>
internal static class AppleBuildProvenance
{
    internal const string XcodeBuildSetting = "POWERFORGE_SOURCE_REVISION";

    internal sealed class Snapshot
    {
        internal Snapshot(
            string rootPath,
            string revision,
            IReadOnlyDictionary<string, string> trackedFileMutationIdentities)
        {
            RootPath = rootPath;
            Revision = revision;
            TrackedFileMutationIdentities = trackedFileMutationIdentities;
        }

        internal string RootPath { get; }

        internal string Revision { get; }

        internal IReadOnlyDictionary<string, string>
            TrackedFileMutationIdentities { get; }
    }

    internal static string? ResolveLocalSourceRevision(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
            return null;

        var git = GitClient.CreateTrustedSystemClient(
            defaultTimeout: TimeSpan.FromSeconds(10));
        return ResolveLocalSourceRevision(projectRoot, git);
    }

    internal static string? ResolveLocalSourceRevision(
        string projectRoot,
        GitClient git)
    {
        var replacementRefs = git.RunRawAsync(
                projectRoot,
                ["for-each-ref", "--format=%(refname)", "refs/replace"])
            .GetAwaiter()
            .GetResult();
        if (!replacementRefs.Succeeded ||
            !string.IsNullOrWhiteSpace(replacementRefs.StdOut))
        {
            return null;
        }

        var head = git.RunRawAsync(projectRoot, ["rev-parse", "HEAD"])
            .GetAwaiter()
            .GetResult();
        var revision = head.StdOut.Trim().ToLowerInvariant();
        if (!head.Succeeded || !GitObjectId.IsFull(revision))
            return null;

        var status = git.RunRawAsync(
                projectRoot,
                [
                    "status",
                    "--porcelain=v1",
                    "--untracked-files=normal",
                    "--ignore-submodules=none"
                ])
            .GetAwaiter()
            .GetResult();
        if (!status.Succeeded)
            return null;
        var indexFlags = git.RunRawAsync(
                projectRoot,
                ["ls-files", "-v", "-z"])
            .GetAwaiter()
            .GetResult();
        if (!indexFlags.Succeeded || HasHiddenTrackedFiles(indexFlags.StdOut))
            return null;
        if (!TrackedWorkingTreeMatchesIndex(projectRoot, git))
            return null;
        return string.IsNullOrWhiteSpace(status.StdOut)
            ? revision
            : null;
    }

    internal static Snapshot CaptureBuildInputs(
        string sourceRoot,
        bool excludesGeneratedDirectories)
    {
        var snapshot = Capture(sourceRoot);
        RejectIgnoredBuildInputs(sourceRoot, excludesGeneratedDirectories);
        RejectSymbolicLinkBuildInputs(sourceRoot, excludesGeneratedDirectories);
        return snapshot;
    }

    internal static void ValidateXcodeBuildInputsWithinSource(
        string sourceRoot,
        string projectPath)
    {
        var canonicalRoot = ResolveRepositoryRoot(projectPath) ??
                            Path.GetFullPath(sourceRoot);
        new AppleReleaseSourceTrustService()
            .ValidateLocalBuildInputContainment(canonicalRoot, projectPath);
    }

    internal static void RejectIgnoredBuildInputs(
        string sourceRoot,
        bool excludesGeneratedDirectories)
    {
        var root = Path.GetFullPath(sourceRoot);
        var git = GitClient.CreateTrustedSystemClient(
            defaultTimeout: TimeSpan.FromSeconds(10));
        var ignored = git.RunRawAsync(
                root,
                ["ls-files", "--others", "--ignored", "--exclude-standard", "-z"])
            .GetAwaiter()
            .GetResult();
        if (!ignored.Succeeded)
        {
            throw new InvalidOperationException(
                "Unable to verify ignored Apple build inputs. " +
                (string.IsNullOrWhiteSpace(ignored.StdErr)
                    ? "git ls-files failed."
                    : ignored.StdErr.Trim()));
        }

        var unexpected = ignored.StdOut
            .Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(path => !excludesGeneratedDirectories || !IsExcludedGeneratedPath(path))
            .Take(5)
            .ToArray();
        if (unexpected.Length == 0)
            return;

        throw new InvalidOperationException(
            "Apple build inputs include Git-ignored files that are not bound by the source revision: " +
            string.Join(", ", unexpected) +
            ". Track or remove them before building.");
    }

    internal static void RejectSymbolicLinkBuildInputs(
        string sourceRoot,
        bool excludesGeneratedDirectories)
    {
        var root = Path.GetFullPath(sourceRoot);
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(root);

        while (pendingDirectories.Count > 0)
        {
            var directory = pendingDirectories.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var relativePath = FrameworkCompatibility
                    .GetRelativePath(root, entry)
                    .Replace('\\', '/');
                if (relativePath.Equals(".git", StringComparison.Ordinal) ||
                    relativePath.StartsWith(".git/", StringComparison.Ordinal) ||
                    excludesGeneratedDirectories && IsExcludedGeneratedPath(relativePath))
                {
                    continue;
                }

                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        $"Apple build inputs must not contain symbolic links or reparse points: '{relativePath}'. " +
                        "Replace the link with content inside the source root before building.");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                    pendingDirectories.Push(entry);
            }
        }
    }

    internal static Snapshot Capture(string sourceRoot)
    {
        var root = Path.GetFullPath(sourceRoot);
        var revision = ResolveLocalSourceRevision(root);
        if (revision is null)
        {
            throw new InvalidOperationException(
                $"Apple source provenance is required, but '{root}' is not a readable, clean Git working tree with a full HEAD revision.");
        }
        var mutationIdentities = CaptureTrackedFileMutationIdentities(root)
            ?? throw new InvalidOperationException(
                $"Apple source provenance is required, but tracked file identities in '{root}' could not be captured safely.");
        return new Snapshot(root, revision, mutationIdentities);
    }

    internal static void ValidateUnchanged(Snapshot snapshot)
    {
        var current = ResolveLocalSourceRevision(snapshot.RootPath);
        var currentMutationIdentities = current is null
            ? null
            : CaptureTrackedFileMutationIdentities(snapshot.RootPath);
        if (!string.Equals(current, snapshot.Revision, StringComparison.Ordinal) ||
            currentMutationIdentities is null ||
            snapshot.TrackedFileMutationIdentities.Count !=
                currentMutationIdentities.Count ||
            snapshot.TrackedFileMutationIdentities.Any(pair =>
                !currentMutationIdentities.TryGetValue(
                    pair.Key,
                    out var currentIdentity) ||
                !pair.Value.Equals(
                    currentIdentity,
                    StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "Apple source changed while PowerForge was preparing or running xcodebuild. Discard the product and rebuild from a stable working tree.");
        }
    }

    internal static string RequireLocalSourceRevision(string sourceRoot)
        => Capture(sourceRoot).Revision;

    internal static string? ResolveRepositoryRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        var fullPath = Path.GetFullPath(path);
        var workingDirectory = Directory.Exists(fullPath)
            ? fullPath
            : Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(workingDirectory) ||
            !Directory.Exists(workingDirectory))
        {
            return null;
        }

        var git = GitClient.CreateTrustedSystemClient(
            defaultTimeout: TimeSpan.FromSeconds(10));
        var relativeTopLevel = git.RunRawAsync(
                workingDirectory!,
                ["rev-parse", "--show-cdup"])
            .GetAwaiter()
            .GetResult();
        if (!relativeTopLevel.Succeeded)
            return null;
        // Build from the caller's path spelling so macOS aliases such as
        // /var and /private/var do not create a false containment failure.
        var repositoryRoot = Path.GetFullPath(Path.Combine(
            workingDirectory!,
            relativeTopLevel.StdOut.Trim()));
        return Directory.Exists(repositoryRoot) ? repositoryRoot : null;
    }

    internal static bool IsGitMetadataMutation(
        FileSystemEventArgs args,
        string sourceRoot,
        StringComparison pathComparison)
    {
        var metadataRoot = Path.Combine(Path.GetFullPath(sourceRoot), ".git");
        var destinationIsMetadata = IsPathWithin(
            args.FullPath,
            metadataRoot,
            pathComparison);
        if (args is not RenamedEventArgs renamed)
            return destinationIsMetadata;

        // A rename is ignorable metadata churn only when both endpoints stay
        // inside .git. Crossing the boundary mutates the build input tree.
        return destinationIsMetadata && IsPathWithin(
            renamed.OldFullPath,
            metadataRoot,
            pathComparison);
    }

    internal static IReadOnlyList<string> AppendXcodeBuildSetting(
        IEnumerable<string>? additionalArguments,
        string? sourceRevision)
    {
        var arguments = (additionalArguments ?? Array.Empty<string>()).ToList();
        if (arguments.Any(IsOwnedXcodeBuildSettingArgument))
        {
            throw new InvalidOperationException(
                $"{XcodeBuildSetting} is owned by PowerForge and cannot be supplied through AdditionalArguments.");
        }

        var normalized = NormalizeSourceRevision(sourceRevision);
        if (normalized is null)
            return arguments;

        arguments.Insert(0, XcodeBuildSetting + "=" + normalized);
        return arguments;
    }

    private static bool IsOwnedXcodeBuildSettingArgument(string? argument)
    {
        var assignment = argument?.Trim();
        if (string.IsNullOrWhiteSpace(assignment))
            return false;
        var equalsIndex = assignment!.IndexOf('=');
        if (equalsIndex <= 0)
            return false;
        var key = assignment.Substring(0, equalsIndex).Trim();
        var conditionIndex = key.IndexOf('[');
        if (conditionIndex >= 0)
            key = key.Substring(0, conditionIndex).Trim();
        return key.Equals(XcodeBuildSetting, StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeSourceRevision(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        if (!GitObjectId.IsFull(normalized!))
        {
            throw new ArgumentException(
                "SourceRevision must be a full SHA-1 or SHA-256 Git object ID from a clean working tree.",
                nameof(value));
        }
        return normalized;
    }

    private static bool IsPathWithin(
        string candidate,
        string root,
        StringComparison comparison)
    {
        var fullCandidate = Path.GetFullPath(candidate);
        var fullRoot = Path.GetFullPath(root);
        if (fullCandidate.Equals(fullRoot, comparison))
            return true;
        var prefix = fullRoot.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullCandidate.StartsWith(prefix, comparison);
    }

    private static bool HasHiddenTrackedFiles(string output)
    {
        foreach (var entry in output.Split(
                     new[] { '\0' },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (entry.Length < 3 || entry[1] != ' ')
                return true;
            var tag = entry[0];
            if (tag == 'S' || char.IsLower(tag))
                return true;
        }
        return false;
    }

    private static bool TrackedWorkingTreeMatchesIndex(
        string projectRoot,
        GitClient git)
    {
        var staged = git.RunRawAsync(
                projectRoot,
                ["ls-files", "--stage", "-z"])
            .GetAwaiter()
            .GetResult();
        if (!staged.Succeeded)
            return false;

        var trackedFiles = new List<(
            string RelativePath,
            string FullPath,
            string ObjectId,
            bool Executable)>();
        foreach (var entry in staged.StdOut.Split(
                     new[] { '\0' },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = entry.IndexOf('\t');
            if (separator < 0)
                return false;
            var metadata = entry.Substring(0, separator).Split(
                new[] { ' ' },
                StringSplitOptions.RemoveEmptyEntries);
            if (metadata.Length != 3 || metadata[2] != "0")
                return false;
            if (metadata[0] != "100644" && metadata[0] != "100755")
                continue;
            var relativePath = entry.Substring(separator + 1);
            var fullPath = Path.GetFullPath(Path.Combine(
                projectRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(fullPath))
                return false;
            trackedFiles.Add((
                relativePath,
                fullPath,
                metadata[1],
                metadata[0] == "100755"));
        }

        try
        {
            var hardLinkCounts = ExistingFilePathIdentityResolver
                .ResolveHardLinkCounts(trackedFiles
                    .Select(static file => file.FullPath)
                    .ToArray());
            if (hardLinkCounts.Any(static count => count != 1))
                return false;
        }
        catch
        {
            return false;
        }

#if NET8_0_OR_GREATER
        if (!OperatingSystem.IsWindows())
        {
            const UnixFileMode executeBits =
                UnixFileMode.UserExecute |
                UnixFileMode.GroupExecute |
                UnixFileMode.OtherExecute;
            foreach (var file in trackedFiles)
            {
                var isExecutable = (File.GetUnixFileMode(file.FullPath) &
                    executeBits) != 0;
                if (isExecutable != file.Executable)
                    return false;
            }
        }
#endif

        const int batchSize = 64;
        for (var offset = 0; offset < trackedFiles.Count; offset += batchSize)
        {
            var batch = trackedFiles.Skip(offset).Take(batchSize).ToArray();
            var arguments = new List<string>
            {
                "hash-object",
                "--no-filters",
                "--"
            };
            arguments.AddRange(batch.Select(static file => file.RelativePath));
            var hashes = git.RunRawAsync(projectRoot, arguments)
                .GetAwaiter()
                .GetResult();
            if (!hashes.Succeeded)
                return false;
            var actual = hashes.StdOut.Split(
                new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            if (actual.Length != batch.Length)
                return false;
            for (var index = 0; index < batch.Length; index++)
            {
                if (!actual[index].Trim().Equals(
                        batch[index].ObjectId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }
        return true;
    }

    private static IReadOnlyDictionary<string, string>?
        CaptureTrackedFileMutationIdentities(string projectRoot)
    {
        var git = GitClient.CreateTrustedSystemClient(
            defaultTimeout: TimeSpan.FromSeconds(10));
        var staged = git.RunRawAsync(
                projectRoot,
                ["ls-files", "--stage", "-z"])
            .GetAwaiter()
            .GetResult();
        if (!staged.Succeeded)
            return null;

        var trackedFiles = new List<(string RelativePath, string FullPath)>();
        foreach (var entry in staged.StdOut.Split(
                     new[] { '\0' },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = entry.IndexOf('\t');
            if (separator < 0)
                return null;
            var metadata = entry.Substring(0, separator).Split(
                new[] { ' ' },
                StringSplitOptions.RemoveEmptyEntries);
            if (metadata.Length != 3 || metadata[2] != "0")
                return null;
            if (metadata[0] != "100644" && metadata[0] != "100755")
                continue;
            var relativePath = entry.Substring(separator + 1);
            var fullPath = Path.GetFullPath(Path.Combine(
                projectRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(fullPath))
                return null;
            trackedFiles.Add((relativePath, fullPath));
        }

        try
        {
            var hardLinkCounts = ExistingFilePathIdentityResolver
                .ResolveHardLinkCounts(trackedFiles
                    .Select(static file => file.FullPath)
                    .ToArray());
            if (hardLinkCounts.Any(static count => count != 1))
                return null;
            var result = new Dictionary<string, string>(
                Path.DirectorySeparatorChar == '\\'
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal);
            foreach (var file in trackedFiles)
            {
                result.Add(
                    file.RelativePath,
                    ExistingFilePathIdentityResolver
                        .ResolveStatus(file.FullPath)
                        .MutationIdentity);
            }
            return result;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsExcludedGeneratedPath(string path)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');
        var separator = normalized.IndexOf('/');
        var rootSegment = separator < 0
            ? normalized
            : normalized.Substring(0, separator);
        return rootSegment.Equals(".build", StringComparison.Ordinal) ||
               rootSegment.Equals(".swiftpm", StringComparison.Ordinal) ||
               rootSegment.Equals("build", StringComparison.Ordinal) ||
               rootSegment.Equals("DerivedData", StringComparison.Ordinal);
    }
}
