namespace PowerForge;

/// <summary>
/// Describes one package version discovered in a managed module repository.
/// </summary>
public sealed class ManagedModuleVersionInfo
{
    /// <summary>
    /// Module or package id.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Package version text.
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Repository name that returned this version.
    /// </summary>
    public string RepositoryName { get; set; } = string.Empty;

    /// <summary>
    /// Repository source URL or local folder.
    /// </summary>
    public string RepositorySource { get; set; } = string.Empty;

    /// <summary>
    /// Direct package download URI or local package path when known.
    /// </summary>
    public string? PackageSource { get; set; }

    /// <summary>
    /// True when the version contains a prerelease label.
    /// </summary>
    public bool IsPrerelease { get; set; }

    /// <summary>
    /// True when repository metadata indicates the version is listed.
    /// </summary>
    public bool Listed { get; set; } = true;

    /// <summary>
    /// License expression, license URL, or license file reference when repository metadata exposes it.
    /// </summary>
    public string? License { get; set; }

    /// <summary>
    /// True when repository metadata indicates this package version requires explicit license acceptance.
    /// </summary>
    public bool RequireLicenseAcceptance { get; set; }

    /// <summary>
    /// Dependencies exposed by repository metadata when available.
    /// </summary>
    public IReadOnlyList<ManagedModuleDependencyInfo> Dependencies { get; set; } = Array.Empty<ManagedModuleDependencyInfo>();

    /// <summary>
    /// Package tags exposed by repository or package metadata when available.
    /// </summary>
    public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Resource kind represented by this result.
    /// </summary>
    public string ResourceType { get; set; } = "Module";
}
