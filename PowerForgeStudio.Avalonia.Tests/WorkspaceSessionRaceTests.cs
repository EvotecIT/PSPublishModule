using PowerForgeStudio.Avalonia.ViewModels;
using PowerForgeStudio.Domain.Hub;
using PowerForgeStudio.Domain.Workspace;
using PowerForgeStudio.Orchestrator.Explorer;
using PowerForgeStudio.Orchestrator.Workspace;

namespace PowerForgeStudio.Avalonia.Tests;

public sealed class WorkspaceSessionRaceTests
{
    [Fact]
    public async Task FailedDiscoveryDoesNotReplacePreviouslySavedDocumentsOrExpansion()
    {
        var directory = Path.Combine(Path.GetTempPath(), "studio-session-discovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var missingWorkspace = Path.Combine(directory, "OfflineWorkspace");
            var store = new WorkspaceRootCatalogService(Path.Combine(directory, "workspace-roots.json"));
            var document = new WorkspaceDocumentReference(missingWorkspace, Path.Combine(missingWorkspace, "README.md"));
            store.SaveSession(missingWorkspace, [document], document, [missingWorkspace]);
            await TestAppBuilder.RunAsync(async () =>
            {
                using var model = new WorkspaceViewModel(missingWorkspace, store);
                await model.RefreshAsync();
                await model.SaveSessionAsync();
                var saved = store.LoadExplorer(missingWorkspace);
                Assert.Equal(document, Assert.Single(saved.OpenDocuments));
                Assert.Equal(document, saved.ActiveDocument);
                Assert.Equal(missingWorkspace, Assert.Single(saved.ExpandedPaths));
                // A second unsuccessful scan must preserve the pending saved state too.
                await model.RefreshAsync();
                await model.SaveSessionAsync();
                Assert.Equal(document, Assert.Single(store.LoadExplorer(missingWorkspace).OpenDocuments));
                return true;
            });
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task ClosingOnlyTabInvalidatesItsPendingFilesystemRead()
    {
        var directory = Path.Combine(Path.GetTempPath(), "studio-session-close-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "README.md");
        await File.WriteAllTextAsync(path, "Contents of closed document");
        try
        {
            await TestAppBuilder.RunAsync(async () =>
            {
                var files = new DelayedExplorer();
                using var model = new WorkspaceViewModel(directory, files: files);
                var loading = model.SelectAsync(new ExplorerNode("README.md", path, "file", directory));
                await files.Entered.Task;
                await model.CloseDocumentCommand.ExecuteAsync(Assert.Single(model.Documents));
                files.Resume.TrySetResult();
                await loading;
                Assert.Empty(model.Documents);
                Assert.True(model.IsWorkspaceTab);
                Assert.False(model.IsSelectionLoading);
                Assert.Null(model.SelectedFile);
                Assert.Equal("Workspace", model.PreviewTitle);
                Assert.DoesNotContain("Contents of closed document", model.Preview);
                return true;
            });
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task CollapsingBuildDuringItsFirstReadIsNotUndoneBySelection()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "studio-tree-collapse-" + Guid.NewGuid().ToString("N"));
        var projectRoot = Path.Combine(workspace, "Sample");
        var buildPath = Path.Combine(projectRoot, "Build");
        Directory.CreateDirectory(buildPath);
        await File.WriteAllTextAsync(Path.Combine(buildPath, "project.build.json"), "{}");
        try
        {
            await TestAppBuilder.RunAsync(async () =>
            {
                var files = new DelayedBuildExplorer(buildPath);
                using var model = new WorkspaceViewModel(workspace, files: files);
                await model.RefreshAsync();
                var project = Assert.Single(model.Projects);
                var selection = model.SelectAsync(project);
                await files.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var primary = Assert.Single(project.Children, child => child.Path == projectRoot);
                var build = Assert.Single(primary.Children, child => child.Name == "Build");
                Assert.True(build.IsExpanded);
                build.IsExpanded = false;
                files.Resume.TrySetResult();
                await selection;
                Assert.False(build.IsExpanded);
                return true;
            });
        }
        finally { Directory.Delete(workspace, recursive: true); }
    }

    private sealed class DelayedBuildExplorer(string buildPath) : IFileExplorerService
    {
        private readonly FileExplorerService _inner = new();
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<IReadOnlyList<FileSystemEntry>> ListDirectoryAsync(string path, CancellationToken cancellationToken = default)
        {
            if (string.Equals(path, buildPath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                Entered.TrySetResult();
                await Resume.Task.WaitAsync(cancellationToken);
            }
            return await _inner.ListDirectoryAsync(path, cancellationToken);
        }
        public Task<string> ReadTextPreviewAsync(string path, CancellationToken cancellationToken = default)
            => _inner.ReadTextPreviewAsync(path, cancellationToken);
        public Task<PowerForge.RepositoryTextDocument> OpenTextDocumentAsync(string root, string path, CancellationToken cancellationToken = default)
            => _inner.OpenTextDocumentAsync(root, path, cancellationToken);
        public Task<PowerForge.RepositoryTextDocument> SaveTextDocumentAsync(string root, PowerForge.RepositoryTextDocument original, string text, CancellationToken cancellationToken = default)
            => _inner.SaveTextDocumentAsync(root, original, text, cancellationToken);
        public Task ExecuteAsync(WorkspaceFileOperationRequest request, CancellationToken cancellationToken = default, IProgress<FileTransferProgress>? progress = null)
            => _inner.ExecuteAsync(request, cancellationToken, progress);
    }

    private sealed class DelayedExplorer : IFileExplorerService
    {
        private readonly FileExplorerService _inner = new();
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<IReadOnlyList<FileSystemEntry>> ListDirectoryAsync(string path, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Resume.Task.WaitAsync(cancellationToken);
            return await _inner.ListDirectoryAsync(path, cancellationToken);
        }
        public Task<string> ReadTextPreviewAsync(string path, CancellationToken cancellationToken = default)
            => _inner.ReadTextPreviewAsync(path, cancellationToken);
        public Task<PowerForge.RepositoryTextDocument> OpenTextDocumentAsync(string root, string path, CancellationToken cancellationToken = default)
            => _inner.OpenTextDocumentAsync(root, path, cancellationToken);
        public Task<PowerForge.RepositoryTextDocument> SaveTextDocumentAsync(string root, PowerForge.RepositoryTextDocument original, string text, CancellationToken cancellationToken = default)
            => _inner.SaveTextDocumentAsync(root, original, text, cancellationToken);
        public Task ExecuteAsync(WorkspaceFileOperationRequest request, CancellationToken cancellationToken = default, IProgress<FileTransferProgress>? progress = null)
            => _inner.ExecuteAsync(request, cancellationToken, progress);
    }
}
