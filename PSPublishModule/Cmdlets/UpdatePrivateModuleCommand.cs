using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Updates one or more modules from a private repository, optionally refreshing Azure Artifacts registration first.
/// </summary>
/// <remarks>
/// <para>
/// This cmdlet is the day-to-day maintenance companion to <c>Install-PrivateModule</c>. When Azure Artifacts details
/// are provided, the repository registration is refreshed before the update is attempted.
/// </para>
/// </remarks>
/// <example>
/// <summary>Update modules from an already registered repository</summary>
/// <code>Update-PrivateModule -Name 'ModuleA', 'ModuleB' -Repository 'Company'</code>
/// </example>
/// <example>
/// <summary>Make an exact module version available</summary>
/// <code>Update-PrivateModule -Name 'ModuleA' -Repository 'Company' -RequiredVersion '1.2.0'</code>
/// </example>
/// <example>
/// <summary>Update modules from a saved Azure Artifacts profile</summary>
/// <code>Update-PrivateModule -Name 'ModuleA', 'ModuleB' -ProfileName 'Company' -InstallPrerequisites</code>
/// </example>
[Cmdlet(VerbsData.Update, "PrivateModule", DefaultParameterSetName = ParameterSetRepository, SupportsShouldProcess = true)]
[OutputType(typeof(ModuleDependencyInstallResult))]
public sealed class UpdatePrivateModuleCommand : PSCmdlet
{
    private const string ParameterSetRepository = "Repository";
    private const string ParameterSetAzureArtifacts = "AzureArtifacts";
    private const string ParameterSetMicrosoftArtifactRegistry = "MicrosoftArtifactRegistry";
    private const string ParameterSetProfile = "Profile";

    /// <summary>Module names to update.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [Alias("ModuleName")]
    [ValidateNotNullOrEmpty]
    public string[] Name { get; set; } = Array.Empty<string>();

    /// <summary>Exact module version to make available for every requested module name.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? RequiredVersion { get; set; }

    /// <summary>PowerShell module installation scope used when an update needs to install a version.</summary>
    [Parameter]
    [ValidateSet("CurrentUser", "AllUsers")]
    public string Scope { get; set; } = "CurrentUser";

    /// <summary>Name of an already registered repository, or provider repository/feed id when a private-gallery provider is selected.</summary>
    [Parameter(Mandatory = true, ParameterSetName = ParameterSetRepository)]
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    [ValidateNotNullOrEmpty]
    public string Repository { get; set; } = string.Empty;

    /// <summary>Saved repository profile name.</summary>
    [Parameter(Mandatory = true, ParameterSetName = ParameterSetProfile)]
    [Alias("Profile")]
    [ValidateNotNullOrEmpty]
    public string ProfileName { get; set; } = string.Empty;

    /// <summary>Private gallery provider used for automatic repository registration.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    public PrivateGalleryProvider Provider { get; set; } = PrivateGalleryProvider.AzureArtifacts;

    /// <summary>Updates Microsoft-owned packages from Microsoft Artifact Registry, registering MAR first when needed.</summary>
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

    /// <summary>When true, marks the repository as trusted during automatic registration refresh.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    [Parameter(ParameterSetName = ParameterSetMicrosoftArtifactRegistry)]
    public bool Trusted { get; set; } = true;

    /// <summary>Optional PSResourceGet repository priority used during automatic registration refresh.</summary>
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

    /// <summary>Installs missing private-gallery prerequisites before automatic registration refresh, including PSResourceGet requirements and, for Azure Artifacts, the credential provider.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    [Parameter(ParameterSetName = ParameterSetMicrosoftArtifactRegistry)]
    [Parameter(ParameterSetName = ParameterSetProfile)]
    public SwitchParameter InstallPrerequisites { get; set; }

    /// <summary>Includes prerelease versions when supported by the selected installer.</summary>
    [Parameter]
    public SwitchParameter Prerelease { get; set; }

    /// <summary>Executes the update workflow.</summary>
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
        var repository = ParameterSetName == ParameterSetAzureArtifacts ? Repository : string.Empty;
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

        var host = new CmdletPrivateGalleryHost(this);
        var logger = new CmdletLogger(this, MyInvocation.BoundParameters.ContainsKey("Verbose"));
        var result = new PrivateModuleWorkflowService(host, new PrivateGalleryService(host), logger).Execute(
            new PrivateModuleWorkflowRequest
            {
                Operation = PrivateModuleWorkflowOperation.Update,
                ModuleNames = Name,
                RequiredVersions = BuildRequiredVersions(Name, RequiredVersion),
                InstallScope = Scope,
                UseAzureArtifacts = useAzureArtifacts,
                UseMicrosoftArtifactRegistry = useMicrosoftArtifactRegistry,
                ProfileName = ParameterSetName == ParameterSetProfile ? ProfileName : null,
                RepositoryName = repositoryName,
                Provider = provider,
                AzureDevOpsOrganization = organization,
                AzureDevOpsProject = project,
                AzureArtifactsFeed = feed,
                Repository = repository,
                RepositoryUri = repositoryUri,
                RepositorySourceUri = repositorySourceUri,
                RepositoryPublishUri = repositoryPublishUri,
                JFrogBaseUri = jfrogBaseUri,
                JFrogRepository = jfrogRepository,
                Tool = tool,
                BootstrapMode = bootstrapMode,
                Trusted = trusted,
                Priority = priority,
                CredentialUserName = CredentialUserName,
                CredentialSecret = CredentialSecret,
                CredentialSecretFilePath = CredentialSecretFilePath,
                PromptForCredential = PromptForCredential,
                InstallPrerequisites = InstallPrerequisites,
                Prerelease = Prerelease
            },
            (target, action) => ShouldProcess(target, action));

        if (!result.OperationPerformed)
            return;

        WriteObject(result.DependencyResults, enumerateCollection: true);
    }

    private static IReadOnlyDictionary<string, string> BuildRequiredVersions(IEnumerable<string> names, string? requiredVersion)
    {
        if (string.IsNullOrWhiteSpace(requiredVersion))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return (names ?? Array.Empty<string>())
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static name => name, _ => requiredVersion!.Trim(), StringComparer.OrdinalIgnoreCase);
    }
}
