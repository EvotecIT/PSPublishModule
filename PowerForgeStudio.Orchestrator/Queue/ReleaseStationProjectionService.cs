using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Domain.Verification;
using PowerForgeStudio.Domain.Workspace;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed class ReleaseStationProjectionService
{
    private readonly ReleaseBuildCheckpointReader _buildCheckpointReader;
    private readonly ReleasePublishExecutionService _publishExecutionService;
    private readonly ReleaseVerificationExecutionService _verificationExecutionService;

    public ReleaseStationProjectionService()
        : this(
            new ReleaseBuildCheckpointReader(),
            new ReleasePublishExecutionService(),
            new ReleaseVerificationExecutionService()) {
    }

    public ReleaseStationProjectionService(
        ReleaseBuildCheckpointReader buildCheckpointReader,
        ReleasePublishExecutionService publishExecutionService,
        ReleaseVerificationExecutionService verificationExecutionService)
    {
        _buildCheckpointReader = buildCheckpointReader;
        _publishExecutionService = publishExecutionService;
        _verificationExecutionService = verificationExecutionService;
    }

    public StationSnapshot<ReleaseSigningArtifact> BuildSigningStation(ReleaseQueueSession queueSession)
    {
        ArgumentNullException.ThrowIfNull(queueSession);

        var manifest = _buildCheckpointReader.BuildSigningManifest(queueSession.Items);
        var waitingItems = queueSession.Items.Count(item => item.Stage == ReleaseQueueStage.Sign && item.Status == ReleaseQueueItemStatus.WaitingApproval);

        return waitingItems == 0
            ? new StationSnapshot<ReleaseSigningArtifact>(
                Headline: "No signing batch waiting.",
                Details: "Build outputs that require the USB token will appear here once the queue reaches the signing gate.",
                Items: manifest)
            : new StationSnapshot<ReleaseSigningArtifact>(
                Headline: $"{waitingItems} repo(s) waiting for USB approval, {manifest.Count} artifact target(s) collected",
                Details: "This is the signing handoff surface: approve when the USB token is ready, then move the batch into publish.",
                Items: manifest);
    }

    public ReceiptBatchSnapshot<ReleaseSigningReceipt> BuildSigningReceipts(IReadOnlyList<ReleaseSigningReceipt> receipts)
    {
        ArgumentNullException.ThrowIfNull(receipts);

        if (receipts.Count == 0)
        {
            return new ReceiptBatchSnapshot<ReleaseSigningReceipt>(
                Headline: "No signing receipts yet.",
                Details: "Signing outcomes will be stored through DbaClientX.SQLite before publish unlocks.",
                Items: receipts);
        }

        var signed = receipts.Count(receipt => receipt.Status == ReleaseSigningReceiptStatus.Signed);
        var skipped = receipts.Count(receipt => receipt.Status == ReleaseSigningReceiptStatus.Skipped);
        var failed = receipts.Count(receipt => receipt.Status == ReleaseSigningReceiptStatus.Failed);
        return new ReceiptBatchSnapshot<ReleaseSigningReceipt>(
            Headline: $"{signed} signed, {skipped} skipped, {failed} failed",
            Details: "The latest signing batch is persisted so publish does not unlock on memory alone.",
            Items: receipts);
    }

    public StationSnapshot<ReleasePublishTarget> BuildPublishStation(ReleaseQueueSession queueSession)
    {
        ArgumentNullException.ThrowIfNull(queueSession);

        var manifest = _publishExecutionService.BuildPendingTargets(queueSession.Items);
        return manifest.Count == 0
            ? new StationSnapshot<ReleasePublishTarget>(
                Headline: "No publish batch ready.",
                Details: "Publish targets will appear here after signing succeeds and before verify unlocks.",
                Items: manifest)
            : new StationSnapshot<ReleasePublishTarget>(
                Headline: $"{manifest.Count} publish target(s) ready",
                Details: "This station is the final external boundary: publish only runs when RELEASE_OPS_STUDIO_ENABLE_PUBLISH=true.",
                Items: manifest);
    }

    public ReceiptBatchSnapshot<ReleasePublishReceipt> BuildPublishReceipts(IReadOnlyList<ReleasePublishReceipt> receipts)
    {
        ArgumentNullException.ThrowIfNull(receipts);

        if (receipts.Count == 0)
        {
            return new ReceiptBatchSnapshot<ReleasePublishReceipt>(
                Headline: "No publish receipts yet.",
                Details: "Publish outcomes will be stored through DbaClientX.SQLite before verification closes the queue item.",
                Items: receipts);
        }

        var published = receipts.Count(receipt => receipt.Status == ReleasePublishReceiptStatus.Published);
        var skipped = receipts.Count(receipt => receipt.Status == ReleasePublishReceiptStatus.Skipped);
        var failed = receipts.Count(receipt => receipt.Status == ReleasePublishReceiptStatus.Failed);
        return new ReceiptBatchSnapshot<ReleasePublishReceipt>(
            Headline: $"{published} published, {skipped} skipped, {failed} failed",
            Details: "The latest publish batch is persisted so verification is grounded in recorded external outcomes.",
            Items: receipts);
    }

    public StationSnapshot<ReleaseVerificationTarget> BuildVerificationStation(ReleaseQueueSession queueSession)
    {
        ArgumentNullException.ThrowIfNull(queueSession);

        var manifest = _verificationExecutionService.BuildPendingTargets(queueSession.Items);
        return manifest.Count == 0
            ? new StationSnapshot<ReleaseVerificationTarget>(
                Headline: "No verification batch ready.",
                Details: "Verification checks will appear here once publish succeeds and evidence is ready to confirm.",
                Items: manifest)
            : new StationSnapshot<ReleaseVerificationTarget>(
                Headline: $"{manifest.Count} verification check(s) ready",
                Details: "This station closes the queue on evidence: release URLs, package presence, and repository publish checks where we can confirm them.",
                Items: manifest);
    }

    public ReceiptBatchSnapshot<ReleaseVerificationReceipt> BuildVerificationReceipts(IReadOnlyList<ReleaseVerificationReceipt> receipts)
    {
        ArgumentNullException.ThrowIfNull(receipts);

        if (receipts.Count == 0)
        {
            return new ReceiptBatchSnapshot<ReleaseVerificationReceipt>(
                Headline: "No verification receipts yet.",
                Details: "Verification outcomes will be stored through DbaClientX.SQLite before a queue item is marked completed.",
                Items: receipts);
        }

        var verified = receipts.Count(receipt => receipt.Status == ReleaseVerificationReceiptStatus.Verified);
        var skipped = receipts.Count(receipt => receipt.Status == ReleaseVerificationReceiptStatus.Skipped);
        var failed = receipts.Count(receipt => receipt.Status == ReleaseVerificationReceiptStatus.Failed);
        return new ReceiptBatchSnapshot<ReleaseVerificationReceipt>(
            Headline: $"{verified} verified, {skipped} skipped, {failed} failed",
            Details: "The latest verification batch is persisted so completion reflects recorded checks, not just queue advancement.",
            Items: receipts);
    }
}
