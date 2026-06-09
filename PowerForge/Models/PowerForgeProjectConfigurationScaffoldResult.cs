namespace PowerForge;

/// <summary>
/// Describes a generated starter project release configuration file.
/// </summary>
public sealed class PowerForgeProjectConfigurationScaffoldResult
{
    /// <summary>
    /// Gets or sets the generated config path.
    /// </summary>
    public string ConfigPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the project path written into the starter config.
    /// </summary>
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the release/project name written into the starter config.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the target name written into the starter config.
    /// </summary>
    public string TargetName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the primary framework written into the starter config.
    /// </summary>
    public string Framework { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the runtime identifiers written into the starter config.
    /// </summary>
    public string[] Runtimes { get; set; } = System.Array.Empty<string>();

    /// <summary>
    /// Gets or sets whether the starter config requests a portable bundle.
    /// </summary>
    public bool IncludesPortableOutput { get; set; }
}
