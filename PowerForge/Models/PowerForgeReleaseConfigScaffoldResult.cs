namespace PowerForge;

/// <summary>
/// Metadata describing a generated unified release configuration file.
/// </summary>
public sealed class PowerForgeReleaseConfigScaffoldResult
{
    /// <summary>
    /// Gets or sets the generated release config path.
    /// </summary>
    public string ConfigPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the generated config includes a package-release section.
    /// </summary>
    public bool IncludesPackages { get; set; }

    /// <summary>
    /// Gets or sets whether the generated config includes a tool/app-release section.
    /// </summary>
    public bool IncludesTools { get; set; }

    /// <summary>
    /// Gets or sets the resolved source path for the embedded project-build config, if any.
    /// </summary>
    public string? PackagesConfigPath { get; set; }

    /// <summary>
    /// Gets or sets the resolved source path for the module build script, if any.
    /// </summary>
    public string? ModuleScriptPath { get; set; }

    /// <summary>
    /// Gets or sets the resolved source path for the referenced DotNet publish config, if any.
    /// </summary>
    public string? DotNetPublishConfigPath { get; set; }

    /// <summary>
    /// Gets or sets the resolved GitHub owner copied into the tool section, if any.
    /// </summary>
    public string? ToolGitHubOwner { get; set; }

    /// <summary>
    /// Gets or sets the resolved GitHub repository copied into the tool section, if any.
    /// </summary>
    public string? ToolGitHubRepository { get; set; }
}
