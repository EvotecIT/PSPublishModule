using System;

namespace PowerForge;

/// <summary>
/// Describes a request to scaffold a starter dotnet publish configuration file.
/// </summary>
public sealed class DotNetPublishConfigScaffoldRequest
{
    /// <summary>
    /// Gets or sets the project root used to resolve relative paths.
    /// </summary>
    public string ProjectRoot { get; set; } = ".";

    /// <summary>
    /// Gets or sets an optional path to a specific project file.
    /// </summary>
    public string? ProjectPath { get; set; }

    /// <summary>
    /// Gets or sets an optional target name override.
    /// </summary>
    public string? TargetName { get; set; }

    /// <summary>
    /// Gets or sets an optional framework override.
    /// </summary>
    public string? Framework { get; set; }

    /// <summary>
    /// Gets or sets optional runtime identifiers override.
    /// </summary>
    public string[]? Runtimes { get; set; }

    /// <summary>
    /// Gets or sets optional publish styles override.
    /// </summary>
    public DotNetPublishStyle[]? Styles { get; set; }

    /// <summary>
    /// Gets or sets the build configuration.
    /// </summary>
    public string Configuration { get; set; } = "Release";

    /// <summary>
    /// Gets or sets the output config path.
    /// </summary>
    public string OutputPath { get; set; } = "powerforge.dotnetpublish.json";

    /// <summary>
    /// Gets or sets whether an existing config file can be overwritten.
    /// </summary>
    public bool Force { get; set; }

    /// <summary>
    /// Gets or sets whether the generated JSON should include the schema property.
    /// </summary>
    public bool IncludeSchema { get; set; } = true;

    /// <summary>
    /// Gets or sets the working directory used to resolve relative paths.
    /// </summary>
    public string WorkingDirectory { get; set; } = Environment.CurrentDirectory;
}
