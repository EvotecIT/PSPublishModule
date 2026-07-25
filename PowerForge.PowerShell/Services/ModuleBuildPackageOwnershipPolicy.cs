namespace PowerForge;

/// <summary>
/// Normalizes module release settings when project packages are owned by the outer unified release.
/// </summary>
internal static class ModuleBuildPackageOwnershipPolicy
{
    /// <summary>
    /// Prevents release version synchronization from referencing package segments removed from the module plan.
    /// </summary>
    /// <param name="segments">The retained module pipeline segments.</param>
    internal static void RemoveUnavailableVersionSources(IConfigurationSegment[] segments)
    {
        foreach (var release in segments.OfType<ConfigurationReleaseSegment>())
        {
            if (release.Configuration.SynchronizeModuleVersion &&
                release.Configuration.VersionSource is
                    ReleaseVersionSource.ProjectBuild or
                    ReleaseVersionSource.PackageBuild)
            {
                release.Configuration.SynchronizeModuleVersion = false;
            }
        }
    }
}
