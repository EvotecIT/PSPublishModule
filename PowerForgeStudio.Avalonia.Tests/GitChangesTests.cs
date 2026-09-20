using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PowerForge;
using PowerForgeStudio.Avalonia.ViewModels;
using PowerForgeStudio.Domain.Hub;
using PowerForgeStudio.Orchestrator.Hub;

namespace PowerForgeStudio.Avalonia.Tests;

public sealed class GitChangesTests
{
    [Fact]
    public async Task GitChangesCanStageUnstageCommitAndHandleRenameWithLiteralUnicodePaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "studio-git-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var git = new GitClient();
        async Task Run(params string[] arguments)
        {
            var result = await git.RunRawAsync(root, arguments);
            Assert.True(result.Succeeded, result.StdErr);
        }
        try
        {
            await Run("init", "-b", "main");
            await Run("config", "user.name", "Studio validation");
            await Run("config", "user.email", "studio-validation@example.invalid");
            await Run("config", "commit.gpgSign", "false");
            await Run("config", "core.hooksPath", Path.Combine(root, "empty-hooks"));
            await File.WriteAllTextAsync(Path.Combine(root, "a[1].txt"), "Literal bracket path\n");
            await File.WriteAllTextAsync(Path.Combine(root, "a1.txt"), "Must stay untracked\n");
            await File.WriteAllTextAsync(Path.Combine(root, "notes Ω.txt"), "Unicode path\n");
            await TestAppBuilder.RunAsync(async () =>
            {
                using var workspace = new WorkspaceViewModel(root) { ActiveWorkingCopyRoot = root, ProjectName = "Git fixture" };
                workspace.ShowChangesCommand.Execute(null);
                var changes = workspace.Changes;
                await changes.RefreshAsync();
                Assert.Equal(3, changes.Files.Count);
                Assert.Contains(changes.Files, x => x.Path == "notes Ω.txt");
                changes.Selected = Assert.Single(changes.Files, x => x.Path == "a[1].txt");
                await changes.StageCommand.ExecuteAsync(null);
                Assert.Equal("a[1].txt", Assert.Single(changes.Files, x => x.Staged).Path);
                changes.Selected = Assert.Single(changes.Files, x => x.Staged);
                await changes.UnstageCommand.ExecuteAsync(null);
                Assert.DoesNotContain(changes.Files, x => x.Staged);
                Assert.True(File.Exists(Path.Combine(root, "a[1].txt")));
                changes.Selected = Assert.Single(changes.Files, x => x.Path == "a[1].txt");
                await changes.StageCommand.ExecuteAsync(null);
                changes.CommitMessage = "Commit the selected fixture file";
                Assert.True(changes.CanCommit);
                changes.Selected = Assert.Single(changes.Files, x => x.Staged);
                for (var attempt = 0; attempt < 100 && changes.Diff == "Loading change…"; attempt++) await Task.Delay(20);
                Assert.Contains("+Literal bracket path", changes.Diff);
                var window = new MainWindow { DataContext = workspace, Width = 1600, Height = 1000 };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var frame = window.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                    var output = Environment.GetEnvironmentVariable("POWERFORGE_STUDIO_VISUAL_OUTPUT");
                    if (!string.IsNullOrEmpty(output))
                    {
                        Directory.CreateDirectory(output);
                        frame.Save(Path.Combine(output, "git-changes.png"), PngBitmapEncoderOptions.Default);
                        window.Width = 1050; window.Height = 720;
                        window.UpdateLayout();
                        Dispatcher.UIThread.RunJobs();
                        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                        using var compact = window.CaptureRenderedFrame();
                        Assert.NotNull(compact);
                        compact.Save(Path.Combine(output, "git-changes-compact.png"), PngBitmapEncoderOptions.Default);
                        var pageScroll = Assert.Single(window.GetVisualDescendants().OfType<ScrollViewer>(), x => x.Name == "PageScroll");
                        pageScroll.ScrollToEnd();
                        window.UpdateLayout();
                        Dispatcher.UIThread.RunJobs();
                        Assert.True(pageScroll.Offset.Y > 0);
                        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                        using var commitFrame = window.CaptureRenderedFrame();
                        Assert.NotNull(commitFrame);
                        commitFrame.Save(Path.Combine(output, "git-changes-compact-commit.png"), PngBitmapEncoderOptions.Default);
                    }
                    await changes.CommitCommand.ExecuteAsync(null);
                    Assert.Equal("", changes.CommitMessage);
                    Assert.Equal(0, changes.Snapshot!.StagedCount);
                    Assert.Equal(2, changes.Snapshot.UntrackedCount);
                    Assert.Contains("completed", changes.LastOperation);
                    var log = await new ProjectGitService().GetLogAsync(root);
                    Assert.Equal("Commit the selected fixture file", Assert.Single(log).Message);
                    await Run("mv", "--", "a[1].txt", "renamed Ω.txt");
                    await changes.RefreshAsync();
                    var rename = Assert.Single(changes.Files, x => x.Staged);
                    Assert.Equal("renamed Ω.txt", rename.Path);
                    Assert.Equal("a[1].txt", rename.Change.OriginalPath);
                    Assert.Equal(GitChangeKind.Renamed, rename.Change.Kind);
                    changes.Selected = rename;
                    for (var attempt = 0; attempt < 100 && changes.Diff == "Loading change…"; attempt++) await Task.Delay(20);
                    Assert.Contains("rename from a[1].txt", changes.Diff);
                    Assert.Contains("similarity index 100%", changes.Diff);
                    Assert.DoesNotContain("new file mode", changes.Diff);
                    await changes.UnstageCommand.ExecuteAsync(null);
                    Assert.Equal(0, changes.Snapshot!.StagedCount);
                    Assert.Contains(changes.Files, x => x.Path == "a[1].txt" && x.Change.Kind == GitChangeKind.Deleted);
                    Assert.True(File.Exists(Path.Combine(root, "renamed Ω.txt")));
                    changes.CommitMessage = "Unfinished draft";
                    changes.SetWorkingCopy("");
                    changes.SetWorkingCopy(root);
                    Assert.Equal("Unfinished draft", changes.CommitMessage);
                    await changes.RefreshAsync();
                }
                finally { window.Close(); }
                return true;
            });
        }
        finally
        {
            foreach (var file in new DirectoryInfo(root).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
                if (file.IsReadOnly) file.IsReadOnly = false;
            Directory.Delete(root, recursive: true);
        }
    }
}
