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
    /// Engines to measure for each scenario. Defaults to the managed C# engine.
    /// </summary>
    public IReadOnlyList<ManagedModuleBenchmarkEngine> Engines { get; set; } = new[] { ManagedModuleBenchmarkEngine.Managed };

    /// <summary>
    /// Continue measuring later scenarios when one scenario fails.
    /// </summary>
    public bool ContinueOnError { get; set; } = true;
}
