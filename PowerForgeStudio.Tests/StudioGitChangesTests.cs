using PowerForge;
using PowerForgeStudio.Domain.Hub;
using PowerForgeStudio.Orchestrator.Hub;

namespace PowerForgeStudio.Tests;

public sealed class StudioGitChangesTests
{
    [Fact]
    public async Task MergeConflictsRemainVisibleAndCannotBeCommitted()
    {
        using var fixture = new RepositoryFixture();
        await fixture.Run("init", "-b", "main");
        await fixture.Run("config", "user.name", "Studio validation");
        await fixture.Run("config", "user.email", "studio-validation@example.invalid");
        await fixture.Run("config", "commit.gpgSign", "false");
        await fixture.Run("config", "core.hooksPath", Path.Combine(fixture.Root, "empty-hooks"));
        var path = Path.Combine(fixture.Root, "conflict.txt");
        await File.WriteAllTextAsync(path, "base\n");
        await fixture.Run("add", "conflict.txt");
        await fixture.Run("commit", "-m", "base");
        await fixture.Run("switch", "-c", "other");
        await File.WriteAllTextAsync(path, "other\n");
        await fixture.Run("commit", "-am", "other");
        await fixture.Run("switch", "main");
        await File.WriteAllTextAsync(path, "main\n");
        await fixture.Run("commit", "-am", "main");
        var merge = await fixture.Git.RunRawAsync(fixture.Root, ["merge", "other"]);
        Assert.Equal(1, merge.ExitCode);
        var service = new ProjectGitService(fixture.Git);
        var status = await service.GetStatusAsync(fixture.Root);
        Assert.True(status.HasConflicts);
        Assert.Equal(GitChangeKind.Unmerged, Assert.Single(status.UnstagedChanges).Kind);
        Assert.Contains("<<<<<<<", await File.ReadAllTextAsync(path));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CommitAsync(fixture.Root, "Must not commit"));
        Assert.Contains("main", (await service.GetLogAsync(fixture.Root))[0].Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedStatusDoesNotMasqueradeAsCleanOrNonRepository(bool timeout)
    {
        using var fixture = new RepositoryFixture();
        Directory.CreateDirectory(Path.Combine(fixture.Root, ".git"));
        var runner = new StubRunner(_ => new ProcessRunResult(128, "", "broken repository", "git", TimeSpan.Zero, timeout));
        var service = new ProjectGitService(new GitClient(runner));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetStatusAsync(fixture.Root));
        Assert.Single(runner.Requests);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetStatusAsync(fixture.Root, cancelled.Token));
        Assert.Single(runner.Requests);
    }

    [Fact]
    public async Task FailedHeadProbeNeverRemovesIndexEntries()
    {
        using var fixture = new RepositoryFixture();
        var runner = new StubRunner(_ => new ProcessRunResult(128, "", "bad object HEAD", "git", TimeSpan.Zero, false));
        var service = new ProjectGitService(new GitClient(runner));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UnstageFileAsync(fixture.Root, "file.txt"));
        Assert.Single(runner.Requests);
        await Assert.ThrowsAsync<ArgumentException>(() => service.StageFileAsync(fixture.Root, "../outside.txt"));
        Assert.Single(runner.Requests);
    }

    [Fact]
    public async Task ExistingBranchWithUnresolvableHeadNeverRemovesIndexEntries()
    {
        using var fixture = new RepositoryFixture();
        var runner = new StubRunner(request => request.Arguments[0] switch
        {
            "rev-parse" => new ProcessRunResult(1, "", "", "git", TimeSpan.Zero, false),
            "symbolic-ref" => new ProcessRunResult(0, "refs/heads/main\n", "", "git", TimeSpan.Zero, false),
            "show-ref" => new ProcessRunResult(0, "", "", "git", TimeSpan.Zero, false),
            _ => throw new InvalidOperationException("Unexpected mutation")
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ProjectGitService(new GitClient(runner)).UnstageFileAsync(fixture.Root, "file.txt"));
        Assert.Equal(3, runner.Requests.Count);
    }

    private sealed class StubRunner(Func<ProcessRunRequest, ProcessRunResult> run) : IProcessRunner
    {
        public List<ProcessRunRequest> Requests { get; } = [];
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(run(request));
        }
    }

    private sealed class RepositoryFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "studio-git-contract-" + Guid.NewGuid().ToString("N"));
        public GitClient Git { get; } = new();
        public RepositoryFixture() => Directory.CreateDirectory(Root);
        public async Task Run(params string[] arguments)
        {
            var result = await Git.RunRawAsync(Root, arguments);
            Assert.True(result.Succeeded, result.StdErr);
        }
        public void Dispose()
        {
            foreach (var file in new DirectoryInfo(Root).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
                if (file.IsReadOnly) file.IsReadOnly = false;
            Directory.Delete(Root, recursive: true);
        }
    }
}
