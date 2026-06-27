namespace PowerForge;

/// <summary>
/// Request for running managed module lifecycle benchmark scenarios.
/// </summary>
public sealed class ManagedModuleBenchmarkRequest
{
    /// <summary>
    /// Scenarios to measure.
    /// </summary>
    public IReadOnlyList<ManagedModuleBenchmarkScenario> Scenarios { get; set; } = Array.Empty<ManagedModuleBenchmarkScenario>();

    /// <summary>
    /// Continue measuring later scenarios when one scenario fails.
    /// </summary>
    public bool ContinueOnError { get; set; } = true;
}
