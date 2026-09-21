using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PowerForge;
using PowerForgeStudio.Avalonia.ViewModels;
using PowerForgeStudio.Avalonia.Views;
using PowerForgeStudio.Domain.Activity;
using PowerForgeStudio.Domain.Workspace;
using PowerForgeStudio.Orchestrator.Activity;
using PowerForgeStudio.Orchestrator.Workspace;

namespace PowerForgeStudio.Avalonia.Tests;

public sealed class WorkspaceSettingsTests
{
    [Fact]
    public async Task SettingsPreserveDraftsPersistPreferencesAndControlSessionAndActivity()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "studio-settings-" + Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(fixture, "Workspace");
        var repository = Path.Combine(workspace, "PowerForge.Sample");
        var catalogPath = Path.Combine(fixture, "workspace-roots.json");
        Directory.CreateDirectory(repository);
        try
        {
            var readme = Path.Combine(repository, "README.md");
            await File.WriteAllTextAsync(readme, "# Settings fixture");
            Assert.True((await new GitClient().RunRawAsync(repository, ["init", "-b", "main"])).Succeeded);

            var store = new WorkspaceRootCatalogService(catalogPath);
            var reference = new WorkspaceDocumentReference(repository, readme);
            store.SaveSession(workspace, [reference], reference, []);
            store.SaveProfile(new WorkspaceProfile(
                "daily-release",
                "Daily release",
                "Retained profile fixture",
                "Continue release follow-through.",
                null,
                [],
                workspace,
                "ready-view",
                "current-family",
                "Current family",
                [WorkspaceProfileLaunchActionKind.RefreshWorkspace, WorkspaceProfileLaunchActionKind.PrepareQueue],
                UpdatedAtUtc: new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero)));
            store.SaveTemplate(new WorkspaceProfileTemplate(
                "release-reference",
                "Release reference",
                "Retained custom template fixture.",
                "Reference only",
                "Continue the selected release."), workspace);
            var initial = new WorkspaceStudioPreferences(false, 11, 4, 222, 75);
            store.SavePreferences(initial, workspace);

            await TestAppBuilder.RunAsync(async () =>
            {
                var activity = new CapturingActivityInventory();
                using var model = new WorkspaceViewModel(workspace, store, activity: activity);
                await model.RefreshAsync();

                Assert.Empty(model.Documents);
                Assert.Equal(initial, model.Settings.SavedPreferences);
                Assert.Equal("Daily release", Assert.Single(model.Settings.WorkspaceProfiles).DisplayName);
                Assert.Equal("Release reference", Assert.Single(model.Settings.WorkspaceProfileTemplates).DisplayName);
                Assert.True(model.Settings.HasWorkspaceProfiles);
                Assert.True(model.Settings.HasWorkspaceProfileTemplates);
                model.ShowSettingsCommand.Execute(null);
                Assert.True(model.IsSettingsPage);
                Assert.True(model.IsWorkspaceUtilityPage);
                Assert.Equal(11, model.Settings.ActivityMaxGitHubRepositories);

                model.Settings.ActivityMaxGitHubRepositories = 17;
                model.ShowFilesCommand.Execute(null);
                model.ShowSettingsCommand.Execute(null);
                Assert.True(model.Settings.IsDirty);
                Assert.Equal(17, model.Settings.ActivityMaxGitHubRepositories);
                await model.RefreshAsync();
                Assert.True(model.Settings.IsDirty);
                Assert.Equal(17, model.Settings.ActivityMaxGitHubRepositories);

                var closeGuard = new MainWindow { DataContext = model };
                closeGuard.Show();
                closeGuard.Close();
                Assert.True(closeGuard.IsVisible);
                Assert.True(model.IsSettingsPage);
                Assert.Contains("Save or explicitly discard", model.Settings.Status, StringComparison.Ordinal);

                model.Settings.ResetCommand.Execute(null);
                Assert.Equal(WorkspaceStudioPreferences.Default.ActivityMaxGitHubRepositories,
                    model.Settings.ActivityMaxGitHubRepositories);
                Assert.Equal(initial, model.Settings.SavedPreferences);
                model.Settings.DiscardChangesCommand.Execute(null);
                Assert.False(model.Settings.IsDirty);
                Assert.Equal(11, model.Settings.ActivityMaxGitHubRepositories);
                model.Settings.ActivityMaxGitHubRepositories = 12;
                model.Settings.ActivityMaxGitHubRepositories = 11;
                Assert.False(model.Settings.IsDirty);
                await CloseAndWaitAsync(closeGuard);

                model.Settings.RestoreOpenDocuments = true;
                model.Settings.ActivityMaxGitHubRepositories = 6;
                model.Settings.ActivityMaxIssuesPerRepository = 2;
                model.Settings.ActivityMaxEntries = 80;
                model.Settings.ActivityGitHubTimeoutSeconds = 30;
                await model.Settings.SaveCommand.ExecuteAsync(null);

                var expected = new WorkspaceStudioPreferences(true, 6, 2, 80, 30);
                Assert.Equal(expected, model.Settings.SavedPreferences);
                Assert.False(model.Settings.IsDirty);
                var savedCatalog = new WorkspaceRootCatalogService(catalogPath).Load(workspace);
                Assert.Equal(expected, savedCatalog.Preferences);
                Assert.Equal("daily-release", Assert.Single(savedCatalog.Profiles).ProfileId);
                Assert.Equal("release-reference", Assert.Single(savedCatalog.Templates!).TemplateId);

                await model.ShowActivityCommand.ExecuteAsync(null);
                Assert.Equal(expected.ToActivityOptions(), activity.LastOptions);

                model.ShowSettingsCommand.Execute(null);
                var window = new MainWindow { DataContext = model, Width = 1600, Height = 1000 };
                window.Show();
                try { Capture(window, "settings-workspace.png"); }
                finally { await CloseAndWaitAsync(window); }

                var compact = new MainWindow { DataContext = model, Width = 1050, Height = 720 };
                compact.Show();
                try
                {
                    Capture(compact, "settings-workspace-compact.png");
                    var page = compact.FindControl<SettingsView>("SettingsPage");
                    var scroller = page?.FindControl<ScrollViewer>("PageScroll");
                    Assert.NotNull(scroller);
                    scroller.Offset = new global::Avalonia.Vector(0, scroller.Extent.Height);
                    Capture(compact, "settings-workspace-compact-bottom.png");
                    model.Settings.ShowDiagnosticsCommand.Execute(null);
                    scroller.Offset = default;
                    Capture(compact, "settings-diagnostics-compact.png");
                }
                finally { await CloseAndWaitAsync(compact); }
                return true;
            });
        }
        finally
        {
            Directory.Delete(fixture, recursive: true);
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

    private static async Task CloseAndWaitAsync(MainWindow window)
    {
        window.Close();
        for (var attempt = 0; attempt < 100 && window.IsVisible; attempt++) await Task.Delay(20);
        Assert.False(window.IsVisible);
    }

    private sealed class CapturingActivityInventory : IWorkspaceActivityInventoryService
    {
        public WorkspaceActivityOptions? LastOptions { get; private set; }

        public Task<WorkspaceActivitySnapshot> InspectAsync(
            string workspaceRoot,
            WorkspaceActivityOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            return Task.FromResult(new WorkspaceActivitySnapshot(DateTimeOffset.UtcNow, [], [], 0, 0));
        }
    }
}
