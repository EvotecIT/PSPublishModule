namespace PowerForge;

/// <summary>
/// Request for generating a project build plan through the PowerShell-host fallback path.
/// </summary>
public sealed class ProjectBuildCommandPlanRequest
{
    /// <summary>Repository root used as the working directory.</summary>
    public string RepositoryRoot { get; set; } = string.Empty;

    /// <summary>Output path for the generated plan JSON.</summary>
    public string PlanOutputPath { get; set; } = string.Empty;

    /// <summary>Optional project build config path.</summary>
    public string? ConfigPath { get; set; }

    /// <summary>Resolved PSPublishModule path used for Import-Module.</summary>
    public string ModulePath { get; set; } = string.Empty;
}
