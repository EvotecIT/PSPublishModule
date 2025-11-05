namespace PowerForge;

/// <summary>
/// Options controlling module installation behavior.
/// </summary>
public sealed class ModuleInstallerOptions
{
    /// <summary>
    /// Destination module roots (e.g., user Documents PowerShell Modules paths).
    /// When empty, defaults will be used based on the OS.
    /// </summary>
    public IReadOnlyList<string> DestinationRoots { get; }

    /// <summary>
    /// Installation strategy to use when a version exists.
    /// </summary>
    public InstallationStrategy Strategy { get; }

    /// <summary>
    /// Number of versions to keep after installing; older versions are pruned.
    /// </summary>
    public int KeepVersions { get; }

    /// <summary>
    /// Creates options with destination roots, strategy, and retention.
    /// </summary>
    public ModuleInstallerOptions(IEnumerable<string>? destinationRoots = null, InstallationStrategy strategy = InstallationStrategy.Exact, int keepVersions = 3)
    {
        DestinationRoots = (destinationRoots ?? Array.Empty<string>()).ToArray();
        Strategy = strategy;
        KeepVersions = keepVersions < 1 ? 1 : keepVersions;
    }
}

