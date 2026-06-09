using System;

namespace PowerForge;

/// <summary>
/// Describes a request to scaffold a unified PowerForge release configuration file.
/// </summary>
public sealed class PowerForgeReleaseConfigScaffoldRequest
{
    /// <summary>
    /// Gets or sets the project root used to resolve relative paths.
    /// </summary>
    public string ProjectRoot { get; set; } = ".";

    /// <summary>
    /// Gets or sets an optional path to an existing project-build configuration file.
    /// </summary>
    public string? PackagesConfigPath { get; set; }

    /// <summary>
    /// Gets or sets an optional path to an existing DotNet publish configuration file.
    /// </summary>
    public string? DotNetPublishConfigPath { get; set; }

    /// <summary>
    /// Gets or sets the output config path.
    /// </summary>
    public string OutputPath { get; set; } = System.IO.Path.Combine("Build", "release.json");

    /// <summary>
    /// Gets or sets the release configuration override.
    /// </summary>
    public string Configuration { get; set; } = "Release";

    /// <summary>
    /// Gets or sets whether an existing config file can be overwritten.
    /// </summary>
    public bool Force { get; set; }

    /// <summary>
    /// Gets or sets whether the generated JSON should include the schema property.
    /// </summary>
    public bool IncludeSchema { get; set; } = true;

    /// <summary>
    /// Gets or sets whether package config discovery should be skipped.
    /// </summary>
    public bool SkipPackages { get; set; }

    /// <summary>
    /// Gets or sets whether tool/app config discovery should be skipped.
    /// </summary>
    public bool SkipTools { get; set; }

    /// <summary>
    /// Gets or sets the working directory used to resolve relative paths.
    /// </summary>
    public string WorkingDirectory { get; set; } = Environment.CurrentDirectory;
}
