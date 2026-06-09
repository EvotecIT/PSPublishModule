namespace PowerForge;

/// <summary>
/// Options controlling whether and how a module is installed as part of <see cref="ModulePipelineSpec"/>.
/// </summary>
public sealed class ModulePipelineInstallOptions
{
    /// <summary>
    /// When true, installs the built module from staging. When false, skips install.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Installation strategy used when installing. When null, falls back to any value provided by configuration
    /// segments and then to <see cref="InstallationStrategy.AutoRevision"/>.
    /// </summary>
    public InstallationStrategy? Strategy { get; set; }

    /// <summary>
    /// Number of versions to keep per module root when installing. When null, falls back to any value provided
    /// by configuration segments and then to 3.
    /// </summary>
    public int? KeepVersions { get; set; }

    /// <summary>
    /// Destination module roots. When null/empty, roots may be derived from <c>CompatiblePSEditions</c> when present,
    /// otherwise defaults are used by the installer.
    /// </summary>
    public string[]? Roots { get; set; }

    /// <summary>
    /// Controls how legacy "flat" installs under &lt;root&gt;\&lt;ModuleName&gt; should be handled during install.
    /// When null, falls back to configuration segments and then to <see cref="LegacyFlatModuleHandling.Warn"/>.
    /// </summary>
    public LegacyFlatModuleHandling? LegacyFlatHandling { get; set; }

    /// <summary>
    /// Version folder names to preserve during pruning. Useful when migrating from older major versions.
    /// </summary>
    public string[]? PreserveVersions { get; set; }

    /// <summary>
    /// When true (default), patches the installed PSD1 ModuleVersion to the resolved install version.
    /// When false, keeps the staged ModuleVersion (useful for dev installs to avoid introducing 4-part versions).
    /// </summary>
    public bool UpdateManifestToResolvedVersion { get; set; } = true;
}
