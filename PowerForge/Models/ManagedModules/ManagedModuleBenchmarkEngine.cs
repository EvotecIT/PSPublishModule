namespace PowerForge;

/// <summary>
/// Module delivery engine measured by the benchmark harness.
/// </summary>
public enum ManagedModuleBenchmarkEngine
{
    /// <summary>
    /// Measure the managed C# module engine.
    /// </summary>
    Managed,

    /// <summary>
    /// Measure the compatibility path through PSResourceGet.
    /// </summary>
    PSResourceGet,

    /// <summary>
    /// Measure the compatibility path through PowerShellGet.
    /// </summary>
    PowerShellGet
}
