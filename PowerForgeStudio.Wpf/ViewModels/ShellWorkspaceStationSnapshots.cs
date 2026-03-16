using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Domain.Verification;
using PowerForgeStudio.Domain.Workspace;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Wpf.ViewModels;

public sealed record ShellWorkspaceStationSnapshots(
    StationSnapshot<ReleaseSigningArtifact> SigningStation,
    ReceiptBatchSnapshot<ReleaseSigningReceipt> SigningReceiptBatch,
    StationSnapshot<ReleasePublishTarget> PublishStation,
    ReceiptBatchSnapshot<ReleasePublishReceipt> PublishReceiptBatch,
    StationSnapshot<ReleaseVerificationTarget> VerificationStation,
    ReceiptBatchSnapshot<ReleaseVerificationReceipt> VerificationReceiptBatch)
{
    public static ShellWorkspaceStationSnapshots Empty { get; } = new(
        new StationSnapshot<ReleaseSigningArtifact>("No signing batch waiting.", "None.", []),
        new ReceiptBatchSnapshot<ReleaseSigningReceipt>("No signing receipts yet.", "None.", []),
        new StationSnapshot<ReleasePublishTarget>("No publish batch ready.", "None.", []),
        new ReceiptBatchSnapshot<ReleasePublishReceipt>("No publish receipts yet.", "None.", []),
        new StationSnapshot<ReleaseVerificationTarget>("No verification batch ready.", "None.", []),
        new ReceiptBatchSnapshot<ReleaseVerificationReceipt>("No verification receipts yet.", "None.", []));

    public static ShellWorkspaceStationSnapshots FromWorkspaceSnapshot(WorkspaceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new ShellWorkspaceStationSnapshots(
            snapshot.SigningStation,
            snapshot.SigningReceiptBatch,
            snapshot.PublishStation,
            snapshot.PublishReceiptBatch,
            snapshot.VerificationStation,
            snapshot.VerificationReceiptBatch);
    }

    public static ShellWorkspaceStationSnapshots FromQueueCommandResult(
        ReleaseQueueCommandResult result,
        ReleaseStationProjectionService stationProjectionService)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(stationProjectionService);

        if (result.QueueSession is null)
        {
            return Empty;
        }

        return new ShellWorkspaceStationSnapshots(
            stationProjectionService.BuildSigningStation(result.QueueSession),
            stationProjectionService.BuildSigningReceipts(result.SigningReceipts),
            stationProjectionService.BuildPublishStation(result.QueueSession),
            stationProjectionService.BuildPublishReceipts(result.PublishReceipts),
            stationProjectionService.BuildVerificationStation(result.QueueSession),
            stationProjectionService.BuildVerificationReceipts(result.VerificationReceipts));
    }
}
