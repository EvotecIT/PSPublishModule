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

    /// <summary>
    /// Enables disposable-host native install and update benchmarks for compatibility engines.
    /// </summary>
    public bool EnableNativeInstallUpdateBenchmarks { get; set; }

    /// <summary>
    /// Maximum allowed ratio between managed median elapsed time and the fastest successful compatibility median.
    /// </summary>
    public double MaximumManagedSlowdownRatio { get; set; } = 3.0;

    /// <summary>
    /// Absolute tolerance, in milliseconds, allowed above the fastest successful compatibility median.
    /// </summary>
    public int MaximumManagedSlowdownMilliseconds { get; set; } = 5000;
}
