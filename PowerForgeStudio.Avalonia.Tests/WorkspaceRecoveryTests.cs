using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PowerForge;
using PowerForgeStudio.Avalonia.ViewModels;
using PowerForgeStudio.Avalonia.Views;
using PowerForgeStudio.Orchestrator.Explorer;

namespace PowerForgeStudio.Avalonia.Tests;

public sealed class WorkspaceRecoveryTests
{
    [Fact]
    public async Task ReviewedDeleteBlocksDirtyDocumentThenRestoresDurableEntry()
    {
        var fixture = Path.Combine(
            Path.GetTempPath(),
            "studio-ui-recovery-" + Guid.NewGuid().ToString("N"));
        var root = Directory.CreateDirectory(Path.Combine(fixture, "Repository")).FullName;
        var recoveryRoot = Path.Combine(fixture, "Recovery");
        var documentPath = Path.Combine(root, "README.md");
        await File.WriteAllTextAsync(documentPath, "# Original content");
        try
        {
            Assert.True((await new GitClient().RunRawAsync(root, ["init", "-b", "main"])).Succeeded);
            await TestAppBuilder.RunAsync(async () =>
            {
                using var model = new WorkspaceViewModel(
                    root,
                    recovery: new FileRecoveryService(recoveryRoot));
                await model.RefreshAsync();
                var project = Assert.Single(model.Projects);
                await project.EnsureLoadedAsync();
                var checkout = Assert.Single(project.Children, node => node.Kind == "branch");
                var readme = Assert.Single(checkout.Children, node => node.Name == "README.md");
                await model.SelectAsync(readme);
                model.SelectedFile = Assert.Single(model.Files, file => file.Name == "README.md");
                var preview = await model.InspectSelectedFileForDeletionAsync();

                await model.EditDocumentCommand.ExecuteAsync(null);
                Assert.NotNull(model.ActiveDocument);
                model.ActiveDocument!.Text = "# Unsaved draft";
                Assert.False(await model.DeleteToRecoveryAsync(preview));
                Assert.True(File.Exists(documentPath));
                Assert.Contains("drafts", model.Status, StringComparison.OrdinalIgnoreCase);
                model.ActiveDocument.Discard();

                var window = new MainWindow { DataContext = model, Width = 1600, Height = 1000 };
                window.Show();
                try
                {
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    var output = Environment.GetEnvironmentVariable("POWERFORGE_STUDIO_VISUAL_OUTPUT");
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        Directory.CreateDirectory(output);
                        using var workspaceFrame = window.CaptureRenderedFrame();
                        Assert.NotNull(workspaceFrame);
                        workspaceFrame.Save(Path.Combine(output, "workspace-recovery-toolbar.png"), PngBitmapEncoderOptions.Default);
                    }

                    var deleteModel = new FileDeleteViewModel(model, preview);
                    var deleteDialog = new FileDeleteDialog { DataContext = deleteModel };
                    var deleteShown = deleteDialog.ShowDialog(window);
                    deleteDialog.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        using var deleteFrame = deleteDialog.CaptureRenderedFrame();
                        Assert.NotNull(deleteFrame);
                        deleteFrame.Save(Path.Combine(output, "file-delete-review.png"), PngBitmapEncoderOptions.Default);
                    }
                    deleteModel.Confirmed = true;
                    Assert.True(deleteModel.CanSubmit);
                    Assert.True(await deleteModel.SubmitAsync(), deleteModel.Error);
                    deleteDialog.Close();
                    await deleteShown;

                    Assert.False(File.Exists(documentPath));
                    Assert.Empty(model.Documents);
                    var recoveryModel = new FileRecoveryViewModel(model);
                    await recoveryModel.RefreshAsync();
                    Assert.Single(recoveryModel.Entries);
                    var recoveryDialog = new FileRecoveryDialog { DataContext = recoveryModel, Width = 760, Height = 520 };
                    var recoveryShown = recoveryDialog.ShowDialog(window);
                    recoveryDialog.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        using var recoveryFrame = recoveryDialog.CaptureRenderedFrame();
                        Assert.NotNull(recoveryFrame);
                        recoveryFrame.Save(Path.Combine(output, "file-recovery.png"), PngBitmapEncoderOptions.Default);
                        recoveryDialog.Width = 620;
                        recoveryDialog.Height = 400;
                        recoveryDialog.UpdateLayout();
                        Dispatcher.UIThread.RunJobs();
                        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                        using var compactFrame = recoveryDialog.CaptureRenderedFrame();
                        Assert.NotNull(compactFrame);
                        compactFrame.Save(Path.Combine(output, "file-recovery-compact.png"), PngBitmapEncoderOptions.Default);
                    }

                    await recoveryModel.RestoreSelectedAsync();
                    Assert.Empty(recoveryModel.Entries);
                    Assert.Equal("# Original content", await File.ReadAllTextAsync(documentPath));

                    model.SelectedFile = Assert.Single(model.Files, file => file.Name == "README.md");
                    Assert.True(await model.DeleteToRecoveryAsync(
                        await model.InspectSelectedFileForDeletionAsync()));
                    await recoveryModel.RefreshAsync();
                    var permanentEntry = Assert.Single(recoveryModel.Entries);
                    recoveryModel.ConfirmPermanentDelete = true;
                    Assert.True(recoveryModel.CanDeletePermanently);
                    await recoveryModel.DeleteSelectedPermanentlyAsync();
                    Assert.Empty(recoveryModel.Entries);
                    Assert.False(File.Exists(permanentEntry.RecoveryPath));
                    Assert.False(File.Exists(documentPath));
                    recoveryDialog.Close();
                    await recoveryShown;
                }
                finally
                {
                    window.Close();
                }

                return true;
            });
        }
        finally
        {
            if (Directory.Exists(fixture))
                Directory.Delete(fixture, recursive: true);
        }
    }
}
