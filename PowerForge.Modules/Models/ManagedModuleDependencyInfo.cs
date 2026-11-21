namespace PowerForge;

/// <summary>
/// NuGet dependency metadata discovered from a module package.
/// </summary>
public sealed class ManagedModuleDependencyInfo
{
    /// <summary>
    /// Dependency package id.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// NuGet version range text when present.
    /// </summary>
    public string? VersionRange { get; set; }

    /// <summary>
    /// Target framework group that declared the dependency.
    /// </summary>
    public string? TargetFramework { get; set; }
}
