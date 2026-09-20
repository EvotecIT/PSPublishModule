using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PowerForge;
using PowerForgeStudio.Avalonia.ViewModels;
using PowerForgeStudio.Avalonia.Views;
using PowerForgeStudio.Domain.Activity;
using PowerForgeStudio.Orchestrator.Activity;

namespace PowerForgeStudio.Avalonia.Tests;

public sealed class WorkspaceActivityTests
{
    [Fact]
    public void OpenSource_AllowsWorkspacePathsAndGitHubButRejectsOtherLocalPaths()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-activity-open-" + Guid.NewGuid().ToString("N"))).FullName;
        var outside = Path.GetTempFileName();
        try
        {
            using var model = new ActivityViewModel(new FakeActivityInventory());
            model.SetWorkspace(root);
            model.SelectedEntry = OpenEntry("inside", root);
            Assert.True(model.CanOpenSelected);
            model.SelectedEntry = OpenEntry("github", "https://github.com/EvotecIT/PSPublishModule/issues/1");
            Assert.True(model.CanOpenSelected);
            model.SelectedEntry = OpenEntry("outside", outside);
            Assert.False(model.CanOpenSelected);
            model.SelectedEntry = OpenEntry("other-host", "https://example.com/item");
            Assert.False(model.CanOpenSelected);
        }
        finally
        {
            File.Delete(outside);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WorkspaceChangeCancelsInspectionAndAllowsImmediateRefresh()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-activity-switch-" + Guid.NewGuid().ToString("N"))).FullName;
        var other = Directory.CreateDirectory(Path.Combine(root, "Other")).FullName;
        try
        {
            var inventory = new ControlledActivityInventory();
            using var model = new ActivityViewModel(inventory);
            model.SetWorkspace(root);
            var first = model.RefreshAsync();
            await inventory.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            model.SetWorkspace(other);
            Assert.False(model.IsLoading);
            await model.RefreshAsync();
            await first;

            Assert.Equal(2, inventory.Calls);
            Assert.Contains("Observed 0 activity signals", model.Status, StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ActivityRouteShowsOwnerEvidenceAndSessionScopedMute()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-activity-ui-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var repository = Directory.CreateDirectory(Path.Combine(root, "PowerForge")).FullName;
            Assert.True((await new GitClient().RunRawAsync(repository, ["init", "-b", "main"])).Succeeded);
            await TestAppBuilder.RunAsync(async () =>
            {
                var inventory = new FakeActivityInventory();
                using var model = new WorkspaceViewModel(root, activity: inventory);
                await model.RefreshAsync();
                await model.ShowActivityCommand.ExecuteAsync(null);
                Assert.True(model.IsActivityPage);
                Assert.True(model.IsWorkspaceUtilityPage);
                Assert.Equal(5, model.Activity.Entries.Count);
                Assert.Equal(5, model.Activity.ActionableCount);
                Assert.Equal(2, model.Activity.CriticalCount);
                Assert.Equal(2, model.Activity.ReviewCount);
                Assert.Equal(1, model.Activity.UnavailableSourceCount);
                Assert.True(model.Activity.IsTruncated);
                Assert.Equal(2, model.Activity.OmittedCount);

                var window = new MainWindow { DataContext = model, Width = 1600, Height = 1000 };
                window.Show();
                try
                {
                    model.Activity.SelectedEntry = model.Activity.Entries[0];
                    Capture(window, "activity-attention.png");
                    model.Activity.MuteSelectedCommand.Execute(null);
                    Assert.Equal(1, model.Activity.MutedCount);
                    Assert.Equal(4, model.Activity.Entries.Count);
                    model.Activity.ShowGitHubCommand.Execute(null);
                    Assert.Equal(2, model.Activity.Entries.Count);
                    model.Activity.ShowMutedCommand.Execute(null);
                    Assert.Single(model.Activity.Entries);
                    model.Activity.RestoreMutedCommand.Execute(null);
                    Assert.Empty(model.Activity.Entries);
                    model.Activity.ShowAttentionCommand.Execute(null);
                }
                finally { window.Close(); }

                await model.Activity.RefreshAsync();
                model.Activity.SelectedEntry = null;
                var compact = new MainWindow { DataContext = model, Width = 1050, Height = 720 };
                compact.Show();
                try
                {
                    Capture(compact, "activity-attention-compact.png");
                    var page = compact.FindControl<ActivityView>("ActivityPage");
                    var scroller = page?.FindControl<ScrollViewer>("PageScroll");
                    Assert.NotNull(scroller);
                    scroller.Offset = new global::Avalonia.Vector(0, scroller.Extent.Height);
                    Capture(compact, "activity-attention-compact-list.png");
                }
                finally { compact.Close(); }

                var otherWorkspace = Directory.CreateDirectory(Path.Combine(root, "OtherWorkspace")).FullName;
                model.WorkspaceRoot = otherWorkspace;
                await model.RefreshAsync();
                Assert.Equal(3, inventory.Calls);
                Assert.Equal(Path.GetFullPath(otherWorkspace), model.Activity.WorkspaceRoot);
                return true;
            });
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void Capture(MainWindow window, string fileName)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var output = Environment.GetEnvironmentVariable("POWERFORGE_STUDIO_VISUAL_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        frame.Save(Path.Combine(output, fileName), PngBitmapEncoderOptions.Default);
    }

    private static WorkspaceActivityEntry OpenEntry(string id, string target)
        => new(id, "Project", "Issue", "Review", "Open", "Open target", "Detail", "Test",
            DateTimeOffset.UtcNow, target, target, true);

    private sealed class FakeActivityInventory : IWorkspaceActivityInventoryService
    {
        public int Calls { get; private set; }

        public Task<WorkspaceActivitySnapshot> InspectAsync(string workspaceRoot, WorkspaceActivityOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            var now = DateTimeOffset.UtcNow;
            WorkspaceActivityEntry[] entries =
            [
                Entry("ci", "PowerForge", "CI", "Critical", "Failed", "Release workflow failed", "Latest run exited with failure.", "GitHub", now, "https://github.com/EvotecIT/PSPublishModule/actions"),
                Entry("schedule", "OfficeIMO", "Schedule", "Critical", "Failed", "Nightly release", "Last task result was 1.", "Windows Task Scheduler", now.AddMinutes(-8), null),
                Entry("release", "Mailozaurr", "Release", "Warning", "Release Drift", "Release drift detected", "Local branch is ahead of the latest release.", "PowerForge portfolio", now.AddMinutes(-2), workspaceRoot),
                Entry("issue", "PowerForge", "Issue", "Review", "Open", "#812 MSI release metadata", "Assigned issue awaiting action.", "GitHub", now.AddHours(-1), "https://github.com/EvotecIT/PSPublishModule/issues/812"),
                Entry("pr", "OfficeIMO", "Pull request", "Review", "Open", "3 pull requests need review", "Remote review queue.", "GitHub", now.AddMinutes(-3), "https://github.com/EvotecIT/OfficeIMO/pulls"),
                Entry("info", "TestimoX", "Schedule", "Information", "Upcoming", "Weekly audit", "Next run tomorrow.", "Windows Task Scheduler", now, null, false)
            ];
            WorkspaceActivitySourceState[] sources =
            [
                new("Local workspace", "Available", 1, now, "Local readiness observed."),
                new("GitHub", "Partial", 3, now, "Evidence loaded; two repositories deferred."),
                new("Windows Task Scheduler", "Available", 2, now, "Runtime evidence observed."),
                new("Codex", "Unavailable", 0, null, "No supported inventory API.")
            ];
            return Task.FromResult(new WorkspaceActivitySnapshot(now, entries, sources, 36, 8));
        }

        private static WorkspaceActivityEntry Entry(string id, string project, string kind, string severity, string state,
            string title, string detail, string provider, DateTimeOffset observed, string? target, bool actionable = true)
            => new(id, project, kind, severity, state, title, detail, provider, observed, target ?? provider, target, actionable);
    }

    private sealed class ControlledActivityInventory : IWorkspaceActivityInventoryService
    {
        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }

        public async Task<WorkspaceActivitySnapshot> InspectAsync(string workspaceRoot, WorkspaceActivityOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Calls == 1)
            {
                FirstStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            return new WorkspaceActivitySnapshot(DateTimeOffset.UtcNow, [], [], 0, 0);
        }
    }
}
