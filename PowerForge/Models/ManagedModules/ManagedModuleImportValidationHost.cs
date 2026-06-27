namespace PowerForge;

/// <summary>
/// PowerShell hosts that can be used to validate imported module versions after benchmark delivery.
/// </summary>
public enum ManagedModuleImportValidationHost
{
    /// <summary>
    /// Validate by importing the module in PowerShell 7 or newer.
    /// </summary>
    PowerShell7 = 0,

    /// <summary>
    /// Validate by importing the module in Windows PowerShell 5.1.
    /// </summary>
    WindowsPowerShell = 1
}
