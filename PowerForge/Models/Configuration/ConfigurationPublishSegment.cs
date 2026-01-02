namespace PowerForge;

/// <summary>
/// Configuration segment that describes publishing behavior (PowerShell repositories/GitHub).
/// </summary>
public sealed class ConfigurationPublishSegment : IConfigurationSegment
{
    /// <summary>Publish configuration payload.</summary>
    public PublishConfiguration Configuration { get; set; } = new();

    /// <inheritdoc />
    public string Type => Configuration.Destination == PublishDestination.PowerShellGallery
        ? "GalleryNuget"
        : "GitHubNuget";
}

/// <summary>
/// Publish configuration payload for <see cref="ConfigurationPublishSegment"/>.
/// </summary>
public sealed class PublishConfiguration
{
    /// <summary>Publish destination type.</summary>
    public PublishDestination Destination { get; set; } = PublishDestination.PowerShellGallery;

    /// <summary>
    /// Publishing tool/provider used for repository publishing. Ignored for GitHub publishing.
    /// </summary>
    public PublishTool Tool { get; set; } = PublishTool.Auto;

    /// <summary>API key used for publishing.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Optional artefact ID used for publishing.</summary>
    public string? ID { get; set; }

    /// <summary>Enable publishing to the chosen destination.</summary>
    public bool Enabled { get; set; }

    /// <summary>GitHub username (required for GitHub publishing).</summary>
    public string? UserName { get; set; }

    /// <summary>Repository name override (GitHub org/repo or PowerShell repository name).</summary>
    public string? RepositoryName { get; set; }

    /// <summary>
    /// Optional repository definition and registration settings (private feeds, custom URLs, credentials).
    /// When <see cref="PublishRepositoryConfiguration.Name"/> is empty, <see cref="RepositoryName"/> is used.
    /// </summary>
    public PublishRepositoryConfiguration? Repository { get; set; }

    /// <summary>Allow publishing lower versions (legacy behavior).</summary>
    public bool Force { get; set; }

    /// <summary>Override tag name used for GitHub publishing.</summary>
    public string? OverwriteTagName { get; set; }

    /// <summary>Publish GitHub release as a release even if prerelease is set.</summary>
    public bool DoNotMarkAsPreRelease { get; set; }

    /// <summary>When true, asks GitHub to generate release notes automatically.</summary>
    public bool GenerateReleaseNotes { get; set; }

    /// <summary>Verbose mode requested.</summary>
    public bool Verbose { get; set; }
}

/// <summary>
/// Repository definition used for publishing and querying versions.
/// </summary>
public sealed class PublishRepositoryConfiguration
{
    /// <summary>Optional repository name (overrides <see cref="PublishConfiguration.RepositoryName"/> when provided).</summary>
    public string? Name { get; set; }

    /// <summary>
    /// Repository base URI. When set, this is used as both source and publish URI unless overridden.
    /// </summary>
    public string? Uri { get; set; }

    /// <summary>
    /// Repository source URI (PowerShellGet Register-PSRepository -SourceLocation).
    /// When not set, <see cref="Uri"/> is used.
    /// </summary>
    public string? SourceUri { get; set; }

    /// <summary>
    /// Repository publish URI (PowerShellGet Register-PSRepository -PublishLocation).
    /// When not set, <see cref="Uri"/> is used.
    /// </summary>
    public string? PublishUri { get; set; }

    /// <summary>
    /// When true, marks the repository as trusted (avoids prompts). Default: true.
    /// </summary>
    public bool Trusted { get; set; } = true;

    /// <summary>
    /// Repository priority for PSResourceGet (lower is higher priority).
    /// </summary>
    public int? Priority { get; set; }

    /// <summary>
    /// API version for PSResourceGet repository registration (v2/v3). Default: <see cref="RepositoryApiVersion.Auto"/>.
    /// </summary>
    public RepositoryApiVersion ApiVersion { get; set; } = RepositoryApiVersion.Auto;

    /// <summary>
    /// When true, ensures the repository is registered/updated before publishing. Default: true when any URI is set.
    /// </summary>
    public bool EnsureRegistered { get; set; } = true;

    /// <summary>
    /// When true, unregister the repository after publishing if it was created by this run.
    /// </summary>
    public bool UnregisterAfterUse { get; set; }

    /// <summary>
    /// Optional credential used for repository operations (Find/Publish).
    /// </summary>
    public RepositoryCredential? Credential { get; set; }
}
