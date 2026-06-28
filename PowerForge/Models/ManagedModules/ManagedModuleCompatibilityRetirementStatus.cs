namespace PowerForge;

/// <summary>
/// Overall readiness for marking compatibility module transport as legacy.
/// </summary>
public enum ManagedModuleCompatibilityRetirementStatus
{
    /// <summary>
    /// Required evidence is still missing or incomplete.
    /// </summary>
    Incomplete,

    /// <summary>
    /// Required evidence is complete and compatibility transport can be treated as legacy.
    /// </summary>
    Ready,

    /// <summary>
    /// Required evidence found a known blocker.
    /// </summary>
    Blocked
}
