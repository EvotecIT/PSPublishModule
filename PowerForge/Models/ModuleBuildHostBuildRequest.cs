namespace PowerForge;

/// <summary>
/// Host-facing request for executing a module build through shared orchestration.
/// </summary>
public sealed class ModuleBuildHostBuildRequest
{
    internal IPowerForgeReleaseProgressReporterV2? Progress { get; set; }

    /// <summary>
    /// Repository root used as the command working directory.
    /// </summary>
    public string RepositoryRoot { get; set; } = string.Empty;

    /// <summary>
    /// Path to a module pipeline JSON configuration. Mutually exclusive with <see cref="ScriptPath"/>.
    /// </summary>
    public string? ConfigPath { get; set; }

    /// <summary>
    /// Path to the repository's legacy <c>Build-Module.ps1</c> script.
    /// </summary>
    public string? ScriptPath { get; set; }

    /// <summary>
    /// Path to the PSPublishModule manifest that should be imported.
    /// </summary>
    public string ModulePath { get; set; } = string.Empty;

    /// <summary>
    /// Optional build configuration override.
    /// </summary>
    public string? Configuration { get; set; }

    /// <summary>
    /// Optional target framework used by the module build script.
    /// </summary>
    public string? Framework { get; set; }

    /// <summary>
    /// Optional module pipeline gate to forward to the build script.
    /// </summary>
    public ConfigurationGateMode? RunMode { get; set; }

    /// <summary>
    /// Marks the invocation as a child stage of the unified release engine.
    /// Repository wrappers can use this to avoid routing back into the release entry point.
    /// </summary>
    public bool PowerForgeReleaseStage { get; set; }

    /// <summary>
    /// Indicates that the parent release workflow will publish the module artifacts
    /// through its unified GitHub release instead of the module's legacy publisher.
    /// </summary>
    public bool UnifiedGitHubRelease { get; set; }

    /// <summary>
    /// Skips the preliminary dotnet build step inside the module script.
    /// </summary>
    public bool NoDotnetBuild { get; set; }

    /// <summary>
    /// Indicates that <see cref="NoDotnetBuild"/> was explicitly selected, including an explicit false value.
    /// </summary>
    public bool NoDotnetBuildWasSpecified { get; set; }

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

    /// <summary>
    /// Indicates that <see cref="SignModule"/> was explicitly selected, including an explicit false value.
    /// </summary>
    public bool SignModuleWasSpecified { get; set; }

    /// <summary>
    /// Includes project-package work declared by the module build script.
    /// </summary>
    public bool IncludeProjectPackages { get; set; } = true;

    /// <summary>
    /// Skips installing the module after a JSON-backed build.
    /// </summary>
    public bool SkipInstall { get; set; }

    /// <summary>
    /// Maximum runtime for the out-of-process module workflow.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromHours(2);

    /// <summary>Optional module signing certificate thumbprint.</summary>
    public string? CertificateThumbprint { get; set; }

    /// <summary>Optional override for signing binaries.</summary>
    public bool? SignIncludeBinaries { get; set; }

    /// <summary>Optional override for signing internal files.</summary>
    public bool? SignIncludeInternals { get; set; }

    /// <summary>Optional override for signing executable files.</summary>
    public bool? SignIncludeExe { get; set; }

    /// <summary>Optional diagnostics baseline path.</summary>
    public string? DiagnosticsBaselinePath { get; set; }

    /// <summary>Optional diagnostics-baseline generation override.</summary>
    public bool? GenerateDiagnosticsBaseline { get; set; }

    /// <summary>Optional diagnostics-baseline update override.</summary>
    public bool? UpdateDiagnosticsBaseline { get; set; }

    /// <summary>Optional new-diagnostics policy override.</summary>
    public bool? FailOnNewDiagnostics { get; set; }

    /// <summary>Optional diagnostics severity threshold.</summary>
    public string? FailOnDiagnosticsSeverity { get; set; }
}
