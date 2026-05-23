using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Installs one or more modules from a private repository, optionally bootstrapping Azure Artifacts registration first.
/// </summary>
/// <remarks>
/// <para>
/// This cmdlet provides the simplified end-user flow for private gallery onboarding. You can point it at an existing
/// repository name or provide Azure Artifacts details and let the cmdlet register the repository before installing
/// the requested modules.
/// </para>
/// </remarks>
/// <example>
/// <summary>Install modules from an already registered repository</summary>
/// <code>Install-PrivateModule -Name 'ModuleA', 'ModuleB' -Repository 'Company'</code>
/// </example>
/// <example>
/// <summary>Install modules from a saved Azure Artifacts profile</summary>
/// <code>Install-PrivateModule -Name 'ModuleA', 'ModuleB' -ProfileName 'Company' -InstallPrerequisites</code>
/// </example>
[Cmdlet(VerbsLifecycle.Install, "PrivateModule", DefaultParameterSetName = ParameterSetRepository, SupportsShouldProcess = true)]
[OutputType(typeof(ModuleDependencyInstallResult))]
public sealed class InstallPrivateModuleCommand : PSCmdlet
{
    private const string ParameterSetRepository = "Repository";
    private const string ParameterSetAzureArtifacts = "AzureArtifacts";
    private const string ParameterSetMicrosoftArtifactRegistry = "MicrosoftArtifactRegistry";
    private const string ParameterSetProfile = "Profile";

    /// <summary>Module names to install.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [Alias("ModuleName")]
    [ValidateNotNullOrEmpty]
    public string[] Name { get; set; } = Array.Empty<string>();

    /// <summary>Name of an already registered repository.</summary>
    [Parameter(Mandatory = true, ParameterSetName = ParameterSetRepository)]
    [ValidateNotNullOrEmpty]
    public string Repository { get; set; } = string.Empty;

    /// <summary>Saved repository profile name.</summary>
    [Parameter(Mandatory = true, ParameterSetName = ParameterSetProfile)]
    [Alias("Profile")]
    [ValidateNotNullOrEmpty]
    public string ProfileName { get; set; } = string.Empty;

    /// <summary>Private gallery provider. Currently only AzureArtifacts is supported.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    public PrivateGalleryProvider Provider { get; set; } = PrivateGalleryProvider.AzureArtifacts;

    /// <summary>Installs Microsoft-owned packages from Microsoft Artifact Registry, registering MAR first when needed.</summary>
    [Parameter(Mandatory = true, ParameterSetName = ParameterSetMicrosoftArtifactRegistry)]
    public SwitchParameter MicrosoftArtifactRegistry { get; set; }

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

    /// <summary>Optional repository name override when Azure Artifacts details are supplied.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    [Parameter(ParameterSetName = ParameterSetMicrosoftArtifactRegistry)]
    public string? RepositoryName { get; set; }

    /// <summary>Registration strategy used when Azure Artifacts details are supplied. Auto prefers PSResourceGet and falls back to PowerShellGet when needed.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    [Parameter(ParameterSetName = ParameterSetMicrosoftArtifactRegistry)]
    public RepositoryRegistrationTool Tool { get; set; } = RepositoryRegistrationTool.Auto;

    /// <summary>Bootstrap/authentication mode used when Azure Artifacts details are supplied. Auto prefers ExistingSession when Azure Artifacts prerequisites are ready and falls back to CredentialPrompt when they are not.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    [Alias("Mode")]
    public PrivateGalleryBootstrapMode BootstrapMode { get; set; } = PrivateGalleryBootstrapMode.Auto;

    /// <summary>When true, marks the repository as trusted during automatic registration.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    [Parameter(ParameterSetName = ParameterSetMicrosoftArtifactRegistry)]
    public bool Trusted { get; set; } = true;

    /// <summary>Optional PSResourceGet repository priority used during automatic registration.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    [Parameter(ParameterSetName = ParameterSetMicrosoftArtifactRegistry)]
    public int? Priority { get; set; }

    /// <summary>Optional repository credential username.</summary>
    [Parameter(ParameterSetName = ParameterSetRepository)]
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    [Parameter(ParameterSetName = ParameterSetProfile)]
    [Alias("UserName")]
    public string? CredentialUserName { get; set; }

    /// <summary>Optional repository credential secret.</summary>
    [Parameter(ParameterSetName = ParameterSetRepository)]
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    [Parameter(ParameterSetName = ParameterSetProfile)]
    [Alias("Password", "Token")]
    public string? CredentialSecret { get; set; }

    /// <summary>Optional path to a file containing the repository credential secret.</summary>
    [Parameter(ParameterSetName = ParameterSetRepository)]
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    [Parameter(ParameterSetName = ParameterSetProfile)]
    [Alias("CredentialPath", "TokenPath")]
    public string? CredentialSecretFilePath { get; set; }

    /// <summary>Prompts interactively for repository credentials.</summary>
    [Parameter(ParameterSetName = ParameterSetRepository)]
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    [Parameter(ParameterSetName = ParameterSetProfile)]
    [Alias("Interactive")]
    public SwitchParameter PromptForCredential { get; set; }

    /// <summary>Installs missing private-gallery prerequisites before automatic registration, including PSResourceGet requirements and, for Azure Artifacts, the credential provider.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    [Parameter(ParameterSetName = ParameterSetMicrosoftArtifactRegistry)]
    [Parameter(ParameterSetName = ParameterSetProfile)]
    public SwitchParameter InstallPrerequisites { get; set; }

    /// <summary>Includes prerelease versions when supported by the selected installer.</summary>
    [Parameter]
    public SwitchParameter Prerelease { get; set; }

    /// <summary>Forces reinstall even when a matching version is already present.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    /// <summary>Executes the install workflow.</summary>
    protected override void ProcessRecord()
    {
        var useAzureArtifacts = ParameterSetName == ParameterSetAzureArtifacts || ParameterSetName == ParameterSetProfile;
        var useMicrosoftArtifactRegistry = ParameterSetName == ParameterSetMicrosoftArtifactRegistry;
        var provider = Provider;
        var organization = AzureDevOpsOrganization;
        var project = AzureDevOpsProject;
        var feed = AzureArtifactsFeed;
        var repositoryName = ParameterSetName == ParameterSetAzureArtifacts || ParameterSetName == ParameterSetMicrosoftArtifactRegistry
            ? (RepositoryName ?? string.Empty)
            : Repository;
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
        var logger = new CmdletLogger(this, MyInvocation.BoundParameters.ContainsKey("Verbose"));
        var result = new PrivateModuleWorkflowService(host, new PrivateGalleryService(host), logger).Execute(
            new PrivateModuleWorkflowRequest
            {
                Operation = PrivateModuleWorkflowOperation.Install,
                ModuleNames = Name,
                UseAzureArtifacts = useAzureArtifacts,
                UseMicrosoftArtifactRegistry = useMicrosoftArtifactRegistry,
                ProfileName = ParameterSetName == ParameterSetProfile ? ProfileName : null,
                RepositoryName = repositoryName,
                Provider = provider,
                AzureDevOpsOrganization = organization,
                AzureDevOpsProject = project,
                AzureArtifactsFeed = feed,
                Tool = tool,
                BootstrapMode = bootstrapMode,
                Trusted = trusted,
                Priority = priority,
                CredentialUserName = CredentialUserName,
                CredentialSecret = CredentialSecret,
                CredentialSecretFilePath = CredentialSecretFilePath,
                PromptForCredential = PromptForCredential,
                InstallPrerequisites = InstallPrerequisites,
                Prerelease = Prerelease,
                Force = Force
            },
            (target, action) => ShouldProcess(target, action));

        if (!result.OperationPerformed)
            return;

        WriteObject(result.DependencyResults, enumerateCollection: true);
    }
}
