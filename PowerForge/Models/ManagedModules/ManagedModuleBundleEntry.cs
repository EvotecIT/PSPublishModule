namespace PowerForge;

/// <summary>
/// A saved module entry in an offline managed module bundle manifest.
/// </summary>
public sealed class ManagedModuleBundleEntry
{
    /// <summary>
    /// Module or package id.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Saved module version.
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Install or save status that produced this entry.
    /// </summary>
    public ManagedModuleInstallStatus Status { get; set; }

    /// <summary>
    /// Repository name used to resolve the package.
    /// </summary>
    public string RepositoryName { get; set; } = string.Empty;

    /// <summary>
    /// Repository source used to resolve the package.
    /// </summary>
    public string RepositorySource { get; set; } = string.Empty;

    /// <summary>
    /// Versioned module directory inside the bundle root.
    /// </summary>
    public string ModulePath { get; set; } = string.Empty;

    /// <summary>
    /// Package cache path used for delivery, when available.
    /// </summary>
    public string? PackagePath { get; set; }

    /// <summary>
    /// SHA256 hash of the delivered package, when available.
    /// </summary>
    public string? PackageSha256 { get; set; }

    /// <summary>
    /// Parent module that caused this dependency to be saved, when this entry is dependency evidence.
    /// </summary>
    public string? DependencyOf { get; set; }
}
