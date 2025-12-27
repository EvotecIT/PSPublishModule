namespace PowerForge;

/// <summary>
/// Strategy used when installing a module into the user Modules directory.
/// </summary>
public enum InstallationStrategy
{
    /// <summary>
    /// Install strictly into the requested version. If it already exists, overwrite its contents to match the new build.
    /// </summary>
    Exact,
    /// <summary>
    /// If the requested version exists, install into an auto-incremented
    /// revision (fourth segment), e.g., 2.0.26.1, 2.0.26.2.
    /// </summary>
    AutoRevision
}

