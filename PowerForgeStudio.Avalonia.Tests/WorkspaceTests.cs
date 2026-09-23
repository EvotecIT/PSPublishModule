using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Automation.Peers;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PowerForgeStudio.Avalonia.ViewModels;
using PowerForgeStudio.Avalonia.Views;
using PowerForgeStudio.Domain.Hub;
using PowerForgeStudio.Orchestrator.Explorer;

namespace PowerForgeStudio.Avalonia.Tests;

public sealed class WorkspaceTests
{
    [Fact]
    public async Task RealRepositoryCanExpandAndPreviewFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "powerforge-studio-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Build"));
        Directory.CreateDirectory(Path.Combine(root, "Archive"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "Build", "Build-Project.ps1"), "# Fixture only");
            await File.WriteAllTextAsync(Path.Combine(root, "README.md"), "# Preview content");
            var start = new ProcessStartInfo("git") { WorkingDirectory = root, CreateNoWindow = true, UseShellExecute = false };
            start.ArgumentList.Add("init");
            using (var process = Process.Start(start)!) { await process.WaitForExitAsync(); Assert.Equal(0, process.ExitCode); }
            await TestAppBuilder.RunAsync(async () =>
            {
                using var model = new WorkspaceViewModel(root);
                await model.RefreshAsync();
                var project = Assert.Single(model.Projects);
                await project.EnsureLoadedAsync();
                project.IsExpanded = true;
                var checkout = project.Children[0];
                Assert.Contains(checkout.Children, x => x.Name == "Build");
                var readme = Assert.Single(checkout.Children, x => x.Name == "README.md");
                await model.SelectAsync(readme);
                Assert.Equal("# Preview content", model.Preview);
                Assert.Contains("untracked", model.GitSummary);
                Assert.True(project.IsContextProject);
                Assert.Equal("folder", project.IconKind);
                Assert.Equal("?", readme.StatusMarker);
                // A central-pane navigation must retain the working-copy root even if tree selection is absent.
                Assert.Same(readme, model.SelectedNode);
                var build = Assert.Single(model.Files, x => x.Name == "Build");
                await model.OpenEntryAsync(build);
                Assert.Equal(root, model.ActiveWorkingCopyRoot);
                Assert.Equal(Path.Combine(root, "Build"), model.CurrentDirectory);
                Assert.Equal(Path.GetFileName(root) + " / " + model.Branch + " / Build", model.CurrentDirectoryDisplay);
                await model.NavigateUpCommand.ExecuteAsync(null);
                Assert.Equal(root, model.CurrentDirectory);
                Assert.Equal(Path.GetFileName(root) + " / " + model.Branch, model.CurrentDirectoryDisplay);
                // Exercise consecutive navigation requests and verify the winning folder.
                await Task.WhenAll(model.SelectAsync(Assert.Single(checkout.Children, x => x.Name == "Build")), model.SelectAsync(checkout));
                Assert.Equal(root, model.CurrentDirectory);
                Assert.Contains(model.Files, x => x.Name == "README.md");
                var archive = Assert.Single(checkout.Children, x => x.Name == "Archive");
                await archive.EnsureLoadedAsync();
                archive.IsExpanded = true;
                model.SelectedFile = Assert.Single(model.Files, x => x.Name == "README.md");
                Assert.Equal("README.md", model.SelectedFileRelativePath);
                Assert.Equal("MD file", model.SelectedFile.KindLabel);
                await model.OpenEntryAsync(model.SelectedFile);
                var window = new MainWindow { DataContext = model, Width = 1600, Height = 1000 };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    var tree = window.FindControl<TreeView>("ProjectTree")!;
                    var buildNode = Assert.Single(checkout.Children, node => node.Name == "Build");
                    tree.SelectedItem = buildNode;
                    var buildContainer = Assert.Single(window.GetVisualDescendants().OfType<TreeViewItem>(), item => ReferenceEquals(item.Header, buildNode));
                    Assert.True(buildContainer.Focus());
                    window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
                    window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
                    await buildNode.EnsureLoadedAsync();
                    Assert.True(buildNode.IsExpanded);
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
                    window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
                    var script = Assert.Single(buildNode.Children, node => node.Name == "Build-Project.ps1");
                    Assert.Same(script, tree.SelectedItem);
                    await model.SelectAsync(script);
                    Assert.Equal("# Fixture only", model.Preview);
                    Assert.Equal(Path.Combine("Build", "Build-Project.ps1"), model.SelectedFileRelativePath);
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    Assert.NotNull(window.FindControl<TreeView>("ProjectTree"));
                    Assert.Single(window.GetVisualDescendants().OfType<FilesView>());
                    var projectActions = window.FindControl<Button>("ProjectActionsButton");
                    Assert.NotNull(projectActions);
                    Assert.True(projectActions.IsEffectivelyVisible);
                    Assert.Equal("Project actions", ControlAutomationPeer.CreatePeerForElement(projectActions)?.GetName());
                    Assert.Equal(model.Branch, window.FindControl<TextBlock>("StatusBranch")?.Text);
                    Assert.Equal(model.GitSummary, window.FindControl<TextBlock>("StatusGitSummary")?.Text);
                    Assert.Equal(model.RepositoryCount, window.FindControl<TextBlock>("StatusRepositoryCount")?.Text);
                    Assert.Equal("1 repository", model.RepositoryCount);
                    Assert.Equal(AutomationLiveSetting.Polite,
                        ControlAutomationPeer.CreatePeerForElement(window.FindControl<TextBlock>("StatusMessage")!)?.GetLiveSetting());
                    using var frame = window.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                    var output = Environment.GetEnvironmentVariable("POWERFORGE_STUDIO_VISUAL_OUTPUT");
                    if (!string.IsNullOrEmpty(output))
                    {
                        Directory.CreateDirectory(output);
                        frame.Save(Path.Combine(output, "workspace.png"), PngBitmapEncoderOptions.Default);
                    }
                    var more = Assert.Single(window.GetVisualDescendants().OfType<Button>(),
                        button => button.Classes.Contains("fileAction") && button.Flyout is MenuFlyout);
                    var namedActions = window.GetVisualDescendants().OfType<Button>()
                        .Where(button => button.Classes.Contains("fileAction"))
                        .Select(button => ControlAutomationPeer.CreatePeerForElement(button)?.GetName())
                        .ToArray();
                    Assert.Equal(new[] { "Parent folder", "New file", "New folder", "Copy", "Move", "Rename", "More file actions" }, namedActions);
                    var menu = Assert.IsType<MenuFlyout>(more.Flyout);
                    Assert.Contains(menu.Items.OfType<MenuItem>(), item => item.Header?.ToString() == "Move to recovery");
                    Assert.Contains(menu.Items.OfType<MenuItem>(), item => item.Header?.ToString() == "Refresh folder");
                    menu.ShowAt(more);
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using (var menuFrame = window.CaptureRenderedFrame())
                    {
                        Assert.NotNull(menuFrame);
                        if (!string.IsNullOrEmpty(output)) menuFrame.Save(Path.Combine(output, "workspace-more.png"), PngBitmapEncoderOptions.Default);
                    }
                    menu.Hide();
                    await model.SelectAsync(readme);
                    model.SelectedFile = Assert.Single(model.Files, file => file.Name == "README.md");
                    var operation = new FileOperationViewModel(model, WorkspaceFileOperation.Copy) { Destination = "README-copy.md" };
                    var dialog = new FileOperationDialog { DataContext = operation };
                    var shown = dialog.ShowDialog(window);
                    dialog.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using (var dialogFrame = dialog.CaptureRenderedFrame())
                    {
                        Assert.NotNull(dialogFrame);
                        if (!string.IsNullOrEmpty(output)) dialogFrame.Save(Path.Combine(output, "file-operation.png"), PngBitmapEncoderOptions.Default);
                    }
                    try
                    {
                        Assert.True(await operation.SubmitAsync(), operation.Error);
                        Assert.Equal("# Preview content", await File.ReadAllTextAsync(Path.Combine(root, "README-copy.md")));
                        Assert.Contains(model.Files, x => x.Name == "README-copy.md");
                        Assert.Contains(checkout.Children, x => x.Name == "README-copy.md");
                        operation.Destination = "README.md";
                        Assert.False(await operation.SubmitAsync());
                        Assert.Contains("already exists", operation.Error);
                    }
                    finally { dialog.Close(); await shown; }
                    Assert.True(await model.ExecuteFileOperationAsync(new WorkspaceFileOperationRequest(
                        WorkspaceFileOperation.Move, root, "README-copy.md", "Archive/README-copy.md")), model.Status);
                    Assert.DoesNotContain(checkout.Children, x => x.Name == "README-copy.md");
                    Assert.Contains(archive.Children, x => x.Name == "README-copy.md");
                    Assert.Same(archive, Assert.Single(checkout.Children, x => x.Name == "Archive"));
                    window.Width = 1050;
                    window.Height = 720;
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    Assert.False(window.FindControl<Border>("ContextPanel")!.IsVisible);
                    Assert.False(window.FindControl<TextBlock>("StatusGitSummary")!.IsEffectivelyVisible);
                    Assert.True(window.FindControl<TextBlock>("StatusBranch")!.IsEffectivelyVisible);
                    Assert.True(window.FindControl<TextBlock>("StatusRepositoryCount")!.IsEffectivelyVisible);
                    using var compact = window.CaptureRenderedFrame();
                    Assert.NotNull(compact);
                    if (!string.IsNullOrEmpty(output)) compact.Save(Path.Combine(output, "workspace-compact.png"), PngBitmapEncoderOptions.Default);
                }
                finally { window.Close(); }
                return true;
            });
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task PreviewSupportsUtf16AndBoundsLargeFiles()
    {
        var path = Path.GetTempFileName();
        try
        {
            var service = new FileExplorerService();
            await File.WriteAllTextAsync(path, "PowerShell Ω", Encoding.Unicode);
            Assert.Equal("PowerShell Ω", await service.ReadTextPreviewAsync(path));
            await File.WriteAllBytesAsync(path, new byte[300 * 1024]);
            Assert.Contains("256 KiB", await service.ReadTextPreviewAsync(path));
            await File.WriteAllBytesAsync(path, [65, 0, 66]);
            Assert.Contains("Binary", await service.ReadTextPreviewAsync(path));
        }
        finally { File.Delete(path); }
    }
}
