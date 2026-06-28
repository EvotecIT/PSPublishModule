namespace PowerForge;

/// <summary>
/// Summary of whether compatibility module transport can be marked legacy.
/// </summary>
public sealed class ManagedModuleCompatibilityRetirementResult
{
    /// <summary>
    /// Overall retirement readiness.
    /// </summary>
    public ManagedModuleCompatibilityRetirementStatus Status { get; set; }

    /// <summary>
    /// True when all required transition and provider-support evidence is ready.
    /// </summary>
    public bool ReadyToMarkCompatibilityLegacy { get; set; }

    /// <summary>
    /// Required lifecycle operations considered by the retirement gate.
    /// </summary>
    public IReadOnlyList<ManagedModuleBenchmarkOperation> RequiredOperations { get; set; } = Array.Empty<ManagedModuleBenchmarkOperation>();

    /// <summary>
    /// Required lifecycle operations with ready transition gates.
    /// </summary>
    public IReadOnlyList<ManagedModuleBenchmarkOperation> ReadyOperations { get; set; } = Array.Empty<ManagedModuleBenchmarkOperation>();

    /// <summary>
    /// Required lifecycle operations without transition-gate evidence.
    /// </summary>
    public IReadOnlyList<ManagedModuleBenchmarkOperation> MissingOperations { get; set; } = Array.Empty<ManagedModuleBenchmarkOperation>();

    /// <summary>
    /// Required lifecycle operations with incomplete transition gates.
    /// </summary>
    public IReadOnlyList<ManagedModuleBenchmarkOperation> IncompleteOperations { get; set; } = Array.Empty<ManagedModuleBenchmarkOperation>();

    /// <summary>
    /// Required lifecycle operations with blocked transition gates.
    /// </summary>
    public IReadOnlyList<ManagedModuleBenchmarkOperation> BlockedOperations { get; set; } = Array.Empty<ManagedModuleBenchmarkOperation>();

    /// <summary>
    /// Provider support entries that still recommend compatibility fallback.
    /// </summary>
    public IReadOnlyList<ManagedModuleProviderSupport> ProviderFallbacks { get; set; } = Array.Empty<ManagedModuleProviderSupport>();

    /// <summary>
    /// Human-readable reasons explaining readiness or remaining work.
    /// </summary>
    public IReadOnlyList<string> Reasons { get; set; } = Array.Empty<string>();
}
