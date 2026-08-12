namespace PowerForge;

/// <summary>Copies one approved archive into a private upload input so exporters cannot observe transient source changes.</summary>
internal sealed class AppleArchiveUploadSnapshot : IDisposable
{
    private readonly SnapshotIdentity _identity;
    private bool _disposed;

    private AppleArchiveUploadSnapshot(
        string rootPath,
        string archivePath,
        SnapshotIdentity identity)
    {
        RootPath = rootPath;
        ArchivePath = archivePath;
        _identity = identity;
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
#if NET8_0_OR_GREATER
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
#endif
        try
        {
            AppleArtifactCopy.CopyDirectory(source, snapshotPath);
            var identity = CaptureCompleteIdentity(snapshotPath);
            if (!identity.Sha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The private Apple upload snapshot does not match the approved archive. Expected '{expectedSha256}', received '{identity.Sha256}'.");
            }
            return new AppleArchiveUploadSnapshot(
                root,
                snapshotPath,
                identity);
        }
        catch
        {
            try { AppleArtifactCopy.DeleteOwnedDirectory(root); } catch { /* best effort private cleanup */ }
            throw;
        }
    }

    internal void ValidateUnchanged(string expectedSha256)
    {
        var current = CaptureCompleteIdentity(ArchivePath);
        if (!current.Sha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The private Apple upload snapshot changed while xcodebuild was reading it. Expected '{expectedSha256}', received '{current.Sha256}'. Discard the upload/export result and inspect remote state before retrying.");
        }

        if (!_identity.MutationDigest.Equals(current.MutationDigest, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The private Apple upload archive snapshot file identity changed while xcodebuild was reading it. " +
                "A transient write or hard-link alias invalidates the approved archive. Discard the upload/export result and inspect remote state before retrying.");
        }
    }

    internal static SnapshotIdentity CaptureCompleteIdentity(
        string archivePath,
        string description = "private Apple upload archive snapshot")
    {
        var sha256 = AppleNotarizationService.ComputeArtifactSha256(archivePath);
        var identities = CaptureFileMutationIdentities(archivePath, description);
        var canonical = new System.Text.StringBuilder();
        foreach (var pair in identities.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            canonical.Append(pair.Key.Length).Append(':').Append(pair.Key);
            canonical.Append(pair.Value.Length).Append(':').Append(pair.Value);
        }
        using var hash = System.Security.Cryptography.SHA256.Create();
        var digest = BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(canonical.ToString())))
            .Replace("-", string.Empty)
            .ToLowerInvariant();
        return new SnapshotIdentity(sha256, digest);
    }

    private static IReadOnlyDictionary<string, string> CaptureFileMutationIdentities(
        string archivePath,
        string description)
    {
        var result = new Dictionary<string, string>(GetPathComparer(archivePath));
        var files = new List<(string RelativePath, string FullPath)>();
        var pending = new Stack<string>();
        pending.Push(archivePath);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    continue;
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                    continue;
                }

                var relativePath = FrameworkCompatibility.GetRelativePath(archivePath, entry).Replace('\\', '/');
                files.Add((relativePath, entry));
            }
        }

        var hardLinkCounts = ExistingFilePathIdentityResolver.ResolveHardLinkCounts(
            files.Select(static file => file.FullPath).ToArray());
        for (var index = 0; index < files.Count; index++)
        {
            if (hardLinkCounts[index] != 1)
            {
                throw new InvalidOperationException(
                    $"The {description} file '{files[index].RelativePath}' has {hardLinkCounts[index]} hard links. " +
                    "Private release snapshots require one pathname per regular file.");
            }
            var status = ExistingFilePathIdentityResolver.ResolveStatus(files[index].FullPath);
            try
            {
                result.Add(files[index].RelativePath, status.MutationIdentity);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidOperationException(
                    $"The {description} contains duplicate platform-equivalent file paths at '{files[index].RelativePath}'.",
                    exception);
            }
        }
        return result;
    }

    private static StringComparer GetPathComparer(string path)
    {
        // Probe the containing volume outside the monitored artifact. The case-semantics probe creates
        // and removes a temporary file; doing that inside a private archive/app would itself invalidate
        // the physical-identity snapshot and produce a false mutation event.
        var fullPath = Path.GetFullPath(path);
        var containingDirectory = Path.GetDirectoryName(fullPath);
        var probePath = containingDirectory is null
            ? fullPath
            : Path.GetDirectoryName(containingDirectory) ?? containingDirectory;
        return FrameworkCompatibility.GetPathStringComparisonForPath(probePath) == StringComparison.OrdinalIgnoreCase
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
    }

    internal sealed class SnapshotIdentity : IEquatable<SnapshotIdentity>
    {
        internal SnapshotIdentity(string sha256, string mutationDigest)
        {
            Sha256 = sha256;
            MutationDigest = mutationDigest;
        }

        internal string Sha256 { get; }

        internal string MutationDigest { get; }

        public bool Equals(SnapshotIdentity? other)
            => other is not null &&
               Sha256.Equals(other.Sha256, StringComparison.OrdinalIgnoreCase) &&
               MutationDigest.Equals(other.MutationDigest, StringComparison.Ordinal);

        public override bool Equals(object? obj) => Equals(obj as SnapshotIdentity);

        public override int GetHashCode()
            => StringComparer.OrdinalIgnoreCase.GetHashCode(Sha256) ^
               StringComparer.Ordinal.GetHashCode(MutationDigest);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try { AppleArtifactCopy.DeleteOwnedDirectory(RootPath); } catch { /* best effort after remote operation */ }
    }

}
