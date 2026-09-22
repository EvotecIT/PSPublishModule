using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PowerForgeStudio.Domain.Hub;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed partial class FileRecoveryViewModel(WorkspaceViewModel workspace) : ObservableObject
{
    public ObservableCollection<WorkspaceFileRecoveryEntry> Entries { get; } = [];
    [ObservableProperty] private WorkspaceFileRecoveryEntry? _selectedEntry;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private bool _confirmPermanentDelete;
    public bool HasEntries => Entries.Count > 0;
    public bool CanRestore => SelectedEntry is not null && !IsBusy;
    public bool CanDeletePermanently => SelectedEntry is not null && ConfirmPermanentDelete && !IsBusy;

    partial void OnSelectedEntryChanged(WorkspaceFileRecoveryEntry? value)
    {
        ConfirmPermanentDelete = false;
        OnPropertyChanged(nameof(CanRestore));
        OnPropertyChanged(nameof(CanDeletePermanently));
    }
    partial void OnIsBusyChanged(bool value) { OnPropertyChanged(nameof(CanRestore)); OnPropertyChanged(nameof(CanDeletePermanently)); }
    partial void OnConfirmPermanentDeleteChanged(bool value) => OnPropertyChanged(nameof(CanDeletePermanently));

    public async Task RefreshAsync()
    {
        if (IsBusy)
            return;
        IsBusy = true;
        Error = "";
        try
        {
            var selectedId = SelectedEntry?.Id;
            var entries = await workspace.ListRecoveryEntriesAsync();
            Entries.Clear();
            foreach (var entry in entries)
                Entries.Add(entry);
            SelectedEntry = Entries.FirstOrDefault(entry => entry.Id == selectedId) ?? Entries.FirstOrDefault();
            OnPropertyChanged(nameof(HasEntries));
        }
        catch (Exception ex)
        {
            Error = StudioDisplayError.From(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RestoreSelectedAsync()
    {
        if (SelectedEntry is not { } entry || IsBusy)
            return;
        IsBusy = true;
        Error = "";
        try
        {
            if (!await workspace.RestoreRecoveryEntryAsync(entry))
            {
                Error = workspace.Status;
                return;
            }
            Entries.Remove(entry);
            SelectedEntry = Entries.FirstOrDefault();
            OnPropertyChanged(nameof(HasEntries));
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DeleteSelectedPermanentlyAsync()
    {
        if (SelectedEntry is not { } entry || !CanDeletePermanently)
            return;
        IsBusy = true;
        Error = "";
        try
        {
            if (!await workspace.DeleteRecoveryEntryPermanentlyAsync(entry))
            {
                Error = workspace.Status;
                return;
            }
            Entries.Remove(entry);
            SelectedEntry = Entries.FirstOrDefault();
            OnPropertyChanged(nameof(HasEntries));
        }
        finally
        {
            IsBusy = false;
        }
    }
}
