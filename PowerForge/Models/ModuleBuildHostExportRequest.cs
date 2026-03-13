namespace PowerForge;

/// <summary>
/// Host-facing request for exporting module pipeline JSON via a module build script.
/// </summary>
public sealed class ModuleBuildHostExportRequest
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
    /// Output path for the exported JSON file.
    /// </summary>
    public string OutputPath { get; set; } = string.Empty;
}
