using CommunityToolkit.Mvvm.ComponentModel;
using PowerForgeStudio.Domain.Workspace;
using PowerForge;

namespace PowerForgeStudio.Avalonia.ViewModels;

/// <summary>A document tab identity; previews are loaded only when the tab is selected.</summary>
public sealed partial class WorkspaceDocumentViewModel(WorkspaceDocumentReference reference) : ObservableObject
{
    public WorkspaceDocumentReference Reference { get; private set; } = reference;
    public string Name => Path.GetFileName(Reference.Path);
    public string Caption => $"{(IsDirty ? "● " : "")}{Name} · {Path.GetFileName(Reference.WorkingCopyRoot)}";
    public string Location => Reference.Path;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private string _error = "";
    public RepositoryTextDocument? Snapshot { get; private set; }
    public bool IsDirty => Snapshot is not null && Text != Snapshot.Text;
    public bool HasError => Error.Length > 0;
    public string EncodingLabel => Snapshot?.EncodingName ?? "";
    partial void OnTextChanged(string value) { OnPropertyChanged(nameof(IsDirty)); OnPropertyChanged(nameof(Caption)); }
    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));
    public void Load(RepositoryTextDocument snapshot)
    {
        Snapshot = snapshot; Text = snapshot.Text; IsEditing = true; Error = "";
        NotifySnapshotChanged();
    }
    public void Saved(RepositoryTextDocument snapshot)
    {
        Snapshot = snapshot; Error = ""; NotifySnapshotChanged();
    }
    public void Discard()
    {
        Snapshot = null; Text = ""; IsEditing = false; Error = ""; NotifySnapshotChanged();
    }
    public void Relocate(WorkspaceDocumentReference reference)
    {
        Reference = reference;
        Snapshot = Snapshot?.WithPath(reference.Path);
        OnPropertyChanged(nameof(Reference)); OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(Location));
        NotifySnapshotChanged();
    }
    private void NotifySnapshotChanged()
    {
        OnPropertyChanged(nameof(IsDirty)); OnPropertyChanged(nameof(Caption)); OnPropertyChanged(nameof(EncodingLabel));
    }
}
