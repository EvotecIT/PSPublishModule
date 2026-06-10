namespace PowerForge;

/// <summary>
/// Controls how <c>Install-ModuleDocumentation</c> organizes the output folder structure.
/// </summary>
public enum DocumentationLayout
{
    /// <summary>
    /// Copy files directly into the specified <c>-Path</c> with no module or version subfolders.
    /// This is best for disposable folders or caller-managed custom layouts.
    /// </summary>
    Direct,
    /// <summary>
    /// Create a <c>&lt;ModuleName&gt;</c> subfolder beneath <c>-Path</c>. Repeated installs for newer module
    /// versions target the same module folder, so <see cref="OnExistsOption"/> controls update behavior.
    /// </summary>
    Module,
    /// <summary>
    /// Create nested <c>&lt;ModuleName&gt;\&lt;Version&gt;</c> subfolders under <c>-Path</c>. This default layout
    /// keeps documentation for different module versions side by side.
    /// </summary>
    ModuleAndVersion
}
