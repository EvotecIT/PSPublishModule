namespace PowerForge;

/// <summary>
/// Provides a private detached Git worktree for an exact-source Apple archive build.
/// Generated archives remain at their configured paths in the caller worktree; only
/// Xcode's source/project input path is rebound to this snapshot.
/// </summary>
internal sealed class AppleReleaseSourceSnapshot : IDisposable
{
    private readonly GitClient _git = new(defaultTimeout: TimeSpan.FromMinutes(2));
    private readonly string _repositoryRoot;
    private readonly string _sourceProjectRoot;
    private readonly string _snapshotProjectRoot;
    private readonly string _sourceCommit;
    private bool _disposed;

    private AppleReleaseSourceSnapshot(
        string repositoryRoot,
        string sourceProjectRoot,
        string snapshotRoot,
        string snapshotProjectRoot,
        string sourceCommit)
    {
        _repositoryRoot = repositoryRoot;
        _sourceProjectRoot = sourceProjectRoot;
        _snapshotProjectRoot = snapshotProjectRoot;
        RootPath = snapshotRoot;
        _sourceCommit = sourceCommit;
    }

    internal string RootPath { get; }

    internal static AppleReleaseSourceSnapshot? CreateIfRequired(PowerForgeAppleReleasePlan plan)
    {
        if (!plan.Archive || !plan.RequireImmutableSourceSnapshot || string.IsNullOrWhiteSpace(plan.SourceCommit))
            return null;

        var sourceCommit = plan.SourceCommit!.Trim();
        var git = new GitClient(defaultTimeout: TimeSpan.FromMinutes(2));
        var topLevel = Run(git, plan.ProjectRoot, new[] { "rev-parse", "--show-toplevel" }, "resolve the source repository");
        var repositoryRoot = Path.GetFullPath(topLevel.StdOut.Trim());
        var projectPrefix = Run(git, plan.ProjectRoot, new[] { "rev-parse", "--show-prefix" }, "resolve the Apple project root")
            .StdOut.Trim().Replace('/', Path.DirectorySeparatorChar);

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
            var snapshotProjectRoot = Path.GetFullPath(Path.Combine(snapshotRoot, projectPrefix));
            var snapshot = new AppleReleaseSourceSnapshot(
                repositoryRoot,
                Path.GetFullPath(plan.ProjectRoot),
                snapshotRoot,
                snapshotProjectRoot,
                sourceCommit);
            snapshot.ValidateUnchanged();
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

    internal void ValidateUnchanged()
    {
        var head = Run(_git, RootPath, new[] { "rev-parse", "HEAD" }, "verify the Apple build snapshot commit")
            .StdOut.Trim();
        if (!head.Equals(_sourceCommit, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The isolated Apple build snapshot changed commits. Expected '{_sourceCommit}', received '{head}'.");
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
    }

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
