using System.Diagnostics;

namespace PowerForge;

/// <summary>
/// Captures source provenance around an external benchmark run and writes a hash-bound sidecar.
/// </summary>
public sealed class BenchmarkArtifactProvenanceService
{
    /// <summary>Name of the provenance sidecar stored in a benchmark artifact root.</summary>
    public const string SidecarFileName = ".powerforge-benchmark-provenance.json";

    /// <summary>
    /// Starts a provenance capture and reserves an empty artifact directory.
    /// </summary>
    /// <param name="sourceRoot">Git repository root being measured.</param>
    /// <param name="artifactRoot">Fresh directory where the benchmark process will write artifacts.</param>
    /// <returns>Capture session to complete after measurement.</returns>
    public BenchmarkProvenanceCaptureSession Start(
        string sourceRoot,
        string artifactRoot)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot))
            throw new ArgumentException("Source root is required.", nameof(sourceRoot));
        if (string.IsNullOrWhiteSpace(artifactRoot))
            throw new ArgumentException("Artifact root is required.", nameof(artifactRoot));

        string fullSourceRoot = Path.GetFullPath(sourceRoot);
        string fullArtifactRoot = Path.GetFullPath(artifactRoot);
        if (!Directory.Exists(fullSourceRoot))
            throw new DirectoryNotFoundException($"Benchmark source root was not found: {fullSourceRoot}");
        if (Directory.Exists(fullArtifactRoot) &&
            Directory.EnumerateFileSystemEntries(fullArtifactRoot).Any())
        {
            throw new InvalidOperationException(
                "Benchmark provenance capture requires an empty artifact directory so stale reports cannot be attributed to the current source.");
        }

        GitSourceState state = CaptureGitState(fullSourceRoot);
        ValidateCleanSourceState(state, "before");
        Directory.CreateDirectory(fullArtifactRoot);
        return new BenchmarkProvenanceCaptureSession
        {
            SourceRoot = fullSourceRoot,
            ArtifactRoot = fullArtifactRoot,
            StartedUtc = DateTimeOffset.UtcNow,
            SourceCommit = state.Commit,
            SourceBranch = state.Branch
        };
    }

    /// <summary>
    /// Completes a capture after measurement, verifies unchanged source, and hashes every produced artifact.
    /// </summary>
    /// <param name="session">Session returned by <see cref="Start"/> before measurement.</param>
    /// <returns>Path to the completed provenance sidecar.</returns>
    public string Complete(BenchmarkProvenanceCaptureSession session)
    {
        if (session is null) throw new ArgumentNullException(nameof(session));
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
            Artifacts = artifacts
        };
        BenchmarkJson.Write(sidecarPath, document);
        return sidecarPath;
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
            : Path.GetDirectoryName(fullInput) ?? string.Empty;
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

    private static IEnumerable<string> EnumerateArtifactFiles(string artifactRoot)
        => Directory.Exists(artifactRoot)
            ? Directory.EnumerateFiles(artifactRoot, "*", SearchOption.AllDirectories)
                .Where(path =>
                    !string.Equals(
                        Path.GetFullPath(path),
                        Path.GetFullPath(Path.Combine(artifactRoot, SidecarFileName)),
                        FrameworkCompatibility.GetPathStringComparison(artifactRoot)) &&
                    !path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            : Enumerable.Empty<string>();

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
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(5000) || process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Unable to capture benchmark source provenance: {error.Trim()}");
        }

        return output.TrimEnd();
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
