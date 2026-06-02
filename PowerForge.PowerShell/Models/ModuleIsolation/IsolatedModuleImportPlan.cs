namespace PowerForge;

/// <summary>
/// Prepared isolated module import paths and profile metadata.
/// </summary>
public sealed class IsolatedModuleImportPlan
{
    /// <summary>Profile selected for this import.</summary>
    public ModuleIsolationProfile Profile { get; set; } = new();

    /// <summary>Original module base path.</summary>
    public string SourceModuleBase { get; set; } = string.Empty;

    /// <summary>Root folder containing the copied module payload.</summary>
    public string WorkPath { get; set; } = string.Empty;

    /// <summary>Copied module base path inside <see cref="WorkPath"/>.</summary>
    public string IsolatedModuleBase { get; set; } = string.Empty;

    /// <summary>Generated script module path to import.</summary>
    public string IsolatedScriptPath { get; set; } = string.Empty;

    /// <summary>Generated manifest path when the profile preserves a manifest export contract.</summary>
    public string IsolatedManifestPath { get; set; } = string.Empty;

    /// <summary>Path imported into PowerShell, either the generated manifest or generated script.</summary>
    public string IsolatedImportPath { get; set; } = string.Empty;
}
