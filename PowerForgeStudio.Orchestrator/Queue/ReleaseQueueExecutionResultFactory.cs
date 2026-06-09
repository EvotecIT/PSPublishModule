using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Verification;

namespace PowerForgeStudio.Orchestrator.Queue;

public static class ReleaseQueueExecutionResultFactory
{
    public static ReleaseBuildExecutionResult CreateBuildResult(
        string rootPath,
        TimeSpan duration,
        IReadOnlyList<ReleaseBuildAdapterResult> adapterResults)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(adapterResults);

        var succeeded = adapterResults.Count > 0 && adapterResults.All(result => result.Succeeded);
        var summary = succeeded
            ? $"Build completed for {adapterResults.Count} adapter(s) without publish/install side effects."
            : FirstLine(adapterResults.FirstOrDefault(result => !result.Succeeded)?.ErrorTail
                ?? adapterResults.FirstOrDefault(result => !result.Succeeded)?.OutputTail
                ?? "Build execution failed.");

        return new ReleaseBuildExecutionResult(
            RootPath: rootPath,
            Succeeded: succeeded,
            Summary: summary,
            DurationSeconds: Math.Round(duration.TotalSeconds, 2),
            AdapterResults: adapterResults);
    }

    public static ReleasePublishExecutionResult CreatePublishResult(
        ReleaseQueueItem queueItem,
        IReadOnlyList<ReleasePublishReceipt> receipts)
    {
        ArgumentNullException.ThrowIfNull(queueItem);
        ArgumentNullException.ThrowIfNull(receipts);

        var published = receipts.Count(receipt => receipt.Status == Domain.Publish.ReleasePublishReceiptStatus.Published);
        var skipped = receipts.Count(receipt => receipt.Status == Domain.Publish.ReleasePublishReceiptStatus.Skipped);
        var failed = receipts.Count(receipt => receipt.Status == Domain.Publish.ReleasePublishReceiptStatus.Failed);
        var summary = failed > 0
            ? $"Publish completed with {published} published, {skipped} skipped, and {failed} failed target(s)."
            : $"Publish completed with {published} published and {skipped} skipped target(s).";

        return new ReleasePublishExecutionResult(
            RootPath: queueItem.RootPath,
            Succeeded: failed == 0,
            Summary: summary,
            SourceCheckpointStateJson: queueItem.CheckpointStateJson,
            Receipts: receipts);
    }

    public static ReleaseVerificationExecutionResult CreateVerificationResult(
        ReleaseQueueItem queueItem,
        IReadOnlyList<ReleaseVerificationReceipt> receipts)
    {
        ArgumentNullException.ThrowIfNull(queueItem);
        ArgumentNullException.ThrowIfNull(receipts);

        var verified = receipts.Count(receipt => receipt.Status == Domain.Verification.ReleaseVerificationReceiptStatus.Verified);
        var skipped = receipts.Count(receipt => receipt.Status == Domain.Verification.ReleaseVerificationReceiptStatus.Skipped);
        var failed = receipts.Count(receipt => receipt.Status == Domain.Verification.ReleaseVerificationReceiptStatus.Failed);
        var summary = failed > 0
            ? $"Verification completed with {verified} verified, {skipped} skipped, and {failed} failed check(s)."
            : $"Verification completed with {verified} verified and {skipped} skipped check(s).";

        return new ReleaseVerificationExecutionResult(
            RootPath: queueItem.RootPath,
            Succeeded: failed == 0,
            Summary: summary,
            SourceCheckpointStateJson: queueItem.CheckpointStateJson,
            Receipts: receipts);
    }

    private static string FirstLine(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? value;
}
