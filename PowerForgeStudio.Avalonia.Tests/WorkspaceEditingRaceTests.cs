using PowerForge;
using PowerForgeStudio.Avalonia.ViewModels;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Hub;
using PowerForgeStudio.Orchestrator.Catalog;
using PowerForgeStudio.Orchestrator.Explorer;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;

namespace PowerForgeStudio.Avalonia.Tests;

public sealed class WorkspaceEditingRaceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "studio-editor-races-" + Guid.NewGuid().ToString("N"));
    public WorkspaceEditingRaceTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "first.txt"), "first original");
        File.WriteAllText(Path.Combine(_root, "second.txt"), "second original");
    }
    public void Dispose()
    {
        for (var attempt = 0; ; attempt++)
        {
            try { Directory.Delete(_root, recursive: true); return; }
            catch (IOException) when (attempt < 9) { Thread.Sleep(200); }
            catch (UnauthorizedAccessException) when (attempt < 9) { Thread.Sleep(200); }
        }
    }

    [Fact]
    public async Task RefreshRetainsNewDraftAndDoesNotReopenATabClosedDuringDiscovery()
    {
        Assert.True((await new GitClient().RunRawAsync(_root, ["init", "-b", "main"])).Succeeded);
        await TestAppBuilder.RunAsync(async () =>
        {
            var repositories = new DelayedRepositories();
            using var model = new WorkspaceViewModel(_root, repositories: repositories);
            await model.RefreshAsync();
            await model.SelectAsync(new ExplorerNode("first.txt", Path.Combine(_root, "first.txt"), "file", _root));
            var first = Assert.Single(model.Documents);
            repositories.Delay = true;
            var refresh = model.RefreshAsync();
            await repositories.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            try
            {
                await model.CloseDocumentCommand.ExecuteAsync(first);
                await model.SelectAsync(new ExplorerNode("second.txt", Path.Combine(_root, "second.txt"), "file", _root));
                await model.EditDocumentCommand.ExecuteAsync(null);
                var draft = Assert.Single(model.Documents);
                draft.Text = "draft opened during discovery";
                repositories.Resume.TrySetResult();
                await refresh;
                Assert.Same(draft, Assert.Single(model.Documents));
                Assert.Same(draft, model.ActiveDocument);
                Assert.True(draft.IsDirty);
                Assert.True(await model.SaveDocumentAsync(draft));
                Assert.Equal("draft opened during discovery", await File.ReadAllTextAsync(draft.Location));
            }
            finally { repositories.Resume.TrySetResult(); await refresh; }
            return true;
        });
    }

    [Fact]
    public async Task RelocationLocksTheEditorAndRetainsAnyLateDraftUpdateInTheSameDocument()
    {
        await TestAppBuilder.RunAsync(async () =>
        {
            var files = new DelayedMove();
            using var model = new WorkspaceViewModel(_root, files: files);
            var original = Path.Combine(_root, "first.txt");
            var destination = Path.Combine(_root, "renamed.txt");
            await model.SelectAsync(new ExplorerNode("first.txt", original, "file", _root));
            await model.EditDocumentCommand.ExecuteAsync(null);
            var document = Assert.Single(model.Documents);
            var window = new MainWindow { DataContext = model };
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Closed += (_, _) => closed.TrySetResult();
            window.Show(); window.UpdateLayout();
            var moving = model.ExecuteFileOperationAsync(new(WorkspaceFileOperation.Rename, _root, original, destination));
            await files.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            try
            {
                Assert.True(model.IsEditorReadOnly);
                var editor = Assert.Single(window.GetVisualDescendants().OfType<TextBox>(), box => box.Name == "EditorBox");
                Assert.True(editor.IsReadOnly);
                Assert.True(editor.Focus());
                window.KeyTextInput("must not enter the draft");
                Assert.Equal("first original", document.Text);
                // A late binding update must remain safe even after the input control becomes read-only.
                document.Text = "late draft during move";
                Assert.False(await model.SaveDocumentAsync(document));
                files.Resume.TrySetResult();
                Assert.True(await moving);
                Assert.Same(document, Assert.Single(model.Documents));
                Assert.Equal(destination, document.Location);
                Assert.True(document.IsDirty);
                Assert.False(model.IsEditorReadOnly);
                Assert.True(await model.SaveDocumentAsync(document));
                Assert.Equal("late draft during move", await File.ReadAllTextAsync(destination));
                Assert.False(File.Exists(original));
            }
            finally
            {
                files.Resume.TrySetResult(); await moving;
                model.ResolveUnsavedChanges = _ => Task.FromResult(UnsavedChangesChoice.Discard);
                window.Close(); await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            return true;
        });
    }

    private sealed class DelayedRepositories : IWorkspaceRepositorySource
    {
        public bool Delay { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<IReadOnlyList<RepositoryCatalogEntry>> DiscoverAsync(string root, CancellationToken cancellationToken = default)
        {
            if (Delay) { Entered.TrySetResult(); await Resume.Task.WaitAsync(cancellationToken); }
            return await new WorkspaceRepositorySource().DiscoverAsync(root, cancellationToken);
        }
    }

    private sealed class DelayedMove : IFileExplorerService
    {
        private readonly FileExplorerService _inner = new();
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<FileSystemEntry>> ListDirectoryAsync(string path, CancellationToken cancellationToken = default)
            => _inner.ListDirectoryAsync(path, cancellationToken);
        public Task<string> ReadTextPreviewAsync(string path, CancellationToken cancellationToken = default)
            => _inner.ReadTextPreviewAsync(path, cancellationToken);
        public Task<RepositoryTextDocument> OpenTextDocumentAsync(string root, string path, CancellationToken cancellationToken = default)
            => _inner.OpenTextDocumentAsync(root, path, cancellationToken);
        public Task<RepositoryTextDocument> SaveTextDocumentAsync(string root, RepositoryTextDocument original, string text, CancellationToken cancellationToken = default)
            => _inner.SaveTextDocumentAsync(root, original, text, cancellationToken);
        public async Task ExecuteAsync(WorkspaceFileOperationRequest request, CancellationToken cancellationToken = default, IProgress<FileTransferProgress>? progress = null)
        {
            Entered.TrySetResult();
            await Resume.Task.WaitAsync(cancellationToken);
            await _inner.ExecuteAsync(request, cancellationToken, progress);
        }
    }
}
