namespace PowerForge;

/// <summary>
/// Host-facing request for executing a module build script through shared orchestration.
/// </summary>
public sealed class ModuleBuildHostBuildRequest
{
    /// <summary>
    /// Repository root used as the command working directory.
    /// </summary>
    public string RepositoryRoot { get; set; } = string.Empty;

    /// <summary>
    /// Path to the repository's <c>Build-Module.ps1</c> script.
    /// </summary>
    public string ScriptPath { get; set; } = string.Empty;

    /// <summary>
    /// Path to the PSPublishModule manifest that should be imported.
    /// </summary>
    public string ModulePath { get; set; } = string.Empty;

    /// <summary>
    /// Optional build configuration override.
    /// </summary>
    public string? Configuration { get; set; }

    /// <summary>
    /// Skips the preliminary dotnet build step inside the module script.
    /// </summary>
    public bool NoDotnetBuild { get; set; }

    /// <summary>
    /// Optional module version override.
    /// </summary>
    public string? ModuleVersion { get; set; }

    /// <summary>
    /// Optional prerelease tag override.
    /// </summary>
    public string? PreReleaseTag { get; set; }

    /// <summary>
    /// Disables module signing when true.
    /// </summary>
    public bool NoSign { get; set; }

    /// <summary>
    /// Enables module signing when true.
    /// </summary>
    public bool SignModule { get; set; }
}
