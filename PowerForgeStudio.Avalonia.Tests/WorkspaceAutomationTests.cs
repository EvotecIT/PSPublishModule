using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PowerForge;
using PowerForgeStudio.Avalonia.ViewModels;
using PowerForgeStudio.Avalonia.Views;
using PowerForgeStudio.Domain.Automation;
using PowerForgeStudio.Orchestrator.Automation;

namespace PowerForgeStudio.Avalonia.Tests;

public sealed class WorkspaceAutomationTests
{
    [Fact]
    public async Task WorkspaceChangeCancelsInspectionAndAllowsImmediateRefresh()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-automation-switch-" + Guid.NewGuid().ToString("N"))).FullName;
        var other = Directory.CreateDirectory(Path.Combine(root, "Other")).FullName;
        try
        {
            var inventory = new ControlledAutomationInventory();
            using var model = new AutomationsViewModel(inventory);
            model.SetWorkspace(root);
            var first = model.RefreshAsync();
            await inventory.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            model.SetWorkspace(other);
            Assert.False(model.IsLoading);
            await model.RefreshAsync();
            await first;

            Assert.Equal(2, inventory.Calls);
            Assert.Contains("Inspected 0 schedule definitions", model.Status, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AutomationsRouteSeparatesRuntimeEvidenceAndProviderDefinitions()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-automation-ui-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var repository = Directory.CreateDirectory(Path.Combine(root, "PowerForge")).FullName;
            Assert.True((await new GitClient().RunRawAsync(repository, ["init", "-b", "main"])).Succeeded);
            await TestAppBuilder.RunAsync(async () =>
            {
                using var model = new WorkspaceViewModel(root, automations: new FakeAutomationInventory());
                await model.RefreshAsync();
                await model.ShowAutomationsCommand.ExecuteAsync(null);
                Assert.True(model.IsAutomationsPage);
                Assert.Equal(4, model.Automations.Entries.Count);
                Assert.Equal(3, model.Automations.Sources.Count);
                Assert.Equal(1, model.Automations.AttentionCount);
                Assert.Equal(2, model.Automations.DefinitionOnlyCount);

                var window = new MainWindow { DataContext = model, Width = 1600, Height = 1000 };
                window.Show();
                try
                {
                    model.Automations.SelectedEntry = model.Automations.Entries[0];
                    Capture(window, "automations-inventory.png");
                    model.Automations.ShowAllCommand.Execute(null);
                    Assert.Equal(5, model.Automations.Entries.Count);
                    model.Automations.ShowAttentionCommand.Execute(null);
                    Assert.Equal("Failed", Assert.Single(model.Automations.Entries).State);
                    model.Automations.ShowRelevantCommand.Execute(null);
                }
                finally
                {
                    window.Close();
                }
                model.Automations.SelectedEntry = null;
                var compact = new MainWindow { DataContext = model, Width = 1050, Height = 720 };
                compact.Show();
                try
                {
                    Capture(compact, "automations-inventory-compact.png");
                    var page = compact.FindControl<AutomationsView>("AutomationsPage");
                    var scroller = page?.FindControl<global::Avalonia.Controls.ScrollViewer>("PageScroll");
                    Assert.NotNull(scroller);
                    scroller.Offset = new global::Avalonia.Vector(0, scroller.Extent.Height);
                    Capture(compact, "automations-inventory-compact-list.png");
                }
                finally
                {
                    compact.Close();
                }
                return true;
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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

    private sealed class FakeAutomationInventory : IWorkspaceAutomationInventoryService
    {
        public Task<WorkspaceAutomationSnapshot> InspectAsync(string workspaceRoot, CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;
            WorkspaceAutomationEntry[] entries =
            [
                Entry("profile", "EvotecIT Codex Profile Sync", "Windows Task Scheduler", "Local workstation", "", "Every 30 minutes", "Upcoming", now.AddMinutes(18), now.AddMinutes(-12), "Succeeded", true, true),
                Entry("health", "PasswordSolutionX health", "Windows Task Scheduler", "Local workstation", "PasswordSolutionX", "Hourly", "Failed", now.AddMinutes(42), now.AddMinutes(-18), "Result 0x00000001", true, true),
                Entry("pf", "workflow-failure-sweeper.yml", "GitHub Actions", ".github/workflows", "PowerForge", "20 8 * * *", "Definition only", null, null, "Runtime not checked", false, true),
                Entry("office", "codeql.yml", "GitHub Actions", ".github/workflows", "OfficeIMO", "22 3 * * 1", "Definition only", null, null, "Runtime not checked", false, true),
                Entry("vendor", "Vendor updater", "Windows Task Scheduler", "Local workstation", "", "Daily · 10:30", "Healthy", null, now.AddHours(-2), "Succeeded", true, false)
            ];
            WorkspaceAutomationSourceState[] sources =
            [
                new("Windows Task Scheduler", "Available", 3, "Runtime evidence is read locally. Action arguments are never collected."),
                new("GitHub Actions", "Definitions", 2, "Local workflow definitions only; runtime evidence is not connected."),
                new("Codex", "Unavailable", 0, "No supported external inventory API is available.")
            ];
            return Task.FromResult(new WorkspaceAutomationSnapshot(now, entries, sources));
        }

        private static WorkspaceAutomationEntry Entry(string id, string name, string provider, string scope, string project,
            string schedule, string state, DateTimeOffset? next, DateTimeOffset? last, string result, bool runtime, bool relevant)
            => new(id, name, provider, scope, project, schedule, state, next, last, result, runtime,
                state != "Paused", relevant, scope, runtime ? "Provider runtime evidence." : "Local definition only.");
    }

    private sealed class ControlledAutomationInventory : IWorkspaceAutomationInventoryService
    {
        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }

        public async Task<WorkspaceAutomationSnapshot> InspectAsync(string workspaceRoot, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Calls == 1)
            {
                FirstStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            return new WorkspaceAutomationSnapshot(DateTimeOffset.UtcNow, [],
                [new("Windows Task Scheduler", "Available", 0, "No tasks")]);
        }
    }
}
