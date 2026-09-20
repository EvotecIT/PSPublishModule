using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerForgeStudio.Orchestrator.Host;
using PowerForgeStudio.Orchestrator.Queue;
using PowerForgeStudio.Orchestrator.Storage;
using System.Collections.ObjectModel;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed partial class ReleaseViewModel
{
    private readonly IReleaseHistoryService _history = history ?? new ReleaseHistoryService(PowerForgeStudioHostPaths.GetReleaseHistoryDatabasePath());
    private ReleaseSigningWorkflowResult? _pendingSave;
    public ObservableCollection<ReleaseHistoryEntry> History { get; } = [];
    [ObservableProperty] private ReleaseHistoryEntry? _selectedHistory;
    [ObservableProperty] private bool _isLoadingHistory;
    [ObservableProperty] private bool _isSavingReceipts;
    [ObservableProperty] private bool _hasUnpersistedEvidence;
    [ObservableProperty] private bool _confirmDiscardReceipts;
    public bool HasProtectedReleaseWork => IsSigning || IsSavingReceipts || HasUnpersistedEvidence;
    public bool CanBrowseHistory => !_disposed && !HasProtectedReleaseWork && !IsPreparing && !IsLoadingHistory;
    partial void OnHasUnpersistedEvidenceChanged(bool value) => NotifyReleaseState();
    partial void OnIsSavingReceiptsChanged(bool value) => NotifyReleaseState();
    partial void OnIsLoadingHistoryChanged(bool value) => NotifyReleaseState();

    [RelayCommand]
    public async Task RetrySaveReceiptsAsync()
    {
        if (_disposed || IsSavingReceipts || _pendingSave is null || _signing is not IReleaseSigningRecovery recovery) return;
        IsSavingReceipts = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var saved = await Task.Run(() => recovery.RetrySaveAsync(_pendingSave, timeout.Token));
            _pendingSave = saved.PersistenceError is null ? null : saved;
            HasUnpersistedEvidence = _pendingSave is not null;
            Status = saved.PersistenceError ?? "Signing receipts saved. Publication has not started.";
            Stage = saved.PersistenceError is not null ? "Receipts not saved" : saved.Execution.Succeeded ? "Signed · saved" : "Signing failed · saved";
        }
        catch (Exception ex) { Status = StudioOutputSanitizer.Sanitize(ex.Message); }
        finally { IsSavingReceipts = false; }
    }

    [RelayCommand]
    private void DiscardUnsavedReceipts()
    {
        if (!ConfirmDiscardReceipts || IsSavingReceipts || IsSigning || !HasUnpersistedEvidence) return;
        _pendingSave = null; HasUnpersistedEvidence = false; ConfirmDiscardReceipts = false;
        RequiresRebuild = true; Stage = "Rebuild required";
        Status = "Unsaved receipt copy discarded. The durable record remains incomplete. Rebuild before continuing.";
    }

    [RelayCommand]
    public async Task RefreshHistoryAsync()
    {
        if (!CanBrowseHistory) return;
        IsLoadingHistory = true;
        try
        {
            var entries = await Task.Run(() => _history.ListAsync());
            if (_disposed) return;
            History.Clear(); foreach (var entry in entries) History.Add(entry);
        }
        catch (Exception ex) { Status = StudioOutputSanitizer.Sanitize(ex.Message); }
        finally { IsLoadingHistory = false; }
    }

    [RelayCommand]
    public async Task OpenHistoryAsync()
    {
        if (!CanBrowseHistory || SelectedHistory is not { } selected) return;
        var version = ++_version; IsLoadingHistory = true;
        try
        {
            var snapshot = await Task.Run(() => _history.LoadAsync(selected.SessionId));
            if (_disposed || version != _version) return;
            if (snapshot is null) { Status = "Saved release was not found."; return; }
            _candidate = null; Handoff = null; PublicationTargets.Clear(); PublicationSummary = ""; SigningResult = null; Artifacts.Clear(); Receipts.Clear();
            foreach (var receipt in snapshot.SigningReceipts) Receipts.Add(receipt with { Summary = StudioOutputSanitizer.Sanitize(receipt.Summary) });
            BuildRoot = snapshot.Session.WorkspaceRoot;
            Stage = "Saved release · " + string.Join(", ", snapshot.Session.Items.Select(x => x.StageDisplay + " / " + x.StatusDisplay));
            Status = string.Join(" ", snapshot.Session.Items.Select(x => StudioOutputSanitizer.Sanitize(x.Summary)))
                + " Saved records are view-only here; start a new build for a new signing attempt.";
        }
        catch (Exception ex) { if (!_disposed && version == _version) Status = StudioOutputSanitizer.Sanitize(ex.Message); }
        finally { IsLoadingHistory = false; NotifyReleaseState(); }
    }
}
