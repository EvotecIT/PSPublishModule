namespace PowerForge;

/// <summary>
/// Source from which the current version was resolved.
/// </summary>
public enum ModuleVersionSource
{
    /// <summary>No current version was resolved.</summary>
    None,
    /// <summary>Resolved from PSGallery (or another PSResourceGet repository).</summary>
    Repository,
    /// <summary>Resolved from a local PSD1 manifest file.</summary>
    LocalPsd1
}

/// <summary>
/// Result of stepping a module version based on an expected pattern.
/// </summary>
public sealed class ModuleVersionStepResult
{
    /// <summary>The input expected version string.</summary>
    public string ExpectedVersion { get; }

    /// <summary>The resolved next version.</summary>
    public string Version { get; }

    /// <summary>The current version that was used as a baseline (when available).</summary>
    public string? CurrentVersion { get; }

    /// <summary>Where <see cref="CurrentVersion"/> was resolved from.</summary>
    public ModuleVersionSource CurrentVersionSource { get; }

    /// <summary>True when version auto-stepping was used (pattern with X).</summary>
    public bool UsedAutoVersioning { get; }

    /// <summary>
    /// Creates a new result instance.
    /// </summary>
    public ModuleVersionStepResult(
        string expectedVersion,
        string version,
        string? currentVersion,
        ModuleVersionSource currentVersionSource,
        bool usedAutoVersioning)
    {
        ExpectedVersion = expectedVersion;
        Version = version;
        CurrentVersion = currentVersion;
        CurrentVersionSource = currentVersionSource;
        UsedAutoVersioning = usedAutoVersioning;
    }
}

