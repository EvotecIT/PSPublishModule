using PowerForge;
using PowerForgeStudio.Orchestrator.Hub;

namespace PowerForgeStudio.Tests;

public sealed class StudioGitHistoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "studio-git-history-" + Guid.NewGuid().ToString("N"));
    private readonly GitClient _git = new();

    [Fact]
    public void BoundedGitOverloadPreservesTheExistingPublicClrSignature()
    {
        var legacy = typeof(GitClient).GetMethod(nameof(GitClient.RunRawAsync),
            [typeof(string), typeof(IReadOnlyList<string>), typeof(TimeSpan?), typeof(CancellationToken)]);

        Assert.NotNull(legacy);
        Assert.Equal(typeof(Task<ProcessRunResult>), legacy.ReturnType);
    }

    [Fact]
    public async Task CommitDetailIsReadOnlyValidatedAndBounded()
    {
        Directory.CreateDirectory(_root);
        await Run("init", "-b", "main");
        await Run("config", "user.name", "Studio validation");
        await Run("config", "user.email", "studio-validation@example.invalid");
        await Run("config", "commit.gpgSign", "false");
        await Run("config", "core.hooksPath", Path.Combine(_root, "empty-hooks"));
        await File.WriteAllTextAsync(Path.Combine(_root, "README.md"), "history fixture\n");
        await Run("add", "README.md");
        await Run("commit", "-m", "Initial history");
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        await File.WriteAllTextAsync(Path.Combine(_root, "src", "large.txt"), new string('A', 300 * 1024));
        await File.WriteAllTextAsync(Path.Combine(_root, "src", "small.txt"), "bounded detail\n");
        await Run("add", "src");
        await Run("commit", "-m", "Add bounded detail");
        var service = new ProjectGitService(_git);

        var history = await service.GetLogAsync(_root, 1000);
        var latest = history[0];
        Assert.Equal("Add bounded detail", latest.Message);
        var detail = await service.GetCommitDetailAsync(_root, latest.Hash);

        Assert.Equal(2, history.Count);
        Assert.Contains("src/large.txt", detail.ChangedFiles);
        Assert.Contains("src/small.txt", detail.ChangedFiles);
        Assert.True(detail.DiffTruncated);
        Assert.EndsWith("[Commit diff truncated at 256K characters]", detail.Diff, StringComparison.Ordinal);
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetCommitDetailAsync(_root, "--all"));
        Assert.False((await service.GetStatusAsync(_root)).IsDirty);
    }

    [Fact]
    public async Task UnbornHistoryIsEmptyButARealLogFailureRemainsUnavailable()
    {
        Directory.CreateDirectory(_root);
        await Run("init", "-b", "main");
        var service = new ProjectGitService(_git);
        Assert.Empty(await service.GetHistoryLogAsync(_root));

        var runner = new StubRunner(request => request.Arguments[0] == "rev-parse"
            ? new ProcessRunResult(0, new string('a', 40), "", "git", TimeSpan.Zero, false)
            : new ProcessRunResult(128, "", "corrupt history", "git", TimeSpan.Zero, false));
        var broken = new ProjectGitService(new GitClient(runner));
        await Assert.ThrowsAsync<InvalidOperationException>(() => broken.GetHistoryLogAsync(_root));
        Assert.Equal(2, runner.Requests.Count);
    }

    [Fact]
    public async Task TruncatedFileOutputNeverPublishesAPartialPath()
    {
        var hash = new string('a', 40);
        var runner = new StubRunner(request => request.Arguments[0] switch
        {
            "rev-list" => new ProcessRunResult(0, hash, "", "git", TimeSpan.Zero, false),
            "diff-tree" => new ProcessRunResult(0, "complete.txt\0partial", "", "git", TimeSpan.Zero, false, standardOutputLimitExceeded: true),
            _ => new ProcessRunResult(0, "diff evidence", "", "git", TimeSpan.Zero, false)
        });
        var service = new ProjectGitService(new GitClient(runner));

        var detail = await service.GetCommitDetailAsync(Path.GetTempPath(), hash);

        Assert.Equal(["complete.txt"], detail.ChangedFiles);
        Assert.True(detail.ChangedFilesTruncated);
        Assert.Equal(16 * 1024, runner.Requests[0].MaxCapturedOutputCharacters);
        Assert.All(runner.Requests.Skip(1), request => Assert.Equal(256 * 1024, request.MaxCapturedOutputCharacters));
    }

    [Fact]
    public async Task HistoryContextDoesNotEnumerateUntrackedFilesAndBoundsCapture()
    {
        Directory.CreateDirectory(_root);
        await Run("init", "-b", "main");
        var runner = new StubRunner(_ => new ProcessRunResult(
            0, "# branch.oid (initial)\n# branch.head main\n", "", "git", TimeSpan.Zero, false,
            standardOutputLimitExceeded: true));
        var service = new ProjectGitService(new GitClient(runner));

        var context = await service.GetHistoryContextAsync(_root);

        Assert.True(context.IsGitRepository);
        Assert.Equal("main", context.Branch);
        var request = Assert.Single(runner.Requests);
        Assert.Contains("--untracked-files=no", request.Arguments);
        Assert.Equal(16 * 1024, request.MaxCapturedOutputCharacters);
    }

    [Fact]
    public async Task MergeCommitDetailUsesTheFirstParentComparison()
    {
        Directory.CreateDirectory(_root);
        await Run("init", "-b", "main");
        await Run("config", "user.name", "Studio validation");
        await Run("config", "user.email", "studio-validation@example.invalid");
        await Run("config", "commit.gpgSign", "false");
        await Run("config", "core.hooksPath", Path.Combine(_root, "empty-hooks"));
        await File.WriteAllTextAsync(Path.Combine(_root, "base.txt"), "base\n");
        await Run("add", "base.txt");
        await Run("commit", "-m", "Base");
        await Run("switch", "-c", "feature");
        await File.WriteAllTextAsync(Path.Combine(_root, "feature.txt"), "feature evidence\n");
        await Run("add", "feature.txt");
        await Run("commit", "-m", "Feature");
        await Run("switch", "main");
        await File.WriteAllTextAsync(Path.Combine(_root, "main.txt"), "main evidence\n");
        await Run("add", "main.txt");
        await Run("commit", "-m", "Main");
        await Run("merge", "--no-ff", "feature", "-m", "Merge feature");
        var service = new ProjectGitService(_git);
        var merge = Assert.Single((await service.GetHistoryLogAsync(_root, 1)));

        var detail = await service.GetCommitDetailAsync(_root, merge.Hash);

        Assert.Contains("feature.txt", detail.ChangedFiles);
        Assert.Contains("+feature evidence", detail.Diff, StringComparison.Ordinal);
        Assert.DoesNotContain("main.txt", detail.ChangedFiles);
        Assert.False((await service.GetStatusAsync(_root)).IsDirty);
    }

    private async Task Run(params string[] arguments)
    {
        var result = await _git.RunRawAsync(_root, arguments);
        Assert.True(result.Succeeded, result.StdErr);
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

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        foreach (var file in new DirectoryInfo(_root).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
            if (file.IsReadOnly) file.IsReadOnly = false;
        Directory.Delete(_root, recursive: true);
    }
}
