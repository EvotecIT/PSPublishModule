namespace PowerForge;

/// <summary>
/// Captures the normalized <c>New-ConfigurationPublish</c> parameter values before they are mapped
/// into a reusable publish configuration segment.
/// </summary>
internal sealed class PublishConfigurationRequest
{
    /// <summary>PowerShell parameter set that produced the request.</summary>
    public string ParameterSetName { get; set; } = string.Empty;

    /// <summary>Publish destination selected by the caller.</summary>
    public PublishDestination Type { get; set; }

    /// <summary>Azure DevOps organization used by the Azure Artifacts shortcut.</summary>
    public string AzureDevOpsOrganization { get; set; } = string.Empty;

    /// <summary>Optional Azure DevOps project used by project-scoped Azure Artifacts feeds.</summary>
    public string? AzureDevOpsProject { get; set; }

    /// <summary>Azure Artifacts feed name used by the Azure Artifacts shortcut.</summary>
    public string AzureArtifactsFeed { get; set; } = string.Empty;

    /// <summary>Path to a file containing the publish API key.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Inline publish API key. For JFrog this is only needed when the feed requires a separate NuGet API key.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>GitHub owner or user name used by GitHub release publishing.</summary>
    public string? UserName { get; set; }

    /// <summary>PowerShell repository name or GitHub repository name, depending on the destination.</summary>
    public string? RepositoryName { get; set; }

    /// <summary>PowerShell repository publishing tool requested by the caller.</summary>
    public PublishTool Tool { get; set; } = PublishTool.Auto;

    /// <summary>Repository URI used for both source and publish operations unless source/publish URIs are provided separately.</summary>
    public string? RepositoryUri { get; set; }

    /// <summary>PowerShellGet source URI for repositories that expose separate source and publish endpoints.</summary>
    public string? RepositorySourceUri { get; set; }

    /// <summary>PowerShellGet publish URI for repositories that expose separate source and publish endpoints.</summary>
    public string? RepositoryPublishUri { get; set; }

    /// <summary>JFrog Artifactory base URI used with <see cref="JFrogRepository"/> to derive repository endpoints.</summary>
    public string? JFrogBaseUri { get; set; }

    /// <summary>JFrog NuGet repository key used with <see cref="JFrogBaseUri"/> to derive repository endpoints.</summary>
    public string? JFrogRepository { get; set; }

    /// <summary>Whether the generated repository configuration should be trusted when registered.</summary>
    public bool RepositoryTrusted { get; set; } = true;

    /// <summary>PSResourceGet repository priority. Lower values have higher priority.</summary>
    public int? RepositoryPriority { get; set; }

    /// <summary>PSResourceGet repository API version used during registration.</summary>
    public RepositoryApiVersion RepositoryApiVersion { get; set; } = RepositoryApiVersion.Auto;

    /// <summary>Whether the repository should be registered or updated before publishing.</summary>
    public bool EnsureRepositoryRegistered { get; set; } = true;

    /// <summary>Whether a repository created by the publish run should be unregistered afterward.</summary>
    public bool UnregisterRepositoryAfterPublish { get; set; }

    /// <summary>User name for repository basic authentication.</summary>
    public string? RepositoryCredentialUserName { get; set; }

    /// <summary>Inline repository credential secret.</summary>
    public string? RepositoryCredentialSecret { get; set; }

    /// <summary>Whether the repository credential secret parameter was explicitly supplied.</summary>
    public bool RepositoryCredentialSecretSpecified { get; set; }

    /// <summary>Path to a file containing the repository credential secret.</summary>
    public string? RepositoryCredentialSecretFilePath { get; set; }

    /// <summary>Whether the repository credential secret file parameter was explicitly supplied.</summary>
    public bool RepositoryCredentialSecretFilePathSpecified { get; set; }

    /// <summary>Environment variable containing the repository credential secret.</summary>
    public string? RepositoryCredentialSecretEnvironmentVariable { get; set; }

    /// <summary>Whether the repository credential secret environment variable parameter was explicitly supplied.</summary>
    public bool RepositoryCredentialSecretEnvironmentVariableSpecified { get; set; }

    /// <summary>JFrog Platform URL used for runtime OIDC token exchange.</summary>
    public string? JFrogPlatformUri { get; set; }

    /// <summary>JFrog OIDC provider name configured in Artifactory.</summary>
    public string? JFrogOidcProvider { get; set; }

    /// <summary>CI-issued OIDC token value passed to JFrog CLI token exchange.</summary>
    public string? JFrogOidcTokenId { get; set; }

    /// <summary>Environment variable containing the CI-issued OIDC token value for JFrog CLI token exchange.</summary>
    public string? JFrogOidcTokenIdEnvironmentVariable { get; set; }

    /// <summary>JFrog OIDC provider implementation passed to JFrog CLI.</summary>
    public JFrogOidcProviderType JFrogOidcProviderType { get; set; } = JFrogOidcProviderType.GitHub;

    /// <summary>Whether the generated publish configuration segment is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Optional GitHub release tag override.</summary>
    public string? OverwriteTagName { get; set; }

    /// <summary>Whether repository publishing should skip the remote version guard.</summary>
    public bool Force { get; set; }

    /// <summary>Optional publish segment identifier used by artefact selection.</summary>
    public string? ID { get; set; }

    /// <summary>Whether GitHub release publishing should avoid marking prerelease module versions as prereleases.</summary>
    public bool DoNotMarkAsPreRelease { get; set; }

    /// <summary>Whether GitHub should generate release notes automatically.</summary>
    public bool GenerateReleaseNotes { get; set; }

    /// <summary>Whether this repository should be used as a dependency-version source.</summary>
    public bool UseAsDependencyVersionSource { get; set; }

    /// <summary>Whether missing manifest RequiredModules should be published to the target repository first.</summary>
    public bool PublishRequiredModules { get; set; }

    /// <summary>Repository used as the source for publishing missing RequiredModules.</summary>
    public string? RequiredModuleSourceRepository { get; set; }

    /// <summary>Whether verbose logging was requested by the cmdlet invocation.</summary>
    public bool Verbose { get; set; }
}
