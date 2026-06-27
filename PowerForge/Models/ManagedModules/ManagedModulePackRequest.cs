namespace PowerForge;

/// <summary>
/// Request for creating a managed module NuGet package.
/// </summary>
public sealed class ManagedModulePackRequest
{
    /// <summary>
    /// Module folder to package.
    /// </summary>
    public string ModulePath { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit module manifest path.
    /// </summary>
    public string? ManifestPath { get; set; }

    /// <summary>
    /// Optional package id override. Defaults to the module folder or manifest name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Optional package version override. Defaults to ModuleVersion from the manifest.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// Directory where the package is written.
    /// </summary>
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Optional package authors override.
    /// </summary>
    public string? Authors { get; set; }

    /// <summary>
    /// Optional package description override.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Optional package project URL override.
    /// </summary>
    public string? ProjectUrl { get; set; }

    /// <summary>
    /// Optional package tags override.
    /// </summary>
    public IReadOnlyList<string>? Tags { get; set; }

    /// <summary>
    /// Overwrite an existing package file.
    /// </summary>
    public bool Force { get; set; }
}
