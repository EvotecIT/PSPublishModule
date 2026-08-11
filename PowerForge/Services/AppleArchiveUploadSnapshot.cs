namespace PowerForge;

/// <summary>Copies one approved archive into a private upload input so exporters cannot observe transient source changes.</summary>
internal sealed class AppleArchiveUploadSnapshot : IDisposable
{
    private readonly IReadOnlyDictionary<string, string> _fileMutationIdentities;
    private bool _disposed;

    private AppleArchiveUploadSnapshot(
        string rootPath,
        string archivePath,
        IReadOnlyDictionary<string, string> fileMutationIdentities)
    {
        RootPath = rootPath;
        ArchivePath = archivePath;
        _fileMutationIdentities = fileMutationIdentities;
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
            var actual = AppleNotarizationService.ComputeArtifactSha256(snapshotPath);
            if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The private Apple upload snapshot does not match the approved archive. Expected '{expectedSha256}', received '{actual}'.");
            }
            return new AppleArchiveUploadSnapshot(
                root,
                snapshotPath,
                CaptureFileMutationIdentities(snapshotPath));
        }
        catch
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
            throw;
        }
    }

    internal void ValidateUnchanged(string expectedSha256)
    {
        var actual = AppleNotarizationService.ComputeArtifactSha256(ArchivePath);
        if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The private Apple upload snapshot changed while xcodebuild was reading it. Expected '{expectedSha256}', received '{actual}'. Discard the upload/export result and inspect remote state before retrying.");
        }

        var currentMutationIdentities = CaptureFileMutationIdentities(ArchivePath);
        if (_fileMutationIdentities.Count != currentMutationIdentities.Count ||
            _fileMutationIdentities.Any(pair =>
                !currentMutationIdentities.TryGetValue(pair.Key, out var current) ||
                !string.Equals(pair.Value, current, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "The private Apple upload archive snapshot file identity changed while xcodebuild was reading it. " +
                "A transient write or hard-link alias invalidates the approved archive. Discard the upload/export result and inspect remote state before retrying.");
        }
    }

    private static IReadOnlyDictionary<string, string> CaptureFileMutationIdentities(string archivePath)
    {
        var result = new Dictionary<string, string>(GetPathComparer());
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
                    $"The private Apple upload archive snapshot file '{files[index].RelativePath}' has {hardLinkCounts[index]} hard links. " +
                    "Upload snapshots require one private pathname per regular file.");
            }
            var status = ExistingFilePathIdentityResolver.ResolveStatus(files[index].FullPath);
            try
            {
                result.Add(files[index].RelativePath, status.MutationIdentity);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidOperationException(
                    $"The private Apple upload archive snapshot contains duplicate platform-equivalent file paths at '{files[index].RelativePath}'.",
                    exception);
            }
        }
        return result;
    }

    private static StringComparer GetPathComparer()
        => Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (Directory.Exists(RootPath))
            Directory.Delete(RootPath, recursive: true);
    }

}
