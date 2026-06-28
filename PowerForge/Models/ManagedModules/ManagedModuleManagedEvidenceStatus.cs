namespace PowerForge;

/// <summary>
/// Overall readiness for managed module engine benchmark evidence.
/// </summary>
public enum ManagedModuleManagedEvidenceStatus
{
    /// <summary>
    /// Managed engine evidence is missing or incomplete for one or more required operations.
    /// </summary>
    Incomplete,

    /// <summary>
    /// Managed engine evidence is present and ready for the required operations.
    /// </summary>
    Ready
}
