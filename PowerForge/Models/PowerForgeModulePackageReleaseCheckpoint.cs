namespace PowerForge;

/// <summary>
/// Immutable project release plan captured for a package lane owned by a module build.
/// </summary>
internal sealed class PowerForgeModulePackageReleaseCheckpoint
{
    /// <summary>
    /// Stable lane name from the module configuration.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Full path to the configuration that defined the lane.
    /// </summary>
    public string ConfigPath { get; set; } = string.Empty;

    /// <summary>
    /// Project release plan approved by the build and signing checkpoint.
    /// </summary>
    public DotNetRepositoryReleaseResult Release { get; set; } = new();
}
