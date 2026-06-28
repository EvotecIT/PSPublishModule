namespace PowerForge;

/// <summary>
/// Result returned by the managed module benchmark harness.
/// </summary>
public sealed class ManagedModuleBenchmarkResult
{
    /// <summary>
    /// UTC timestamp when benchmark execution started.
    /// </summary>
    public DateTimeOffset StartedAtUtc { get; set; }

    /// <summary>
    /// UTC timestamp when benchmark execution completed.
    /// </summary>
    public DateTimeOffset CompletedAtUtc { get; set; }

    /// <summary>
    /// Individual measured scenario runs.
    /// </summary>
    public IReadOnlyList<ManagedModuleBenchmarkRunResult> Runs { get; set; } = Array.Empty<ManagedModuleBenchmarkRunResult>();

    /// <summary>
    /// Runtime and host metadata describing where this benchmark result was produced.
    /// </summary>
    public ManagedModuleBenchmarkEnvironment Environment { get; set; } = ManagedModuleBenchmarkEnvironment.Capture();

    /// <summary>
    /// Operation-level evidence gates used when deciding whether the managed engine can replace compatibility defaults.
    /// </summary>
    public IReadOnlyList<ManagedModuleBenchmarkTransitionGateResult> TransitionGates { get; set; } = Array.Empty<ManagedModuleBenchmarkTransitionGateResult>();
}
