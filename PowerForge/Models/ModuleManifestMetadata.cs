namespace PowerForge;

/// <summary>
/// Minimal metadata read from a PowerShell module manifest.
/// </summary>
public sealed class ModuleManifestMetadata
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ModuleManifestMetadata"/> class.
    /// </summary>
    public ModuleManifestMetadata(string moduleName, string moduleVersion, string? preRelease)
    {
        ModuleName = moduleName;
        ModuleVersion = moduleVersion;
        PreRelease = preRelease;
    }

    /// <summary>
    /// Gets the resolved module name.
    /// </summary>
    public string ModuleName { get; }

    /// <summary>
    /// Gets the resolved module version.
    /// </summary>
    public string ModuleVersion { get; }

    /// <summary>
    /// Gets the prerelease label, when present.
    /// </summary>
    public string? PreRelease { get; }
}
