namespace PowerForge;

/// <summary>
/// Typed specification for running a legacy-style build pipeline driven by configuration segments.
/// Intended for CLI/VSCode scenarios where a JSON configuration should map to the same behavior as the
/// <c>Build-Module</c> / <c>New-Configuration*</c> DSL.
/// </summary>
public sealed class ModulePipelineSpec
{
    /// <summary>
    /// Optional schema version for external tooling.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// Base build specification (module name, source path, staging path, etc).
    /// Configuration segments can override individual fields (e.g., version, author, frameworks) when provided.
    /// </summary>
    public ModuleBuildSpec Build { get; set; } = new();

    /// <summary>
    /// Install options for the pipeline. When <see cref="ModulePipelineInstallOptions.Enabled"/> is false,
    /// the pipeline will only build to staging.
    /// </summary>
    public ModulePipelineInstallOptions Install { get; set; } = new();

    /// <summary>
    /// Configuration segments (as emitted by <c>New-Configuration*</c> cmdlets).
    /// </summary>
    public IConfigurationSegment[] Segments { get; set; } = Array.Empty<IConfigurationSegment>();
}

