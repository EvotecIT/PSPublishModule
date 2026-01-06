namespace PowerForge;

/// <summary>
/// Specification for running module validation checks.
/// </summary>
public sealed class ModuleValidationSpec
{
    /// <summary>Project root for the module.</summary>
    public string ProjectRoot { get; set; } = string.Empty;

    /// <summary>Staging path containing the built module.</summary>
    public string StagingPath { get; set; } = string.Empty;

    /// <summary>Module name.</summary>
    public string ModuleName { get; set; } = string.Empty;

    /// <summary>Path to the module manifest (PSD1).</summary>
    public string ManifestPath { get; set; } = string.Empty;

    /// <summary>Build spec for resolving csproj metadata.</summary>
    public ModuleBuildSpec? BuildSpec { get; set; }

    /// <summary>Validation settings.</summary>
    public ModuleValidationSettings Settings { get; set; } = new();
}
