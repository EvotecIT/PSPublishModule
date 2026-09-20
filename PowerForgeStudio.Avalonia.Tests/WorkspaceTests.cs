using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PowerForgeStudio.Avalonia.ViewModels;
using PowerForgeStudio.Orchestrator.Explorer;

namespace PowerForgeStudio.Avalonia.Tests;

public sealed class WorkspaceTests
{
    [Fact]
    public async Task RealRepositoryCanExpandAndPreviewFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "powerforge-studio-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Build"));
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
                var window = new MainWindow { DataContext = model, Width = 1600, Height = 1000 };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    Assert.NotNull(window.FindControl<TreeView>("ProjectTree"));
                    using var frame = window.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                    var output = Environment.GetEnvironmentVariable("POWERFORGE_STUDIO_VISUAL_OUTPUT");
                    if (!string.IsNullOrEmpty(output))
                    {
                        Directory.CreateDirectory(output);
                        frame.Save(Path.Combine(output, "workspace.png"), PngBitmapEncoderOptions.Default);
                    }
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
