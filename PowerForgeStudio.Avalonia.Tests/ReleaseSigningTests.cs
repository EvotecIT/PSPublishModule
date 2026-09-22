using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PowerForgeStudio.Avalonia.ViewModels;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Workspace;
using PowerForgeStudio.Orchestrator.Workspace;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Avalonia.Tests;

public sealed class ReleaseSigningTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SigningCapturesRootBlocksBuildAndRetainsReceipts(bool cancel)
    {
        await TestAppBuilder.RunAsync(async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "StudioSigningFixture");
            var fake = new Signer();
            var release = new ReleaseViewModel(new Handoff(root), new ReleaseSigningWorkflow(fake));
            using var workspace = new WorkspaceViewModel(root, release: release);
            workspace.Build.HasSuccessfulInspection = true;
            release.SetBuild(new(root, true, "Built", 1, []), false, false);
            await release.PrepareAsync();
            Assert.True(release.CanSign);
            var signing = release.SignAsync();
            await fake.Started.Task;
            Assert.True(release.HasExecutionProgress);
            Assert.Equal(1, release.ProgressTotal);
            Assert.Equal(0, release.ProgressCompleted);
            Assert.Contains("Running", release.ProgressStatus);
            Assert.True(workspace.Build.IsReleaseRunning);
            Assert.False(workspace.Build.CanBuild);
            Assert.False(release.CanPrepare);
            Assert.False(release.CanSign);
            var activeWindow = new MainWindow { DataContext = workspace };
            activeWindow.Show(); activeWindow.Close(); Assert.True(activeWindow.IsVisible);
            Assert.Contains("still running", release.Status);
            Assert.Throws<InvalidOperationException>(() => release.SetBuild(null, true, false));
            workspace.ActiveWorkingCopyRoot = Path.Combine(root, "another-project");
            Assert.Equal(root, release.BuildRoot);
            Assert.Equal(Path.Combine(root, "another-project"), release.HistoryScopeRoot);
            if (cancel) release.CancelSigningCommand.Execute(null);
            fake.Finish.TrySetResult(); await signing;
            activeWindow.Close();
            Assert.False(workspace.Build.IsReleaseRunning);
            Assert.Single(release.Receipts);
            Assert.Equal(1, release.ProgressCompleted);
            Assert.Contains(cancel ? "Cancelled" : "Signed", release.ProgressStatus);
            Assert.Equal(root, release.SigningResult!.RootPath);
            Assert.Equal(cancel, release.RequiresRebuild);
            Assert.Equal(!cancel, release.SigningResult.Succeeded);
            Assert.False(release.CanSign); Assert.False(release.CanPrepare);
            Assert.Equal(cancel ? ReleaseQueueStage.Sign : ReleaseQueueStage.Publish, release.Handoff!.Session.Items.Single().Stage);
            workspace.ShowReleaseCommand.Execute(null);
            var window = new MainWindow { DataContext = workspace };
            try
            {
                window.Show();
                foreach (var compact in new[] { false, true })
                {
                    window.Width = compact ? 1050 : 1600; window.Height = compact ? 720 : 1000;
                    window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                    var output = Environment.GetEnvironmentVariable("POWERFORGE_STUDIO_VISUAL_OUTPUT");
                    if (!string.IsNullOrEmpty(output))
                    {
                        Directory.CreateDirectory(output);
                        frame.Save(Path.Combine(output, $"release-signing-{(cancel ? "cancelled" : "complete")}{(compact ? "-compact" : "")}.png"), PngBitmapEncoderOptions.Default);
                    }
                }
            }
            finally { window.Close(); }
            return true;
        });
    }

    [Fact]
    public async Task SigningStartedDuringCloseSaveKeepsWindowOpen()
    {
        var root = Path.Combine(Path.GetTempPath(), "studio-sign-close-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await TestAppBuilder.RunAsync(async () =>
            {
                var signer = new Signer(); var store = new WaitingStore();
                var release = new ReleaseViewModel(new Handoff(root), new ReleaseSigningWorkflow(signer));
                using var model = new WorkspaceViewModel(root, stateStore: store, release: release);
                await model.RefreshAsync();
                release.SetBuild(new(root, true, "Built", 1, []), false, false); await release.PrepareAsync();
                var window = new MainWindow { DataContext = model }; window.Show();
                Task? signing = null;
                try
                {
                    window.Close(); await store.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    signing = release.SignAsync(); await signer.Started.Task;
                    store.Finish.TrySetResult();
                    await store.Returned.Task;
                    for (var attempt = 0; attempt < 100 && !release.Status.Contains("still running") && window.IsVisible; attempt++)
                        await Task.Delay(10);
                    Assert.True(window.IsVisible); Assert.Contains("still running", release.Status);
                    signer.Finish.TrySetResult(); await signing;
                    Assert.Single(release.Receipts);
                    var file = Path.Combine(root, "notes.txt"); await File.WriteAllTextAsync(file, "Original");
                    await model.SelectAsync(new ExplorerNode("notes.txt", file, "file", root));
                    await model.EditDocumentCommand.ExecuteAsync(null);
                    model.ActiveDocument!.Text = "Unsaved after blocked close";
                    Assert.True(model.ActiveDocument.IsDirty);
                    var asked = false;
                    model.ResolveUnsavedChanges = _ => { asked = true; return Task.FromResult(UnsavedChangesChoice.Cancel); };
                    window.Close(); Assert.True(asked); Assert.True(window.IsVisible);
                    Assert.True(model.ActiveDocument.IsDirty);
                    model.ResolveUnsavedChanges = _ => Task.FromResult(UnsavedChangesChoice.Discard);
                    window.Close();
                }
                finally
                {
                    store.Finish.TrySetResult(); signer.Finish.TrySetResult();
                    if (signing is not null) await signing;
                    model.ResolveUnsavedChanges = _ => Task.FromResult(UnsavedChangesChoice.Discard);
                    window.Close();
                }
                return true;
            });
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class WaitingStore : IWorkspaceExplorerStateStore
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Returned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public WorkspaceExplorerState LoadExplorer(string root) => new(root, [], [], null, []);
        public WorkspaceExplorerState SetFavorite(string root, string project, bool favorite) => LoadExplorer(root);
        public WorkspaceExplorerState SaveSession(string root, IReadOnlyList<WorkspaceDocumentReference> docs, WorkspaceDocumentReference? active, IReadOnlyList<string> expanded)
        {
            Started.TrySetResult(); Finish.Task.Wait(TimeSpan.FromSeconds(10)); Returned.TrySetResult(); return LoadExplorer(root);
        }
    }

    private sealed class Handoff(string root) : IReleaseBuildHandoffService
    {
        public Task<ReleaseBuildHandoff> PrepareAsync(ReleaseBuildExecutionResult build, CancellationToken token = default)
        {
            var item = new ReleaseQueueItem(root, "StudioSigningFixture", default, default, 1,
                ReleaseQueueStage.Sign, ReleaseQueueItemStatus.WaitingApproval, "Built", "sign.waiting.usb", "{}", DateTimeOffset.UtcNow);
            return Task.FromResult(new ReleaseBuildHandoff(ReleaseQueueSessionFactory.Create(root, [item], DateTimeOffset.UtcNow), [new("StudioSigningFixture", "Project", Path.Combine(root, "artifacts", "StudioSigningFixture.1.0.0.nupkg"), "NuGet")]));
        }
    }

    private sealed class Signer : IReleaseSigningExecutionService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ReleaseSigningExecutionResult> ExecuteAsync(ReleaseQueueItem item, CancellationToken token = default)
            => ExecuteAsync(item, token, null);

        public async Task<ReleaseSigningExecutionResult> ExecuteAsync(ReleaseQueueItem item, CancellationToken token,
            IReleaseArtifactProgressSink? progress)
        {
            var artifactPath = Path.Combine(item.RootPath, "artifacts", "StudioSigningFixture.1.0.0.nupkg");
            if (progress is not null)
                await progress.ReportAsync(new(ReleaseQueueStage.Sign, "StudioSigningFixture.1.0.0.nupkg", artifactPath,
                    "Running", 0, 1, "Signing package.", DateTimeOffset.UtcNow), CancellationToken.None);
            Started.TrySetResult(); await Finish.Task;
            if (progress is not null)
                await progress.ReportAsync(new(ReleaseQueueStage.Sign, "StudioSigningFixture.1.0.0.nupkg", artifactPath,
                    token.IsCancellationRequested ? "Cancelled" : "Signed", 1, 1,
                    token.IsCancellationRequested ? "Signing was cancelled." : "Package signed.", DateTimeOffset.UtcNow), CancellationToken.None);
            return new(item.RootPath, true, "Signed 1 artifact; publication has not run.", item.CheckpointStateJson,
                [new(item.RootPath, item.RepositoryName, "Project", artifactPath, "NuGet", ReleaseSigningReceiptStatus.Signed,
                    "Package signed using configured certificate.", DateTimeOffset.UtcNow)]);
        }
    }
}
