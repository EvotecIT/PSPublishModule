using PowerForgeStudio.Orchestrator.Host;
using PowerForgeStudio.Orchestrator.Storage;

namespace PowerForgeStudio.Orchestrator.Queue;

/// <summary>Persists each progress event before forwarding it to an optional live presentation sink.</summary>
internal sealed class DurableReleaseArtifactProgressSink(
    ReleaseStateDatabase database,
    string sessionId,
    IReleaseArtifactProgressSink? live) : IReleaseArtifactProgressSink
{
    public async ValueTask ReportAsync(ReleaseArtifactProgress progress, CancellationToken cancellationToken = default)
    {
        var safe = progress with {
            ItemName = StudioOutputSanitizer.Sanitize(progress.ItemName),
            ItemPath = string.IsNullOrWhiteSpace(progress.ItemPath) ? null : StudioOutputSanitizer.Sanitize(progress.ItemPath),
            State = StudioOutputSanitizer.Sanitize(progress.State),
            Detail = StudioOutputSanitizer.Sanitize(progress.Detail)
        };
        await database.AppendReleaseProgressAsync(sessionId, safe, cancellationToken).ConfigureAwait(false);
        if (live is not null)
            await live.ReportAsync(safe, cancellationToken).ConfigureAwait(false);
    }
}
