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
}
