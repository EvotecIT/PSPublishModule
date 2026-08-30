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
        internal Snapshot(string rootPath, string revision)
        {
            RootPath = rootPath;
            Revision = revision;
        }

        internal string RootPath { get; }

        internal string Revision { get; }
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
        return new Snapshot(root, revision);
    }

    internal static void ValidateUnchanged(Snapshot snapshot)
    {
        var current = ResolveLocalSourceRevision(snapshot.RootPath);
        if (!string.Equals(current, snapshot.Revision, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Apple source changed while PowerForge was preparing or running xcodebuild. Discard the product and rebuild from a stable working tree.");
        }
    }

    internal static string RequireLocalSourceRevision(string sourceRoot)
        => Capture(sourceRoot).Revision;

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
        if (arguments.Any(argument => argument.StartsWith(
                XcodeBuildSetting + "=",
                StringComparison.OrdinalIgnoreCase)))
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

    private static bool IsExcludedGeneratedPath(string path)
    {
        var segments = path.Replace('\\', '/').Split('/');
        return segments.Any(segment =>
            segment.Equals(".build", StringComparison.Ordinal) ||
            segment.Equals(".swiftpm", StringComparison.Ordinal) ||
            segment.Equals("build", StringComparison.Ordinal) ||
            segment.Equals("DerivedData", StringComparison.Ordinal));
    }
}
