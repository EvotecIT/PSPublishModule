namespace PowerForge;

/// <summary>
/// Result of a module install operation.
/// </summary>
public sealed class ModuleInstallerResult
{
    /// <summary>Resolved version installed (may include auto-revision).</summary>
    public string Version { get; }
    /// <summary>Full paths where the version was installed.</summary>
    public IReadOnlyList<string> InstalledPaths { get; }
    /// <summary>Paths of pruned old versions.</summary>
    public IReadOnlyList<string> PrunedPaths { get; }
    /// <summary>
    /// Creates a new result.
    /// </summary>
    public ModuleInstallerResult(string version, IReadOnlyList<string> installedPaths, IReadOnlyList<string> prunedPaths)
    { Version = version; InstalledPaths = installedPaths; PrunedPaths = prunedPaths; }
}

