namespace PowerForge;

/// <summary>
/// PowerShell module path family used for managed install root resolution.
/// </summary>
public enum ManagedModuleShellEdition
{
    /// <summary>
    /// Infer the path family from the current runtime.
    /// </summary>
    Auto,

    /// <summary>
    /// Windows PowerShell module path family.
    /// </summary>
    Desktop,

    /// <summary>
    /// PowerShell 7+ module path family.
    /// </summary>
    Core
}
