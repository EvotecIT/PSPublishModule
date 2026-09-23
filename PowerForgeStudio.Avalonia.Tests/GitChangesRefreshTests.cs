using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PowerForge;
using PowerForgeStudio.Avalonia.ViewModels;
using PowerForgeStudio.Avalonia.Views;
using PowerForgeStudio.Orchestrator.Hub;

namespace PowerForgeStudio.Avalonia.Tests;

public sealed class GitChangesRefreshTests
{
    [Fact]
    public async Task FailedRefreshKeepsDatedEvidenceButDisablesGitActionsUntilRetry()
    {
        var root = Path.Combine(Path.GetTempPath(), "studio-git-refresh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var runner = new PausedStatusRunner();
        try
        {
            var initialized = await new GitClient().RunRawAsync(root, ["init", "-b", "main"]);
            Assert.True(initialized.Succeeded, initialized.StdErr);
            var file = Path.Combine(root, "notes.txt");
            await File.WriteAllTextAsync(file, "first observation");
            var otherFile = Path.Combine(root, "other.txt");
            await File.WriteAllTextAsync(otherFile, "other first observation");

            await TestAppBuilder.RunAsync(async () =>
            {
                using var changes = new GitChangesViewModel(new ProjectGitService(new GitClient(runner)));
                changes.SetWorkingCopy(root);
                await changes.RefreshAsync();
                changes.Selected = Assert.Single(changes.Files, row => row.Path == "notes.txt");
                var snapshot = Assert.IsType<PowerForgeStudio.Domain.Hub.ProjectGitStatus>(changes.Snapshot);
                Assert.True(changes.CanStage);

                runner.PauseNext(fail: true);
                var refresh = changes.RefreshAsync();
                await runner.Entered.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.True(changes.IsLoading);
                Assert.Same(snapshot, changes.Snapshot);
                Assert.Equal(2, changes.Files.Count);
                Assert.Equal("notes.txt", changes.Selected?.Path);
                Assert.Contains("showing the observation", changes.Status);
                Assert.False(changes.CanStage);

                runner.Release();
                await refresh;
                Assert.False(changes.IsLoading);
                Assert.True(changes.IsStale);
                Assert.Same(snapshot, changes.Snapshot);
                Assert.Equal("notes.txt", changes.Selected?.Path);
                Assert.Contains("Git refresh failed; showing the observation", changes.Status);
                Assert.False(changes.CanStage);

                var window = new Window { Content = new GitChangesView { DataContext = changes }, Width = 1050, Height = 720 };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), block =>
                        block.IsEffectivelyVisible && block.Text?.Contains("Git refresh failed", StringComparison.Ordinal) == true);
                    Assert.False(Assert.Single(window.GetVisualDescendants().OfType<Button>(), button =>
                        Equals(button.Content, "Stage selected")).IsEnabled);
                    var output = Environment.GetEnvironmentVariable("POWERFORGE_STUDIO_VISUAL_OUTPUT");
                    if (!string.IsNullOrEmpty(output))
                    {
                        Directory.CreateDirectory(output);
                        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                        using var frame = window.CaptureRenderedFrame();
                        Assert.NotNull(frame);
                        frame.Save(Path.Combine(output, "git-stale-refresh.png"), PngBitmapEncoderOptions.Default);
                    }
                }
                finally { window.Close(); }

                await File.WriteAllTextAsync(otherFile, "second observation");
                runner.PauseNext(fail: false);
                var retry = changes.RefreshAsync();
                await runner.Entered.WaitAsync(TimeSpan.FromSeconds(10));
                changes.Selected = Assert.Single(changes.Files, row => row.Path == "other.txt");
                runner.Release();
                await retry;
                Assert.False(changes.IsStale);
                Assert.True(changes.CanStage);
                Assert.Equal("other.txt", changes.Selected?.Path);
                for (var attempt = 0; attempt < 100 && !changes.Diff.Contains("second observation", StringComparison.Ordinal); attempt++)
                    await Task.Delay(20);
                Assert.Contains("second observation", changes.Diff);
                changes.SetWorkingCopy("");
                Assert.Null(changes.Snapshot);
                Assert.Empty(changes.Files);
                return true;
            });
        }
        finally
        {
            runner.Release();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class PausedStatusRunner : IProcessRunner
    {
        private bool _pauseNextStatus;
        private bool _failPausedStatus;
        private TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource _continue = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Entered => _entered.Task;

        public void PauseNext(bool fail)
        {
            _failPausedStatus = fail;
            _pauseNextStatus = true;
            _entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _continue = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void Release() => _continue.TrySetResult();

        public async Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            if (_pauseNextStatus && request.Arguments.FirstOrDefault() == "status")
            {
                _pauseNextStatus = false;
                var shouldFail = _failPausedStatus;
                var continueTask = _continue.Task;
                _entered.TrySetResult();
                await continueTask.WaitAsync(cancellationToken);
                if (shouldFail) throw new InvalidOperationException("Simulated Git read failure.");
            }
            return await new ProcessRunner().RunAsync(request, cancellationToken);
        }
    }
}
