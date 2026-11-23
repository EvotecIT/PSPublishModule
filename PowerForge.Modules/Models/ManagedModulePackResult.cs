namespace PowerForge;

/// <summary>
/// Result returned after creating a managed module package.
/// </summary>
public sealed class ManagedModulePackResult
{
    /// <summary>
    /// Package id.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Package version.
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Source module folder.
    /// </summary>
    public string ModulePath { get; set; } = string.Empty;

    /// <summary>
    /// Manifest path used to create package metadata.
    /// </summary>
    public string ManifestPath { get; set; } = string.Empty;

    /// <summary>
    /// Created package path.
    /// </summary>
    public string PackagePath { get; set; } = string.Empty;

    /// <summary>
    /// Number of module files added to the package.
    /// </summary>
    public int FileCount { get; set; }

    /// <summary>
    /// Size of the created package in bytes.
    /// </summary>
    public long PackageBytes { get; set; }
}
