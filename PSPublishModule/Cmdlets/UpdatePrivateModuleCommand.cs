using System;
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
/// <summary>Refresh an Azure Artifacts repository and update modules in one command</summary>
/// <code>Update-PrivateModule -Name 'ModuleA', 'ModuleB' -AzureDevOpsOrganization 'contoso' -AzureDevOpsProject 'Platform' -AzureArtifactsFeed 'Modules' -PromptForCredential</code>
/// </example>
[Cmdlet(VerbsData.Update, "PrivateModule", DefaultParameterSetName = ParameterSetRepository, SupportsShouldProcess = true)]
[OutputType(typeof(ModuleDependencyInstallResult))]
public sealed class UpdatePrivateModuleCommand : PSCmdlet
{
    private const string ParameterSetRepository = "Repository";
    private const string ParameterSetAzureArtifacts = "AzureArtifacts";

    /// <summary>Module names to update.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [Alias("ModuleName")]
    [ValidateNotNullOrEmpty]
    public string[] Name { get; set; } = Array.Empty<string>();

    /// <summary>Name of an already registered repository.</summary>
    [Parameter(Mandatory = true, ParameterSetName = ParameterSetRepository)]
    [ValidateNotNullOrEmpty]
    public string Repository { get; set; } = string.Empty;

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

    /// <summary>Optional repository name override when Azure Artifacts details are supplied.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    public string? RepositoryName { get; set; }

    /// <summary>Registration strategy used when Azure Artifacts details are supplied. Auto prefers PSResourceGet and falls back to PowerShellGet when needed.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    public RepositoryRegistrationTool Tool { get; set; } = RepositoryRegistrationTool.Auto;

    /// <summary>Bootstrap/authentication mode used when Azure Artifacts details are supplied. Auto prefers ExistingSession when Azure Artifacts prerequisites are ready and falls back to CredentialPrompt when they are not.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    [Alias("Mode")]
    public PrivateGalleryBootstrapMode BootstrapMode { get; set; } = PrivateGalleryBootstrapMode.Auto;

    /// <summary>When true, marks the repository as trusted during automatic registration refresh.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    public bool Trusted { get; set; } = true;

    /// <summary>Optional PSResourceGet repository priority used during automatic registration refresh.</summary>
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

    /// <summary>Installs missing private-gallery prerequisites such as PSResourceGet and the Azure Artifacts credential provider before automatic registration refresh.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    public SwitchParameter InstallPrerequisites { get; set; }

    /// <summary>Includes prerelease versions when supported by the selected installer.</summary>
    [Parameter]
    public SwitchParameter Prerelease { get; set; }

    /// <summary>Executes the update workflow.</summary>
    protected override void ProcessRecord()
    {
        var host = new CmdletPrivateGalleryHost(this);
        var logger = new CmdletLogger(this, MyInvocation.BoundParameters.ContainsKey("Verbose"));
        var result = new PrivateModuleWorkflowService(host, new PrivateGalleryService(host), logger).Execute(
            new PrivateModuleWorkflowRequest
            {
                Operation = PrivateModuleWorkflowOperation.Update,
                ModuleNames = Name,
                UseAzureArtifacts = ParameterSetName == ParameterSetAzureArtifacts,
                RepositoryName = ParameterSetName == ParameterSetAzureArtifacts ? (RepositoryName ?? string.Empty) : Repository,
                Provider = Provider,
                AzureDevOpsOrganization = AzureDevOpsOrganization,
                AzureDevOpsProject = AzureDevOpsProject,
                AzureArtifactsFeed = AzureArtifactsFeed,
                Tool = Tool,
                BootstrapMode = BootstrapMode,
                Trusted = Trusted,
                Priority = Priority,
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
}
