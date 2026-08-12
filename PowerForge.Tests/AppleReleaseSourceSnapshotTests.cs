namespace PowerForge.Tests;

public sealed class AppleReleaseSourceSnapshotTests
{
    [Fact]
    public void RemoveWorktreeBestEffort_does_not_surface_git_cleanup_failure()
    {
        var git = new GitClient(
            new FailingGitRunner(),
            gitExecutable: "git-test");

        AppleReleaseSourceSnapshot.RemoveWorktreeBestEffort(
            git,
            Path.GetTempPath(),
            Path.Combine(Path.GetTempPath(), "missing-apple-source-snapshot"));
    }

    private sealed class FailingGitRunner : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new ProcessRunResult(
                1,
                string.Empty,
                "simulated cleanup failure",
                request.FileName,
                TimeSpan.Zero,
                false));
    }
}
