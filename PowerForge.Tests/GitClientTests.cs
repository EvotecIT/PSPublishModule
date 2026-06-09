using PowerForge;

namespace PowerForge.Tests;

public sealed class GitClientTests
{
    [Fact]
    public async Task GetStatusAsync_ParsesBranchAheadBehindAndChangeCounts()
    {
        const string output = """
# branch.head codex/release-flow
# branch.upstream origin/codex/release-flow
# branch.ab +2 -1
1 .M N... 100644 100644 100644 123456 123456 file.cs
? notes.txt
""";

        var runner = new StubProcessRunner(_ => new ProcessRunResult(
            exitCode: 0,
            stdOut: output,
            stdErr: string.Empty,
            executable: "git",
            duration: TimeSpan.FromSeconds(1),
            timedOut: false));
        var client = new GitClient(runner);

        var snapshot = await client.GetStatusAsync(@"C:\repo");

        Assert.True(snapshot.IsGitRepository);
        Assert.Equal("codex/release-flow", snapshot.BranchName);
        Assert.Equal("origin/codex/release-flow", snapshot.UpstreamBranch);
        Assert.Equal(2, snapshot.AheadCount);
        Assert.Equal(1, snapshot.BehindCount);
        Assert.Equal(1, snapshot.TrackedChangeCount);
        Assert.Equal(1, snapshot.UntrackedChangeCount);
    }

    [Fact]
    public async Task CreateBranchAsync_BuildsTypedGitArguments()
    {
        ProcessRunRequest? captured = null;
        var runner = new StubProcessRunner(request => {
            captured = request;
            return new ProcessRunResult(0, string.Empty, string.Empty, "git", TimeSpan.Zero, timedOut: false);
        });
        var client = new GitClient(runner);

        var result = await client.CreateBranchAsync(@"C:\repo", "codex/pspublishmodule-release-flow");

        Assert.NotNull(captured);
        Assert.Equal("git", captured!.FileName);
        Assert.Equal(@"C:\repo", captured.WorkingDirectory);
        Assert.Equal(["switch", "-c", "codex/pspublishmodule-release-flow"], captured.Arguments);
        Assert.Equal("git switch -c codex/pspublishmodule-release-flow", result.DisplayCommand);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task GetRemoteUrlAsync_BuildsTypedGitArguments()
    {
        ProcessRunRequest? captured = null;
        var runner = new StubProcessRunner(request => {
            captured = request;
            return new ProcessRunResult(0, "https://github.com/EvotecIT/PSPublishModule.git", string.Empty, "git", TimeSpan.Zero, timedOut: false);
        });
        var client = new GitClient(runner);

        var result = await client.GetRemoteUrlAsync(@"C:\repo");

        Assert.NotNull(captured);
        Assert.Equal(["remote", "get-url", "origin"], captured!.Arguments);
        Assert.Equal("git remote get-url origin", result.DisplayCommand);
        Assert.True(result.Succeeded);
    }

    private sealed class StubProcessRunner : IProcessRunner
    {
        private readonly Func<ProcessRunRequest, ProcessRunResult> _execute;

        public StubProcessRunner(Func<ProcessRunRequest, ProcessRunResult> execute)
        {
            _execute = execute;
        }

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(_execute(request));
    }
}
