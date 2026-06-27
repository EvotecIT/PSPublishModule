namespace PowerForge;

/// <summary>
/// Managed module lifecycle operation measured by the benchmark harness.
/// </summary>
public enum ManagedModuleBenchmarkOperation
{
    /// <summary>
    /// Install a module into a PowerShell module root.
    /// </summary>
    Install,

    /// <summary>
    /// Save a module into an explicit module root.
    /// </summary>
    Save,

    /// <summary>
    /// Update an existing module in a PowerShell module root.
    /// </summary>
    Update
}
