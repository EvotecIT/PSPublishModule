namespace PowerForge;

public sealed partial class AppStoreConnectScreenshotSyncService
{
    private static IReadOnlyDictionary<string, string>? MergeExpectedFileSha256(
        string baseDirectory,
        IReadOnlyDictionary<string, string>? releaseExpected,
        IReadOnlyDictionary<string, string>? manifestExpected)
    {
        if (releaseExpected is null || releaseExpected.Count == 0)
            return manifestExpected;
        if (manifestExpected is null || manifestExpected.Count == 0)
            return releaseExpected;

        var comparer = FrameworkCompatibility.GetPathStringComparisonForPath(baseDirectory) == StringComparison.OrdinalIgnoreCase
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var merged = new Dictionary<string, string>(comparer);
        foreach (var pair in releaseExpected.Concat(manifestExpected))
        {
            var path = Path.GetFullPath(pair.Key);
            if (merged.TryGetValue(path, out var existing) &&
                !existing.Equals(pair.Value, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Screenshot '{path}' has conflicting release-plan and approval-manifest SHA-256 evidence.");
            }
            merged[path] = pair.Value;
        }
        return merged;
    }

    private static ScreenshotSnapshotIdentity CaptureScreenshotSnapshotIdentity(
        string root,
        IEnumerable<PreflightedScreenshotSet> sets)
    {
        var canonical = new System.Text.StringBuilder();
        var files = sets.SelectMany(static set => set.Files).OrderBy(static path => path, StringComparer.Ordinal).ToArray();
        var hardLinkCounts = ExistingFilePathIdentityResolver.ResolveHardLinkCounts(files);
        var evidence = new Dictionary<string, ScreenshotFileIdentity>(StringComparer.Ordinal);
        for (var index = 0; index < files.Length; index++)
        {
            var file = files[index];
            var relativePath = FrameworkCompatibility.GetRelativePath(root, file).Replace('\\', '/');
            var sha256 = ComputeSha256(file);
            var md5 = ComputeSourceChecksum(file);
            if (hardLinkCounts[index] != 1)
            {
                throw new InvalidOperationException(
                    $"The private approved screenshot snapshot file '{relativePath}' has {hardLinkCounts[index]} hard links. " +
                    "Approved screenshot snapshots require one private pathname per regular file.");
            }
            var mutationIdentity = ExistingFilePathIdentityResolver.ResolveStatus(file).MutationIdentity;
            evidence[file] = new ScreenshotFileIdentity(sha256, md5, mutationIdentity);
            canonical.Append(relativePath.Length).Append(':').Append(relativePath);
            canonical.Append(sha256.Length).Append(':').Append(sha256);
            canonical.Append(md5.Length).Append(':').Append(md5);
            canonical.Append(mutationIdentity.Length).Append(':').Append(mutationIdentity);
        }
        using var hash = System.Security.Cryptography.SHA256.Create();
        var digest = BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(canonical.ToString())))
            .Replace("-", string.Empty)
            .ToLowerInvariant();
        return new ScreenshotSnapshotIdentity(digest, evidence);
    }

    private sealed class ScreenshotSnapshotIdentity : IEquatable<ScreenshotSnapshotIdentity>
    {
        internal ScreenshotSnapshotIdentity(string digest, IReadOnlyDictionary<string, ScreenshotFileIdentity> files)
        {
            Digest = digest;
            Files = files;
        }

        internal string Digest { get; }

        internal IReadOnlyDictionary<string, ScreenshotFileIdentity> Files { get; }

        public bool Equals(ScreenshotSnapshotIdentity? other)
            => other is not null && Digest.Equals(other.Digest, StringComparison.Ordinal);

        public override bool Equals(object? obj) => Equals(obj as ScreenshotSnapshotIdentity);

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Digest);
    }

    private sealed class ScreenshotFileIdentity
    {
        internal ScreenshotFileIdentity(string sha256, string md5, string mutationIdentity)
        {
            Sha256 = sha256;
            Md5 = md5;
            MutationIdentity = mutationIdentity;
        }

        internal string Sha256 { get; }

        internal string Md5 { get; }

        internal string MutationIdentity { get; }
    }
}
