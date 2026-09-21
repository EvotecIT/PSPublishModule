using PowerForgeStudio.Domain.Queue;

namespace PowerForgeStudio.Orchestrator.Queue;

/// <summary>A bounded, secret-free release work-item update suitable for live display and crash recovery.</summary>
public sealed record ReleaseArtifactProgress(
    ReleaseQueueStage Stage,
    string ItemName,
    string? ItemPath,
    string State,
    int CompletedItems,
    int TotalItems,
    string Detail,
    DateTimeOffset ObservedAtUtc)
{
    public string CountDisplay => TotalItems > 0 ? $"{CompletedItems}/{TotalItems}" : "—";
    public string Display => $"{Stage}: {ItemName} · {State} · {CountDisplay}";
}

/// <summary>Receives release progress in execution order. Implementations may persist before returning.</summary>
public interface IReleaseArtifactProgressSink
{
    ValueTask ReportAsync(ReleaseArtifactProgress progress, CancellationToken cancellationToken = default);
}

internal static class ReleaseArtifactProgressSinkExtensions
{
    public static ValueTask ReportAsync(
        this IReleaseArtifactProgressSink? sink,
        ReleaseQueueStage stage,
        string itemName,
        string? itemPath,
        string state,
        int completedItems,
        int totalItems,
        string? detail,
        CancellationToken cancellationToken)
    {
        if (sink is null) return ValueTask.CompletedTask;
        return sink.ReportAsync(new ReleaseArtifactProgress(
            stage,
            itemName,
            itemPath,
            state,
            completedItems,
            totalItems,
            Host.StudioOutputSanitizer.Sanitize(detail),
            DateTimeOffset.UtcNow), cancellationToken);
    }
}
