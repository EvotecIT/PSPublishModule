namespace PowerForge;

/// <summary>
/// Provides a private detached Git worktree for an exact-source Apple archive build.
/// Generated archives remain at their configured paths in the caller worktree; only
/// Xcode's source/project input path is rebound to this snapshot.
/// </summary>
internal sealed class AppleReleaseSourceSnapshot : IDisposable
{
    private readonly GitClient _git = GitClient.CreateTrustedSystemClient(defaultTimeout: TimeSpan.FromMinutes(2));
    private readonly string _repositoryRoot;
    private readonly string _sourceRepositoryRoot;
    private readonly string _sourceProjectRoot;
    private readonly string _snapshotProjectRoot;
    private readonly string _sourceCommit;
    private IReadOnlyDictionary<string, string> _trackedFileMutationIdentities =
        new Dictionary<string, string>(StringComparer.Ordinal);
    private string? _snapshotConfigPath;
    private bool _disposed;

    private AppleReleaseSourceSnapshot(
        string repositoryRoot,
        string sourceRepositoryRoot,
        string sourceProjectRoot,
        string snapshotRoot,
        string snapshotProjectRoot,
        string sourceCommit)
    {
        _repositoryRoot = repositoryRoot;
        _sourceRepositoryRoot = sourceRepositoryRoot;
        _sourceProjectRoot = sourceProjectRoot;
        _snapshotProjectRoot = snapshotProjectRoot;
        RootPath = snapshotRoot;
        _sourceCommit = sourceCommit;
    }

    internal string RootPath { get; }

    internal static AppleReleaseSourceSnapshot? CreateIfRequired(PowerForgeAppleReleasePlan plan)
    {
        if (!plan.RequireImmutableSourceSnapshot || string.IsNullOrWhiteSpace(plan.SourceCommit))
            return null;

        if (!plan.Archive)
        {
            ValidateCurrentSource(plan);
            return null;
        }

        var sourceCommit = plan.SourceCommit!.Trim();
        var git = GitClient.CreateTrustedSystemClient(defaultTimeout: TimeSpan.FromMinutes(2));
        var topLevel = Run(git, plan.ProjectRoot, new[] { "rev-parse", "--show-toplevel" }, "resolve the source repository");
        var repositoryRoot = Path.GetFullPath(topLevel.StdOut.Trim());
        var projectPrefix = Run(git, plan.ProjectRoot, new[] { "rev-parse", "--show-prefix" }, "resolve the Apple project root")
            .StdOut.Trim().Replace('/', Path.DirectorySeparatorChar);
        var sourceRepositoryRoot = Path.GetFullPath(plan.ProjectRoot);
        foreach (var _ in projectPrefix.Split(new[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
            sourceRepositoryRoot = Path.GetDirectoryName(sourceRepositoryRoot)!;

        var snapshotParent = Path.Combine(Path.GetTempPath(), "PowerForge", "apple-source-snapshots");
        Directory.CreateDirectory(snapshotParent);
        var snapshotRoot = Path.Combine(snapshotParent, Guid.NewGuid().ToString("N"));
        try
        {
            Run(
                git,
                repositoryRoot,
                new[] { "worktree", "add", "--detach", snapshotRoot, sourceCommit },
                "create the exact-source Apple build snapshot");
#if NET8_0_OR_GREATER
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(snapshotRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
#endif
            var snapshotProjectRoot = Path.GetFullPath(Path.Combine(snapshotRoot, projectPrefix));
            var snapshot = new AppleReleaseSourceSnapshot(
                repositoryRoot,
                sourceRepositoryRoot,
                Path.GetFullPath(plan.ProjectRoot),
                snapshotRoot,
                snapshotProjectRoot,
                sourceCommit);
            snapshot._trackedFileMutationIdentities = snapshot.CaptureTrackedFileMutationIdentities();
            snapshot.ValidateUnchanged();
            if (!string.IsNullOrWhiteSpace(plan.ExactSourceConfigPath))
                snapshot.ValidateExactSourceInputs(plan.ExactSourceConfigPath!);
            return snapshot;
        }
        catch
        {
            if (Directory.Exists(snapshotRoot))
            {
                try
                {
                    Run(git, repositoryRoot, new[] { "worktree", "remove", "--force", snapshotRoot }, "remove the failed Apple build snapshot");
                }
                catch
                {
                    // Preserve the primary failure. Git can prune this unregistered temporary path later.
                }
            }
            throw;
        }
    }

    private static void ValidateCurrentSource(PowerForgeAppleReleasePlan plan)
    {
        var sourceCommit = plan.SourceCommit!.Trim();
        var git = GitClient.CreateTrustedSystemClient(defaultTimeout: TimeSpan.FromMinutes(2));
        var topLevel = Run(git, plan.ProjectRoot, new[] { "rev-parse", "--show-toplevel" }, "resolve the source repository");
        var repositoryRoot = Path.GetFullPath(topLevel.StdOut.Trim());

        string observedCommit;
        if (!string.IsNullOrWhiteSpace(plan.ExactSourceConfigPath) && File.Exists(plan.ExactSourceConfigPath))
        {
            var configPath = Path.GetFullPath(plan.ExactSourceConfigPath!);
            EnsureContained(repositoryRoot, configPath, "Apple exact-source config path");
            observedCommit = new AppleReleaseSourceTrustService()
                .Capture(repositoryRoot, configPath)
                .SourceCommit;
        }
        else
        {
            new HomeAssistantReleaseGitService(git).EnsureClean(repositoryRoot);
            observedCommit = Run(git, repositoryRoot, new[] { "rev-parse", "HEAD" }, "verify the Apple source commit")
                .StdOut.Trim();
        }

        if (!observedCommit.Equals(sourceCommit, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The current Apple release source resolved commit '{observedCommit}' instead of the approved commit '{sourceCommit}'. " +
                "Run the action from the exact clean source commit or omit the source binding.");
        }
    }

    internal string MapPath(string sourcePath)
    {
        var fullPath = Path.GetFullPath(sourcePath);
        EnsureContained(_sourceProjectRoot, fullPath, "Apple Xcode project path");
        var relative = FrameworkCompatibility.GetRelativePath(_sourceProjectRoot, fullPath);
        var mapped = Path.GetFullPath(Path.Combine(_snapshotProjectRoot, relative));
        EnsureContained(RootPath, mapped, "Apple snapshot project path");
        if (!File.Exists(mapped) && !Directory.Exists(mapped))
            throw new FileNotFoundException($"Apple snapshot project input was not found: {mapped}", mapped);
        return mapped;
    }

    /// <summary>Begins monitoring the detached source tree for transient changes during xcodebuild.</summary>
    internal AppleReleaseSourceMutationMonitor MonitorChanges()
        => new(RootPath);

    private string MapRepositoryPath(string sourcePath)
    {
        var fullPath = Path.GetFullPath(sourcePath);
        EnsureContained(_sourceRepositoryRoot, fullPath, "Apple exact-source config path");
        var relative = FrameworkCompatibility.GetRelativePath(_sourceRepositoryRoot, fullPath);
        var mapped = Path.GetFullPath(Path.Combine(RootPath, relative));
        EnsureContained(RootPath, mapped, "Apple snapshot config path");
        return mapped;
    }

    private void ValidateExactSourceInputs(string configPath)
    {
        _snapshotConfigPath = MapRepositoryPath(configPath);
        ValidateMappedExactSourceInputs();
    }

    private void ValidateMappedExactSourceInputs()
    {
        var trust = new AppleReleaseSourceTrustService().Capture(RootPath, _snapshotConfigPath!);
        if (!trust.SourceCommit.Equals(_sourceCommit, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The isolated Apple build snapshot resolved commit '{trust.SourceCommit}' instead of '{_sourceCommit}'.");
        }
    }

    internal void ValidateUnchanged()
    {
        var head = Run(_git, RootPath, new[] { "rev-parse", "HEAD" }, "verify the Apple build snapshot commit")
            .StdOut.Trim();
        if (!head.Equals(_sourceCommit, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The isolated Apple build snapshot changed commits. Expected '{_sourceCommit}', received '{head}'.");
        }

        var currentMutationIdentities = CaptureTrackedFileMutationIdentities();
        if (_trackedFileMutationIdentities.Count != currentMutationIdentities.Count ||
            _trackedFileMutationIdentities.Any(pair =>
                !currentMutationIdentities.TryGetValue(pair.Key, out var current) ||
                !pair.Value.Equals(current, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "The isolated Apple build snapshot file identity changed while xcodebuild was running. " +
                "A transient write or hard-link alias invalidates exact-source evidence. Discard the archive and rebuild from a new snapshot.");
        }

        var status = Run(
            _git,
            RootPath,
            new[] { "status", "--porcelain", "--untracked-files=all" },
            "verify the Apple build snapshot contents");
        if (!string.IsNullOrWhiteSpace(status.StdOut))
        {
            throw new InvalidOperationException(
                "The isolated Apple build snapshot changed while xcodebuild was running. Discard the archive and rebuild from a new snapshot.");
        }
        if (!string.IsNullOrWhiteSpace(_snapshotConfigPath))
            ValidateMappedExactSourceInputs();
    }

    private IReadOnlyDictionary<string, string> CaptureTrackedFileMutationIdentities()
    {
        var tracked = Run(
                _git,
                RootPath,
                new[] { "ls-files", "--stage", "-z" },
                "enumerate the Apple build snapshot files")
            .StdOut.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
        var result = new Dictionary<string, string>(GetPathComparer());
        var trackedFiles = new List<(string RelativePath, string FullPath)>();
        foreach (var entry in tracked)
        {
            var separator = entry.IndexOf('\t');
            if (separator < 0 || !entry.StartsWith("100", StringComparison.Ordinal))
                continue;
            var relativePath = entry.Substring(separator + 1);
            var fullPath = Path.GetFullPath(Path.Combine(
                RootPath,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            EnsureContained(RootPath, fullPath, "Apple snapshot tracked file");
            trackedFiles.Add((relativePath, fullPath));
            var status = ExistingFilePathIdentityResolver.ResolveStatus(fullPath);
            result.Add(relativePath, status.MutationIdentity);
        }

        var hardLinkCounts = ReadHardLinkCounts(trackedFiles.Select(static file => file.FullPath).ToArray());
        for (var index = 0; index < trackedFiles.Count; index++)
        {
            if (hardLinkCounts[index] != 1)
            {
                throw new InvalidOperationException(
                    $"The isolated Apple build snapshot tracked file '{trackedFiles[index].RelativePath}' has {hardLinkCounts[index]} hard links. " +
                    "Exact-source builds require one private pathname per tracked file.");
            }
        }
        return result;
    }

    private IReadOnlyList<int> ReadHardLinkCounts(IReadOnlyList<string> paths)
    {
        if (Path.DirectorySeparatorChar == '\\')
        {
            return paths.Select(path =>
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return ReadWindowsHardLinkCount(stream.SafeFileHandle);
            }).ToArray();
        }

        const int batchSize = 64;
        var executable = "/usr/bin/stat";
#if NET8_0_OR_GREATER
        var isMacOs = OperatingSystem.IsMacOS();
#else
        var isMacOs = true;
#endif
        var counts = new List<int>(paths.Count);
        for (var offset = 0; offset < paths.Count; offset += batchSize)
        {
            var batch = paths.Skip(offset).Take(batchSize).ToArray();
            var arguments = new List<string>
            {
                isMacOs ? "-f" : "-c",
                isMacOs ? "%l" : "%h"
            };
            arguments.AddRange(batch);
            var result = new ProcessRunner().RunAsync(new ProcessRunRequest(
                    executable,
                    RootPath,
                    arguments,
                    TimeSpan.FromMinutes(1),
                    AppleTrustedExecutionEnvironment.Create(),
                    captureOutput: true,
                    captureError: true,
                    inheritEnvironment: false))
                .GetAwaiter()
                .GetResult();
            if (!result.Succeeded)
                throw new InvalidOperationException($"Failed to inspect Apple snapshot hard-link counts: {result.StdErr}".Trim());
            var batchCounts = result.StdOut
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => int.TryParse(value.Trim(), out var count) ? count : -1)
                .ToArray();
            if (batchCounts.Length != batch.Length || batchCounts.Any(static count => count < 0))
                throw new InvalidOperationException("The Apple snapshot hard-link inspection returned an incomplete result.");
            counts.AddRange(batchCounts);
        }
        return counts;
    }

    private static int ReadWindowsHardLinkCount(Microsoft.Win32.SafeHandles.SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var information))
            throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        return checked((int)information.NumberOfLinks);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct WindowsFileTime
    {
        internal uint LowDateTime;
        internal uint HighDateTime;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct WindowsFileInformation
    {
        internal uint FileAttributes;
        internal WindowsFileTime CreationTime;
        internal WindowsFileTime LastAccessTime;
        internal WindowsFileTime LastWriteTime;
        internal uint VolumeSerialNumber;
        internal uint FileSizeHigh;
        internal uint FileSizeLow;
        internal uint NumberOfLinks;
        internal uint FileIndexHigh;
        internal uint FileIndexLow;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        Microsoft.Win32.SafeHandles.SafeFileHandle file,
        out WindowsFileInformation information);

    private static StringComparer GetPathComparer()
        => Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        var result = _git.RunRawAsync(
                _repositoryRoot,
                new[] { "worktree", "remove", "--force", RootPath },
                TimeSpan.FromMinutes(2))
            .GetAwaiter()
            .GetResult();
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to remove the isolated Apple build snapshot '{RootPath}': {result.StdErr}".Trim());
        }
    }

    private static ProcessRunResult Run(
        GitClient git,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        string operation)
    {
        var result = git.RunRawAsync(workingDirectory, arguments, TimeSpan.FromMinutes(2))
            .GetAwaiter()
            .GetResult();
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to {operation}: {result.StdErr}".Trim());
        }
        return result;
    }

    private static void EnsureContained(string root, string candidate, string name)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedCandidate = Path.GetFullPath(candidate);
        var comparison = Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!normalizedCandidate.StartsWith(normalizedRoot, comparison) &&
            !normalizedCandidate.Equals(normalizedRoot.TrimEnd(Path.DirectorySeparatorChar), comparison))
        {
            throw new InvalidOperationException($"{name} must be inside the exact Git repository: {normalizedCandidate}");
        }
    }
}
