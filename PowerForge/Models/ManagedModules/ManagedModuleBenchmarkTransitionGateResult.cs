namespace PowerForge;

/// <summary>
/// Operation-level benchmark evidence used to decide whether managed transport can become the default.
/// </summary>
public sealed class ManagedModuleBenchmarkTransitionGateResult
{
    /// <summary>
    /// Lifecycle operation being assessed.
    /// </summary>
    public ManagedModuleBenchmarkOperation Operation { get; set; }

    /// <summary>
    /// Overall gate status.
    /// </summary>
    public ManagedModuleBenchmarkTransitionGateStatus Status { get; set; }

    /// <summary>
    /// True when the operation has enough evidence to move to managed transport by default.
    /// </summary>
    public bool ReadyForDefaultManagedTransport { get; set; }

    /// <summary>
    /// True when compatibility fallback is still required for this operation.
    /// </summary>
    public bool CompatibilityFallbackRequired { get; set; }

    /// <summary>
    /// Reason compatibility fallback is still required.
    /// </summary>
    public string? CompatibilityFallbackReason { get; set; }

    /// <summary>
    /// True when native install/update comparison needs an isolated disposable host before it can be trusted.
    /// </summary>
    public bool NativeIsolationRequired { get; set; }

    /// <summary>
    /// Number of managed engine runs recorded for this operation.
    /// </summary>
    public int ManagedRunCount { get; set; }

    /// <summary>
    /// Number of successful managed engine runs recorded for this operation.
    /// </summary>
    public int SuccessfulManagedRunCount { get; set; }

    /// <summary>
    /// Number of compatibility engine runs recorded for this operation.
    /// </summary>
    public int CompatibilityRunCount { get; set; }

    /// <summary>
    /// Number of successful compatibility engine runs recorded for this operation.
    /// </summary>
    public int SuccessfulCompatibilityRunCount { get; set; }

    /// <summary>
    /// Compatibility engines expected before this operation can replace existing defaults.
    /// </summary>
    public IReadOnlyList<string> RequiredCompatibilityEngines { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Required compatibility engines that have at least one successful run.
    /// </summary>
    public IReadOnlyList<string> CoveredCompatibilityEngines { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Human-readable reasons explaining why the gate is not ready or why it passed.
    /// </summary>
    public IReadOnlyList<string> Reasons { get; set; } = Array.Empty<string>();
}
