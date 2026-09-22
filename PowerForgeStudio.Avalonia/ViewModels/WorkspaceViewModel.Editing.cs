using CommunityToolkit.Mvvm.Input;

namespace PowerForgeStudio.Avalonia.ViewModels;

public enum UnsavedChangesChoice { Cancel, Save, Discard }

public sealed partial class WorkspaceViewModel
{
    public Func<IReadOnlyList<WorkspaceDocumentViewModel>, Task<UnsavedChangesChoice>>? ResolveUnsavedChanges { get; set; }
    public bool IsEditorVisible => ActiveDocument?.IsEditing == true;
    public bool IsPreviewVisible => !IsEditorVisible;
    public int EditorColumn => IsEditorVisible ? 0 : 2;
    public int EditorColumnSpan => IsEditorVisible ? 3 : 1;
    public bool CanEditDocument => ActiveDocument is { IsBusy: false } && !IsSelectionLoading && !IsFileOperationRunning;
    public bool CanSaveDocument => ActiveDocument is { IsDirty: true, IsBusy: false } && !IsFileOperationRunning;
    public bool IsEditorReadOnly => ActiveDocument?.IsBusy == true || IsFileOperationRunning;

    private WorkspaceDocumentViewModel CreateDocument(PowerForgeStudio.Domain.Workspace.WorkspaceDocumentReference reference)
    {
        var document = new WorkspaceDocumentViewModel(reference);
        document.PropertyChanged += (_, _) => NotifyEditorChanged();
        return document;
    }

    private void NotifyEditorChanged()
    {
        Build.HasUnsavedChanges = Documents.Any(document => document.IsDirty && !string.IsNullOrEmpty(ActiveWorkingCopyRoot) && SamePath(document.Reference.WorkingCopyRoot, ActiveWorkingCopyRoot));
        OnPropertyChanged(nameof(IsEditorVisible)); OnPropertyChanged(nameof(IsPreviewVisible));
        OnPropertyChanged(nameof(EditorColumn)); OnPropertyChanged(nameof(EditorColumnSpan));
        OnPropertyChanged(nameof(CanEditDocument)); OnPropertyChanged(nameof(CanSaveDocument));
        OnPropertyChanged(nameof(IsEditorReadOnly));
    }

    [RelayCommand]
    private async Task EditDocumentAsync()
    {
        var document = ActiveDocument;
        if (document is null || document.IsBusy || document.IsEditing || IsSelectionLoading || IsFileOperationRunning) return;
        document.IsBusy = true;
        try
        {
            var snapshot = await _files.OpenTextDocumentAsync(document.Reference.WorkingCopyRoot, document.Location, _lifetime.Token);
            if (!_disposed && Documents.Contains(document)) document.Load(snapshot);
        }
        catch (Exception ex) { document.Error = StudioDisplayError.From(ex); Report(ex); }
        finally { document.IsBusy = false; }
    }

    [RelayCommand]
    private Task SaveActiveDocumentAsync() => ActiveDocument is { } document ? SaveDocumentAsync(document) : Task.FromResult(false);

    public async Task<bool> SaveDocumentAsync(WorkspaceDocumentViewModel document)
    {
        if (document.IsBusy || document.Snapshot is null || IsFileOperationRunning) return false;
        if (!document.IsDirty) return true;
        document.IsBusy = true;
        var text = document.Text;
        try
        {
            var saved = await _files.SaveTextDocumentAsync(document.Reference.WorkingCopyRoot, document.Snapshot, text, _lifetime.Token);
            document.Saved(saved);
            if (ReferenceEquals(document, ActiveDocument)) Preview = saved.Text;
            AppendOutput($"Saved {document.Location}");
            Status = $"Saved {document.Name}";
            return !document.IsDirty;
        }
        catch (Exception ex) { document.Error = StudioDisplayError.From(ex); Report(ex); return false; }
        finally { document.IsBusy = false; }
    }

    [RelayCommand]
    private async Task ReloadDocumentAsync()
    {
        var document = ActiveDocument;
        if (document is null || !await ResolveUnsavedDocumentsAsync([document])) return;
        document.Discard();
        if (ReferenceEquals(document, ActiveDocument)) await EditDocumentAsync();
    }

    public async Task<bool> ResolveUnsavedDocumentsAsync(IReadOnlyList<WorkspaceDocumentViewModel>? documents = null)
    {
        documents ??= Documents.ToArray();
        if (documents.Any(document => document.IsBusy)) { Status = "Wait for the document operation to finish."; return false; }
        var dirty = documents.Where(document => document.IsDirty).ToArray();
        if (dirty.Length == 0) return true;
        var choice = ResolveUnsavedChanges is null ? UnsavedChangesChoice.Cancel : await ResolveUnsavedChanges(dirty);
        if (choice == UnsavedChangesChoice.Cancel) return false;
        foreach (var document in dirty)
        {
            if (choice == UnsavedChangesChoice.Discard) document.Discard();
            else if (!await SaveDocumentAsync(document)) return false;
        }
        return true;
    }
}
