using System.Collections.ObjectModel;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Domain.Verification;
using PowerForgeStudio.Domain.Workspace;

namespace PowerForgeStudio.Wpf.ViewModels;

public sealed class ReleaseStationsViewModel : ViewModelBase
{
    private string _queueHeadline = "Draft queue not prepared yet.";
    private string _queueDetails = "Queue persistence will appear here once prepare completes.";
    private string _signingHeadline = "No signing batch waiting.";
    private string _signingDetails = "Build outputs that require the USB token will appear here once the queue reaches the signing gate.";
    private string _receiptHeadline = "No signing receipts yet.";
    private string _receiptDetails = "Signing outcomes will be stored through DbaClientX.SQLite before publish unlocks.";
    private string _publishHeadline = "No publish batch ready.";
    private string _publishDetails = "Publish targets will appear here after signing succeeds and before verify unlocks.";
    private string _publishReceiptHeadline = "No publish receipts yet.";
    private string _publishReceiptDetails = "Publish outcomes will be stored through DbaClientX.SQLite before verification closes the queue item.";
    private string _verificationHeadline = "No verification batch ready.";
    private string _verificationDetails = "Verification checks will appear here once publish succeeds and evidence is ready to confirm.";
    private string _verificationReceiptHeadline = "No verification receipts yet.";
    private string _verificationReceiptDetails = "Verification outcomes will be stored through DbaClientX.SQLite before a queue item is marked completed.";

    public ObservableCollection<ReleaseQueueItem> DraftQueue { get; } = [];

    public ObservableCollection<ReleaseSigningArtifact> SigningArtifacts { get; } = [];

    public ObservableCollection<ReleaseSigningReceipt> SigningReceipts { get; } = [];

    public ObservableCollection<ReleasePublishTarget> PublishTargets { get; } = [];

    public ObservableCollection<ReleasePublishReceipt> PublishReceipts { get; } = [];

    public ObservableCollection<ReleaseVerificationTarget> VerificationTargets { get; } = [];

    public ObservableCollection<ReleaseVerificationReceipt> VerificationReceipts { get; } = [];

    public string QueueHeadline
    {
        get => _queueHeadline;
        private set => SetProperty(ref _queueHeadline, value);
    }

    public string QueueDetails
    {
        get => _queueDetails;
        private set => SetProperty(ref _queueDetails, value);
    }

    public string SigningHeadline
    {
        get => _signingHeadline;
        private set => SetProperty(ref _signingHeadline, value);
    }

    public string SigningDetails
    {
        get => _signingDetails;
        private set => SetProperty(ref _signingDetails, value);
    }

    public string ReceiptHeadline
    {
        get => _receiptHeadline;
        private set => SetProperty(ref _receiptHeadline, value);
    }

    public string ReceiptDetails
    {
        get => _receiptDetails;
        private set => SetProperty(ref _receiptDetails, value);
    }

    public string PublishHeadline
    {
        get => _publishHeadline;
        private set => SetProperty(ref _publishHeadline, value);
    }

    public string PublishDetails
    {
        get => _publishDetails;
        private set => SetProperty(ref _publishDetails, value);
    }

    public string PublishReceiptHeadline
    {
        get => _publishReceiptHeadline;
        private set => SetProperty(ref _publishReceiptHeadline, value);
    }

    public string PublishReceiptDetails
    {
        get => _publishReceiptDetails;
        private set => SetProperty(ref _publishReceiptDetails, value);
    }

    public string VerificationHeadline
    {
        get => _verificationHeadline;
        private set => SetProperty(ref _verificationHeadline, value);
    }

    public string VerificationDetails
    {
        get => _verificationDetails;
        private set => SetProperty(ref _verificationDetails, value);
    }

    public string VerificationReceiptHeadline
    {
        get => _verificationReceiptHeadline;
        private set => SetProperty(ref _verificationReceiptHeadline, value);
    }

    public string VerificationReceiptDetails
    {
        get => _verificationReceiptDetails;
        private set => SetProperty(ref _verificationReceiptDetails, value);
    }

    public void ApplyQueueSession(ReleaseQueueSession queueSession)
    {
        ArgumentNullException.ThrowIfNull(queueSession);

        DraftQueue.Clear();
        foreach (var item in queueSession.Items)
        {
            DraftQueue.Add(item);
        }

        QueueHeadline = $"{queueSession.Summary.BuildReadyItems} build-ready, {queueSession.Summary.PreparePendingItems} waiting in prepare, {queueSession.Summary.BlockedItems} blocked";
        var scopeLabel = string.IsNullOrWhiteSpace(queueSession.ScopeDisplayName)
            ? "workspace-wide"
            : $"family-scoped for {queueSession.ScopeDisplayName}";
        QueueDetails = $"Draft queue {queueSession.SessionId[..8]} is {scopeLabel}, was persisted through DbaClientX.SQLite at {queueSession.CreatedAtUtc:yyyy-MM-dd HH:mm} UTC, and can become the resumable execution spine.";
    }

    public void ApplySigningStationSnapshot(StationSnapshot<ReleaseSigningArtifact> snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        SigningArtifacts.Clear();
        foreach (var artifact in snapshot.Items)
        {
            SigningArtifacts.Add(artifact);
        }

        SigningHeadline = snapshot.Headline;
        SigningDetails = snapshot.Details;
    }

    public void ApplySigningReceiptBatch(ReceiptBatchSnapshot<ReleaseSigningReceipt> batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        SigningReceipts.Clear();
        foreach (var receipt in batch.Items)
        {
            SigningReceipts.Add(receipt);
        }

        ReceiptHeadline = batch.Headline;
        ReceiptDetails = batch.Details;
    }

    public void ApplyPublishStationSnapshot(StationSnapshot<ReleasePublishTarget> snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        PublishTargets.Clear();
        foreach (var target in snapshot.Items)
        {
            PublishTargets.Add(target);
        }

        PublishHeadline = snapshot.Headline;
        PublishDetails = snapshot.Details;
    }

    public void ApplyPublishReceiptBatch(ReceiptBatchSnapshot<ReleasePublishReceipt> batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        PublishReceipts.Clear();
        foreach (var receipt in batch.Items)
        {
            PublishReceipts.Add(receipt);
        }

        PublishReceiptHeadline = batch.Headline;
        PublishReceiptDetails = batch.Details;
    }

    public void ApplyVerificationStationSnapshot(StationSnapshot<ReleaseVerificationTarget> snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        VerificationTargets.Clear();
        foreach (var target in snapshot.Items)
        {
            VerificationTargets.Add(target);
        }

        VerificationHeadline = snapshot.Headline;
        VerificationDetails = snapshot.Details;
    }

    public void ApplyVerificationReceiptBatch(ReceiptBatchSnapshot<ReleaseVerificationReceipt> batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        VerificationReceipts.Clear();
        foreach (var receipt in batch.Items)
        {
            VerificationReceipts.Add(receipt);
        }

        VerificationReceiptHeadline = batch.Headline;
        VerificationReceiptDetails = batch.Details;
    }
}
