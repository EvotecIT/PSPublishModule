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
    private const string ParameterSetProfile = "Profile";

    /// <summary>Saved repository profile name.</summary>
    [Parameter(Mandatory = true, ParameterSetName = ParameterSetProfile)]
    [Alias("Profile")]
    [ValidateNotNullOrEmpty]
    public string ProfileName { get; set; } = string.Empty;

    /// <summary>Private gallery provider. Currently only AzureArtifacts is supported.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    public PrivateGalleryProvider Provider { get; set; } = PrivateGalleryProvider.AzureArtifacts;

    /// <summary>Azure DevOps organization name.</summary>
    [Parameter(Mandatory = true, ParameterSetName = ParameterSetAzureArtifacts)]
    [Alias("Organization")]
    [ValidateNotNullOrEmpty]
    public string AzureDevOpsOrganization { get; set; } = string.Empty;

    /// <summary>Optional Azure DevOps project name for project-scoped feeds.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    [Alias("Project")]
    public string? AzureDevOpsProject { get; set; }

    /// <summary>Azure Artifacts feed name.</summary>
    [Parameter(Mandatory = true, ParameterSetName = ParameterSetAzureArtifacts)]
    [Alias("Feed")]
    [ValidateNotNullOrEmpty]
    public string AzureArtifactsFeed { get; set; } = string.Empty;

    /// <summary>Optional repository name override. Defaults to the feed name.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    [Alias("Repository")]
    public string? Name { get; set; }

    /// <summary>Registration strategy. Auto prefers PSResourceGet and falls back to PowerShellGet when needed.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    public RepositoryRegistrationTool Tool { get; set; } = RepositoryRegistrationTool.Auto;

    /// <summary>Bootstrap/authentication mode. Auto uses supplied or prompted credentials when requested; otherwise it prefers ExistingSession when Azure Artifacts prerequisites are ready and falls back to CredentialPrompt when they are not.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    [Alias("Mode")]
    public PrivateGalleryBootstrapMode BootstrapMode { get; set; } = PrivateGalleryBootstrapMode.Auto;

    /// <summary>When true, marks the repository as trusted.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    public bool Trusted { get; set; } = true;

    /// <summary>Optional PSResourceGet repository priority.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    public int? Priority { get; set; }

    /// <summary>Optional repository credential username.</summary>
    [Parameter]
    [Alias("UserName")]
    public string? CredentialUserName { get; set; }

    /// <summary>Optional repository credential secret.</summary>
    [Parameter]
    [Alias("Password", "Token")]
    public string? CredentialSecret { get; set; }

    /// <summary>Optional path to a file containing the repository credential secret.</summary>
    [Parameter]
    [Alias("CredentialPath", "TokenPath")]
    public string? CredentialSecretFilePath { get; set; }

    /// <summary>Prompts interactively for repository credentials.</summary>
    [Parameter]
    [Alias("Interactive")]
    public SwitchParameter PromptForCredential { get; set; }

    /// <summary>Installs missing private-gallery prerequisites such as PSResourceGet and the Azure Artifacts credential provider before registration.</summary>
    [Parameter]
    public SwitchParameter InstallPrerequisites { get; set; }

    /// <summary>Executes the repository registration.</summary>
    protected override void ProcessRecord()
    {
        var provider = Provider;
        var organization = AzureDevOpsOrganization;
        var project = AzureDevOpsProject;
        var feed = AzureArtifactsFeed;
        var repositoryName = Name;
        var tool = Tool;
        var bootstrapMode = BootstrapMode;
        var trusted = Trusted;
        var priority = Priority;

        if (ParameterSetName == ParameterSetProfile)
        {
            var profile = ModuleRepositoryProfileCommandSupport.ResolveRequired(ProfileName);
            provider = profile.Provider;
            organization = profile.AzureDevOpsOrganization;
            project = profile.AzureDevOpsProject;
            feed = profile.AzureArtifactsFeed;
            repositoryName = profile.RepositoryName;
            tool = profile.Tool;
            bootstrapMode = profile.BootstrapMode;
            trusted = profile.Trusted;
            priority = profile.Priority;
        }

        var host = new CmdletPrivateGalleryHost(this);
        var service = new PrivateGalleryService(host);
        service.EnsureProviderSupported(provider);

        var endpoint = AzureArtifactsRepositoryEndpoints.Create(
            organization,
            project,
            feed,
            repositoryName);
        var prerequisiteInstall = service.EnsureBootstrapPrerequisites(InstallPrerequisites.IsPresent);
        var allowInteractivePrompt = !host.IsWhatIfRequested;

        var credentialResolution = service.ResolveCredential(
            endpoint.RepositoryName,
            bootstrapMode,
            CredentialUserName,
            CredentialSecret,
            CredentialSecretFilePath,
            PromptForCredential,
            prerequisiteInstall.Status,
            allowInteractivePrompt);

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
                : $"Register module repository using {tool}");
        result.InstalledPrerequisites = prerequisiteInstall.InstalledPrerequisites;
        result.PrerequisiteInstallMessages = prerequisiteInstall.Messages;

        service.WriteRegistrationSummary(result);
        WriteObject(ModuleRepositoryRegistrationResultMapper.ToCmdletResult(result));
    }
}
