using PowerForge;
using PowerForgeStudio.Avalonia.ViewModels;
using PowerForgeStudio.Orchestrator.Hub;

namespace PowerForgeStudio.Avalonia.Tests;

public sealed class GitChangesNavigationTests
{
    [Fact]
    public async Task SwitchingProjectsDuringCommitRetainsOriginalTargetAndSeparateDrafts()
    {
        var root = Path.Combine(Path.GetTempPath(), "studio-git-navigation-" + Guid.NewGuid().ToString("N"));
        var first = Path.Combine(root, "first");
        var second = Path.Combine(root, "second");
        var runner = new DelayedCommitRunner();
        var service = new ProjectGitService(new GitClient(runner));
        try
        {
            foreach (var path in new[] { first, second })
            {
                Directory.CreateDirectory(path);
                async Task Run(params string[] args)
                {
                    var result = await new GitClient().RunRawAsync(path, args);
                    Assert.True(result.Succeeded, result.StdErr);
                }
                await Run("init", "-b", "main");
                await Run("config", "user.name", "Studio validation");
                await Run("config", "user.email", "studio-validation@example.invalid");
                await Run("config", "commit.gpgSign", "false");
                await Run("config", "core.hooksPath", Path.Combine(path, "empty-hooks"));
                await File.WriteAllTextAsync(Path.Combine(path, "file.txt"), path);
                await Run("add", "file.txt");
            }
            await TestAppBuilder.RunAsync(async () =>
            {
                using var changes = new GitChangesViewModel(service);
                changes.SetWorkingCopy(first);
                await changes.RefreshAsync();
                changes.CommitMessage = "First project commit";
                var committing = changes.CommitCommand.ExecuteAsync(null);
                await runner.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                changes.SetWorkingCopy(second);
                await changes.RefreshAsync();
                changes.CommitMessage = "Second project draft";
                Assert.False(changes.CanCommit);
                runner.Continue.TrySetResult();
                await committing;
                Assert.Equal(second, changes.Root);
                Assert.Equal("Second project draft", changes.CommitMessage);
                Assert.Equal(1, changes.Snapshot!.StagedCount);
                Assert.Equal(first, runner.CommitRoot);
                Assert.Equal("First project commit", Assert.Single(await service.GetLogAsync(first)).Message);
                changes.SetWorkingCopy(first);
                await changes.RefreshAsync();
                Assert.Empty(changes.CommitMessage);
                Assert.False(changes.Snapshot!.IsDirty);
                changes.SetWorkingCopy(second);
                await changes.RefreshAsync();
                Assert.Equal("Second project draft", changes.CommitMessage);
                return true;
            });
        }
        finally
        {
            runner.Continue.TrySetResult();
            if (Directory.Exists(root))
            {
                foreach (var file in new DirectoryInfo(root).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
                    if (file.IsReadOnly) file.IsReadOnly = false;
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class DelayedCommitRunner : IProcessRunner
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? CommitRoot { get; private set; }
        public async Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            if (request.Arguments[0] == "commit")
            {
                CommitRoot = request.WorkingDirectory;
                Entered.TrySetResult();
                await Continue.Task.WaitAsync(cancellationToken);
            }
            return await new ProcessRunner().RunAsync(request, cancellationToken);
        }
    }
}
