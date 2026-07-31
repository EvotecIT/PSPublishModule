using System.Diagnostics;
using System.Collections.ObjectModel;

namespace PowerForge;

/// <summary>
/// Captures source provenance around an external benchmark run and writes a hash-bound sidecar.
/// </summary>
public sealed class BenchmarkArtifactProvenanceService
{
    /// <summary>Name of the provenance sidecar stored in a benchmark artifact root.</summary>
    public const string SidecarFileName = ".powerforge-benchmark-provenance.json";
    private const string ArtifactRootReservationName = ".powerforge-artifact-root";

    /// <summary>
    /// Starts a provenance capture and reserves an empty artifact directory.
    /// </summary>
    /// <param name="sourceRoot">Git repository root being measured.</param>
    /// <param name="artifactRoot">Fresh directory where the benchmark process will write artifacts.</param>
    /// <param name="metadata">Optional workload metadata declared before measurement.</param>
    /// <param name="runMode">Optional run mode declared before measurement.</param>
    /// <returns>Capture session to complete after measurement.</returns>
    public BenchmarkProvenanceCaptureSession Start(
        string sourceRoot,
        string artifactRoot,
        IReadOnlyDictionary<string, string>? metadata = null,
        string? runMode = null)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot))
            throw new ArgumentException("Source root is required.", nameof(sourceRoot));
        if (string.IsNullOrWhiteSpace(artifactRoot))
            throw new ArgumentException("Artifact root is required.", nameof(artifactRoot));

        string fullSourceRoot = Path.GetFullPath(sourceRoot);
        string fullArtifactRoot = Path.GetFullPath(artifactRoot);
        Dictionary<string, string> declaredMetadata = NormalizeDeclaredMetadata(metadata);
        string normalizedRunMode = NormalizeDeclaredRunMode(runMode);
        if (!Directory.Exists(fullSourceRoot))
            throw new DirectoryNotFoundException($"Benchmark source root was not found: {fullSourceRoot}");
        GitSourceState state = CaptureGitState(fullSourceRoot);
        ValidateCleanSourceState(state, "before");
        ValidateArtifactRootLocation(fullSourceRoot, fullArtifactRoot);
        string reservationTarget = Path.Combine(
            fullArtifactRoot,
            ArtifactRootReservationName);
        IDisposable? artifactRootLease = null;
        try
        {
            artifactRootLease = BenchmarkFileUpdateLock.Acquire(reservationTarget);
            if (Directory.EnumerateFileSystemEntries(fullArtifactRoot)
                .Any(path => !IsArtifactRootReservationLock(path, fullArtifactRoot)))
            {
                throw new InvalidOperationException(
                    "Benchmark provenance capture requires an empty artifact directory so stale reports cannot be attributed to the current source.");
            }

            var session = new BenchmarkProvenanceCaptureSession
            {
                SourceRoot = fullSourceRoot,
                ArtifactRoot = fullArtifactRoot,
                StartedUtc = DateTimeOffset.UtcNow,
                SourceCommit = state.Commit,
                SourceBranch = state.Branch,
                Metadata = new ReadOnlyDictionary<string, string>(declaredMetadata),
                RunMode = normalizedRunMode,
                ArtifactRootLease = artifactRootLease
            };
            artifactRootLease = null;
            return session;
        }
        finally
        {
            artifactRootLease?.Dispose();
        }
    }

    /// <summary>
    /// Completes a capture after measurement, verifies unchanged source, and hashes every produced artifact.
    /// </summary>
    /// <param name="session">Session returned by <see cref="Start"/> before measurement.</param>
    /// <returns>Path to the completed provenance sidecar.</returns>
    public string Complete(BenchmarkProvenanceCaptureSession session)
    {
        if (session is null) throw new ArgumentNullException(nameof(session));
        if (session.ArtifactRootLease is null)
            throw new InvalidOperationException(
                "The benchmark provenance capture session is no longer active.");
        try
        {
            GitSourceState state = CaptureGitState(session.SourceRoot);
            ValidateCleanSourceState(state, "after");
            if (!string.Equals(state.Commit, session.SourceCommit, StringComparison.Ordinal) ||
                !string.Equals(state.Branch, session.SourceBranch, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Benchmark source provenance changed while the external measurement was running. Discard the artifacts and run again.");
            }

            string sidecarPath = Path.Combine(session.ArtifactRoot, SidecarFileName);
            BenchmarkProducedArtifact[] artifacts = EnumerateArtifactFiles(session.ArtifactRoot)
                .Select(path => new BenchmarkProducedArtifact
                {
                    Path = NormalizeRelativePath(
                        FrameworkCompatibility.GetRelativePath(session.ArtifactRoot, path)),
                    Length = new FileInfo(path).Length,
                    Sha256 = BenchmarkJson.ComputeFileSha256(path)
                })
                .OrderBy(artifact => artifact.Path, StringComparer.Ordinal)
                .ToArray();
            if (artifacts.Length == 0)
            {
                throw new InvalidOperationException(
                    "The benchmark process did not produce any files in the reserved artifact directory.");
            }

            var document = new BenchmarkArtifactProvenanceDocument
            {
                SourceCommit = session.SourceCommit,
                SourceBranch = session.SourceBranch,
                GitWorktreeClean = true,
                StartedUtc = session.StartedUtc,
                FinishedUtc = DateTimeOffset.UtcNow,
                Metadata = session.Metadata.ToDictionary(
                    item => item.Key,
                    item => item.Value,
                    StringComparer.OrdinalIgnoreCase),
                RunMode = session.RunMode,
                Artifacts = artifacts
            };
            BenchmarkJson.Write(sidecarPath, document);
            return sidecarPath;
        }
        finally
        {
            session.Dispose();
        }
    }

    internal static bool TryLoadAndValidate(
        string inputPath,
        out BenchmarkArtifactProvenanceDocument? provenance,
        out string artifactRoot,
        out string sidecarPath)
    {
        string fullInput = Path.GetFullPath(inputPath);
        artifactRoot = Directory.Exists(fullInput)
            ? fullInput
            : FindArtifactRoot(fullInput);
        string resolvedArtifactRoot = artifactRoot;
        sidecarPath = Path.Combine(artifactRoot, SidecarFileName);
        provenance = null;
        if (!File.Exists(sidecarPath))
            return false;

        provenance = BenchmarkJson.Read<BenchmarkArtifactProvenanceDocument>(sidecarPath);
        if (provenance.SchemaVersion != 1 ||
            !IsFullGitObjectId(provenance.SourceCommit) ||
            !provenance.GitWorktreeClean ||
            provenance.StartedUtc == default ||
            provenance.FinishedUtc < provenance.StartedUtc ||
            provenance.Artifacts.Length == 0)
        {
            throw new InvalidOperationException(
                $"Benchmark provenance sidecar is incomplete or unsupported: {sidecarPath}");
        }
        provenance.Metadata = NormalizeDeclaredMetadata(provenance.Metadata);
        provenance.RunMode = NormalizeDeclaredRunMode(provenance.RunMode);

        string[] actualFiles = EnumerateArtifactFiles(resolvedArtifactRoot)
            .Select(path => NormalizeRelativePath(
                FrameworkCompatibility.GetRelativePath(resolvedArtifactRoot, path)))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        string[] declaredFiles = provenance.Artifacts
            .Select(artifact => NormalizeRelativePath(artifact.Path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (!actualFiles.SequenceEqual(declaredFiles, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "Benchmark artifact contents changed after the provenance sidecar was completed.");
        }

        foreach (BenchmarkProducedArtifact artifact in provenance.Artifacts)
        {
            string path = ResolveContainedArtifactPath(resolvedArtifactRoot, artifact.Path);
            var info = new FileInfo(path);
            if (!info.Exists ||
                info.Length != artifact.Length ||
                !string.Equals(
                    BenchmarkJson.ComputeFileSha256(path),
                    artifact.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Benchmark artifact '{artifact.Path}' does not match its production provenance sidecar.");
            }
        }

        if (File.Exists(fullInput))
        {
            string relativeInput = NormalizeRelativePath(
                FrameworkCompatibility.GetRelativePath(artifactRoot, fullInput));
            if (!declaredFiles.Contains(relativeInput, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    "The imported benchmark file is not listed in its production provenance sidecar.");
            }
        }

        return true;
    }

    internal static BenchmarkArtifactSnapshot CreateValidatedSnapshot(
        string inputPath,
        BenchmarkArtifactProvenanceDocument provenance,
        string artifactRoot,
        string sidecarPath)
    {
        if (provenance is null) throw new ArgumentNullException(nameof(provenance));
        string snapshotContainer = Path.Combine(
            Path.GetTempPath(),
            "powerforge-benchmark-import-" + Guid.NewGuid().ToString("N"));
        string rootName = new DirectoryInfo(
            Path.GetFullPath(artifactRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).Name;
        string snapshotRoot = Path.Combine(snapshotContainer, rootName);
        Directory.CreateDirectory(snapshotRoot);
        try
        {
            File.Copy(sidecarPath, Path.Combine(snapshotRoot, SidecarFileName), overwrite: false);
            foreach (BenchmarkProducedArtifact artifact in provenance.Artifacts)
            {
                string sourcePath = ResolveContainedArtifactPath(artifactRoot, artifact.Path);
                string destinationPath = ResolveContainedArtifactPath(snapshotRoot, artifact.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(sourcePath, destinationPath, overwrite: false);
                File.SetLastWriteTimeUtc(destinationPath, File.GetLastWriteTimeUtc(sourcePath));
            }

            string fullInput = Path.GetFullPath(inputPath);
            string snapshotInput = Directory.Exists(fullInput)
                ? snapshotRoot
                : ResolveContainedArtifactPath(
                    snapshotRoot,
                    NormalizeRelativePath(
                        FrameworkCompatibility.GetRelativePath(artifactRoot, fullInput)));
            if (!TryLoadAndValidate(
                    snapshotInput,
                    out BenchmarkArtifactProvenanceDocument? snapshotProvenance,
                    out _,
                    out string snapshotSidecarPath))
            {
                throw new InvalidOperationException(
                    "Unable to validate the isolated benchmark artifact snapshot.");
            }

            return new BenchmarkArtifactSnapshot(
                snapshotContainer,
                snapshotInput,
                snapshotProvenance!,
                snapshotSidecarPath);
        }
        catch
        {
            TryDeleteDirectory(snapshotContainer);
            throw;
        }
    }

    private static IEnumerable<string> EnumerateArtifactFiles(string artifactRoot)
        => Directory.Exists(artifactRoot)
            ? Directory.EnumerateFiles(artifactRoot, "*", SearchOption.AllDirectories)
                .Where(path =>
                     !string.Equals(
                        Path.GetFullPath(path),
                        Path.GetFullPath(Path.Combine(artifactRoot, SidecarFileName)),
                         FrameworkCompatibility.GetPathStringComparison(artifactRoot)) &&
                    !IsArtifactRootReservationLock(path, artifactRoot) &&
                     !path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            : Enumerable.Empty<string>();

    private static string FindArtifactRoot(string inputPath)
    {
        string? current = Path.GetDirectoryName(inputPath);
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, SidecarFileName)))
                return current;
            DirectoryInfo? parent = Directory.GetParent(current);
            if (parent is null)
                break;
            current = parent.FullName;
        }

        return Path.GetDirectoryName(inputPath) ?? string.Empty;
    }

    private static bool IsArtifactRootReservationLock(
        string path,
        string artifactRoot)
    {
        string reservationPath = BenchmarkFileUpdateLock.CreateLockPath(
            Path.Combine(artifactRoot, ArtifactRootReservationName));
        return string.Equals(
            Path.GetFullPath(path),
            Path.GetFullPath(reservationPath),
            FrameworkCompatibility.GetPathStringComparison(artifactRoot));
    }

    private static void ValidateArtifactRootLocation(
        string sourceRoot,
        string artifactRoot)
    {
        string normalizedSourceRoot = sourceRoot
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string normalizedArtifactRoot = artifactRoot
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        StringComparison comparison =
            FrameworkCompatibility.GetPathStringComparison(sourceRoot);
        if (string.Equals(
                normalizedSourceRoot,
                normalizedArtifactRoot,
                comparison))
        {
            throw new InvalidOperationException(
                "The benchmark artifact root cannot be the source repository root.");
        }
        if (!normalizedArtifactRoot.StartsWith(normalizedSourceRoot, comparison))
            return;

        string relativePath = NormalizeRelativePath(
            FrameworkCompatibility.GetRelativePath(sourceRoot, artifactRoot));
        string tracked = ReadGitValue(sourceRoot, "ls-files -z");
        string prefix = relativePath.TrimEnd('/') + "/";
        if (tracked.Split('\0')
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Any(path =>
                string.Equals(path, relativePath, StringComparison.Ordinal) ||
                path.StartsWith(prefix, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "An in-repository benchmark artifact root cannot contain tracked files.");
        }
        if (!IsGitIgnored(sourceRoot, relativePath))
        {
            throw new InvalidOperationException(
                "An in-repository benchmark artifact root must be ignored by Git. Use an ignored generated-output directory or a directory outside the source repository.");
        }
    }

    private static bool IsGitIgnored(string sourceRoot, string relativePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = "check-ignore -q --no-index --stdin",
            WorkingDirectory = sourceRoot,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using Process process = Process.Start(startInfo)
                                ?? throw new InvalidOperationException(
                                    "Unable to start Git while validating the benchmark artifact root.");
        process.StandardInput.WriteLine(relativePath);
        process.StandardInput.Close();
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(5000))
        {
            try
            {
#if NET8_0_OR_GREATER
                process.Kill(entireProcessTree: true);
#else
                process.Kill();
#endif
            }
            catch
            {
            }
            throw new InvalidOperationException(
                "Timed out while validating the benchmark artifact root.");
        }
        outputTask.GetAwaiter().GetResult();
        string error = errorTask.GetAwaiter().GetResult();
        if (process.ExitCode is 0 or 1)
            return process.ExitCode == 0;
        throw new InvalidOperationException(
            $"Unable to validate the benchmark artifact root against Git ignore rules: {error.Trim()}");
    }

    private static string ResolveContainedArtifactPath(
        string artifactRoot,
        string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new InvalidOperationException("Benchmark provenance artifact paths must be relative.");
        string root = Path.GetFullPath(artifactRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string candidate = Path.GetFullPath(Path.Combine(
            root,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(
                root,
                FrameworkCompatibility.GetPathStringComparison(artifactRoot)))
        {
            throw new InvalidOperationException(
                $"Benchmark provenance artifact path escapes its artifact root: {relativePath}");
        }

        return candidate;
    }

    private static string NormalizeRelativePath(string value)
        => value.Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');

    private static GitSourceState CaptureGitState(string sourceRoot)
        => new(
            ReadGitValue(sourceRoot, "rev-parse HEAD").Trim(),
            ReadGitValue(sourceRoot, "branch --show-current").Trim(),
            ReadGitValue(sourceRoot, "status --porcelain --untracked-files=normal"));

    private static string ReadGitValue(string sourceRoot, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = sourceRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using Process process = Process.Start(startInfo)
                                ?? throw new InvalidOperationException(
                                    "Unable to start Git while capturing benchmark provenance.");
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(5000))
        {
            try
            {
#if NET8_0_OR_GREATER
                process.Kill(entireProcessTree: true);
#else
                process.Kill();
#endif
                process.WaitForExit(1000);
            }
            catch
            {
                // The process may have exited between the timeout and cleanup.
            }
            throw new InvalidOperationException(
                "Timed out while capturing benchmark source provenance.");
        }

        process.WaitForExit();
        string output = outputTask.GetAwaiter().GetResult();
        string error = errorTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Unable to capture benchmark source provenance: {error.Trim()}");
        }

        return output.TrimEnd();
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Temporary import snapshots are best-effort cleanup only.
        }
    }

    private static void ValidateCleanSourceState(GitSourceState state, string stage)
    {
        if (!IsFullGitObjectId(state.Commit) || !string.IsNullOrWhiteSpace(state.Status))
        {
            throw new InvalidOperationException(
                $"External benchmark provenance requires a clean Git worktree {stage} measurement.");
        }
    }

    private static bool IsFullGitObjectId(string value)
        => (value.Length == 40 || value.Length == 64) && value.All(Uri.IsHexDigit);

    private static Dictionary<string, string> NormalizeDeclaredMetadata(
        IReadOnlyDictionary<string, string>? metadata)
    {
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> item in
                 metadata ?? new Dictionary<string, string>())
        {
            string key = item.Key?.Trim() ?? string.Empty;
            if (key.Length == 0)
                throw new ArgumentException("Benchmark provenance metadata keys cannot be empty.", nameof(metadata));
            if (item.Value is null)
                throw new ArgumentException(
                    $"Benchmark provenance metadata value '{key}' cannot be null.",
                    nameof(metadata));
            if (IsCaptureOwnedMetadataKey(key))
            {
                throw new ArgumentException(
                    $"Benchmark provenance metadata key '{key}' is owned by the capture service.",
                    nameof(metadata));
            }
            if (normalized.ContainsKey(key))
            {
                throw new ArgumentException(
                    $"Benchmark provenance metadata contains duplicate key '{key}'.",
                    nameof(metadata));
            }
            normalized.Add(key, item.Value);
        }

        return normalized;
    }

    private static string NormalizeDeclaredRunMode(string? runMode)
    {
        if (string.IsNullOrWhiteSpace(runMode))
            return string.Empty;
        return runMode!.Trim().ToLowerInvariant();
    }

    private static bool IsCaptureOwnedMetadataKey(string key)
        => key.StartsWith("benchmark.provenance.", StringComparison.OrdinalIgnoreCase) ||
           key.Equals("gitSha", StringComparison.OrdinalIgnoreCase) ||
           key.Equals("gitBranch", StringComparison.OrdinalIgnoreCase) ||
           key.Equals("gitWorktreeClean", StringComparison.OrdinalIgnoreCase) ||
           key.Equals("importedUtc", StringComparison.OrdinalIgnoreCase) ||
           key.Equals("runMode", StringComparison.OrdinalIgnoreCase);

    private sealed class GitSourceState
    {
        internal GitSourceState(
            string commit,
            string branch,
            string status)
        {
            Commit = commit;
            Branch = branch;
            Status = status;
        }

        internal string Commit { get; }
        internal string Branch { get; }
        internal string Status { get; }
    }
}

internal sealed class BenchmarkArtifactSnapshot : IDisposable
{
    internal BenchmarkArtifactSnapshot(
        string containerPath,
        string inputPath,
        BenchmarkArtifactProvenanceDocument provenance,
        string sidecarPath)
    {
        ContainerPath = containerPath;
        InputPath = inputPath;
        Provenance = provenance;
        SidecarPath = sidecarPath;
    }

    internal string ContainerPath { get; }
    internal string InputPath { get; }
    internal BenchmarkArtifactProvenanceDocument Provenance { get; }
    internal string SidecarPath { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(ContainerPath))
                Directory.Delete(ContainerPath, recursive: true);
        }
        catch
        {
            // Temporary import snapshots are best-effort cleanup only.
        }
    }
}
