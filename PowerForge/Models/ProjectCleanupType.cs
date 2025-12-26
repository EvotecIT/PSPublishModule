namespace PowerForge;

/// <summary>
/// Identifies a built-in cleanup profile for project file removal.
/// </summary>
public enum ProjectCleanupType
{
    /// <summary>Build artefacts (bin/obj/etc.).</summary>
    Build,
    /// <summary>Log and trace files/folders.</summary>
    Logs,
    /// <summary>HTML files.</summary>
    Html,
    /// <summary>Temporary files/folders.</summary>
    Temp,
    /// <summary>All supported cleanup types combined.</summary>
    All
}

