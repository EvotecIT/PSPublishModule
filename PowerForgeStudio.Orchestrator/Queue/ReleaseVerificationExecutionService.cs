using System.Net.Http;
using PowerForge;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Verification;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed class ReleaseVerificationExecutionService : IReleaseVerificationExecutionService, IDisposable
{
    private readonly ReleaseQueueCheckpointSerializer _checkpointSerializer = new();
    private readonly ReleaseQueueTargetProjectionService _targetProjectionService = new();
    private readonly PublishVerificationHostService _verificationHostService;
    private readonly bool _ownsVerificationHostService;

    public ReleaseVerificationExecutionService()
        : this(new PublishVerificationHostService(), ownsVerificationHostService: true)
    {
    }

    internal ReleaseVerificationExecutionService(
        HttpClient httpClient,
        PowerShellRepositoryResolver powerShellRepositoryResolver)
        : this(
            new PublishVerificationHostService(httpClient, powerShellRepositoryResolver),
            ownsVerificationHostService: false)
    {
    }

    internal ReleaseVerificationExecutionService(
        PublishVerificationHostService verificationHostService)
        : this(verificationHostService, ownsVerificationHostService: false)
    {
    }

    internal ReleaseVerificationExecutionService(
        PublishVerificationHostService verificationHostService,
        bool ownsVerificationHostService)
    {
        _verificationHostService = verificationHostService ?? throw new ArgumentNullException(nameof(verificationHostService));
        _ownsVerificationHostService = ownsVerificationHostService;
    }

    public void Dispose()
    {
        if (_ownsVerificationHostService)
        {
            _verificationHostService.Dispose();
        }
    }

    public IReadOnlyList<ReleaseVerificationTarget> BuildPendingTargets(IEnumerable<ReleaseQueueItem> queueItems)
    {
        return _targetProjectionService.BuildTargetsByKey(
            queueItems,
            ReleaseQueueStage.Verify,
            TryDeserializePublishResult,
            static (_, publishResult) => publishResult.Receipts.Select(receipt => new ReleaseVerificationTarget(
                RootPath: receipt.RootPath,
                RepositoryName: receipt.RepositoryName,
                AdapterKind: receipt.AdapterKind,
                TargetName: receipt.TargetName,
                TargetKind: receipt.TargetKind,
                Destination: receipt.Destination,
                SourcePath: receipt.SourcePath)),
            static target => (target.RootPath, target.AdapterKind, target.TargetName, target.TargetKind, target.Destination, target.SourcePath));
    }

    public async Task<ReleaseVerificationExecutionResult> ExecuteAsync(ReleaseQueueItem queueItem, CancellationToken cancellationToken = default)
        => await ExecuteAsync(queueItem, cancellationToken, progress: null).ConfigureAwait(false);

    public async Task<ReleaseVerificationExecutionResult> ExecuteAsync(
        ReleaseQueueItem queueItem,
        CancellationToken cancellationToken,
        IReleaseArtifactProgressSink? progress)
    {
        ArgumentNullException.ThrowIfNull(queueItem);

        var publishResult = TryDeserializePublishResult(queueItem);
        if (publishResult is null)
        {
            return new ReleaseVerificationExecutionResult(
                RootPath: queueItem.RootPath,
                Succeeded: false,
                Summary: "Verification checkpoint could not be read from queue state.",
                SourceCheckpointStateJson: queueItem.CheckpointStateJson,
                Receipts: [
                    FailedReceipt(queueItem.RootPath, queueItem.RepositoryName, "Verify", "Queue checkpoint", null, "Queue state is missing the publish checkpoint.", "Checkpoint")
                ]);
        }

        if (publishResult.Receipts.Count == 0)
        {
            return new ReleaseVerificationExecutionResult(
                RootPath: queueItem.RootPath,
                Succeeded: false,
                Summary: "Verification cannot run because no publish receipts were captured.",
                SourceCheckpointStateJson: queueItem.CheckpointStateJson,
                Receipts: [
                    FailedReceipt(queueItem.RootPath, queueItem.RepositoryName, "Verify", "Publish receipts", null, "No publish receipts were captured for verification.", "Publish")
                ]);
        }

        for (var index = 0; index < publishResult.Receipts.Count; index++)
        {
            var planned = publishResult.Receipts[index];
            await progress.ReportAsync(
                ReleaseQueueStage.Verify,
                planned.TargetName,
                planned.SourcePath,
                "Planned",
                completedItems: 0,
                totalItems: publishResult.Receipts.Count,
                $"Recorded {planned.TargetKind} destination ready for verification.",
                CancellationToken.None).ConfigureAwait(false);
        }

        var receipts = new List<ReleaseVerificationReceipt>(publishResult.Receipts.Count);
        for (var index = 0; index < publishResult.Receipts.Count; index++)
        {
            var receipt = publishResult.Receipts[index];
            await progress.ReportAsync(
                ReleaseQueueStage.Verify,
                receipt.TargetName,
                receipt.SourcePath,
                "Checking",
                index,
                publishResult.Receipts.Count,
                $"Checking the recorded {receipt.TargetKind} destination.",
                CancellationToken.None).ConfigureAwait(false);
            ReleaseVerificationReceipt result;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                result = await VerifyReceiptAsync(receipt, cancellationToken).ConfigureAwait(false);
                // Some hosts return a failed probe when cancelled instead of throwing.
                if (result.Status != ReleaseVerificationReceiptStatus.Verified)
                    cancellationToken.ThrowIfCancellationRequested();
            }
            catch (Exception exception)
            {
                var cancelled = cancellationToken.IsCancellationRequested || exception is OperationCanceledException;
                var failed = FailedReceipt(receipt.RootPath, receipt.RepositoryName, receipt.AdapterKind,
                    receipt.TargetName, receipt.Destination,
                    cancelled ? "Verification was cancelled before this check completed."
                        : "Verification was interrupted before this check completed. Retry verification to check remote state.",
                    receipt.TargetKind);
                receipts.Add(failed);
                var progressSaved = await TryReportTerminalProgressAsync(
                    progress,
                    ReleaseQueueStage.Verify,
                    receipt.TargetName,
                    receipt.SourcePath,
                    cancelled ? "Cancelled" : "Failed",
                    index + 1,
                    publishResult.Receipts.Count,
                    failed.Summary,
                    CancellationToken.None).ConfigureAwait(false);
                var failedResult = ReleaseQueueExecutionResultFactory.CreateVerificationResult(queueItem, receipts) with
                {
                    WasCancelled = cancelled,
                    Summary = cancelled ? "Verification cancelled; completed check results retained."
                        : "Verification interrupted; completed check results retained."
                };
                return progressSaved ? failedResult : ProgressPersistenceFailure(queueItem, receipts, cancelled);
            }

            receipts.Add(result);
            var terminalSaved = await TryReportTerminalProgressAsync(
                progress,
                ReleaseQueueStage.Verify,
                receipt.TargetName,
                receipt.SourcePath,
                result.Status.ToString(),
                index + 1,
                publishResult.Receipts.Count,
                result.Summary,
                CancellationToken.None).ConfigureAwait(false);
            if (!terminalSaved)
                return ProgressPersistenceFailure(queueItem, receipts, cancelled: false);
        }

        return ReleaseQueueExecutionResultFactory.CreateVerificationResult(queueItem, receipts);
    }

    private static async Task<bool> TryReportTerminalProgressAsync(
        IReleaseArtifactProgressSink? progress,
        ReleaseQueueStage stage,
        string itemName,
        string? itemPath,
        string state,
        int completedItems,
        int totalItems,
        string detail,
        CancellationToken cancellationToken)
    {
        try
        {
            await progress.ReportAsync(stage, itemName, itemPath, state, completedItems, totalItems, detail, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static ReleaseVerificationExecutionResult ProgressPersistenceFailure(
        ReleaseQueueItem queueItem,
        IReadOnlyList<ReleaseVerificationReceipt> receipts,
        bool cancelled)
        => new(
            queueItem.RootPath,
            false,
            "A verification check completed, but its progress update could not be saved. Completed receipts were retained; retry verification to finish the remaining targets.",
            queueItem.CheckpointStateJson,
            receipts)
        {
            WasCancelled = cancelled
        };

    private async Task<ReleaseVerificationReceipt> VerifyReceiptAsync(ReleasePublishReceipt publishReceipt, CancellationToken cancellationToken)
    {
        if (publishReceipt.Status == ReleasePublishReceiptStatus.Skipped)
        {
            return SkippedReceipt(
                publishReceipt,
                string.IsNullOrWhiteSpace(publishReceipt.Summary)
                    ? "Publish was intentionally skipped, so verification is not required."
                    : publishReceipt.Summary);
        }

        if (publishReceipt.Status != ReleasePublishReceiptStatus.Published)
        {
            return FailedReceipt(
                publishReceipt.RootPath,
                publishReceipt.RepositoryName,
                publishReceipt.AdapterKind,
                publishReceipt.TargetName,
                publishReceipt.Destination,
                $"Publish receipt status was {publishReceipt.Status}; verification cannot pass.",
                publishReceipt.TargetKind);
        }

        return publishReceipt.TargetKind switch
        {
            "GitHub" => await VerifyGitHubAsync(publishReceipt, cancellationToken),
            "NuGet" => await VerifyNuGetAsync(publishReceipt, cancellationToken),
            "PowerShellRepository" => await VerifyPowerShellRepositoryAsync(publishReceipt, cancellationToken),
            _ => FailedReceipt(publishReceipt.RootPath, publishReceipt.RepositoryName, publishReceipt.AdapterKind,
                publishReceipt.TargetName, publishReceipt.Destination,
                $"Verification is not available for published {publishReceipt.TargetKind} targets. Delivery remains unverified.", publishReceipt.TargetKind)
        };
    }

    private async Task<ReleaseVerificationReceipt> VerifyGitHubAsync(ReleasePublishReceipt publishReceipt, CancellationToken cancellationToken)
    {
        var result = await VerifyWithHostAsync(publishReceipt, cancellationToken);
        return MapReceipt(publishReceipt, result);
    }

    private async Task<ReleaseVerificationReceipt> VerifyNuGetAsync(ReleasePublishReceipt publishReceipt, CancellationToken cancellationToken)
    {
        var result = await VerifyWithHostAsync(publishReceipt, cancellationToken);
        return MapReceipt(publishReceipt, result);
    }

    private async Task<ReleaseVerificationReceipt> VerifyPowerShellRepositoryAsync(ReleasePublishReceipt publishReceipt, CancellationToken cancellationToken)
    {
        var result = await VerifyWithHostAsync(publishReceipt, cancellationToken);
        return MapReceipt(publishReceipt, result);
    }

    private ReleasePublishExecutionResult? TryDeserializePublishResult(ReleaseQueueItem queueItem)
        => _checkpointSerializer.TryDeserialize<ReleasePublishExecutionResult>(queueItem.CheckpointStateJson);

    private static ReleaseVerificationReceipt VerifiedReceipt(ReleasePublishReceipt publishReceipt, string summary)
        => ReleaseQueueReceiptFactory.CreateVerificationReceipt(
            publishReceipt,
            ReleaseVerificationReceiptStatus.Verified,
            summary);

    private static ReleaseVerificationReceipt SkippedReceipt(ReleasePublishReceipt publishReceipt, string summary)
        => ReleaseQueueReceiptFactory.CreateVerificationReceipt(
            publishReceipt,
            ReleaseVerificationReceiptStatus.Skipped,
            summary);

    private static ReleaseVerificationReceipt FailedReceipt(string rootPath, string repositoryName, string adapterKind, string targetName, string? destination, string summary, string? targetKind = null)
        => ReleaseQueueReceiptFactory.FailedVerificationReceipt(rootPath, repositoryName, adapterKind, targetName, destination, summary, targetKind);

    private async Task<PublishVerificationResult> VerifyWithHostAsync(ReleasePublishReceipt publishReceipt, CancellationToken cancellationToken)
        => await _verificationHostService.VerifyAsync(new PublishVerificationRequest {
            RootPath = publishReceipt.RootPath,
            RepositoryName = publishReceipt.RepositoryName,
            AdapterKind = publishReceipt.AdapterKind,
            TargetName = publishReceipt.TargetName,
            TargetKind = publishReceipt.TargetKind,
            Destination = publishReceipt.Destination,
            SourcePath = publishReceipt.SourcePath
        }, cancellationToken).ConfigureAwait(false);

    private static ReleaseVerificationReceipt MapReceipt(ReleasePublishReceipt publishReceipt, PublishVerificationResult result)
        => result.Status switch
        {
            PublishVerificationStatus.Verified => VerifiedReceipt(publishReceipt, result.Summary),
            _ => FailedReceipt(
                publishReceipt.RootPath,
                publishReceipt.RepositoryName,
                publishReceipt.AdapterKind,
                publishReceipt.TargetName,
                publishReceipt.Destination,
                result.Summary,
                publishReceipt.TargetKind)
        };
}
