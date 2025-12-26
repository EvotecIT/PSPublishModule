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
}

