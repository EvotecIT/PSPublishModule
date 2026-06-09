using System;
using System.Collections.Generic;
using System.Linq;

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
    /// Controls how legacy flat installs under &lt;root&gt;\&lt;ModuleName&gt; should be handled.
    /// </summary>
    public LegacyFlatModuleHandling LegacyFlatHandling { get; }

    /// <summary>
    /// Version folder names to preserve during pruning (case-insensitive).
    /// </summary>
    public IReadOnlyCollection<string> PreserveVersions { get; }

    /// <summary>
    /// Creates options with destination roots, strategy, and retention.
    /// </summary>
    public ModuleInstallerOptions(
        IEnumerable<string>? destinationRoots = null,
        InstallationStrategy strategy = InstallationStrategy.Exact,
        int keepVersions = 3,
        LegacyFlatModuleHandling legacyFlatHandling = LegacyFlatModuleHandling.Warn,
        IEnumerable<string>? preserveVersions = null)
    {
        DestinationRoots = (destinationRoots ?? Array.Empty<string>()).ToArray();
        Strategy = strategy;
        KeepVersions = keepVersions < 1 ? 1 : keepVersions;
        LegacyFlatHandling = legacyFlatHandling;
        PreserveVersions = (preserveVersions ?? Array.Empty<string>())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
