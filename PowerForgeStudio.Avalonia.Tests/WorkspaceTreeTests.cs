using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PowerForge;
using PowerForgeStudio.Avalonia.ViewModels;

namespace PowerForgeStudio.Avalonia.Tests;

public sealed class WorkspaceTreeTests
{
    [Fact]
    public async Task LinkedWorktreeRetainsProjectContextAndShowsItsOwnGitMarkers()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "studio-tree-" + Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(fixture, "Workspace");
        var primary = Path.Combine(workspace, "Studio.Sample");
        var other = Path.Combine(workspace, "Module.Sample");
        var linked = Path.Combine(fixture, "release-notes");
        var git = new GitClient();
        async Task Run(string root, params string[] args)
        {
            var result = await git.RunRawAsync(root, args);
            Assert.True(result.Succeeded, result.StdErr);
        }
        try
        {
            foreach (var repository in new[] { primary, other })
            {
                Directory.CreateDirectory(Path.Combine(repository, "Build"));
                Directory.CreateDirectory(Path.Combine(repository, "Docs"));
                Directory.CreateDirectory(Path.Combine(repository, "Source"));
                Directory.CreateDirectory(Path.Combine(repository, ".github"));
                Directory.CreateDirectory(Path.Combine(repository, "Artifacts"));
                await File.WriteAllTextAsync(Path.Combine(repository, "README.md"), "# Studio sample\n");
                await File.WriteAllTextAsync(Path.Combine(repository, "Build", "Build-Project.ps1"), "# Sample build entry point\n");
                await File.WriteAllTextAsync(Path.Combine(repository, "Build", "project.build.json"), "{}\n");
                await File.WriteAllTextAsync(Path.Combine(repository, "Studio.Sample.slnx"), "<Solution />\n");
                await Run(repository, "init", "-b", "main");
                await Run(repository, "config", "user.name", "Studio validation");
                await Run(repository, "config", "user.email", "studio-validation@example.invalid");
                await Run(repository, "config", "commit.gpgSign", "false");
                await Run(repository, "config", "core.hooksPath", Path.Combine(fixture, "empty-hooks"));
                await Run(repository, "add", "-A");
                await Run(repository, "commit", "-m", "Initial fixture");
            }
            await Run(primary, "worktree", "add", "-b", "feature/release-notes", linked);
            await File.AppendAllTextAsync(Path.Combine(linked, "README.md"), "\nRelease notes in progress.\n");
            await TestAppBuilder.RunAsync(async () =>
            {
                using var model = new WorkspaceViewModel(workspace);
                await model.RefreshAsync();
                var project = Assert.Single(model.Projects, item => item.Name == "Studio.Sample");
                await project.EnsureLoadedAsync(); project.IsExpanded = true;
                var checkout = project.Children[0];
                Assert.Equal(["Build", "Docs", "Source", ".github", "README.md", "Studio.Sample.slnx", "Artifacts"],
                    checkout.Children.Select(child => child.Name));
                var build = Assert.Single(checkout.Children, item => item.Name == "Build");
                await build.EnsureLoadedAsync(); build.IsExpanded = true;
                var worktreeGroup = Assert.Single(project.Children, item => item.Name.StartsWith("Worktrees"));
                var workingCopy = Assert.Single(worktreeGroup.Children);
                await workingCopy.EnsureLoadedAsync(); workingCopy.IsExpanded = true;
                var readme = Assert.Single(workingCopy.Children, item => item.Name == "README.md");
                model.SelectedNode = readme;
                await model.SelectAsync(readme);
                Assert.True(project.IsContextProject);
                Assert.Equal("Studio.Sample", model.ProjectName);
                Assert.Equal("M", readme.StatusMarker);
                Assert.Equal("README.md", model.SelectedFile?.Name);
                Assert.True(model.HasSelectedFile);
                Assert.Equal("1 change", workingCopy.StatusMarker);
                Assert.Equal("clean", checkout.StatusMarker);
                Assert.False(Assert.Single(model.Projects, item => item.Name == "Module.Sample").IsContextProject);
                var window = new MainWindow { DataContext = model, Width = 1600, Height = 1000 };
                try
                {
                    window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var frame = window.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                    var output = Environment.GetEnvironmentVariable("POWERFORGE_STUDIO_VISUAL_OUTPUT");
                    if (!string.IsNullOrEmpty(output)) frame.Save(Path.Combine(output, "workspace-tree.png"), PngBitmapEncoderOptions.Default);
                    window.KeyPress(Key.K, RawInputModifiers.Control, PhysicalKey.None, null);
                    window.KeyRelease(Key.K, RawInputModifiers.Control, PhysicalKey.None, null);
                    Assert.True(window.FindControl<TextBox>("QuickProjectSearchBox")!.IsFocused);
                    model.QuickProjectQuery = "Module";
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(2, model.Projects.Count);
                    var quickMatch = Assert.Single(model.QuickProjectMatches);
                    Assert.Equal("Module.Sample", quickMatch.Name);
                    Assert.True(model.HasQuickProjectQuery);
                    if (!string.IsNullOrEmpty(output))
                    {
                        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                        using var searchFrame = window.CaptureRenderedFrame();
                        Assert.NotNull(searchFrame);
                        searchFrame.Save(Path.Combine(output, "workspace-quick-search.png"), PngBitmapEncoderOptions.Default);
                        window.Width = 1050; window.Height = 720;
                        window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                        using var compactSearchFrame = window.CaptureRenderedFrame();
                        Assert.NotNull(compactSearchFrame);
                        compactSearchFrame.Save(Path.Combine(output, "workspace-quick-search-compact.png"), PngBitmapEncoderOptions.Default);
                    }
                    var searchPopup = window.FindControl<Popup>("QuickProjectResultsPopup")!;
                    Assert.True(searchPopup.IsOpen);
                    searchPopup.IsOpen = false;
                    Dispatcher.UIThread.RunJobs();
                    Assert.False(model.IsQuickProjectSearchOpen);
                    window.KeyPress(Key.K, RawInputModifiers.Control, PhysicalKey.None, null);
                    window.KeyRelease(Key.K, RawInputModifiers.Control, PhysicalKey.None, null);
                    Assert.True(searchPopup.IsOpen);
                    Assert.True(window.FindControl<TextBox>("ProjectTreeFilterBox")!.Focus());
                    searchPopup.IsOpen = false;
                    Assert.True(window.FindControl<TextBox>("QuickProjectSearchBox")!.Focus());
                    Assert.True(searchPopup.IsOpen);
                    model.Filter = "Studio";
                    model.FavoritesOnly = true;
                    Assert.Empty(model.Projects);
                    Assert.Same(quickMatch, Assert.Single(model.QuickProjectMatches));
                    await model.OpenQuickProjectAsync(quickMatch);
                    Assert.Equal("Module.Sample", model.ProjectName);
                    Assert.Equal(other, model.ActiveWorkingCopyRoot);
                    Assert.Empty(model.Filter);
                    Assert.False(model.FavoritesOnly);
                    Assert.False(model.HasQuickProjectQuery);
                    // Filtering must retain expansion and project identity, not clone its hierarchy.
                    model.Filter = "Studio";
                    Assert.Same(project, Assert.Single(model.Projects));
                    model.Filter = "";
                    Assert.True(workingCopy.IsExpanded);
                    Assert.Same(project, Assert.Single(model.Projects, item => item.Name == "Studio.Sample"));
                }
                finally { window.Close(); }
                return true;
            });
        }
        finally
        {
            if (Directory.Exists(fixture))
            {
                foreach (var file in new DirectoryInfo(fixture).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
                    if (file.IsReadOnly) file.IsReadOnly = false;
                Directory.Delete(fixture, recursive: true);
            }
        }
    }
}
