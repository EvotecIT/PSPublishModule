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
    private ReleasePublicationWorkflowResult? _pendingPublicationSave;
    private ReleaseVerificationWorkflowResult? _pendingVerificationSave;
    public ObservableCollection<ReleaseHistoryEntry> History { get; } = [];
    [ObservableProperty] private ReleaseHistoryEntry? _selectedHistory;
    [ObservableProperty] private bool _isLoadingHistory;
    [ObservableProperty] private bool _isSavingReceipts;
    [ObservableProperty] private bool _hasUnpersistedEvidence;
    [ObservableProperty] private bool _confirmDiscardReceipts;
    public bool HasProtectedReleaseWork => IsSigning || IsPublishing || IsVerifying || IsSavingReceipts || HasUnpersistedEvidence;
    public bool CanBrowseHistory => !_disposed && !HasProtectedReleaseWork && !IsPreparing && !IsLoadingHistory;
    partial void OnHasUnpersistedEvidenceChanged(bool value) => NotifyReleaseState();
    partial void OnIsSavingReceiptsChanged(bool value) => NotifyReleaseState();
    partial void OnIsLoadingHistoryChanged(bool value) => NotifyReleaseState();

    [RelayCommand]
    public async Task RetrySaveReceiptsAsync()
    {
        if (_disposed || IsSavingReceipts || !HasUnpersistedEvidence) return;
        IsSavingReceipts = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            if (_pendingSave is not null && _signing is IReleaseSigningRecovery recovery)
            {
                var saved = await Task.Run(() => recovery.RetrySaveAsync(_pendingSave, timeout.Token));
                _pendingSave = saved.PersistenceError is null ? null : saved;
                Status = saved.PersistenceError ?? "Signing receipts saved. Publication has not started.";
                Stage = saved.PersistenceError is not null ? "Receipts not saved" : saved.Execution.Succeeded ? "Signed · saved" : "Signing failed · saved";
            }
            else if (_pendingPublicationSave is not null)
            {
                var saved = await Task.Run(() => _publishing.RetrySaveAsync(_pendingPublicationSave, timeout.Token));
                _pendingPublicationSave = saved.PersistenceError is null ? null : saved;
                Status = saved.PersistenceError ?? "Publication receipts saved.";
                Stage = saved.PersistenceError is not null ? "Receipts not saved" : saved.Execution.Succeeded ? "Published · ready to verify" : "Publication failed · saved";
                if (saved.PersistenceError is null && Handoff is not null) Handoff = Handoff with { Session = saved.Session };
            }
            else if (_pendingVerificationSave is not null)
            {
                var saved = await Task.Run(() => _verification.RetrySaveAsync(_pendingVerificationSave, timeout.Token));
                _pendingVerificationSave = saved.PersistenceError is null ? null : saved;
                Status = saved.PersistenceError ?? "Verification receipts saved.";
                Stage = saved.PersistenceError is not null ? "Receipts not saved" : saved.Execution.Succeeded ? "Release verified" : "Verification failed · saved";
                if (saved.PersistenceError is null && Handoff is not null) Handoff = Handoff with { Session = saved.Session };
            }
            HasUnpersistedEvidence = _pendingSave is not null || _pendingPublicationSave is not null || _pendingVerificationSave is not null;
        }
        catch (Exception ex) { Status = StudioOutputSanitizer.Sanitize(ex.Message); }
        finally { IsSavingReceipts = false; }
    }

    [RelayCommand]
    private void DiscardUnsavedReceipts()
    {
        if (!ConfirmDiscardReceipts || IsSavingReceipts || IsSigning || !HasUnpersistedEvidence) return;
        var signingEvidence = _pendingSave is not null;
        _pendingSave = null; _pendingPublicationSave = null; _pendingVerificationSave = null;
        HasUnpersistedEvidence = false; ConfirmDiscardReceipts = false;
        RequiresRebuild = signingEvidence;
        Stage = signingEvidence ? "Rebuild required" : "Saved release requires review";
        Status = signingEvidence
            ? "Unsaved signing receipt copy discarded. The durable record remains incomplete. Rebuild before continuing."
            : "Unsaved receipt copy discarded. Reopen the saved release and review its incomplete checkpoint before continuing.";
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
            _candidate = null; Handoff = null; ResetPublicationState(); SigningResult = null; Artifacts.Clear(); Receipts.Clear();
            foreach (var receipt in snapshot.SigningReceipts) Receipts.Add(receipt with { Summary = StudioOutputSanitizer.Sanitize(receipt.Summary) });
            foreach (var receipt in snapshot.PublishReceipts)
                PublicationReceipts.Add(receipt with { Summary = StudioOutputSanitizer.Sanitize(receipt.Summary), Destination = StudioOutputSanitizer.Sanitize(receipt.Destination) });
            foreach (var receipt in snapshot.VerificationReceipts)
                VerificationReceipts.Add(receipt with { Summary = StudioOutputSanitizer.Sanitize(receipt.Summary), Destination = StudioOutputSanitizer.Sanitize(receipt.Destination) });
            OnPropertyChanged(nameof(HasPublicationReceipts)); OnPropertyChanged(nameof(HasVerificationReceipts));
            BuildRoot = snapshot.Session.WorkspaceRoot;
            Stage = "Saved release · " + string.Join(", ", snapshot.Session.Items.Select(x => x.StageDisplay + " / " + x.StatusDisplay));
            Status = string.Join(" ", snapshot.Session.Items.Select(x => StudioOutputSanitizer.Sanitize(x.Summary)))
                + " Saved records are view-only here; start a new build for a new signing attempt.";
        }
        catch (Exception ex) { if (!_disposed && version == _version) Status = StudioOutputSanitizer.Sanitize(ex.Message); }
        finally { IsLoadingHistory = false; NotifyReleaseState(); }
    }
}
