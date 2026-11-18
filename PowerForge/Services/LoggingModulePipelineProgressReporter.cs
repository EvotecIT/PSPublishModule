using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

/// <summary>
/// Step progress as log lines, for builds that don't draw a live view (CI, redirected output, -Verbose, plain mode).
/// Each step logs "[n/N] Title" when it starts and its elapsed time when it finishes, and a step that runs longer
/// than <see cref="HeartbeatInterval"/> keeps saying it is still running, so a quiet step never looks hung.
/// </summary>
internal sealed class LoggingModulePipelineProgressReporter : IModulePipelineProgressReporterV2
{
    // Slower than OperationHeartbeat: inner operations (dotnet, Pester, uploads) report their own heartbeats.
    internal static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    private readonly ILogger _logger;
    private readonly TimeSpan _heartbeatInterval;
    private readonly Dictionary<string, int> _positions;
    private readonly int _total;
    private readonly ConcurrentDictionary<string, OperationHeartbeat> _running = new(StringComparer.OrdinalIgnoreCase);

    internal LoggingModulePipelineProgressReporter(ILogger logger, IReadOnlyList<ModulePipelineStep> steps, TimeSpan? heartbeatInterval = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _heartbeatInterval = heartbeatInterval ?? HeartbeatInterval;
        var ordered = (steps ?? Array.Empty<ModulePipelineStep>()).Where(static step => !string.IsNullOrWhiteSpace(step.Key)).ToArray();
        _total = ordered.Length;
        _positions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < ordered.Length; i++)
            _positions[ordered[i].Key] = i + 1;
    }

    public void StepStarting(ModulePipelineStep step)
    {
        if (step is null) return;
        var label = Label(step);
        _logger.Info(label);
        var heartbeat = OperationHeartbeat.Start(_logger, label, interval: _heartbeatInterval);
        if (_running.TryRemove(step.Key, out var previous)) previous.Dispose();
        _running[step.Key] = heartbeat;
    }

    public void StepCompleted(ModulePipelineStep step)
    {
        if (step is null) return;
        _logger.Success($"{Label(step)} done{Elapsed(step)}");
    }

    public void StepFailed(ModulePipelineStep step, Exception error)
    {
        // The error itself is reported by the caller; this line only closes the step.
        if (step is null) return;
        _logger.Info($"{Label(step)} failed{Elapsed(step)}");
    }

    public void StepSkipped(ModulePipelineStep step)
    {
        if (step is null) return;
        Stop(step);
        _logger.Verbose($"{Label(step)} skipped");
    }

    private string Label(ModulePipelineStep step)
        => _positions.TryGetValue(step.Key ?? string.Empty, out var position)
            ? $"[{position}/{_total}] {step.Title}"
            : step.Title;

    private string Elapsed(ModulePipelineStep step)
    {
        var elapsed = Stop(step);
        return elapsed is { } value ? $" in {DotNetRepositoryReleaseService.FormatDuration(value)}" : string.Empty;
    }

    private TimeSpan? Stop(ModulePipelineStep step)
    {
        if (!_running.TryRemove(step.Key ?? string.Empty, out var heartbeat)) return null;
        var elapsed = heartbeat.Elapsed;
        heartbeat.Dispose();
        return elapsed;
    }
}
