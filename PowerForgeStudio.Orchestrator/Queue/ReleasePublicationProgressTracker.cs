using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Orchestrator.Host;

namespace PowerForgeStudio.Orchestrator.Queue;

/// <summary>
/// Reports the reviewed publication targets as stable progress units. A running target without a
/// terminal event is intentionally retained as reconciliation evidence after an interruption.
/// </summary>
internal sealed class ReleasePublicationProgressTracker(
    IReadOnlyList<ReleasePublishTarget> targets,
    IReleaseArtifactProgressSink? sink)
{
    private readonly HashSet<ReleasePublishTarget> _completed = [];

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        foreach (var target in targets)
        {
            await sink.ReportAsync(
                ReleaseQueueStage.Publish,
                target.TargetName,
                target.SourcePath,
                "Planned",
                completedItems: 0,
                totalItems: targets.Count,
                $"Destination: {StudioOutputSanitizer.SanitizeDestination(target.Destination)}",
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    public async Task StartAsync(
        Func<ReleasePublishTarget, bool> predicate,
        string detail,
        CancellationToken cancellationToken)
    {
        foreach (var target in targets.Where(target => !_completed.Contains(target) && predicate(target)))
        {
            await sink.ReportAsync(
                ReleaseQueueStage.Publish,
                target.TargetName,
                target.SourcePath,
                "Publishing",
                _completed.Count,
                targets.Count,
                StudioOutputSanitizer.Sanitize(detail),
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    public async Task CompleteAsync(
        Func<ReleasePublishTarget, bool> predicate,
        IReadOnlyList<ReleasePublishReceipt> receipts,
        string fallbackDetail,
        CancellationToken cancellationToken)
    {
        foreach (var target in targets.Where(target => !_completed.Contains(target) && predicate(target)))
        {
            var matching = receipts
                .Where(receipt => string.Equals(receipt.TargetKind, target.TargetKind, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var state = matching.Length == 0 || matching.Any(static receipt => receipt.Status == ReleasePublishReceiptStatus.Failed)
                ? "Failed"
                : matching.All(static receipt => receipt.Status == ReleasePublishReceiptStatus.Skipped)
                    ? "Skipped"
                    : "Published";
            var detail = matching.FirstOrDefault(static receipt => receipt.Status == ReleasePublishReceiptStatus.Failed)?.Summary
                         ?? matching.LastOrDefault()?.Summary
                         ?? fallbackDetail;
            _completed.Add(target);
            await sink.ReportAsync(
                ReleaseQueueStage.Publish,
                target.TargetName,
                target.SourcePath,
                state,
                _completed.Count,
                targets.Count,
                StudioOutputSanitizer.Sanitize(detail),
                CancellationToken.None).ConfigureAwait(false);
        }
    }
}
