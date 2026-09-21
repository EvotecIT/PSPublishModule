using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Verification;
using PowerForgeStudio.Orchestrator.Host;
using PowerForgeStudio.Orchestrator.Queue;
using System.Collections.ObjectModel;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed partial class ReleaseViewModel
{
    private readonly IReleasePublicationPreviewService _publication = publication ?? new ReleasePublicationPreviewService();
    private readonly IReleasePublicationWorkflow _publishing = publishing ?? new DurableReleasePublicationWorkflow(PowerForgeStudioHostPaths.GetReleaseHistoryDatabasePath());
    private readonly IReleaseVerificationWorkflow _verification = verification ?? new DurableReleaseVerificationWorkflow(PowerForgeStudioHostPaths.GetReleaseHistoryDatabasePath());
    private CancellationTokenSource? _publicationCancellation;
    private CancellationTokenSource? _verificationCancellation;
    private ReleasePublicationPreview? _publicationSnapshot;
    private ReleaseQueueSession? _inspectedSession;

    public ObservableCollection<ReleasePublishTarget> PublicationTargets { get; } = [];
    public ObservableCollection<ReleasePublishReceipt> PublicationReceipts { get; } = [];
    public ObservableCollection<ReleaseVerificationReceipt> VerificationReceipts { get; } = [];
    [ObservableProperty] private string _publicationSummary = "";
    [ObservableProperty] private bool _isInspectingPublication;
    [ObservableProperty] private bool _isPublishing;
    [ObservableProperty] private bool _isVerifying;
    [ObservableProperty] private bool _confirmPublication;

    public bool CanInspectPublication => !_disposed && !HasProtectedReleaseWork && !IsLoadingHistory && !IsInspectingPublication
        && Handoff?.Session.Items.SingleOrDefault()?.Stage == ReleaseQueueStage.Publish;
    public bool CanPublish => !_disposed && !HasProtectedReleaseWork && ConfirmPublication && _publicationSnapshot is not null
        && ReferenceEquals(_inspectedSession, Handoff?.Session)
        && Handoff?.Session.Items.SingleOrDefault() is { Stage: ReleaseQueueStage.Publish, Status: ReleaseQueueItemStatus.ReadyToRun };
    public bool CanVerify => !_disposed && !HasProtectedReleaseWork
        && Handoff?.Session.Items.SingleOrDefault() is { Stage: ReleaseQueueStage.Verify, Status: ReleaseQueueItemStatus.ReadyToRun };
    public bool HasPublicationReceipts => PublicationReceipts.Count > 0;
    public bool HasVerificationReceipts => VerificationReceipts.Count > 0;

    partial void OnIsInspectingPublicationChanged(bool value) => NotifyReleaseState();
    partial void OnIsPublishingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowIndeterminateProgress));
        NotifyReleaseState();
    }
    partial void OnIsVerifyingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowIndeterminateProgress));
        NotifyReleaseState();
    }
    partial void OnConfirmPublicationChanged(bool value) => NotifyReleaseState();

    [RelayCommand]
    public async Task InspectPublicationAsync()
    {
        if (!CanInspectPublication || Handoff is not { } handoff) return;
        var version = _version;
        IsInspectingPublication = true;
        ResetPublicationApproval();
        PublicationTargets.Clear();
        PublicationSummary = "Inspecting destinations…";
        try
        {
            var preview = await _publication.PreviewAsync(handoff.Session);
            if (_disposed || version != _version || !ReferenceEquals(Handoff, handoff)) return;
            foreach (var target in preview.Targets) PublicationTargets.Add(target);
            _publicationSnapshot = preview;
            _inspectedSession = handoff.Session;
            PublicationSummary = preview.Summary;
        }
        catch (Exception ex) { if (!_disposed && version == _version) PublicationSummary = StudioOutputSanitizer.Sanitize(ex.Message); }
        finally { IsInspectingPublication = false; }
    }

    [RelayCommand]
    public async Task PublishAsync()
    {
        if (!CanPublish || Handoff is not { } captured || _publicationSnapshot is not { } inspected) return;
        using var cancellation = new CancellationTokenSource();
        _publicationCancellation = cancellation; BeginExecutionStage(); IsPublishing = true; Stage = "Publishing";
        Status = "Rechecking the inspected destinations before publication…";
        try
        {
            var current = await _publication.PreviewAsync(captured.Session, cancellation.Token);
            if (!current.Targets.SequenceEqual(inspected.Targets))
            {
                ResetPublicationApproval(); Stage = "Destinations changed";
                Status = "Publication settings changed after inspection. Inspect the targets again before publishing."; return;
            }
            Status = "Publishing the signed artifacts to the inspected destinations…";
            var result = await Task.Run(() => _publishing.PublishAsync(captured.Session, cancellation.Token, new LiveProgressSink(this)));
            _pendingPublicationSave = result.PersistenceError is null ? null : result;
            HasUnpersistedEvidence = _pendingPublicationSave is not null;
            Handoff = captured with { Session = result.Session };
            PublicationReceipts.Clear();
            foreach (var receipt in result.Execution.Receipts)
                PublicationReceipts.Add(receipt with { Summary = StudioOutputSanitizer.Sanitize(receipt.Summary), Destination = StudioOutputSanitizer.Sanitize(receipt.Destination) });
            OnPropertyChanged(nameof(HasPublicationReceipts)); ResetPublicationApproval();
            PublicationSummary = result.Execution.Succeeded
                ? "The inspected destinations are shown above. Publication receipts are recorded below."
                : "The inspected destinations are shown above. Review the publication receipts before any retry.";
            Stage = result.Execution.Succeeded ? "Published · ready to verify"
                : result.Execution.RequiresReconciliation ? "Publication requires reconciliation" : "Publication failed";
            Status = StudioOutputSanitizer.Sanitize(result.Execution.Summary);
            if (result.PersistenceError is not null) { Stage = "Receipts not saved"; Status += " " + result.PersistenceError; }
        }
        catch (OperationCanceledException)
        {
            Stage = "Publication cancellation requested";
            Status = "Publication stopped before a durable result was returned. Review the saved release checkpoint before retrying.";
        }
        catch (Exception ex) { Stage = "Publication failed"; Status = StudioOutputSanitizer.Sanitize(ex.Message); }
        finally { _publicationCancellation = null; IsPublishing = false; NotifyReleaseState(); }
    }

    [RelayCommand]
    private void CancelPublication()
    {
        if (!IsPublishing) return;
        Status = "Cancellation requested; waiting for publication evidence to be captured…";
        _publicationCancellation?.Cancel();
    }

    [RelayCommand]
    public async Task VerifyAsync()
    {
        if (!CanVerify || Handoff is not { } captured) return;
        using var cancellation = new CancellationTokenSource();
        _verificationCancellation = cancellation; IsVerifying = true; Stage = "Verifying publication";
        Status = "Checking each published destination…";
        try
        {
            BeginExecutionStage();
            var result = await Task.Run(() => _verification.VerifyAsync(captured.Session, cancellation.Token, new LiveProgressSink(this)));
            _pendingVerificationSave = result.PersistenceError is null ? null : result;
            HasUnpersistedEvidence = _pendingVerificationSave is not null;
            Handoff = captured with { Session = result.Session };
            VerificationReceipts.Clear();
            foreach (var receipt in result.Execution.Receipts)
                VerificationReceipts.Add(receipt with { Summary = StudioOutputSanitizer.Sanitize(receipt.Summary), Destination = StudioOutputSanitizer.Sanitize(receipt.Destination) });
            OnPropertyChanged(nameof(HasVerificationReceipts));
            PublicationSummary = result.Execution.Succeeded
                ? "Publication and verification receipts are recorded below."
                : "Publication receipts are recorded below; one or more destinations remain unverified.";
            Stage = result.Execution.Succeeded ? "Release verified" : result.Execution.WasCancelled ? "Verification cancelled" : "Verification failed";
            Status = StudioOutputSanitizer.Sanitize(result.Execution.Summary);
            if (result.PersistenceError is not null) { Stage = "Receipts not saved"; Status += " " + result.PersistenceError; }
        }
        catch (OperationCanceledException)
        {
            Stage = "Verification cancellation requested";
            Status = "Verification stopped before a durable result was returned. Reopen the saved checkpoint before retrying.";
        }
        catch (Exception ex) { Stage = "Verification failed"; Status = StudioOutputSanitizer.Sanitize(ex.Message); }
        finally { _verificationCancellation = null; IsVerifying = false; NotifyReleaseState(); }
    }

    [RelayCommand]
    private void CancelVerification()
    {
        if (!IsVerifying) return;
        Status = "Cancellation requested; retaining completed verification checks…";
        _verificationCancellation?.Cancel();
    }

    private void ResetPublicationApproval()
    {
        ConfirmPublication = false; _publicationSnapshot = null; _inspectedSession = null;
    }

    private void ResetPublicationState()
    {
        ResetPublicationApproval(); PublicationTargets.Clear(); PublicationReceipts.Clear(); VerificationReceipts.Clear(); PublicationSummary = "";
        OnPropertyChanged(nameof(HasPublicationReceipts)); OnPropertyChanged(nameof(HasVerificationReceipts));
    }
}
