using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Registers an Azure Artifacts feed as a private PowerShell module repository for PowerShellGet and/or PSResourceGet.
/// </summary>
/// <remarks>
/// <para>
/// This cmdlet simplifies Azure Artifacts setup on end-user machines by resolving the correct v2/v3 endpoints
/// and registering the repository for the selected client tools.
/// </para>
/// <para>
/// For PowerShellGet, supplied credentials are forwarded to <c>Register-PSRepository</c> so later
/// <c>Install-Module</c> calls can reuse the registered source. For PSResourceGet, the repository is registered
/// with the Azure Artifacts v3 endpoint so <c>Install-PSResource</c> can use the Azure Artifacts credential provider.
/// </para>
/// <para>
/// The output object indicates which native install paths are ready after registration, so callers can see whether
/// <c>Install-PSResource</c>, <c>Install-Module</c>, or both are available for the configured repository.
/// </para>
/// </remarks>
/// <example>
/// <summary>Register a saved Azure Artifacts profile for PSResourceGet</summary>
/// <code>Register-ModuleRepository -ProfileName 'Company' -InstallPrerequisites</code>
/// </example>
[Cmdlet(VerbsLifecycle.Register, "ModuleRepository", DefaultParameterSetName = ParameterSetAzureArtifacts, SupportsShouldProcess = true)]
[Alias("Register-Gallery")]
[OutputType(typeof(ModuleRepositoryRegistrationResult))]
public sealed class RegisterModuleRepositoryCommand : PSCmdlet
{
    private const string ParameterSetAzureArtifacts = "AzureArtifacts";
    private const string ParameterSetMicrosoftArtifactRegistry = "MicrosoftArtifactRegistry";
    private const string ParameterSetProfile = "Profile";

    /// <summary>Saved repository profile name.</summary>
    [Parameter(Mandatory = true, ParameterSetName = ParameterSetProfile)]
    [Alias("Profile")]
    [ValidateNotNullOrEmpty]
    public string ProfileName { get; set; } = string.Empty;

    /// <summary>Private gallery provider.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    public PrivateGalleryProvider Provider { get; set; } = PrivateGalleryProvider.AzureArtifacts;

    /// <summary>Registers Microsoft Artifact Registry as a trusted PSResourceGet repository for Microsoft-owned packages.</summary>
    [Parameter(Mandatory = true, ParameterSetName = ParameterSetMicrosoftArtifactRegistry)]
    public SwitchParameter MicrosoftArtifactRegistry { get; set; }

    /// <summary>Azure DevOps organization name.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    [Alias("Organization")]
    [ValidateNotNullOrEmpty]
    public string AzureDevOpsOrganization { get; set; } = string.Empty;

    /// <summary>Optional Azure DevOps project name for project-scoped feeds.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    [Alias("Project")]
    public string? AzureDevOpsProject { get; set; }

    /// <summary>Azure Artifacts feed name.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    [Alias("Feed")]
    [ValidateNotNullOrEmpty]
    public string AzureArtifactsFeed { get; set; } = string.Empty;

    /// <summary>Optional repository name override. Defaults to the feed name.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    [Parameter(ParameterSetName = ParameterSetMicrosoftArtifactRegistry)]
    public string? Name { get; set; }

    /// <summary>Provider repository/feed id. For Azure this is the feed when AzureArtifactsFeed is omitted; for JFrog this is the Artifactory NuGet repository key.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    [Parameter(ParameterSetName = ParameterSetMicrosoftArtifactRegistry)]
    public string? Repository { get; set; }

    /// <summary>PSResourceGet v3 repository URI for generic/JFrog feeds.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    public string? RepositoryUri { get; set; }

    /// <summary>PowerShellGet source URI for generic/JFrog feeds.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    public string? RepositorySourceUri { get; set; }

    /// <summary>PowerShellGet publish URI for generic/JFrog feeds.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    public string? RepositoryPublishUri { get; set; }

    /// <summary>JFrog Artifactory base URI, for example https://company.jfrog.io/artifactory.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    public string? JFrogBaseUri { get; set; }

    /// <summary>JFrog NuGet repository key. Defaults from Repository when omitted.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    public string? JFrogRepository { get; set; }

    /// <summary>Registration strategy. Auto prefers PSResourceGet and falls back to PowerShellGet when needed.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    [Parameter(ParameterSetName = ParameterSetMicrosoftArtifactRegistry)]
    public RepositoryRegistrationTool Tool { get; set; } = RepositoryRegistrationTool.Auto;

    /// <summary>Bootstrap/authentication mode. Auto uses supplied or prompted credentials when requested; otherwise it prefers ExistingSession when Azure Artifacts prerequisites are ready and falls back to CredentialPrompt when they are not.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    [Alias("Mode")]
    public PrivateGalleryBootstrapMode BootstrapMode { get; set; } = PrivateGalleryBootstrapMode.Auto;

    /// <summary>When true, marks the repository as trusted.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    [Parameter(ParameterSetName = ParameterSetMicrosoftArtifactRegistry)]
    public bool Trusted { get; set; } = true;

    /// <summary>Optional PSResourceGet repository priority.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    [Parameter(ParameterSetName = ParameterSetMicrosoftArtifactRegistry)]
    public int? Priority { get; set; }

    /// <summary>Optional repository credential username.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    [Parameter(ParameterSetName = ParameterSetProfile)]
    [Alias("UserName")]
    public string? CredentialUserName { get; set; }

    /// <summary>Optional repository credential secret.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    [Parameter(ParameterSetName = ParameterSetProfile)]
    [Alias("Password", "Token")]
    public string? CredentialSecret { get; set; }

    /// <summary>Optional path to a file containing the repository credential secret.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    [Parameter(ParameterSetName = ParameterSetProfile)]
    [Alias("CredentialPath", "TokenPath")]
    public string? CredentialSecretFilePath { get; set; }

    /// <summary>Prompts interactively for repository credentials.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    [Parameter(ParameterSetName = ParameterSetProfile)]
    [Alias("Interactive")]
    public SwitchParameter PromptForCredential { get; set; }

    /// <summary>Installs missing private-gallery prerequisites before registration, including PSResourceGet requirements and, for Azure Artifacts, the credential provider.</summary>
    [Parameter]
    public SwitchParameter InstallPrerequisites { get; set; }

    /// <summary>Executes the repository registration.</summary>
    protected override void ProcessRecord()
    {
        var host = new CmdletPrivateGalleryHost(this);
        var service = new PrivateGalleryService(host);

        if (ParameterSetName == ParameterSetMicrosoftArtifactRegistry)
        {
            var prerequisites = service.EnsureMicrosoftArtifactRegistryPrerequisites(
                InstallPrerequisites.IsPresent);
            var marResult = service.EnsureMicrosoftArtifactRegistryRegistered(
                Name ?? Repository,
                Tool,
                Trusted,
                Priority,
                prerequisites.Status,
                Tool == RepositoryRegistrationTool.Auto
                    ? "Register Microsoft Artifact Registry using PSResourceGet"
                    : $"Register Microsoft Artifact Registry using {Tool}");
            marResult.InstalledPrerequisites = prerequisites.InstalledPrerequisites;
            marResult.PrerequisiteInstallMessages = prerequisites.Messages;

            service.WriteRegistrationSummary(marResult);
            WriteObject(ModuleRepositoryRegistrationResultMapper.ToCmdletResult(marResult));
            return;
        }

        var provider = Provider;
        var organization = AzureDevOpsOrganization;
        var project = AzureDevOpsProject;
        var feed = AzureArtifactsFeed;
        var repositoryName = Name;
        var tool = Tool;
        var bootstrapMode = BootstrapMode;
        var trusted = Trusted;
        var priority = Priority;
        var repository = Repository ?? string.Empty;
        var repositoryUri = RepositoryUri ?? string.Empty;
        var repositorySourceUri = RepositorySourceUri ?? string.Empty;
        var repositoryPublishUri = RepositoryPublishUri ?? string.Empty;
        var jfrogBaseUri = JFrogBaseUri ?? string.Empty;
        var jfrogRepository = JFrogRepository ?? string.Empty;

        if (ParameterSetName == ParameterSetProfile)
        {
            var profile = ModuleRepositoryProfileCommandSupport.ResolveRequired(ProfileName);
            provider = profile.Provider;
            organization = profile.AzureDevOpsOrganization;
            project = profile.AzureDevOpsProject;
            feed = profile.AzureArtifactsFeed;
            repositoryName = profile.RepositoryName;
            repository = profile.Repository;
            repositoryUri = profile.RepositoryUri;
            repositorySourceUri = profile.RepositorySourceUri;
            repositoryPublishUri = profile.RepositoryPublishUri;
            jfrogBaseUri = profile.JFrogBaseUri;
            jfrogRepository = profile.JFrogRepository;
            tool = profile.Tool;
            bootstrapMode = profile.BootstrapMode;
            trusted = profile.Trusted;
            priority = profile.Priority;
        }

        service.EnsureProviderSupported(provider);

        var endpoint = PrivateGalleryRepositoryEndpoints.Create(
            provider,
            organization,
            project,
            feed,
            repositoryName,
            repository,
            repositoryUri,
            repositorySourceUri,
            repositoryPublishUri,
            jfrogBaseUri,
            jfrogRepository);
        var prerequisiteInstall = service.EnsureBootstrapPrerequisites(
            InstallPrerequisites.IsPresent,
            bootstrapMode,
            includeAzureArtifactsCredentialProvider: provider == PrivateGalleryProvider.AzureArtifacts,
            artefactsRepositoryName: endpoint.RepositoryName,
            artefactsPSResourceGetUri: endpoint.PSResourceGetUri,
            artefactsPowerShellGetSourceUri: endpoint.PowerShellGetSourceUri);
        var allowInteractivePrompt = !host.IsWhatIfRequested;

        var credentialResolution = service.ResolveCredential(
            endpoint.RepositoryName,
            bootstrapMode,
            CredentialUserName,
            CredentialSecret,
            CredentialSecretFilePath,
            PromptForCredential,
            prerequisiteInstall.Status,
            allowInteractivePrompt,
            endpoint.Provider);

        var result = service.EnsureAzureArtifactsRepositoryRegistered(
            organization,
            project,
            feed,
            repositoryName,
            tool,
            trusted,
            priority,
            bootstrapMode,
            credentialResolution.BootstrapModeUsed,
            credentialResolution.CredentialSource,
            credentialResolution.Credential,
            prerequisiteInstall.Status,
            shouldProcessAction: tool == RepositoryRegistrationTool.Auto
                ? "Register module repository using Auto (prefer PSResourceGet, fall back to PowerShellGet)"
                : $"Register module repository using {tool}",
            provider: provider,
            repository: repository,
            repositoryUri: repositoryUri,
            repositorySourceUri: repositorySourceUri,
            repositoryPublishUri: repositoryPublishUri,
            jfrogBaseUri: jfrogBaseUri,
            jfrogRepository: jfrogRepository);
        result.InstalledPrerequisites = prerequisiteInstall.InstalledPrerequisites;
        result.PrerequisiteInstallMessages = prerequisiteInstall.Messages;

        service.WriteRegistrationSummary(result);
        WriteObject(ModuleRepositoryRegistrationResultMapper.ToCmdletResult(result));
    }
}
