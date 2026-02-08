namespace PowerForge;

/// <summary>
/// Controls how the installer should handle a legacy "flat" module layout where module files live directly under
/// &lt;root&gt;\&lt;ModuleName&gt; (no version subfolder).
/// </summary>
public enum LegacyFlatModuleHandling
{
    /// <summary>
    /// Emit a warning and continue. No conversion or deletion is performed.
    /// </summary>
    Warn,

    /// <summary>
    /// Convert the flat layout to a versioned layout by moving/copying flat items into a version subfolder inferred
    /// from the legacy manifest's ModuleVersion.
    /// </summary>
    Convert,

    /// <summary>
    /// Delete the flat layout items (files and non-version directories) under &lt;root&gt;\&lt;ModuleName&gt;.
    /// </summary>
    Delete,

    /// <summary>
    /// Do nothing and do not warn.
    /// </summary>
    Ignore
}

