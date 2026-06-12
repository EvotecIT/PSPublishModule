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

    /// <summary>Use this PowerShell repository as the source for resolving Auto/Latest dependency versions.</summary>
    public bool UseAsDependencyVersionSource { get; set; }

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
    /// API version for PSResourceGet repository registration. Default: <see cref="RepositoryApiVersion.Auto"/>.
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

    /// <summary>
    /// Optional runtime credential provider used when credentials should be resolved immediately before publishing.
    /// </summary>
    public RepositoryCredentialProviderConfiguration? CredentialProvider { get; set; }
}

/// <summary>
/// Describes a runtime repository credential source.
/// </summary>
public sealed class RepositoryCredentialProviderConfiguration
{
    /// <summary>Credential provider kind.</summary>
    public RepositoryCredentialProviderKind Kind { get; set; } = RepositoryCredentialProviderKind.None;

    /// <summary>
    /// Optional fallback username when the provider does not return one.
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// JFrog Platform URL used by <c>jf exchange-oidc-token --url</c>, for example https://company.jfrog.io/.
    /// </summary>
    public string? JFrogPlatformUri { get; set; }

    /// <summary>
    /// JFrog OIDC provider name configured in Artifactory.
    /// </summary>
    public string? JFrogOidcProvider { get; set; }

    /// <summary>
    /// Optional CI-issued OIDC token value. Prefer <see cref="JFrogOidcTokenIdEnvironmentVariable"/> for CI use.
    /// </summary>
    public string? JFrogOidcTokenId { get; set; }

    /// <summary>
    /// Environment variable containing the CI-issued OIDC token value.
    /// </summary>
    public string? JFrogOidcTokenIdEnvironmentVariable { get; set; }

    /// <summary>
    /// JFrog OIDC provider implementation.
    /// </summary>
    public JFrogOidcProviderType JFrogOidcProviderType { get; set; } = JFrogOidcProviderType.GitHub;
}
