using PowerForgeStudio.Domain.Hub;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed partial class WorkspaceViewModel
{
    public async Task<WorkspaceFileDeletionPreview> InspectSelectedFileForDeletionAsync(
        CancellationToken cancellationToken = default)
    {
        if (SelectedFile is not { } selected || string.IsNullOrWhiteSpace(ActiveWorkingCopyRoot))
            throw new InvalidOperationException("Select a file or folder first.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        return await _recovery.InspectDeleteAsync(
            ActiveWorkingCopyRoot,
            selected.FullPath,
            linked.Token);
    }

    public async Task<bool> DeleteToRecoveryAsync(
        WorkspaceFileDeletionPreview preview,
        CancellationToken cancellationToken = default)
    {
        if (IsFileOperationRunning)
            return false;
        var affectedDocuments = Documents
            .Where(document => SamePath(document.Reference.WorkingCopyRoot, preview.WorkingCopyRoot) &&
                               IsSameOrWithin(preview.SourcePath, document.Location))
            .ToArray();
        if (affectedDocuments.Any(static document => document.IsDirty || document.IsBusy))
        {
            Status = "Save or discard drafts inside the selected item before moving it to recovery.";
            return false;
        }

        IsFileOperationRunning = true;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        try
        {
            var entry = await _recovery.DeleteAsync(preview, linked.Token);
            foreach (var document in affectedDocuments)
                Documents.Remove(document);
            if (ActiveDocument is { } activeDocument && affectedDocuments.Contains(activeDocument))
                ActiveDocument = null;
            NotifyEditorChanged();
            AppendOutput($"Moved to recovery: {entry.OriginalPath}");
            await RefreshAfterRecoveryChangeAsync(entry.WorkingCopyRoot, entry.OriginalPath);
            await SaveSessionAsync();
            Status = $"Moved {Path.GetFileName(entry.OriginalPath)} to recovery";
            return true;
        }
        catch (OperationCanceledException)
        {
            Status = "Delete operation cancelled";
            return false;
        }
        catch (Exception ex)
        {
            Report(ex);
            return false;
        }
        finally
        {
            IsFileOperationRunning = false;
        }
    }

    public Task<IReadOnlyList<WorkspaceFileRecoveryEntry>> ListRecoveryEntriesAsync(
        CancellationToken cancellationToken = default)
        => _recovery.ListAsync(HasWorkingCopy ? ActiveWorkingCopyRoot : null, cancellationToken);

    public async Task<bool> RestoreRecoveryEntryAsync(
        WorkspaceFileRecoveryEntry entry,
        CancellationToken cancellationToken = default)
    {
        if (IsFileOperationRunning)
            return false;
        IsFileOperationRunning = true;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        try
        {
            await _recovery.RestoreAsync(entry, linked.Token);
            AppendOutput($"Restored from recovery: {entry.OriginalPath}");
            await RefreshAfterRecoveryChangeAsync(entry.WorkingCopyRoot, entry.OriginalPath);
            Status = $"Restored {Path.GetFileName(entry.OriginalPath)}";
            return true;
        }
        catch (OperationCanceledException)
        {
            Status = "Restore operation cancelled";
            return false;
        }
        catch (Exception ex)
        {
            Report(ex);
            return false;
        }
        finally
        {
            IsFileOperationRunning = false;
        }
    }

    public async Task<bool> DeleteRecoveryEntryPermanentlyAsync(
        WorkspaceFileRecoveryEntry entry,
        CancellationToken cancellationToken = default)
    {
        if (IsFileOperationRunning)
            return false;
        IsFileOperationRunning = true;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        try
        {
            await _recovery.DeletePermanentlyAsync(entry, linked.Token);
            AppendOutput($"Deleted recovery entry permanently: {entry.OriginalPath}");
            Status = $"Deleted {Path.GetFileName(entry.OriginalPath)} permanently";
            return true;
        }
        catch (OperationCanceledException)
        {
            Status = "Recovery cleanup cancelled";
            return false;
        }
        catch (Exception ex)
        {
            Report(ex);
            return false;
        }
        finally
        {
            IsFileOperationRunning = false;
        }
    }

    private async Task RefreshAfterRecoveryChangeAsync(string workingCopyRoot, string path)
    {
        var parent = Path.GetDirectoryName(path);
        foreach (var node in LoadedNodes().Where(node => node.Kind is "folder" or "branch" &&
                     parent is not null && node.Path.Length > 0 && SamePath(node.Path, parent)).ToArray())
            await node.ReloadAsync();
        if (!_disposed && SamePath(workingCopyRoot, ActiveWorkingCopyRoot) &&
            parent is not null && SamePath(parent, CurrentDirectory))
            await RefreshFolderAsync();
    }

    private static bool IsSameOrWithin(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative == "." ||
               (!Path.IsPathRooted(relative) &&
                relative != ".." &&
                !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }
}
