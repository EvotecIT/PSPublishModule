namespace PowerForge;

/// <summary>
/// Typed specification for scaffolding a new PowerShell module project from templates.
/// </summary>
public sealed class ModuleScaffoldSpec
{
    /// <summary>Destination path where the module project should exist.</summary>
    public string ProjectRoot { get; set; } = string.Empty;

    /// <summary>Name of the module being scaffolded.</summary>
    public string ModuleName { get; set; } = string.Empty;

    /// <summary>
    /// Optional template root path (directory containing Example-* template files).
    /// When null/empty, PowerForge attempts to locate templates relative to the executing assemblies.
    /// </summary>
    public string? TemplateRootPath { get; set; }
}

