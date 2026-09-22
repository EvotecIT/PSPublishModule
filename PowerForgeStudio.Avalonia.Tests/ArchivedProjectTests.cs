using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PowerForge;
using PowerForgeStudio.Avalonia.ViewModels;
using PowerForgeStudio.Orchestrator.Workspace;

namespace PowerForgeStudio.Avalonia.Tests;

public sealed class ArchivedProjectTests
{
    [Fact]
    public async Task ArchiveIsLocalPersistentAndReversibleWithoutChangingRepository()
    {
        var fixture = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-archive-" + Guid.NewGuid().ToString("N"))).FullName;
        var workspace = Directory.CreateDirectory(Path.Combine(fixture, "Workspace")).FullName;
        var first = Directory.CreateDirectory(Path.Combine(workspace, "First")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(workspace, "Second")).FullName;
        var statePath = Path.Combine(fixture, "workspace-roots.json");
        try
        {
            foreach (var repository in new[] { first, second })
            {
                await File.WriteAllTextAsync(Path.Combine(repository, "README.md"), "# " + Path.GetFileName(repository));
                var initialized = await new GitClient().RunRawAsync(repository, ["init", "--quiet", "-b", "main"]);
                Assert.True(initialized.Succeeded, initialized.StdErr);
            }

            await TestAppBuilder.RunAsync(async () =>
            {
                var store = new WorkspaceRootCatalogService(statePath);
                using (var model = new WorkspaceViewModel(workspace, store))
                {
                    await model.RefreshAsync();
                    var project = Assert.Single(model.Projects, item => item.Name == "First");
                    await model.SelectAsync(project);
                    await model.ToggleFavoriteCommand.ExecuteAsync(null);
                    await model.ToggleProjectArchivedCommand.ExecuteAsync(null);

                    Assert.Equal("Restore project", model.ArchiveActionLabel);
                    Assert.Equal("Second", Assert.Single(model.Projects).Name);
                    Assert.Empty(Assert.Single(model.ExplorerRoots, group => group.Name == "Favorites").Children);
                    var archived = Assert.Single(model.ExplorerRoots, group => group.Name == "Archived (1)");
                    Assert.Equal("First", Assert.Single(archived.Children).Name);
                    Assert.True(File.Exists(Path.Combine(first, "README.md")));
                    Assert.Equal(first, Assert.Single(store.LoadExplorer(workspace).ArchivedProjectRoots!));
                }

                using var reopened = new WorkspaceViewModel(workspace, new WorkspaceRootCatalogService(statePath));
                await reopened.RefreshAsync();
                var archivedGroup = Assert.Single(reopened.ExplorerRoots, group => group.Name == "Archived (1)");
                Assert.False(archivedGroup.IsExpanded);
                reopened.QuickProjectQuery = "First";
                var archivedProject = Assert.Single(reopened.QuickProjectMatches);
                await reopened.OpenQuickProjectAsync(archivedProject);
                archivedGroup = Assert.Single(reopened.ExplorerRoots, group => group.Name == "Archived (1)");
                Assert.True(archivedGroup.IsExpanded);
                Assert.Same(archivedProject, reopened.SelectedNode);
                Assert.Equal("Restore project", reopened.ArchiveActionLabel);
                var window = new MainWindow { DataContext = reopened, Width = 1600, Height = 1000 };
                window.Show();
                try
                {
                    Capture(window, "archived-project-wide.png");
                    window.Width = 1050; window.Height = 720;
                    Capture(window, "archived-project-compact.png");

                    await reopened.ToggleProjectArchivedCommand.ExecuteAsync(null);
                    Assert.Equal(2, reopened.Projects.Count);
                    Assert.DoesNotContain(reopened.ExplorerRoots, group => group.Name.StartsWith("Archived", StringComparison.Ordinal));
                    Assert.Equal("First", Assert.Single(reopened.ExplorerRoots[0].Children).Name);
                    Assert.Empty(new WorkspaceRootCatalogService(statePath).LoadExplorer(workspace).ArchivedProjectRoots!);
                }
                finally { window.Close(); }
                return true;
            });
        }
        finally { await DeleteFixtureAsync(fixture); }
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

    private static async Task DeleteFixtureAsync(string fixture)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { Directory.Delete(fixture, recursive: true); return; }
            catch (IOException) when (attempt < 4) { await Task.Delay(100 * (attempt + 1)); }
        }
    }
}
