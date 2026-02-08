namespace PowerForge;

/// <summary>
/// Typed specification for installing a module from a staging directory into module roots.
/// </summary>
public sealed class ModuleInstallSpec
{
    /// <summary>Name of the module being installed.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Base module version used for resolution.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Path to the staging directory containing the module to install.</summary>
    public string StagingPath { get; set; } = string.Empty;

    /// <summary>Installation strategy controlling version resolution.</summary>
    public InstallationStrategy Strategy { get; set; } = InstallationStrategy.Exact;

    /// <summary>Number of installed versions to keep after install.</summary>
    public int KeepVersions { get; set; } = 3;

    /// <summary>Destination module roots. When empty, defaults are used.</summary>
    public string[] Roots { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Controls how legacy flat installs under &lt;root&gt;\&lt;ModuleName&gt; should be handled.
    /// </summary>
    public LegacyFlatModuleHandling LegacyFlatHandling { get; set; } = LegacyFlatModuleHandling.Warn;

    /// <summary>
    /// Version folder names to preserve during pruning.
    /// </summary>
    public string[] PreserveVersions { get; set; } = Array.Empty<string>();

    /// <summary>
    /// When true (default), patches the installed PSD1 ModuleVersion to the resolved install version.
    /// When false, keeps the staged ModuleVersion.
    /// </summary>
    public bool UpdateManifestToResolvedVersion { get; set; } = true;
}
