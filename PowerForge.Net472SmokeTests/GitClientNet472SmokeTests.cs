using PowerForge;

namespace PowerForge.Net472SmokeTests;

public sealed class GitClientNet472SmokeTests
{
    [Fact]
    public async Task GetStatusAsync_ParsesBranchCountsUnderNet472()
    {
        const string output =
            "# branch.head main\r\n" +
            "# branch.upstream origin/main\r\n" +
            "# branch.ab +3 -2\r\n" +
            "1 .M N... 100644 100644 100644 123456 123456 src/file.cs\r\n" +
            "? notes.txt\r\n";

        var runner = new StubProcessRunner(_ => new ProcessRunResult(
            exitCode: 0,
            stdOut: output,
            stdErr: string.Empty,
            executable: "git",
            duration: TimeSpan.FromMilliseconds(25),
            timedOut: false));
        var client = new GitClient(runner);

        var snapshot = await client.GetStatusAsync(@"C:\Repo");

        Assert.True(snapshot.IsGitRepository);
        Assert.Equal("main", snapshot.BranchName);
        Assert.Equal("origin/main", snapshot.UpstreamBranch);
        Assert.Equal(3, snapshot.AheadCount);
        Assert.Equal(2, snapshot.BehindCount);
        Assert.Equal(1, snapshot.TrackedChangeCount);
        Assert.Equal(1, snapshot.UntrackedChangeCount);
    }

    private sealed class StubProcessRunner : IProcessRunner
    {
        private readonly Func<ProcessRunRequest, ProcessRunResult> _execute;

        public StubProcessRunner(Func<ProcessRunRequest, ProcessRunResult> execute)
        {
            _execute = execute;
        }

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_execute(request));
        }
    }
}
