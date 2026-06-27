namespace PowerForge;

/// <summary>
/// Static NuGet package metadata used by the managed module engine.
/// </summary>
public sealed class ManagedModulePackageMetadata
{
    /// <summary>
    /// Package id from the nuspec.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Package version from the nuspec.
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Package authors.
    /// </summary>
    public string? Authors { get; set; }

    /// <summary>
    /// Package description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Project URL from the nuspec.
    /// </summary>
    public string? ProjectUrl { get; set; }

    /// <summary>
    /// License expression or license file reference.
    /// </summary>
    public string? License { get; set; }

    /// <summary>
    /// Package tags split into individual values.
    /// </summary>
    public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Dependencies declared by the package.
    /// </summary>
    public IReadOnlyList<ManagedModuleDependencyInfo> Dependencies { get; set; } = Array.Empty<ManagedModuleDependencyInfo>();

    /// <summary>
    /// Full path to the package that was read.
    /// </summary>
    public string? PackagePath { get; set; }

    /// <summary>
    /// True when the version contains a prerelease label.
    /// </summary>
    public bool IsPrerelease => Version.IndexOf('-') >= 0;
}
