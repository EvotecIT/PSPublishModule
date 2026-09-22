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
    private int _historyVersion;
    public ObservableCollection<ReleaseHistoryEntry> History { get; } = [];
    [ObservableProperty] private ReleaseHistoryEntry? _selectedHistory;
    [ObservableProperty] private string _historyScopeRoot = "";
    [ObservableProperty] private string _historyStatus = "Refresh saved releases to inspect durable receipts.";
    [ObservableProperty] private bool _isLoadingHistory;
    [ObservableProperty] private bool _isSavingReceipts;
    [ObservableProperty] private bool _hasUnpersistedEvidence;
    [ObservableProperty] private bool _confirmDiscardReceipts;
    public bool HasProtectedReleaseWork => IsSigning || IsPublishing || IsVerifying || IsSavingReceipts || HasUnpersistedEvidence;
    public bool CanBrowseHistory => !_disposed && !HasProtectedReleaseWork && !IsPreparing && !IsLoadingHistory;
    public bool CanOpenHistory => CanBrowseHistory && SelectedHistory is not null;
    public string HistoryScopeDisplay => string.IsNullOrWhiteSpace(HistoryScopeRoot)
        ? "Workspace release history" : $"Saved releases for {Path.GetFileName(HistoryScopeRoot)}";

    partial void OnHistoryScopeRootChanged(string value) => OnPropertyChanged(nameof(HistoryScopeDisplay));
    partial void OnSelectedHistoryChanged(ReleaseHistoryEntry? value) => OnPropertyChanged(nameof(CanOpenHistory));

    public void SetHistoryScope(string? workingCopyRoot)
    {
        if (_disposed) return;
        var root = string.IsNullOrWhiteSpace(workingCopyRoot) ? "" : Path.TrimEndingDirectorySeparator(Path.GetFullPath(workingCopyRoot));
        if (string.Equals(root, HistoryScopeRoot,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return;
        ++_historyVersion;
        HistoryScopeRoot = root;
        History.Clear();
        SelectedHistory = null;
        IsLoadingHistory = false;
        HistoryStatus = root.Length == 0 ? "Refresh saved releases for the workspace."
            : "Refresh saved releases for this working copy.";
        if (HasProtectedReleaseWork || IsPreparing || !Stage.StartsWith("Saved release", StringComparison.Ordinal)) return;
        Handoff = null; SigningResult = null; Artifacts.Clear(); Receipts.Clear();
        ResetPublicationState(); ResetExecutionProgress();
        BuildRoot = ""; Stage = "Build required";
        Status = "Selected working copy changed. Open a saved release from this project's history or run a new build.";
        NotifyReleaseState();
    }
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
        var version = ++_historyVersion;
        var root = HistoryScopeRoot;
        IsLoadingHistory = true;
        try
        {
            var entries = root.Length == 0
                ? await _history.ListAsync()
                : await _history.ListForWorkingCopyAsync(root);
            if (_disposed || version != _historyVersion) return;
            History.Clear(); foreach (var entry in entries) History.Add(entry);
            SelectedHistory = null;
            var scope = root.Length == 0 ? "workspace" : "working copy";
            HistoryStatus = entries.Count == 0 ? $"No saved releases were found for this {scope}."
                : $"{entries.Count} saved release(s) found for this {scope}.";
        }
        catch (Exception ex) { if (!_disposed && version == _historyVersion) HistoryStatus = StudioOutputSanitizer.Sanitize(ex.Message); }
        finally { if (version == _historyVersion) IsLoadingHistory = false; }
    }

    [RelayCommand]
    public Task OpenHistoryAsync() => SelectedHistory is { } selected
        ? OpenHistorySessionAsync(selected.SessionId) : Task.CompletedTask;

    /// <summary>Opens an exact journal session, including one older than the recent-history picker.</summary>
    public async Task OpenHistorySessionAsync(string sessionId)
    {
        if (!CanBrowseHistory || string.IsNullOrWhiteSpace(sessionId)) return;
        var version = ++_version;
        var historyVersion = _historyVersion;
        var root = HistoryScopeRoot;
        IsLoadingHistory = true;
        try
        {
            var snapshot = root.Length == 0
                ? await _history.LoadAsync(sessionId)
                : await _history.LoadForWorkingCopyAsync(sessionId, root);
            if (_disposed || version != _version || historyVersion != _historyVersion) return;
            if (snapshot is null) { Status = "Saved release was not found."; return; }
            var selected = History.FirstOrDefault(entry => entry.SessionId == sessionId);
            if (selected is null)
            {
                selected = new ReleaseHistoryEntry(sessionId, root.Length == 0 ? snapshot.Session.WorkspaceRoot : root,
                    snapshot.Session.CreatedAtUtc);
                History.Insert(0, selected);
                if (History.Count > 100) History.RemoveAt(History.Count - 1);
                HistoryStatus = "Opened a saved release outside the 100 most recently created sessions.";
            }
            SelectedHistory = selected;
            _candidate = null; Handoff = null; ResetPublicationState(); SigningResult = null; Artifacts.Clear(); Receipts.Clear(); ResetExecutionProgress();
            foreach (var progress in snapshot.Progress) ApplyExecutionProgress(progress);
            foreach (var receipt in snapshot.SigningReceipts) Receipts.Add(receipt with { Summary = StudioOutputSanitizer.Sanitize(receipt.Summary) });
            foreach (var receipt in snapshot.PublishReceipts)
                PublicationReceipts.Add(receipt with { Summary = StudioOutputSanitizer.Sanitize(receipt.Summary), Destination = StudioOutputSanitizer.SanitizeDestination(receipt.Destination) });
            SelectedPublicationReceipt = null;
            foreach (var receipt in snapshot.VerificationReceipts)
                VerificationReceipts.Add(receipt with { Summary = StudioOutputSanitizer.Sanitize(receipt.Summary), Destination = StudioOutputSanitizer.SanitizeDestination(receipt.Destination) });
            OnPropertyChanged(nameof(HasPublicationReceipts)); OnPropertyChanged(nameof(HasVerificationReceipts));
            BuildRoot = root.Length == 0 ? snapshot.Session.WorkspaceRoot : root;
            Stage = "Saved release · " + string.Join(", ", snapshot.Session.Items.Select(x => x.StageDisplay + " / " + x.StatusDisplay));
            Status = string.Join(" ", snapshot.Session.Items.Select(x => StudioOutputSanitizer.Sanitize(x.Summary)))
                + (snapshot.IsScopedBatch ? " This release was part of a multi-project batch; shared progress is omitted from this project view." : "")
                + " Saved records are view-only here; start a new build for a new signing attempt.";
        }
        catch (Exception ex) { if (!_disposed && version == _version && historyVersion == _historyVersion) Status = StudioOutputSanitizer.Sanitize(ex.Message); }
        finally { if (historyVersion == _historyVersion) { IsLoadingHistory = false; NotifyReleaseState(); } }
    }
}
