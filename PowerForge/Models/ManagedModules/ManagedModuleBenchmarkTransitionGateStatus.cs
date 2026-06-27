namespace PowerForge;

/// <summary>
/// Readiness state for moving a module lifecycle operation to managed transport by default.
/// </summary>
public enum ManagedModuleBenchmarkTransitionGateStatus
{
    /// <summary>
    /// Required benchmark evidence is not complete yet.
    /// </summary>
    Incomplete,

    /// <summary>
    /// Required benchmark evidence is present and successful.
    /// </summary>
    Ready,

    /// <summary>
    /// Benchmark evidence hit a known blocker that requires an explicit compatibility runner or provider work.
    /// </summary>
    Blocked
}
