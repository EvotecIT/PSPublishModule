using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PowerForgeStudio.Avalonia.ViewModels;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Workspace;
using PowerForgeStudio.Orchestrator.Catalog;
using PowerForgeStudio.Orchestrator.Workspace;

namespace PowerForgeStudio.Avalonia.Tests;

public sealed class ChangedProjectFilterTests
{
    [Fact]
    public async Task ChangedFilterCannotReadPreviousCatalogDuringWorkspaceSwitch()
    {
        var parent = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-changed-switch-" + Guid.NewGuid().ToString("N"))).FullName;
        var oldRoot = Directory.CreateDirectory(Path.Combine(parent, "Old")).FullName;
        var newRoot = Directory.CreateDirectory(Path.Combine(parent, "New")).FullName;
        var oldProject = Directory.CreateDirectory(Path.Combine(oldRoot, "OldProject")).FullName;
        var newProject = Directory.CreateDirectory(Path.Combine(newRoot, "NewProject")).FullName;
        try
        {
            await TestAppBuilder.RunAsync(async () =>
            {
                var source = new SwitchingCatalogSource(oldRoot, Entry("OldProject", oldProject), Entry("NewProject", newProject));
                var changes = new ChangeSource();
                using var model = new WorkspaceViewModel(oldRoot, repositories: source, projectChanges: changes);
                await model.RefreshAsync();
                Assert.True(model.CanShowChangedProjects);

                model.WorkspaceRoot = newRoot;
                var switching = model.RefreshAsync();
                await source.NewWorkspaceStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.False(model.CanShowChangedProjects);
                await model.ShowChangedProjectsCommand.ExecuteAsync(null);
                Assert.Equal(0, changes.Calls);

                source.AllowNewWorkspace.TrySetResult();
                await switching;
                Assert.True(model.CanShowChangedProjects);
                changes.Next = Snapshot(newProject);
                await model.ShowChangedProjectsCommand.ExecuteAsync(null);
                Assert.Equal(1, changes.Calls);
                Assert.Equal(newProject, Assert.Single(changes.LastRepositories!).RootPath);
                Assert.Equal("NewProject", Assert.Single(model.Projects).Name);
                return true;
            });
        }
        finally { Directory.Delete(parent, recursive: true); }
    }

    [Fact]
    public async Task ChangedFilterKeepsEvidenceDuringRefreshAndSupportsCancellation()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-changed-filter-" + Guid.NewGuid().ToString("N"))).FullName;
        var first = Directory.CreateDirectory(Path.Combine(root, "First")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(root, "Second")).FullName;
        try
        {
            await TestAppBuilder.RunAsync(async () =>
            {
                var source = new CatalogSource([Entry("First", first), Entry("Second", second)]);
                var changes = new ChangeSource();
                using var model = new WorkspaceViewModel(root, repositories: source, projectChanges: changes);
                await model.RefreshAsync();
                Assert.Equal(2, model.Projects.Count);

                changes.DelayNext = true;
                var cancelled = model.ShowChangedProjectsCommand.ExecuteAsync(null);
                await changes.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.True(model.ChangedProjectsOnly);
                Assert.Equal(2, model.Projects.Count);
                model.CancelProjectChangeRefreshCommand.Execute(null);
                await cancelled;
                Assert.Contains("cancelled", model.ProjectChangeStatus, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(2, model.Projects.Count);

                changes.Next = Snapshot(first, second);
                await model.ShowChangedProjectsCommand.ExecuteAsync(null);
                Assert.Equal("First", Assert.Single(model.Projects).Name);
                Assert.Contains("Local Git was unavailable", model.ProjectChangeStatus);
                Dispatcher.UIThread.RunJobs();
                Assert.StartsWith("1 changed project, 1 changed working copy", model.ProjectChangeStatus);

                var window = new MainWindow { DataContext = model, Width = 1600, Height = 1000 };
                window.Show();
                try
                {
                    Capture(window, "changed-projects-wide.png");
                    window.Width = 1050; window.Height = 720;
                    Capture(window, "changed-projects-compact.png");

                    changes.DelayNext = true;
                    changes.Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    var refresh = model.ShowChangedProjectsCommand.ExecuteAsync(null);
                    await changes.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    Assert.Equal("First", Assert.Single(model.Projects).Name);
                    changes.Complete(Snapshot(second));
                    await refresh;
                    Assert.Equal("Second", Assert.Single(model.Projects).Name);
                }
                finally { window.Close(); }
                return true;
            });
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void Capture(MainWindow window, string name)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
        var output = Environment.GetEnvironmentVariable("POWERFORGE_STUDIO_VISUAL_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        frame.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default);
    }

    private static RepositoryCatalogEntry Entry(string name, string root) => new(
        name, root, ReleaseRepositoryKind.Unknown, ReleaseWorkspaceKind.PrimaryRepository,
        null, null, false, false);

    private static WorkspaceProjectChangeSnapshot Snapshot(string changed, string? unavailable = null) => new(
        DateTimeOffset.UtcNow,
        new Dictionary<string, IReadOnlyList<WorkspaceWorkingCopyChange>>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
        {
            [changed] = [new(changed, 2)]
        },
        unavailable is null ? [] : [unavailable]);

    private sealed class CatalogSource(IReadOnlyList<RepositoryCatalogEntry> entries) : IWorkspaceRepositorySource
    {
        public Task<IReadOnlyList<RepositoryCatalogEntry>> DiscoverAsync(string workspaceRoot, CancellationToken cancellationToken = default)
            => Task.FromResult(entries);
    }

    private sealed class SwitchingCatalogSource(string oldRoot, RepositoryCatalogEntry oldEntry, RepositoryCatalogEntry newEntry) : IWorkspaceRepositorySource
    {
        public TaskCompletionSource NewWorkspaceStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowNewWorkspace { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlyList<RepositoryCatalogEntry>> DiscoverAsync(string workspaceRoot, CancellationToken cancellationToken = default)
        {
            if (string.Equals(Path.GetFullPath(workspaceRoot), Path.GetFullPath(oldRoot), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                return [oldEntry];
            NewWorkspaceStarted.TrySetResult();
            await AllowNewWorkspace.Task.WaitAsync(cancellationToken);
            return [newEntry];
        }
    }

    private sealed class ChangeSource : IWorkspaceProjectChangeService
    {
        private TaskCompletionSource<WorkspaceProjectChangeSnapshot>? _pending;
        public bool DelayNext { get; set; }
        public TaskCompletionSource Started { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public WorkspaceProjectChangeSnapshot Next { get; set; } = new(DateTimeOffset.UtcNow,
            new Dictionary<string, IReadOnlyList<WorkspaceWorkingCopyChange>>(), []);
        public int Calls { get; private set; }
        public IReadOnlyList<RepositoryCatalogEntry>? LastRepositories { get; private set; }
        public Task<WorkspaceProjectChangeSnapshot> InspectAsync(IReadOnlyList<RepositoryCatalogEntry> repositories,
            IProgress<WorkspaceProjectChangeProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastRepositories = repositories;
            progress?.Report(new(0, repositories.Count, repositories[0].Name));
            if (!DelayNext) return Task.FromResult(Next);
            DelayNext = false;
            Started.TrySetResult();
            _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return _pending.Task.WaitAsync(cancellationToken);
        }
        public void Complete(WorkspaceProjectChangeSnapshot snapshot) => _pending!.TrySetResult(snapshot);
    }
}
