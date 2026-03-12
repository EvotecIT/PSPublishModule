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
        return _targetProjectionService.BuildTargets(
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
            static target => $"{target.RootPath}|{target.AdapterKind}|{target.TargetName}|{target.TargetKind}");
    }

    public async Task<ReleaseVerificationExecutionResult> ExecuteAsync(ReleaseQueueItem queueItem, CancellationToken cancellationToken = default)
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

        var receipts = new List<ReleaseVerificationReceipt>(publishResult.Receipts.Count);
        foreach (var receipt in publishResult.Receipts)
        {
            receipts.Add(await VerifyReceiptAsync(receipt, cancellationToken));
        }

        return ReleaseQueueExecutionResultFactory.CreateVerificationResult(queueItem, receipts);
    }

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
            _ => SkippedReceipt(publishReceipt, $"Verification is not implemented for {publishReceipt.TargetKind} targets yet.")
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
            PublishVerificationStatus.Skipped => SkippedReceipt(publishReceipt, result.Summary),
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
