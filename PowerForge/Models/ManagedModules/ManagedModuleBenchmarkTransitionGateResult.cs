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
    /// Compatibility provider failures that explain why fallback remains required.
    /// </summary>
    public IReadOnlyList<string> CompatibilityProviderLimitations { get; set; } = Array.Empty<string>();

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
    /// Managed median elapsed milliseconds used for performance readiness, when comparable evidence exists.
    /// </summary>
    public double? ManagedMedianMilliseconds { get; set; }

    /// <summary>
    /// Fastest successful compatibility median elapsed milliseconds used for performance readiness.
    /// </summary>
    public double? CompatibilityMedianMilliseconds { get; set; }

    /// <summary>
    /// Maximum managed median elapsed milliseconds allowed by the active performance policy.
    /// </summary>
    public double? AllowedManagedMilliseconds { get; set; }

    /// <summary>
    /// True when managed elapsed evidence is within the active performance policy.
    /// </summary>
    public bool? PerformanceWithinPolicy { get; set; }

    /// <summary>
    /// Active maximum managed slowdown ratio, when performance readiness is evaluated.
    /// </summary>
    public double? MaximumManagedSlowdownRatio { get; set; }

    /// <summary>
    /// Active absolute managed slowdown tolerance in milliseconds, when performance readiness is evaluated.
    /// </summary>
    public int? MaximumManagedSlowdownMilliseconds { get; set; }

    /// <summary>
    /// Human-readable reasons explaining why the gate is not ready or why it passed.
    /// </summary>
    public IReadOnlyList<string> Reasons { get; set; } = Array.Empty<string>();
}
