using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PowerForge;
using PowerForgeStudio.Avalonia.ViewModels;
using PowerForgeStudio.Orchestrator.Workspace;
using PowerForgeStudio.Domain.Hub;

namespace PowerForgeStudio.Avalonia.Tests;

public sealed class WorkspaceSessionTests
{
    [Fact]
    public async Task OpenDocumentsFollowFileAndFolderMovesAndMissingFilesDoNotShowPreviousContents()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "studio-session-moves-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(fixture, "Docs"));
        try
        {
            var original = Path.Combine(fixture, "Docs", "guide.md");
            await File.WriteAllTextAsync(original, "Guide contents");
            Assert.True((await new GitClient().RunRawAsync(fixture, ["init", "-b", "main"])).Succeeded);
            await TestAppBuilder.RunAsync(async () =>
            {
                using var model = new WorkspaceViewModel(fixture);
                await model.RefreshAsync();
                await model.SelectAsync(new ExplorerNode("guide.md", original, "file", fixture));
                Assert.True(await model.ExecuteFileOperationAsync(new(WorkspaceFileOperation.Rename, fixture, original, Path.Combine(fixture, "Docs", "renamed.md"))));
                Assert.Equal("renamed.md", Assert.Single(model.Documents).Name);
                Assert.Equal("Guide contents", model.Preview);
                Assert.True(await model.ExecuteFileOperationAsync(new(WorkspaceFileOperation.Move, fixture, Path.Combine(fixture, "Docs"), Path.Combine(fixture, "Guides"))));
                var moved = Assert.Single(model.Documents);
                Assert.Equal(Path.Combine(fixture, "Guides", "renamed.md"), moved.Location);
                Assert.Equal("Guide contents", model.Preview);
                File.Delete(moved.Location);
                await model.SelectDocumentCommand.ExecuteAsync(moved);
                Assert.Same(moved, model.ActiveDocument);
                Assert.DoesNotContain("Guide contents", model.Preview);
                await model.ShowWorkspaceCommand.ExecuteAsync(null);
                Assert.True(model.IsWorkspaceTab);
                Assert.Single(model.Documents);
                return true;
            });
        }
        finally { Directory.Delete(fixture, recursive: true); }
    }

    [Fact]
    public async Task CorruptSettingsRemainIntactAndClosingAgainAllowsExitWithoutSaving()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "studio-session-corrupt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        var catalog = Path.Combine(fixture, "workspace-roots.json");
        await File.WriteAllTextAsync(catalog, "{invalid-json");
        try
        {
            await TestAppBuilder.RunAsync(async () =>
            {
                using var model = new WorkspaceViewModel(fixture, new WorkspaceRootCatalogService(catalog));
                await model.RefreshAsync();
                Assert.True(model.HasStateError);
                var window = new MainWindow { DataContext = model };
                var closed = false;
                window.Closed += (_, _) => closed = true;
                window.Show();
                window.Close();
                for (var attempt = 0; attempt < 100 && !model.StateError.Contains("Close the window again", StringComparison.Ordinal); attempt++)
                    await Task.Delay(20);
                Assert.False(closed);
                Assert.Contains("Close the window again", model.StateError);
                window.Close();
                Assert.True(closed);
                Assert.Equal("{invalid-json", await File.ReadAllTextAsync(catalog));
                return true;
            });
        }
        finally { Directory.Delete(fixture, recursive: true); }
    }

    [Fact]
    public async Task FavoritesTabsAndExpandedFoldersSurviveReopenAndTabActionsUseTheirOwnRepository()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "studio-session-" + Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(fixture, "Workspace");
        var store = new WorkspaceRootCatalogService(Path.Combine(fixture, "workspace-roots.json"));
        try
        {
            foreach (var name in new[] { "Studio.Sample", "Module.Sample" })
            {
                var root = Path.Combine(workspace, name);
                Directory.CreateDirectory(Path.Combine(root, "Docs"));
                Directory.CreateDirectory(Path.Combine(root, "Build"));
                await File.WriteAllTextAsync(Path.Combine(root, "README.md"), "# " + name);
                await File.WriteAllTextAsync(Path.Combine(root, "Docs", "guide.md"), "Guide for " + name);
                await File.WriteAllTextAsync(Path.Combine(root, "Build", "project.build.json"), "{}");
                var result = await new GitClient().RunRawAsync(root, ["init", "-b", "main"]);
                Assert.True(result.Succeeded, result.StdErr);
            }
            await TestAppBuilder.RunAsync(async () =>
            {
                using (var model = new WorkspaceViewModel(workspace, store))
                {
                    await model.RefreshAsync();
                    Assert.Equal("Favorites", model.ExplorerRoots[0].Name);
                    Assert.Empty(model.ExplorerRoots[0].Children);
                    Assert.Equal("Other projects", model.ExplorerRoots[1].Name);
                    Assert.Equal(2, model.ExplorerRoots[1].Children.Count);
                    foreach (var name in new[] { "Studio.Sample", "Module.Sample" })
                    {
                        var project = Assert.Single(model.Projects, item => item.Name == name);
                        await project.EnsureLoadedAsync(); project.IsExpanded = true;
                        var checkout = project.Children[0];
                        var docs = Assert.Single(checkout.Children, item => item.Name == "Docs");
                        await docs.EnsureLoadedAsync(); docs.IsExpanded = true;
                        await model.SelectAsync(Assert.Single(checkout.Children, item => item.Name == "README.md"));
                        if (name == "Studio.Sample")
                        {
                            await model.SelectAsync(project);
                            var build = Assert.Single(checkout.Children, item => item.Name == "Build");
                            Assert.True(build.IsExpanded);
                            build.IsExpanded = false;
                            await model.ToggleFavoriteCommand.ExecuteAsync(null);
                        }
                    }
                    Assert.Equal(2, model.Documents.Count);
                    Assert.Equal("Module.Sample", model.ProjectName);
                    Assert.Equal("Favorites", model.ExplorerRoots[0].Name);
                    await model.RefreshAsync();
                    Assert.Equal(2, model.Documents.Count);
                    Assert.Equal("Module.Sample", model.ProjectName);
                    await model.SaveSessionAsync();
                }
                using var reopened = new WorkspaceViewModel(workspace, store);
                await reopened.RefreshAsync();
                Assert.False(reopened.HasStateError, reopened.StateError);
                Assert.Equal(2, reopened.Documents.Count);
                Assert.Equal("Module.Sample", reopened.ProjectName);
                Assert.Equal("# Module.Sample", reopened.Preview);
                var restoredProject = Assert.Single(reopened.Projects, item => item.Name == "Module.Sample");
                var restoredPrimary = Assert.Single(restoredProject.Children, item => item.Path == Path.Combine(workspace, "Module.Sample"));
                var restoredBuild = Assert.Single(restoredPrimary.Children, item => item.Name == "Build");
                Assert.False(restoredBuild.IsExpanded);
                await reopened.SelectAsync(restoredProject);
                Assert.True(restoredBuild.IsExpanded);
                var restoredFavorite = Assert.Single(reopened.Projects, item => item.Name == "Studio.Sample");
                await reopened.SelectAsync(restoredFavorite);
                var favoriteBuild = Assert.Single(restoredFavorite.Children[0].Children, item => item.Name == "Build");
                Assert.False(favoriteBuild.IsExpanded);
                await reopened.SelectAsync(restoredProject);
                var favoriteGroup = reopened.ExplorerRoots[0];
                Assert.Equal("Studio.Sample", Assert.Single(favoriteGroup.Children).Name);
                reopened.ShowFavoriteProjectsCommand.Execute(null);
                Assert.Equal("Studio.Sample", Assert.Single(reopened.Projects).Name);
                reopened.ShowAllProjectsCommand.Execute(null);
                var window = new MainWindow { DataContext = reopened, Width = 1600, Height = 1000 };
                var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                window.Closed += (_, _) => closed.TrySetResult();
                try
                {
                    window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    var first = reopened.Documents[0];
                    var tab = Assert.Single(window.GetVisualDescendants().OfType<Button>(), button =>
                        ReferenceEquals(button.CommandParameter, first) && ReferenceEquals(button.Command, reopened.SelectDocumentCommand));
                    Assert.True(tab.Focus());
                    window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
                    window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
                    if (reopened.SelectDocumentCommand.ExecutionTask is { } selection) await selection;
                    Assert.Equal("Studio.Sample", reopened.ProjectName);
                    Assert.Equal("# Studio.Sample", reopened.Preview);
                    var output = Environment.GetEnvironmentVariable("POWERFORGE_STUDIO_VISUAL_OUTPUT");
                    foreach (var compact in new[] { false, true })
                    {
                        window.Width = compact ? 1050 : 1600; window.Height = compact ? 720 : 1000;
                        window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                        using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                        if (!string.IsNullOrEmpty(output)) frame.Save(Path.Combine(output, compact ? "workspace-session-compact.png" : "workspace-session.png"), PngBitmapEncoderOptions.Default);
                    }
                    await reopened.CloseDocumentCommand.ExecuteAsync(first);
                    Assert.Single(reopened.Documents);
                    Assert.Equal("Module.Sample", reopened.ProjectName);
                    await reopened.SaveSessionAsync();
                    Assert.Single(store.LoadExplorer(workspace).OpenDocuments);
                    Assert.Single(store.LoadExplorer(workspace).FavoriteProjectRoots);
                    Assert.Contains(store.LoadExplorer(workspace).ExpandedPaths, path => path.EndsWith("Docs", StringComparison.Ordinal));
                    Assert.Contains(Path.Combine(workspace, "Studio.Sample", "Build"), store.LoadExplorer(workspace).CollapsedBuildPaths!);
                }
                finally { window.Close(); await closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
                return true;
            });
        }
        finally { if (Directory.Exists(fixture)) Directory.Delete(fixture, recursive: true); }
    }
}
