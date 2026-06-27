namespace PowerForge;

/// <summary>
/// Identifies the managed module repository protocol.
/// </summary>
public enum ManagedModuleRepositoryKind
{
    /// <summary>
    /// Infer the repository kind from the source value.
    /// </summary>
    Auto,

    /// <summary>
    /// NuGet v3 service index or flat-container source.
    /// </summary>
    NuGetV3,

    /// <summary>
    /// Local folder containing NuGet packages.
    /// </summary>
    LocalFolder
}
