namespace PowerForge;

/// <summary>
/// Summary of whether managed module engine evidence is ready for measured lifecycle operations.
/// </summary>
public sealed class ManagedModuleManagedEvidenceResult
{
    /// <summary>
    /// Overall managed evidence readiness.
    /// </summary>
    public ManagedModuleManagedEvidenceStatus Status { get; set; }

    /// <summary>
    /// True when managed engine evidence is present and ready for all required operations.
    /// </summary>
    public bool Ready { get; set; }

    /// <summary>
    /// Lifecycle operations required by this managed evidence gate.
    /// </summary>
    public IReadOnlyList<ManagedModuleBenchmarkOperation> RequiredOperations { get; set; } = Array.Empty<ManagedModuleBenchmarkOperation>();

    /// <summary>
    /// Required lifecycle operations with ready managed evidence.
    /// </summary>
    public IReadOnlyList<ManagedModuleBenchmarkOperation> ReadyOperations { get; set; } = Array.Empty<ManagedModuleBenchmarkOperation>();

    /// <summary>
    /// Required lifecycle operations without managed evidence.
    /// </summary>
    public IReadOnlyList<ManagedModuleBenchmarkOperation> MissingOperations { get; set; } = Array.Empty<ManagedModuleBenchmarkOperation>();

    /// <summary>
    /// Required lifecycle operations with incomplete managed evidence.
    /// </summary>
    public IReadOnlyList<ManagedModuleBenchmarkOperation> IncompleteOperations { get; set; } = Array.Empty<ManagedModuleBenchmarkOperation>();

    /// <summary>
    /// Human-readable reasons explaining readiness or remaining work.
    /// </summary>
    public IReadOnlyList<string> Reasons { get; set; } = Array.Empty<string>();
}
