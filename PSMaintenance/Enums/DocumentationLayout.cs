namespace PSMaintenance;

/// <summary>
/// Controls how Install-ModuleDocumentation organizes the output folder structure.
/// </summary>
public enum DocumentationLayout
{
    /// <summary>
    /// Copies files directly into the specified <c>-Path</c> with no subfolders.
    /// </summary>
    Direct,
    /// <summary>
    /// Creates a <c>&lt;ModuleName&gt;</c> subfolder beneath <c>-Path</c>.
    /// </summary>
    Module,
    /// <summary>
    /// Creates nested <c>&lt;ModuleName&gt;\&lt;Version&gt;</c> subfolders under <c>-Path</c>.
    /// </summary>
    ModuleAndVersion
}
