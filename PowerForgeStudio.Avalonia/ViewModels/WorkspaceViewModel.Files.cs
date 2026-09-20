using CommunityToolkit.Mvvm.Input;
using PowerForgeStudio.Domain.Hub;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed partial class WorkspaceViewModel
{
    [RelayCommand]
    private async Task NavigateUpAsync()
    {
        if (!HasWorkingCopy || string.IsNullOrEmpty(CurrentDirectory) || SamePath(CurrentDirectory, ActiveWorkingCopyRoot)) return;
        var parent = Path.GetDirectoryName(CurrentDirectory);
        if (parent is not null)
            await SelectAsync(new ExplorerNode(Path.GetFileName(parent), parent, "folder", ActiveWorkingCopyRoot));
    }

    [RelayCommand]
    private async Task RefreshFolderAsync()
    {
        if (!HasWorkingCopy || !Directory.Exists(CurrentDirectory)) return;
        var directory = CurrentDirectory;
        var root = ActiveWorkingCopyRoot;
        var version = _selectionVersion;
        try
        {
            var entries = await _files.ListDirectoryAsync(directory, _lifetime.Token);
            if (_disposed || version != _selectionVersion || !SamePath(directory, CurrentDirectory) || !SamePath(root, ActiveWorkingCopyRoot)) return;
            SelectedFile = null;
            Files.Clear();
            foreach (var entry in entries) Files.Add(new FileItemViewModel(entry));
            foreach (var node in LoadedNodes().Where(n => n.Kind is "folder" or "branch" && n.Path.Length > 0 && SamePath(n.Path, directory)).ToArray())
                await node.ReloadAsync();
            if (_disposed || version != _selectionVersion) return;
            await SelectAsync(new ExplorerNode(Path.GetFileName(directory), directory, "folder", root));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Report(ex); }
    }

    /// <summary>Executes only the concrete paths returned by the file operation dialog.</summary>
    public async Task<bool> ExecuteFileOperationAsync(WorkspaceFileOperationRequest request, CancellationToken cancellationToken = default,
        IProgress<FileTransferProgress>? progress = null)
    {
        if (IsFileOperationRunning) return false;
        IsFileOperationRunning = true;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        try
        {
            await _files.ExecuteAsync(request, linked.Token, progress);
            AppendOutput($"{request.Operation}: {request.DestinationPath}");
            var destination = Path.GetFullPath(request.DestinationPath, request.WorkingCopyRoot);
            var source = request.SourcePath is null ? null : Path.GetFullPath(request.SourcePath, request.WorkingCopyRoot);
            var affected = new[] { Path.GetDirectoryName(destination), source is null ? null : Path.GetDirectoryName(source) };
            // Keep expanded sibling folders current even when they are not displayed in the center pane.
            foreach (var node in LoadedNodes().Where(n => n.Kind is "folder" or "branch" && n.Path.Length > 0 &&
                         affected.Any(path => path is not null && SamePath(n.Path, path))).ToArray())
                await node.ReloadAsync();
            if (!_disposed && SamePath(request.WorkingCopyRoot, ActiveWorkingCopyRoot))
                await RefreshFolderAsync();
            Status = $"{request.Operation} completed";
            return true;
        }
        catch (OperationCanceledException) { Status = "File operation cancelled"; return false; }
        catch (Exception ex) { Report(ex); return false; }
        finally { IsFileOperationRunning = false; }
    }

    private IEnumerable<ExplorerNode> LoadedNodes()
    {
        var pending = new Stack<ExplorerNode>(_projectNodes.Values);
        while (pending.TryPop(out var node))
        {
            yield return node;
            foreach (var child in node.Children) pending.Push(child);
        }
    }

    public void ReportFileActionError(Exception exception) => Report(exception);
}
