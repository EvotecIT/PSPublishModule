using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PowerForgeStudio.Avalonia.ViewModels;
using PowerForgeStudio.Domain.Hub;
using PowerForge;

namespace PowerForgeStudio.Avalonia.Tests;

public sealed class WorkspaceEditingTests
{
    [Fact]
    public async Task DraftSurvivesNavigationAndRefreshKeyboardSaveWorksAndConflictsKeepBothVersions()
    {
        var root = Path.Combine(Path.GetTempPath(), "studio-editor-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "Build-Project.ps1");
        await File.WriteAllTextAsync(path, "# Build project\r\nWrite-Output 'ready'\r\n");
        await File.WriteAllTextAsync(Path.Combine(root, "README.md"), "# Project");
        Assert.True((await new GitClient().RunRawAsync(root, ["init", "-b", "main"])).Succeeded);
        try
        {
            await TestAppBuilder.RunAsync(async () =>
            {
                using var model = new WorkspaceViewModel(root);
                await model.RefreshAsync();
                await model.SelectAsync(new ExplorerNode("Build-Project.ps1", path, "powershell", root));
                await model.EditDocumentCommand.ExecuteAsync(null);
                var document = Assert.Single(model.Documents);
                var window = new MainWindow { DataContext = model, Width = 1600, Height = 1000 };
                var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                window.Closed += (_, _) => closed.TrySetResult();
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                model.ResolveUnsavedChanges = _ => Task.FromResult(UnsavedChangesChoice.Cancel);
                try
                {
                    var editor = Assert.Single(window.GetVisualDescendants().OfType<TextBox>(), box => box.Name == "EditorBox");
                    Assert.True(editor.Focus()); editor.CaretIndex = editor.Text!.Length;
                    window.KeyTextInput("# local draft");
                    Assert.True(document.IsDirty);
                    Assert.True(model.Build.HasUnsavedChanges);
                    Assert.False(model.Build.CanPlan);
                    Assert.False(model.Build.CanBuild);
                    Assert.Contains("●", document.Caption);
                    var draft = document.Text;
                    await model.SelectAsync(new ExplorerNode("README.md", Path.Combine(root, "README.md"), "file", root));
                    await model.SelectDocumentCommand.ExecuteAsync(document);
                    Assert.Equal(draft, document.Text);
                    await model.RefreshAsync();
                    Assert.Same(document, model.ActiveDocument);
                    Assert.Equal(draft, document.Text);
                    Assert.False(await model.ExecuteFileOperationAsync(new(WorkspaceFileOperation.Rename, root, path, Path.Combine(root, "Renamed.ps1"))));
                    Assert.True(editor.Focus());
                    window.KeyPress(Key.S, RawInputModifiers.Control, PhysicalKey.None, null);
                    window.KeyRelease(Key.S, RawInputModifiers.Control, PhysicalKey.None, null);
                    if (model.SaveActiveDocumentCommand.ExecutionTask is { } saving) await saving;
                    Assert.False(document.IsDirty);
                    Assert.False(model.Build.HasUnsavedChanges);
                    Assert.True(model.Build.CanPlan);
                    Assert.False(model.Build.HasSuccessfulInspection);
                    Assert.Equal(draft, await File.ReadAllTextAsync(path));

                    document.Text += "\r\n# another draft";
                    await File.WriteAllTextAsync(path, "# external edit");
                    Assert.False(await model.SaveDocumentAsync(document));
                    Assert.True(document.IsDirty);
                    Assert.Contains("changed on disk", document.Error);
                    Assert.Equal("# external edit", await File.ReadAllTextAsync(path));
                    foreach (var compact in new[] { false, true })
                    {
                        window.Width = compact ? 1050 : 1600; window.Height = compact ? 720 : 1000;
                        window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                        Assert.True(editor.Bounds.Height >= 60, $"Editor height: {editor.Bounds.Height}");
                        using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                        if (Environment.GetEnvironmentVariable("POWERFORGE_STUDIO_VISUAL_OUTPUT") is { Length: > 0 } output)
                            frame.Save(Path.Combine(output, compact ? "workspace-editor-compact.png" : "workspace-editor.png"), PngBitmapEncoderOptions.Default);
                    }
                    await model.CloseDocumentCommand.ExecuteAsync(document);
                    Assert.Contains(document, model.Documents);
                    Assert.False(await model.ActivateWorkspaceAsync(Path.GetTempPath()));
                    model.ResolveUnsavedChanges = _ => Task.FromResult(UnsavedChangesChoice.Discard);
                    await model.ReloadDocumentCommand.ExecuteAsync(null);
                    Assert.False(document.IsDirty);
                    Assert.Equal("# external edit", document.Text);
                    Assert.False(document.HasError);
                }
                finally { model.ResolveUnsavedChanges = _ => Task.FromResult(UnsavedChangesChoice.Discard); window.Close(); await closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
                return true;
            });
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task WindowCloseOffersCancelThenSaveForUnsavedDocument()
    {
        var root = Path.Combine(Path.GetTempPath(), "studio-editor-close-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "README.md");
        await File.WriteAllTextAsync(path, "original");
        try
        {
            await TestAppBuilder.RunAsync(async () =>
            {
                using var model = new WorkspaceViewModel(root);
                await model.SelectAsync(new ExplorerNode("README.md", path, "file", root));
                await model.EditDocumentCommand.ExecuteAsync(null);
                model.ActiveDocument!.Text = "saved through close dialog";
                var window = new MainWindow { DataContext = model };
                var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                window.Closed += (_, _) => closed.TrySetResult();
                window.Show();
                window.Close();
                var dialog = Assert.Single(window.OwnedWindows);
                dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                Assert.False(closed.Task.IsCompleted);
                Assert.True(model.ActiveDocument.IsDirty);
                Assert.Equal("original", await File.ReadAllTextAsync(path));
                window.Close();
                dialog = Assert.Single(window.OwnedWindows);
                dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal("saved through close dialog", await File.ReadAllTextAsync(path));
                return true;
            });
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
