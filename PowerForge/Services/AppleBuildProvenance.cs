namespace PowerForge;

/// <summary>
/// Resolves and binds source provenance to Apple build settings without
/// requiring product repositories to run their own Git scripts.
/// </summary>
internal static class AppleBuildProvenance
{
    internal const string XcodeBuildSetting = "POWERFORGE_SOURCE_REVISION";

    internal sealed record Snapshot(string RootPath, string Revision);

    internal static string? ResolveLocalSourceRevision(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
            return null;

        var git = new GitClient(defaultTimeout: TimeSpan.FromSeconds(10));
        var head = git.RunRawAsync(projectRoot, ["rev-parse", "HEAD"])
            .GetAwaiter()
            .GetResult();
        var revision = head.StdOut.Trim().ToLowerInvariant();
        if (!head.Succeeded || !GitObjectId.IsFull(revision))
            return null;

        var status = git.RunRawAsync(
                projectRoot,
                ["status", "--porcelain=v1", "--untracked-files=normal"])
            .GetAwaiter()
            .GetResult();
        return status.Succeeded && string.IsNullOrWhiteSpace(status.StdOut)
            ? revision
            : revision + "-dirty";
    }

    internal static Snapshot Capture(string sourceRoot)
    {
        var root = Path.GetFullPath(sourceRoot);
        var revision = ResolveLocalSourceRevision(root);
        if (revision is null)
        {
            throw new InvalidOperationException(
                $"Apple source provenance is required, but '{root}' is not a readable Git working tree with a full HEAD revision.");
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
        string sourceRoot)
    {
        var metadataRoot = Path.Combine(Path.GetFullPath(sourceRoot), ".git");
        return IsPathMutation(args, metadataRoot);
    }

    internal static bool IsPathMutation(
        FileSystemEventArgs args,
        string path)
        => IsPathWithin(args.FullPath, path) ||
           args is RenamedEventArgs renamed &&
           IsPathWithin(renamed.OldFullPath, path);

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

        var commit = normalized!.EndsWith("-dirty", StringComparison.Ordinal)
            ? normalized[..^"-dirty".Length]
            : normalized;
        if (!GitObjectId.IsFull(commit))
        {
            throw new ArgumentException(
                "SourceRevision must be a full SHA-1 or SHA-256 Git object ID, optionally followed by '-dirty'.",
                nameof(value));
        }
        return normalized;
    }

    private static bool IsPathWithin(string candidate, string root)
    {
        var comparison = Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var fullCandidate = Path.GetFullPath(candidate);
        var fullRoot = Path.GetFullPath(root);
        if (fullCandidate.Equals(fullRoot, comparison))
            return true;
        var prefix = fullRoot.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullCandidate.StartsWith(prefix, comparison);
    }
}
