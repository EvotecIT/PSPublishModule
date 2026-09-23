using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using PowerForgeStudio.Avalonia.ViewModels;

namespace PowerForgeStudio.Avalonia.Tests;

public sealed class LocalProjectTreeTests
{
    [Fact]
    public async Task BuildEnabledFolderWithoutGitOpensFilesAndBuildButNotGitRoutes()
    {
        var workspace = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
            "studio-local-project-tree-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var projectRoot = Directory.CreateDirectory(Path.Combine(workspace, "LocalBuild")).FullName;
            var build = Directory.CreateDirectory(Path.Combine(projectRoot, "Build")).FullName;
            await File.WriteAllTextAsync(Path.Combine(build, "project.build.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(projectRoot, "README.md"), "# Local build project");
            Directory.CreateDirectory(Path.Combine(workspace, "UnrelatedFolder"));

            await TestAppBuilder.RunAsync(async () =>
            {
                using var model = new WorkspaceViewModel(workspace);
                await model.RefreshAsync();
                var project = Assert.Single(model.Projects);
                Assert.Equal("LocalBuild", project.Name);
                Assert.Equal("1 project", model.RepositoryCount);
                await project.EnsureLoadedAsync();
                var localFiles = Assert.Single(project.Children);
                Assert.Equal("Local files", localFiles.Name);
                Assert.Equal("folder", localFiles.Kind);
                Assert.Contains(localFiles.Children, child => child.Name == "Build");

                await model.SelectAsync(project);
                Assert.True(model.IsOverviewPage);
                Assert.False(model.HasGitWorkingCopy);
                Assert.Equal("Local project", model.Branch);
                Assert.Equal("LocalBuild / Local files", model.CurrentDirectoryDisplay);
                Assert.Equal("No Git working copy", model.GitSummary);
                Assert.Equal(projectRoot, model.Build.WorkingCopyRoot);
                Assert.True(model.Build.CanPlan);
                Assert.False(model.CanShowChangedProjects);
                Assert.Equal("Local project", model.Overview.WorkspaceKind);
                Assert.Contains(model.Overview.Prerequisites, item => item.Name == "Git"
                    && item.Detail.Contains("local files and builds", StringComparison.Ordinal));

                model.ShowChangesCommand.Execute(null);
                model.ShowGitHubCommand.Execute(null);
                await model.ShowHistoryCommand.ExecuteAsync(null);
                Assert.True(model.IsOverviewPage);

                var window = new MainWindow { DataContext = model, Width = 1600, Height = 1000 };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    var tabs = window.GetVisualDescendants().OfType<Button>()
                        .Where(button => button.Classes.Contains("pageTab"))
                        .ToDictionary(button => button.Content?.ToString() ?? "");
                    Assert.False(tabs["Changes"].IsEnabled);
                    Assert.False(tabs["History"].IsEnabled);
                    Assert.False(tabs["GitHub"].IsEnabled);
                    Assert.True(tabs["Files"].IsEnabled);
                    Assert.True(tabs["Build & Run"].IsEnabled);
                    using var frame = window.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                    var output = Environment.GetEnvironmentVariable("POWERFORGE_STUDIO_VISUAL_OUTPUT");
                    if (!string.IsNullOrEmpty(output))
                    {
                        Directory.CreateDirectory(output);
                        frame.Save(Path.Combine(output, "local-project-tree.png"), PngBitmapEncoderOptions.Default);
                    }
                }
                finally { window.Close(); }
                return true;
            });
        }
        finally { Directory.Delete(workspace, recursive: true); }
    }
}
