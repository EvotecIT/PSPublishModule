using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PowerForge;
using PowerForgeStudio.Avalonia.ViewModels;
using PowerForgeStudio.Avalonia.Views;
using PowerForgeStudio.Domain.Workspace;
using PowerForgeStudio.Orchestrator.Workspace;

namespace PowerForgeStudio.Avalonia.Tests;

public sealed class WorkspaceStorageTests
{
    [Fact]
    public async Task InspectionWarningIsRedactedBeforeShowingSelectedWorkingCopy()
    {
        var root = Path.Combine(Path.GetTempPath(), "studio-storage-warning-" + Guid.NewGuid().ToString("N"));
        var entry = Entry("Sample", Path.Combine(root, "worktree"), Path.Combine(root, "Sample"),
            "feature/test", false, 10, "Inspection failed", 0, "Not checked", "Inspect", false)
            with { Warning = "Could not inspect --Token=fixture-secret" };
        using var model = new StorageViewModel(new FakeStorageInspectionService([entry]));
        model.SetWorkspace(root);

        await model.RefreshAsync();

        Assert.Contains("<redacted>", Assert.Single(model.Entries).Warning);
        Assert.DoesNotContain("fixture-secret", Assert.Single(model.Entries).Warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LongStorageScanShowsProgressAndCanBeCancelled()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
            "studio-storage-progress-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            await TestAppBuilder.RunAsync(async () =>
            {
                var source = new BlockingProgressInspectionService();
                using var model = new StorageViewModel(source);
                model.SetWorkspace(root);
                var window = new Window { Content = new StorageView { DataContext = model }, Width = 1000, Height = 700 };
                window.Show();
                var scan = model.RefreshAsync();
                await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                for (var attempt = 0; attempt < 30 && model.ScanProgressPercent < 50; attempt++)
                {
                    Dispatcher.UIThread.RunJobs();
                    await Task.Delay(10);
                }
                Assert.Equal(50, model.ScanProgressPercent);
                Assert.Contains("1/2 repositories", model.Status);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using (var frame = window.CaptureRenderedFrame())
                {
                    Assert.NotNull(frame);
                    var output = Environment.GetEnvironmentVariable("POWERFORGE_STUDIO_VISUAL_OUTPUT");
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        Directory.CreateDirectory(output);
                        frame.Save(Path.Combine(output, "storage-progress.png"), PngBitmapEncoderOptions.Default);
                    }
                }

                model.CancelRefreshCommand.Execute(null);
                await scan;
                Assert.False(model.IsLoading);
                Assert.Equal("Storage inspection cancelled.", model.Status);
                Assert.Equal("Inspection cancelled. Refresh inspection to retry.", model.EmptyMessage);
                window.Close();
                return true;
            });
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task LateRemovalReviewIsDiscardedAfterSelectionChanges()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-storage-late-remove-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var primary = Directory.CreateDirectory(Path.Combine(root, "Product")).FullName;
            var first = Directory.CreateDirectory(Path.Combine(root, "_worktrees", "first")).FullName;
            var second = Directory.CreateDirectory(Path.Combine(root, "_worktrees", "second")).FullName;
            var entries = new[]
            {
                Entry("Product", first, primary, "feature/first", false, 10, "Clean", 0, "Locally merged", "Review removal evidence", true),
                Entry("Product", second, primary, "feature/second", false, 10, "Clean", 0, "Locally merged", "Review removal evidence", true)
            };
            var pending = new PendingStorageRemovalService();
            using var model = new StorageViewModel(new FakeStorageInspectionService(entries), pending);
            model.SetWorkspace(root);
            await model.RefreshAsync();

            var reviewTask = model.ReviewSelectedRemovalAsync();
            await pending.RemovalEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            model.SelectedEntry = entries[1];
            pending.CompleteRemoval(RemovalReview(root, entries[0]));

            Assert.False(await reviewTask);
            Assert.Null(model.RemovalReview);
            Assert.Equal(entries[1], model.SelectedEntry);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LatePruneReviewIsDiscardedAfterWorkspaceChanges()
    {
        var fixture = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-storage-late-prune-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var firstRoot = Directory.CreateDirectory(Path.Combine(fixture, "First")).FullName;
            var secondRoot = Directory.CreateDirectory(Path.Combine(fixture, "Second")).FullName;
            var primary = Directory.CreateDirectory(Path.Combine(firstRoot, "Product")).FullName;
            var broken = Entry("Product", Path.Combine(firstRoot, "_worktrees", "missing"), primary, "feature/missing",
                false, 0, "Broken reference", 0, "Not checked", "Review stale registration", false, exists: false);
            var pending = new PendingStorageRemovalService();
            using var model = new StorageViewModel(new FakeStorageInspectionService([broken]), pending);
            model.SetWorkspace(firstRoot);
            await model.RefreshAsync();

            var reviewTask = model.ReviewSelectedPruneAsync();
            await pending.PruneEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            model.SetWorkspace(secondRoot);
            pending.CompletePrune(PruneReview(firstRoot, broken));

            Assert.False(await reviewTask);
            Assert.Null(model.PruneReview);
            Assert.Equal(Path.GetFullPath(secondRoot), model.WorkspaceRoot);
            Assert.False(model.CanPruneReviewed);
        }
        finally
        {
            Directory.Delete(fixture, recursive: true);
        }
    }

    [Fact]
    public async Task FailedEvidenceRefreshInvalidatesPriorRemovalReview()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-storage-review-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var primary = Directory.CreateDirectory(Path.Combine(root, "Product")).FullName;
            var worktree = Directory.CreateDirectory(Path.Combine(root, "_worktrees", "feature")).FullName;
            var entry = Entry("Product", worktree, primary, "feature", false, 10, "Clean", 0,
                "Locally merged", "Review removal evidence", true);
            var removal = new FakeStorageRemovalService();
            using var model = new StorageViewModel(new FakeStorageInspectionService([entry]), removal);
            model.SetWorkspace(root);
            await model.RefreshAsync();

            Assert.True(await model.ReviewSelectedRemovalAsync());
            model.ConfirmNoExternalUse = true;
            model.ConfirmRetainedArtifacts = true;
            Assert.True(model.CanRemoveReviewed);

            removal.FailReview = true;
            Assert.False(await model.ReviewSelectedRemovalAsync());

            Assert.Null(model.RemovalReview);
            Assert.False(model.CanRemoveReviewed);
            Assert.Equal("Removal review failed.", model.Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task StorageRouteRendersMeasuredEvidenceAndFiltersCandidates()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "studio-storage-ui-" + Guid.NewGuid().ToString("N"));
        var root = Directory.CreateDirectory(Path.Combine(fixture, "Workspace")).FullName;
        var repository = Directory.CreateDirectory(Path.Combine(root, "OfficeIMO")).FullName;
        var worktree = Directory.CreateDirectory(Path.Combine(root, "_worktrees", "officeimo-pdf")).FullName;
        var changed = Directory.CreateDirectory(Path.Combine(root, "_worktrees", "officeimo-html")).FullName;
        try
        {
            Assert.True((await new GitClient().RunRawAsync(repository, ["init", "-b", "main"])).Succeeded);
            var entries = new[]
            {
                Entry("OfficeIMO", repository, repository, "main", isPrimary: true, 240L * 1024 * 1024, "Clean", 0, "Not checked", "Retain primary checkout", false),
                Entry("OfficeIMO", worktree, repository, "pdf-authoring-model", false, 120L * 1024 * 1024, "Clean", 0, "Locally merged", "Refresh remote / PR and active-use evidence", true),
                Entry("OfficeIMO", changed, repository, "html-engine", false, 80L * 1024 * 1024, "Changed", 2, "Not merged", "Preserve or commit local changes", false),
                Entry("PowerForge", Path.Combine(root, "_worktrees", "missing-release"), repository, "release-3.0.143", false, 0, "Broken reference", 0, "Not checked", "Repair or prune the Git worktree reference", false, exists: false)
            };
            await TestAppBuilder.RunAsync(async () =>
            {
                using var model = new WorkspaceViewModel(
                    root,
                    storage: new FakeStorageInspectionService(entries),
                    storageRemoval: new FakeStorageRemovalService());
                await model.RefreshAsync();
                await model.ShowStorageCommand.ExecuteAsync(null);
                Assert.True(model.IsStoragePage);
                Assert.Equal(4, model.Storage.Entries.Count);
                Assert.Equal(1, model.Storage.CandidateCount);
                model.Storage.ShowCandidatesCommand.Execute(null);
                Assert.True(Assert.Single(model.Storage.Entries).IsReviewCandidate);
                model.Storage.ShowAllCommand.Execute(null);

                var window = new MainWindow { DataContext = model, Width = 1600, Height = 1000 };
                window.Show();
                try
                {
                    Capture(window, "storage-review.png");
                    var pathExpander = window.FindControl<Expander>("StoragePathExpander");
                    Assert.NotNull(pathExpander);
                    pathExpander.IsExpanded = true;
                    var pathBox = Assert.IsType<TextBox>(pathExpander.Content);
                    Assert.Equal(model.Storage.SelectedEntry?.Path, pathBox.Text);
                    Capture(window, "storage-inspector-path.png");
                    pathExpander.IsExpanded = false;
                    model.Storage.SelectedEntry = Assert.Single(model.Storage.Entries, entry => entry.IsReviewCandidate);
                    Assert.True(await model.Storage.ReviewSelectedRemovalAsync());
                    var removalDialog = new WorktreeRemovalDialog { DataContext = model.Storage, Width = 760, Height = 680 };
                    var removalShown = removalDialog.ShowDialog(window);
                    Capture(removalDialog, "worktree-removal-review.png");
                    removalDialog.Width = 620;
                    removalDialog.Height = 520;
                    Capture(removalDialog, "worktree-removal-review-compact.png");
                    var reviewScroller = removalDialog.FindControl<ScrollViewer>("ReviewScroller");
                    Assert.NotNull(reviewScroller);
                    reviewScroller.Offset = new global::Avalonia.Vector(0, reviewScroller.Extent.Height);
                    Capture(removalDialog, "worktree-removal-review-compact-bottom.png");
                    model.Storage.ConfirmNoExternalUse = true;
                    model.Storage.ConfirmRetainedArtifacts = true;
                    Assert.True(model.Storage.CanRemoveReviewed);
                    removalDialog.Close();
                    await removalShown;
                    model.Storage.SelectedEntry = Assert.Single(model.Storage.Entries, entry => entry.IsBroken);
                    Assert.True(await model.Storage.ReviewSelectedPruneAsync());
                    var pruneDialog = new WorktreePruneDialog { DataContext = model.Storage, Width = 760, Height = 650 };
                    var pruneShown = pruneDialog.ShowDialog(window);
                    Capture(pruneDialog, "worktree-prune-review.png");
                    pruneDialog.Width = 620;
                    pruneDialog.Height = 500;
                    Capture(pruneDialog, "worktree-prune-review-compact.png");
                    model.Storage.ConfirmPrunableRegistrations = true;
                    Assert.True(model.Storage.CanPruneReviewed);
                    pruneDialog.Close();
                    await pruneShown;
                    window.Width = 1050;
                    window.Height = 720;
                    Capture(window, "storage-review-compact.png");
                    var storageScroller = Assert.Single(window.GetVisualDescendants().OfType<ScrollViewer>(), scroll => scroll.Name == "StorageScroller");
                    storageScroller.Offset = new global::Avalonia.Vector(0, storageScroller.Extent.Height);
                    Capture(window, "storage-review-compact-bottom.png");
                    Assert.True(storageScroller.Offset.Y > 0);
                }
                finally
                {
                    window.Close();
                }
                return true;
            });
        }
        finally
        {
            if (Directory.Exists(fixture))
                Directory.Delete(fixture, recursive: true);
        }
    }

    private static WorkspaceStorageEntry Entry(
        string repository,
        string path,
        string primary,
        string branch,
        bool isPrimary,
        long bytes,
        string state,
        int changes,
        string ancestry,
        string next,
        bool candidate,
        bool exists = true)
        => new(repository, path, primary, branch, isPrimary, exists, false, bytes, 12, changes, state,
            "main", ancestry, next, candidate, null);

    private static WorkspaceStorageRemovalReview RemovalReview(string root, WorkspaceStorageEntry entry)
        => new(Path.GetFullPath(root), entry.PrimaryPath, entry.Path,
            new string('a', 40), "refs/heads/main", new string('b', 40),
            true, true, true, true, true, null, null, false, true,
            new WorkspaceExternalUseEvidence(true, 2, [], "Manual confirmation is still required."),
            [], [], new string('c', 64), DateTimeOffset.UtcNow);

    private static WorkspaceStoragePruneReview PruneReview(string root, WorkspaceStorageEntry entry)
        => new(Path.GetFullPath(root), entry.PrimaryPath, entry.Path,
            true, true, true, true, true,
            [PrunableRegistration(entry)],
            [entry.PrimaryPath], [], new string('d', 64), new string('e', 64), DateTimeOffset.UtcNow);

    private static WorkspacePrunableRegistration PrunableRegistration(WorkspaceStorageEntry entry)
    {
        var administrative = Path.Combine(entry.PrimaryPath, ".git", "worktrees", "feature");
        return new(entry.Path, "administrative files are missing", "feature", administrative,
            Path.Combine(entry.Path, ".git"));
    }

    private static void Capture(MainWindow window, string fileName)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var output = Environment.GetEnvironmentVariable("POWERFORGE_STUDIO_VISUAL_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            frame.Save(Path.Combine(output, fileName), PngBitmapEncoderOptions.Default);
        }
    }

    private static void Capture(Window window, string fileName)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var output = Environment.GetEnvironmentVariable("POWERFORGE_STUDIO_VISUAL_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            frame.Save(Path.Combine(output, fileName), PngBitmapEncoderOptions.Default);
        }
    }

    private sealed class FakeStorageInspectionService(IReadOnlyList<WorkspaceStorageEntry> entries) : IWorkspaceStorageInspectionService
    {
        public Task<WorkspaceStorageSnapshot> InspectAsync(string workspaceRoot, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new WorkspaceStorageSnapshot(
                Path.GetFullPath(workspaceRoot),
                DateTimeOffset.UtcNow,
                entries.Sum(static entry => entry.SizeBytes),
                entries.Where(static entry => !entry.IsPrimary).Sum(static entry => entry.SizeBytes),
                entries.Count(static entry => entry.IsReviewCandidate),
                entries));
        }
    }

    private sealed class BlockingProgressInspectionService : IWorkspaceStorageInspectionService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<WorkspaceStorageSnapshot> InspectAsync(string workspaceRoot, CancellationToken cancellationToken = default)
            => InspectAsync(workspaceRoot, null, cancellationToken);

        public async Task<WorkspaceStorageSnapshot> InspectAsync(string workspaceRoot,
            IProgress<WorkspaceStorageScanProgress>? progress, CancellationToken cancellationToken = default)
        {
            progress?.Report(new(1, 2, workspaceRoot, 1024, 1));
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable");
        }
    }

    private sealed class FakeStorageRemovalService : IWorkspaceStorageRemovalService
    {
        public bool FailReview { get; set; }

        public Task<WorkspaceStorageRemovalReview> ReviewAsync(
            string workspaceRoot,
            WorkspaceStorageEntry entry,
            IReadOnlyCollection<string> protectedWorkingCopies,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailReview)
                return Task.FromException<WorkspaceStorageRemovalReview>(new IOException("review unavailable"));
            return Task.FromResult(new WorkspaceStorageRemovalReview(
                Path.GetFullPath(workspaceRoot), entry.PrimaryPath, entry.Path,
                new string('a', 40), "refs/heads/main", new string('b', 40),
                true, true, true, true, true, null, null, false, true,
                new WorkspaceExternalUseEvidence(true, 2, [], "Manual confirmation is still required."),
                [Path.Combine(entry.Path, "bin")], [], new string('c', 64), DateTimeOffset.UtcNow));
        }

        public Task RemoveAsync(
            WorkspaceStorageRemovalReview reviewed,
            IReadOnlyCollection<string> protectedWorkingCopies,
            bool confirmNoExternalUse,
            bool confirmRetainedArtifacts,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<WorkspaceStoragePruneReview> ReviewPruneAsync(
            string workspaceRoot,
            WorkspaceStorageEntry entry,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkspaceStoragePruneReview(
                Path.GetFullPath(workspaceRoot), entry.PrimaryPath, entry.Path,
                true, true, true, true, true,
                [PrunableRegistration(entry)],
                [entry.PrimaryPath], [], new string('d', 64), new string('e', 64), DateTimeOffset.UtcNow));

        public Task PruneAsync(
            WorkspaceStoragePruneReview reviewed,
            bool confirmRegistrations,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class PendingStorageRemovalService : IWorkspaceStorageRemovalService
    {
        private readonly TaskCompletionSource<WorkspaceStorageRemovalReview> _removal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<WorkspaceStoragePruneReview> _prune = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RemovalEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource PruneEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<WorkspaceStorageRemovalReview> ReviewAsync(string workspaceRoot, WorkspaceStorageEntry entry,
            IReadOnlyCollection<string> protectedWorkingCopies, CancellationToken cancellationToken = default)
        {
            RemovalEntered.TrySetResult();
            return await _removal.Task.WaitAsync(cancellationToken);
        }

        public Task RemoveAsync(WorkspaceStorageRemovalReview reviewed, IReadOnlyCollection<string> protectedWorkingCopies,
            bool confirmNoExternalUse, bool confirmRetainedArtifacts, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public async Task<WorkspaceStoragePruneReview> ReviewPruneAsync(string workspaceRoot, WorkspaceStorageEntry entry,
            CancellationToken cancellationToken = default)
        {
            PruneEntered.TrySetResult();
            return await _prune.Task.WaitAsync(cancellationToken);
        }

        public Task PruneAsync(WorkspaceStoragePruneReview reviewed, bool confirmRegistrations,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void CompleteRemoval(WorkspaceStorageRemovalReview review) => _removal.TrySetResult(review);
        public void CompletePrune(WorkspaceStoragePruneReview review) => _prune.TrySetResult(review);
    }
}
