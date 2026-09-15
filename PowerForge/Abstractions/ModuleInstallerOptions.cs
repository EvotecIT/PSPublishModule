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
    /// When true, installation fails instead of overwriting a destination version that appeared
    /// after AutoRevision resolution.
    /// </summary>
    public bool RequireNewDestination { get; }

    /// <summary>
    /// When true, every configured destination root must receive the new version. Any newly created
    /// destinations are rolled back when one root fails.
    /// </summary>
    public bool RequireAllDestinationRoots { get; }

    /// <summary>
    /// Creates options with destination roots, strategy, and retention.
    /// </summary>
    public ModuleInstallerOptions(
        IEnumerable<string>? destinationRoots = null,
        InstallationStrategy strategy = InstallationStrategy.Exact,
        int keepVersions = 3,
        LegacyFlatModuleHandling legacyFlatHandling = LegacyFlatModuleHandling.Warn,
        IEnumerable<string>? preserveVersions = null)
        : this(
            destinationRoots,
            strategy,
            keepVersions,
            legacyFlatHandling,
            preserveVersions,
            requireNewDestination: false,
            requireAllDestinationRoots: false)
    {
    }

    /// <summary>
    /// Creates options with explicit collision and all-root delivery requirements.
    /// </summary>
    public ModuleInstallerOptions(
        IEnumerable<string>? destinationRoots,
        InstallationStrategy strategy,
        int keepVersions,
        LegacyFlatModuleHandling legacyFlatHandling,
        IEnumerable<string>? preserveVersions,
        bool requireNewDestination,
        bool requireAllDestinationRoots)
    {
        if (requireAllDestinationRoots && !requireNewDestination)
            throw new ArgumentException(
                "All-root installation requires new destinations so a partial install can be rolled back safely.",
                nameof(requireAllDestinationRoots));

        DestinationRoots = (destinationRoots ?? Array.Empty<string>()).ToArray();
        Strategy = strategy;
        KeepVersions = keepVersions < 1 ? 1 : keepVersions;
        LegacyFlatHandling = legacyFlatHandling;
        PreserveVersions = (preserveVersions ?? Array.Empty<string>())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        RequireNewDestination = requireNewDestination;
        RequireAllDestinationRoots = requireAllDestinationRoots;
    }
}
