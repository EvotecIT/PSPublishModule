using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Provides a way to configure publishing to PowerShell Gallery, GitHub, JFrog Artifactory, or other private PowerShell module repositories.
/// </summary>
/// <remarks>
/// <para>
/// This cmdlet emits publish configuration consumed by <c>Invoke-ModuleBuild</c> / <c>Build-Module</c>.
/// Use <c>-Type</c> to choose a destination. For repository publishing, <c>-Tool</c> selects the provider
/// (PowerShellGet/PSResourceGet/Auto).
/// </para>
/// <para>
/// For private repositories (for example Azure DevOps Artifacts, JFrog Artifactory, GitHub Packages, or private NuGet v3 feeds), provide repository URIs
/// and (optionally) credentials, or use provider-specific preset parameters to resolve those URIs automatically.
/// To avoid secrets in source control, pass API keys/tokens via <c>-FilePath</c> or environment-specific tooling.
/// </para>
/// <para>
/// JFrog Artifactory can be configured directly with <c>-JFrogBaseUri</c> and <c>-JFrogRepository</c>.
/// For PAT/basic-auth feeds, use repository credentials only. Add <c>-FilePath</c> or <c>-ApiKey</c> only when the feed requires a separate NuGet API key for package push.
/// </para>
/// </remarks>
/// <example>
/// <summary>Publish to PowerShell Gallery (API key from file)</summary>
/// <code>New-ConfigurationPublish -Type PowerShellGallery -FilePath "$env:USERPROFILE\.secrets\psgallery.key" -Enabled</code>
/// </example>
/// <example>
/// <summary>Publish to GitHub Releases (token from file)</summary>
/// <code>New-ConfigurationPublish -Type GitHub -FilePath "$env:USERPROFILE\.secrets\github.token" -UserName 'EvotecIT' -RepositoryName 'MyModule' -Enabled</code>
/// </example>
/// <example>
/// <summary>Publish to Azure Artifacts (private feed preset)</summary>
/// <code>New-ConfigurationPublish -ProfileName 'Company' -Enabled</code>
/// </example>
/// <example>
/// <summary>Publish to JFrog Artifactory with PAT/basic authentication</summary>
/// <code>New-ConfigurationPublish -JFrogBaseUri 'https://company.jfrog.io/artifactory' -JFrogRepository 'powershell-virtual' -RepositoryName 'JFrogPS' -Tool PSResourceGet -RepositoryCredentialUserName 'name@company.com' -RepositoryCredentialSecretFilePath "$env:USERPROFILE\.secrets\jfrog-pat.txt" -Enabled</code>
/// </example>
/// <example>
/// <summary>Publish to JFrog Artifactory with a clear-text PAT for local testing</summary>
/// <code>New-ConfigurationPublish -JFrogBaseUri 'https://company.jfrog.io/artifactory' -JFrogRepository 'powershell-virtual' -RepositoryName 'JFrogPS' -Tool PSResourceGet -RepositoryCredentialUserName 'name@company.com' -RepositoryCredentialSecret 'temporary-pat' -Enabled</code>
/// </example>
/// <example>
/// <summary>Publish to JFrog Artifactory with a separate NuGet API key</summary>
/// <code>New-ConfigurationPublish -JFrogBaseUri 'https://company.jfrog.io/artifactory' -JFrogRepository 'powershell-virtual' -RepositoryName 'JFrogPS' -Tool PSResourceGet -FilePath "$env:USERPROFILE\.secrets\jfrog-nuget-api-key.txt" -RepositoryCredentialUserName 'name@company.com' -RepositoryCredentialSecretFilePath "$env:USERPROFILE\.secrets\jfrog-pat.txt" -Enabled</code>
/// </example>
/// <example>
/// <summary>Publish to JFrog Artifactory with a PAT/access token stored in an environment variable</summary>
/// <code>New-ConfigurationPublish -JFrogBaseUri 'https://company.jfrog.io/artifactory' -JFrogRepository 'powershell-virtual' -RepositoryName 'JFrogPS' -Tool PSResourceGet -RepositoryCredentialUserName 'name@company.com' -RepositoryCredentialSecretEnvironmentVariable 'JFROG_ACCESS_TOKEN' -Enabled</code>
/// </example>
/// <example>
/// <summary>Publish to JFrog Artifactory with CI OIDC token exchange</summary>
/// <code>New-ConfigurationPublish -JFrogBaseUri 'https://company.jfrog.io/artifactory' -JFrogRepository 'powershell-virtual' -RepositoryName 'JFrogPS' -Tool PSResourceGet -JFrogOidcProvider 'azure-oidc' -JFrogOidcProviderType Azure -JFrogOidcTokenIdEnvironmentVariable 'JFROG_CLI_OIDC_EXCHANGE_TOKEN_ID' -Enabled</code>
/// </example>
/// <example>
/// <summary>Publish missing RequiredModules into a private repository before publishing the module</summary>
/// <code>New-ConfigurationPublish -JFrogBaseUri 'https://company.jfrog.io/artifactory' -JFrogRepository 'powershell-virtual' -RepositoryName 'JFrogPS' -Tool PSResourceGet -RepositoryCredentialUserName 'name@company.com' -RepositoryCredentialSecretEnvironmentVariable 'JFROG_ACCESS_TOKEN' -PublishRequiredModules -RequiredModuleSourceRepository PSGallery -Enabled</code>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationPublish", DefaultParameterSetName = "ApiFromFile")]
public sealed class NewConfigurationPublishCommand : PSCmdlet
{
    /// <summary>Choose between PowerShellGallery and GitHub.</summary>
    [Parameter(Mandatory = true, ParameterSetName = "ApiKey")]
    [Parameter(Mandatory = true, ParameterSetName = "ApiFromFile")]
    [Parameter(ParameterSetName = "JFrog")]
    public PowerForge.PublishDestination Type { get; set; }

    /// <summary>Azure DevOps organization name for the Azure Artifacts preset.</summary>
    [Parameter(Mandatory = true, ParameterSetName = "AzureArtifacts")]
    public string AzureDevOpsOrganization { get; set; } = string.Empty;

    /// <summary>Optional Azure DevOps project name for project-scoped feeds.</summary>
    [Parameter(ParameterSetName = "AzureArtifacts")]
    public string? AzureDevOpsProject { get; set; }

    /// <summary>Azure Artifacts feed name for the private gallery preset.</summary>
    [Parameter(Mandatory = true, ParameterSetName = "AzureArtifacts")]
    public string AzureArtifactsFeed { get; set; } = string.Empty;

    /// <summary>Saved private gallery profile name for Azure Artifacts publishing.</summary>
    [Parameter(Mandatory = true, ParameterSetName = "Profile")]
    [Alias("Profile")]
    [ValidateNotNullOrEmpty]
    public string ProfileName { get; set; } = string.Empty;

    /// <summary>API key to be used for publishing in clear text in a file. For JFrog, use this only when the feed requires a separate NuGet API key.</summary>
    [Parameter(Mandatory = true, ParameterSetName = "ApiFromFile")]
    [Parameter(ParameterSetName = "Profile")]
    [Parameter(ParameterSetName = "JFrog")]
    public string FilePath { get; set; } = string.Empty;

    /// <summary>API key to be used for publishing in clear text. For JFrog, use this only when the feed requires a separate NuGet API key.</summary>
    [Parameter(Mandatory = true, ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "Profile")]
    [Parameter(ParameterSetName = "JFrog")]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>GitHub username (required for GitHub publishing).</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    public string? UserName { get; set; }

    /// <summary>Repository name override (GitHub or PowerShell repository name).</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    [Parameter(ParameterSetName = "AzureArtifacts")]
    [Parameter(ParameterSetName = "JFrog")]
    public string? RepositoryName { get; set; }

    /// <summary>Publishing tool/provider used for repository publishing. Ignored for GitHub publishing.</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    [Parameter(ParameterSetName = "AzureArtifacts")]
    [Parameter(ParameterSetName = "JFrog")]
    public PowerForge.PublishTool Tool { get; set; } = PowerForge.PublishTool.Auto;

    /// <summary>Repository base URI (used for both source and publish unless overridden).</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    [Parameter(ParameterSetName = "JFrog")]
    public string? RepositoryUri { get; set; }

    /// <summary>Repository source URI (PowerShellGet SourceLocation).</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    [Parameter(ParameterSetName = "JFrog")]
    public string? RepositorySourceUri { get; set; }

    /// <summary>Repository publish URI (PowerShellGet PublishLocation).</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    [Parameter(ParameterSetName = "JFrog")]
    public string? RepositoryPublishUri { get; set; }

    /// <summary>JFrog Artifactory base URI, for example https://company.jfrog.io/artifactory. PowerShellGet and PSResourceGet URLs are derived automatically.</summary>
    [Parameter(Mandatory = true, ParameterSetName = "JFrog")]
    public string? JFrogBaseUri { get; set; }

    /// <summary>JFrog NuGet repository key used to derive PowerShellGet and PSResourceGet endpoints, for example powershell-virtual.</summary>
    [Parameter(Mandatory = true, ParameterSetName = "JFrog")]
    public string? JFrogRepository { get; set; }

    /// <summary>Whether to mark the repository as trusted (avoids prompts). Default: true.</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    [Parameter(ParameterSetName = "AzureArtifacts")]
    [Parameter(ParameterSetName = "JFrog")]
    public bool RepositoryTrusted { get; set; } = true;

    /// <summary>Repository priority for PSResourceGet (lower is higher priority).</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    [Parameter(ParameterSetName = "AzureArtifacts")]
    [Parameter(ParameterSetName = "JFrog")]
    public int? RepositoryPriority { get; set; }

    /// <summary>Repository API version for PSResourceGet registration (v2/v3).</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    [Parameter(ParameterSetName = "AzureArtifacts")]
    [Parameter(ParameterSetName = "JFrog")]
    public PowerForge.RepositoryApiVersion RepositoryApiVersion { get; set; } = PowerForge.RepositoryApiVersion.Auto;

    /// <summary>When true, registers/updates the repository before publishing. Default: true.</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    [Parameter(ParameterSetName = "AzureArtifacts")]
    [Parameter(ParameterSetName = "JFrog")]
    public bool EnsureRepositoryRegistered { get; set; } = true;

    /// <summary>When set, unregisters the repository after publish if it was created by this run.</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    [Parameter(ParameterSetName = "AzureArtifacts")]
    [Parameter(ParameterSetName = "JFrog")]
    public SwitchParameter UnregisterRepositoryAfterPublish { get; set; }

    /// <summary>Repository credential username (basic auth). For JFrog PAT/basic-auth flows, this is the JFrog user name or email.</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    [Parameter(ParameterSetName = "AzureArtifacts")]
    [Parameter(ParameterSetName = "Profile")]
    [Parameter(ParameterSetName = "JFrog")]
    public string? RepositoryCredentialUserName { get; set; }

    /// <summary>Repository credential secret (password/token) in clear text. For JFrog PAT/basic-auth flows, this is the PAT or access token.</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    [Parameter(ParameterSetName = "AzureArtifacts")]
    [Parameter(ParameterSetName = "Profile")]
    [Parameter(ParameterSetName = "JFrog")]
    public string? RepositoryCredentialSecret { get; set; }

    /// <summary>Repository credential secret (password/token) in a clear-text file. For JFrog PAT/basic-auth flows, prefer this over inline token values.</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    [Parameter(ParameterSetName = "AzureArtifacts")]
    [Parameter(ParameterSetName = "Profile")]
    [Parameter(ParameterSetName = "JFrog")]
    public string? RepositoryCredentialSecretFilePath { get; set; }

    /// <summary>Environment variable containing the repository credential secret (password/token). For JFrog PAT/access-token flows, this can be JFROG_ACCESS_TOKEN or a CI secret variable.</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    [Parameter(ParameterSetName = "AzureArtifacts")]
    [Parameter(ParameterSetName = "Profile")]
    [Parameter(ParameterSetName = "JFrog")]
    public string? RepositoryCredentialSecretEnvironmentVariable { get; set; }

    /// <summary>JFrog Platform URL used for JFrog CLI OIDC token exchange. Defaults from JFrogBaseUri when omitted.</summary>
    [Parameter(ParameterSetName = "JFrog")]
    public string? JFrogPlatformUri { get; set; }

    /// <summary>JFrog OIDC provider name configured in Artifactory. Enables runtime token exchange through JFrog CLI.</summary>
    [Parameter(ParameterSetName = "JFrog")]
    public string? JFrogOidcProvider { get; set; }

    /// <summary>CI-issued OIDC token value used by JFrog CLI token exchange. Prefer JFrogOidcTokenIdEnvironmentVariable in CI.</summary>
    [Parameter(ParameterSetName = "JFrog")]
    public string? JFrogOidcTokenId { get; set; }

    /// <summary>Environment variable containing the CI-issued OIDC token value used by JFrog CLI token exchange.</summary>
    [Parameter(ParameterSetName = "JFrog")]
    public string? JFrogOidcTokenIdEnvironmentVariable { get; set; }

    /// <summary>JFrog OIDC provider implementation passed to JFrog CLI. Use Azure for Azure DevOps or Entra-backed OIDC mappings.</summary>
    [Parameter(ParameterSetName = "JFrog")]
    public PowerForge.JFrogOidcProviderType JFrogOidcProviderType { get; set; } = PowerForge.JFrogOidcProviderType.GitHub;

    /// <summary>Enable publishing to the chosen destination.</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    [Parameter(ParameterSetName = "AzureArtifacts")]
    [Parameter(ParameterSetName = "Profile")]
    [Parameter(ParameterSetName = "JFrog")]
    public SwitchParameter Enabled { get; set; }

    /// <summary>Override tag name used for GitHub publishing.</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    public string? OverwriteTagName { get; set; }

    /// <summary>Allow publishing lower version of a module on a PowerShell repository.</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    [Parameter(ParameterSetName = "AzureArtifacts")]
    [Parameter(ParameterSetName = "Profile")]
    [Parameter(ParameterSetName = "JFrog")]
    public SwitchParameter Force { get; set; }

    /// <summary>Optional ID of the artefact used for publishing.</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    [Parameter(ParameterSetName = "AzureArtifacts")]
    [Parameter(ParameterSetName = "Profile")]
    [Parameter(ParameterSetName = "JFrog")]
    public string? ID { get; set; }

    /// <summary>Publish GitHub release as a release even if module prerelease is set.</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    public SwitchParameter DoNotMarkAsPreRelease { get; set; }

    /// <summary>When set, asks GitHub to generate release notes automatically.</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    public SwitchParameter GenerateReleaseNotes { get; set; }

    /// <summary>Use this PowerShell repository as the source for resolving Auto/Latest dependency versions.</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    [Parameter(ParameterSetName = "AzureArtifacts")]
    [Parameter(ParameterSetName = "JFrog")]
    public SwitchParameter UseAsDependencyVersionSource { get; set; }

    /// <summary>When set, publishes missing manifest RequiredModules to the target repository before publishing the main module.</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    [Parameter(ParameterSetName = "AzureArtifacts")]
    [Parameter(ParameterSetName = "Profile")]
    [Parameter(ParameterSetName = "JFrog")]
    public SwitchParameter PublishRequiredModules { get; set; }

    /// <summary>Repository used as the source for publishing missing RequiredModules. Defaults to PSGallery.</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    [Parameter(ParameterSetName = "AzureArtifacts")]
    [Parameter(ParameterSetName = "Profile")]
    [Parameter(ParameterSetName = "JFrog")]
    public string? RequiredModuleSourceRepository { get; set; }

    /// <summary>Emits publish configuration for the build pipeline.</summary>
    protected override void ProcessRecord()
    {
        var parameterSetName = ParameterSetName;
        var type = Type;
        var azureDevOpsOrganization = AzureDevOpsOrganization;
        var azureDevOpsProject = AzureDevOpsProject;
        var azureArtifactsFeed = AzureArtifactsFeed;
        var repositoryName = RepositoryName;
        var tool = Tool;
        var repositoryTrusted = RepositoryTrusted;
        var repositoryPriority = RepositoryPriority;
        var repositoryApiVersion = RepositoryApiVersion;
        var repositoryUri = RepositoryUri;
        var repositorySourceUri = RepositorySourceUri;
        var repositoryPublishUri = RepositoryPublishUri;

        if (ParameterSetName == "JFrog")
        {
            type = PowerForge.PublishDestination.PowerShellGallery;
        }

        if (ParameterSetName == "Profile")
        {
            var profile = ModuleRepositoryProfileCommandSupport.ResolveRequired(ProfileName);
            type = PowerForge.PublishDestination.PowerShellGallery;
            repositoryName = profile.RepositoryName;
            tool = profile.Tool switch
            {
                PowerForge.RepositoryRegistrationTool.PSResourceGet => PowerForge.PublishTool.PSResourceGet,
                PowerForge.RepositoryRegistrationTool.PowerShellGet => PowerForge.PublishTool.PowerShellGet,
                _ => PowerForge.PublishTool.Auto
            };
            repositoryTrusted = profile.Trusted;
            repositoryPriority = profile.Priority;
            repositoryApiVersion = PowerForge.RepositoryApiVersion.V3;

            if (profile.Provider == PowerForge.PrivateGalleryProvider.AzureArtifacts)
            {
                parameterSetName = "AzureArtifacts";
                azureDevOpsOrganization = profile.AzureDevOpsOrganization;
                azureDevOpsProject = profile.AzureDevOpsProject;
                azureArtifactsFeed = profile.AzureArtifactsFeed;
            }
            else
            {
                var apiKeySpecified = MyInvocation.BoundParameters.ContainsKey(nameof(ApiKey)) && !string.IsNullOrWhiteSpace(ApiKey);
                var filePathSpecified = MyInvocation.BoundParameters.ContainsKey(nameof(FilePath)) && !string.IsNullOrWhiteSpace(FilePath);
                var repositoryCredentialSpecified =
                    !string.IsNullOrWhiteSpace(RepositoryCredentialUserName) &&
                    (!string.IsNullOrWhiteSpace(RepositoryCredentialSecret) ||
                     !string.IsNullOrWhiteSpace(RepositoryCredentialSecretFilePath) ||
                     !string.IsNullOrWhiteSpace(RepositoryCredentialSecretEnvironmentVariable));

                if (apiKeySpecified && filePathSpecified)
                    throw new ArgumentException("Specify either ApiKey or FilePath for profile-based private gallery publishing, not both.", nameof(FilePath));

                if (Enabled.IsPresent && !apiKeySpecified && !filePathSpecified && !repositoryCredentialSpecified)
                    throw new ArgumentException("ApiKey, FilePath, or repository credentials are required when enabling publish configuration from a non-Azure private gallery profile.", nameof(ProfileName));

                parameterSetName = filePathSpecified ? "ApiFromFile" : "ApiKey";
                repositoryUri = profile.RepositoryUri;
                repositorySourceUri = profile.RepositorySourceUri;
                repositoryPublishUri = profile.RepositoryPublishUri;
            }
        }

        var settings = new PublishConfigurationFactory().Create(new PublishConfigurationRequest
        {
            ParameterSetName = parameterSetName,
            Type = type,
            AzureDevOpsOrganization = azureDevOpsOrganization,
            AzureDevOpsProject = azureDevOpsProject,
            AzureArtifactsFeed = azureArtifactsFeed,
            FilePath = FilePath,
            ApiKey = ApiKey,
            UserName = UserName,
            RepositoryName = repositoryName,
            Tool = tool,
            RepositoryUri = repositoryUri,
            RepositorySourceUri = repositorySourceUri,
            RepositoryPublishUri = repositoryPublishUri,
            JFrogBaseUri = JFrogBaseUri,
            JFrogRepository = JFrogRepository,
            RepositoryTrusted = repositoryTrusted,
            RepositoryPriority = repositoryPriority,
            RepositoryApiVersion = repositoryApiVersion,
            EnsureRepositoryRegistered = EnsureRepositoryRegistered,
            UnregisterRepositoryAfterPublish = UnregisterRepositoryAfterPublish.IsPresent,
            RepositoryCredentialUserName = RepositoryCredentialUserName,
            RepositoryCredentialSecret = RepositoryCredentialSecret,
            RepositoryCredentialSecretSpecified = MyInvocation.BoundParameters.ContainsKey(nameof(RepositoryCredentialSecret)),
            RepositoryCredentialSecretFilePath = RepositoryCredentialSecretFilePath,
            RepositoryCredentialSecretFilePathSpecified = MyInvocation.BoundParameters.ContainsKey(nameof(RepositoryCredentialSecretFilePath)),
            RepositoryCredentialSecretEnvironmentVariable = RepositoryCredentialSecretEnvironmentVariable,
            RepositoryCredentialSecretEnvironmentVariableSpecified = MyInvocation.BoundParameters.ContainsKey(nameof(RepositoryCredentialSecretEnvironmentVariable)),
            JFrogPlatformUri = JFrogPlatformUri,
            JFrogOidcProvider = JFrogOidcProvider,
            JFrogOidcTokenId = JFrogOidcTokenId,
            JFrogOidcTokenIdEnvironmentVariable = JFrogOidcTokenIdEnvironmentVariable,
            JFrogOidcProviderType = JFrogOidcProviderType,
            Enabled = Enabled.IsPresent,
            OverwriteTagName = OverwriteTagName,
            Force = Force.IsPresent,
            ID = ID,
            DoNotMarkAsPreRelease = DoNotMarkAsPreRelease.IsPresent,
            GenerateReleaseNotes = GenerateReleaseNotes.IsPresent,
            UseAsDependencyVersionSource = UseAsDependencyVersionSource.IsPresent,
            PublishRequiredModules = PublishRequiredModules.IsPresent,
            RequiredModuleSourceRepository = RequiredModuleSourceRepository,
            Verbose = MyInvocation.BoundParameters.ContainsKey("Verbose")
        });

        WriteObject(settings);
    }
}
