namespace PowerForge;

/// <summary>
/// Immutable project release plan captured for a package lane owned by a module build.
/// </summary>
internal sealed class PowerForgeModulePackageReleaseCheckpoint
{
    /// <summary>
    /// Stable segment key that uniquely identifies the package lane within the module recipe.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Stable lane name from the module configuration.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Full path to the configuration that defined the lane.
    /// </summary>
    public string ConfigPath { get; set; } = string.Empty;

    /// <summary>
    /// True when this checkpoint is approved for NuGet publication.
    /// </summary>
    public bool PublishNuget { get; set; }

    /// <summary>
    /// True when this checkpoint is approved for GitHub release publication.
    /// </summary>
    public bool PublishGitHub { get; set; }

    /// <summary>
    /// Project release plan approved by the build and signing checkpoint.
    /// </summary>
    public DotNetRepositoryReleaseResult Release { get; set; } = new();
}

/// <summary>Publication outcome for one module-owned NuGet package lane.</summary>
internal sealed class PowerForgeModulePackagePublicationResult
{
    public string Name { get; set; } = string.Empty;

    public bool Success { get; set; }

    public string? ErrorMessage { get; set; }

    public string? PublishSource { get; set; }

    public string[] PublishedPackages { get; set; } = Array.Empty<string>();

    public string[] SkippedDuplicatePackages { get; set; } = Array.Empty<string>();

    public string[] FailedPackages { get; set; } = Array.Empty<string>();
}
