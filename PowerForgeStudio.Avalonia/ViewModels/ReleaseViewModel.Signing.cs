using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Orchestrator.Host;
using PowerForgeStudio.Orchestrator.Queue;
using System.Collections.ObjectModel;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed partial class ReleaseViewModel
{
    private CancellationTokenSource? _signingCancellation;
    public ObservableCollection<ReleaseSigningReceipt> Receipts { get; } = [];
    public ReleaseSigningExecutionResult? SigningResult { get; private set; }
    [ObservableProperty] private bool _isSigning;
    [ObservableProperty] private bool _requiresRebuild;
    [ObservableProperty] private string _signingConfigurationStatus = "";
    [ObservableProperty] private bool _signingConfigurationAvailable;
    public bool CanSign => !_disposed && !IsProjectTaskRunning && !HasProtectedReleaseWork && !IsLoadingHistory && !IsPreparing && Handoff is not null && SigningResult is null && !RequiresRebuild && SigningConfigurationAvailable;
    public bool ShowSigningConfiguration => Handoff is not null && SigningResult is null && !string.IsNullOrWhiteSpace(SigningConfigurationStatus);
    public bool HasArtifacts => Artifacts.Count > 0;
    public bool HasReceipts => Receipts.Count > 0;
    partial void OnRequiresRebuildChanged(bool value) => NotifyReleaseState();
    partial void OnSigningConfigurationAvailableChanged(bool value) => NotifyReleaseState();
    partial void OnIsSigningChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowIndeterminateProgress));
        NotifyReleaseState();
    }
    private void NotifyReleaseState()
    {
        OnPropertyChanged(nameof(CanInspectPublication)); OnPropertyChanged(nameof(CanPrepare)); OnPropertyChanged(nameof(EmphasizePrepare)); OnPropertyChanged(nameof(CanSign)); OnPropertyChanged(nameof(ShowSigningConfiguration));
        OnPropertyChanged(nameof(CanPublish)); OnPropertyChanged(nameof(CanVerify));
        OnPropertyChanged(nameof(CanInspectPublicPackage));
        OnPropertyChanged(nameof(HasArtifacts)); OnPropertyChanged(nameof(HasHandoff)); OnPropertyChanged(nameof(HasReceipts));
        OnPropertyChanged(nameof(ShowPublicationDetails)); OnPropertyChanged(nameof(CanReviewPublication));
        OnPropertyChanged(nameof(HasProtectedReleaseWork)); OnPropertyChanged(nameof(CanBrowseHistory)); OnPropertyChanged(nameof(CanOpenHistory));
        OnPropertyChanged(nameof(HasActiveReleaseOperation));
    }

    [RelayCommand]
    public async Task SignAsync()
    {
        if (!CanSign || Handoff is not { } captured) return;
        ResetExecutionProgress();
        using var cancellation = new CancellationTokenSource(); _signingCancellation = cancellation;
        IsSigning = true; Stage = "Signing";
        Status = "Signing captured artifacts using the configured certificate. Publication will not run automatically.";
        try
        {
            var progress = new LiveProgressSink(this);
            var result = await Task.Run(() => _signing.SignAsync(captured, cancellation.Token, progress));
            ConfirmDiscardReceipts = false;
            _pendingSave = result.PersistenceError is null ? null : result;
            HasUnpersistedEvidence = _pendingSave is not null;
            SigningResult = result.Execution;
            Handoff = captured with { Session = result.Session }; ResetPublicationState();
            Receipts.Clear();
            foreach (var receipt in result.Execution.Receipts)
                Receipts.Add(receipt with { Summary = StudioOutputSanitizer.Sanitize(receipt.Summary) });
            RequiresRebuild = result.Execution.RequiresRebuild;
            Stage = result.Execution.Succeeded ? "Signed · publication not started" : RequiresRebuild ? "Rebuild required" : "Signing failed";
            Status = StudioOutputSanitizer.Sanitize(result.Execution.Summary);
            if (result.PersistenceError is not null) { Stage = "Receipts not saved"; Status += " " + result.PersistenceError; }
        }
        catch (Exception ex)
        {
            // A host failure can arrive after files changed. Never reuse this handoff for another signing attempt.
            RequiresRebuild = true; Stage = "Rebuild required";
            Status = "Signing stopped without a complete result. Rebuild before retrying. " + StudioOutputSanitizer.Sanitize(ex.Message);
        }
        finally
        {
            _signingCancellation = null; IsSigning = false; NotifyReleaseState();
        }
    }

    [RelayCommand]
    private void CancelSigning()
    {
        if (!IsSigning) return;
        Status = "Cancellation requested; waiting for signing and receipt capture to finish…";
        _signingCancellation?.Cancel();
    }
}
