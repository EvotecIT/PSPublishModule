using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates or updates a saved private module repository profile.
/// </summary>
/// <remarks>
/// <para>
/// Profiles store the non-secret Azure Artifacts feed settings used by <c>Connect-ModuleRepository</c>,
/// <c>Install-PrivateModule</c>, and <c>Update-PrivateModule</c>. Azure Artifacts profiles default to
/// PSResourceGet with the Azure Artifacts Credential Provider so Entra ID/MFA is handled by the provider
/// instead of storing PATs in PSPublishModule.
/// </para>
/// </remarks>
/// <example>
/// <summary>Create an Entra-first Azure Artifacts profile</summary>
/// <code>Set-ModuleRepositoryProfile -Name Company -AzureDevOpsOrganization contoso -AzureDevOpsProject Platform -AzureArtifactsFeed Modules</code>
/// </example>
[Cmdlet(VerbsCommon.Set, "ModuleRepositoryProfile", SupportsShouldProcess = true)]
[Alias("Set-GalleryProfile")]
[OutputType(typeof(ModuleRepositoryProfileResult))]
public sealed class SetModuleRepositoryProfileCommand : PSCmdlet
{
    /// <summary>Profile name used by connect/install/update commands.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [Alias("ProfileName")]
    [ValidateNotNullOrEmpty]
    public string Name { get; set; } = string.Empty;

    /// <summary>Private gallery provider. Currently only AzureArtifacts is supported.</summary>
    [Parameter]
    public PrivateGalleryProvider Provider { get; set; } = PrivateGalleryProvider.AzureArtifacts;

    /// <summary>Azure DevOps organization name.</summary>
    [Parameter(Mandatory = true)]
    [Alias("Organization")]
    [ValidateNotNullOrEmpty]
    public string AzureDevOpsOrganization { get; set; } = string.Empty;

    /// <summary>Optional Azure DevOps project name for project-scoped feeds.</summary>
    [Parameter]
    [Alias("Project")]
    public string? AzureDevOpsProject { get; set; }

    /// <summary>Azure Artifacts feed name.</summary>
    [Parameter(Mandatory = true)]
    [Alias("Feed")]
    [ValidateNotNullOrEmpty]
    public string AzureArtifactsFeed { get; set; } = string.Empty;

    /// <summary>Optional local repository name override. Defaults to the feed name.</summary>
    [Parameter]
    [Alias("Repository")]
    public string? RepositoryName { get; set; }

    /// <summary>Registration strategy saved in the profile. Defaults to PSResourceGet for Entra-first Azure Artifacts use.</summary>
    [Parameter]
    public RepositoryRegistrationTool Tool { get; set; } = RepositoryRegistrationTool.PSResourceGet;

    /// <summary>Bootstrap/authentication mode saved in the profile. Defaults to ExistingSession for Azure Artifacts Credential Provider login.</summary>
    [Parameter]
    [Alias("Mode")]
    public PrivateGalleryBootstrapMode BootstrapMode { get; set; } = PrivateGalleryBootstrapMode.ExistingSession;

    /// <summary>When true, marks the repository as trusted during registration.</summary>
    [Parameter]
    public bool Trusted { get; set; } = true;

    /// <summary>Optional PSResourceGet repository priority.</summary>
    [Parameter]
    public int? Priority { get; set; }

    /// <summary>Saves the profile.</summary>
    protected override void ProcessRecord()
    {
        var store = new ModuleRepositoryProfileStore();
        var profile = ModuleRepositoryProfileStore.Normalize(new ModuleRepositoryProfile
        {
            Name = Name,
            Provider = Provider,
            AzureDevOpsOrganization = AzureDevOpsOrganization,
            AzureDevOpsProject = AzureDevOpsProject,
            AzureArtifactsFeed = AzureArtifactsFeed,
            RepositoryName = RepositoryName ?? string.Empty,
            Tool = Tool,
            BootstrapMode = BootstrapMode,
            Trusted = Trusted,
            Priority = Priority,
            AuthenticationMode = "AzureArtifactsCredentialProvider"
        });

        if (!ShouldProcess(profile.Name, "Save module repository profile"))
            return;

        var saved = store.SaveProfile(profile);
        WriteObject(ModuleRepositoryProfileResultMapper.ToCmdletResult(saved, store.Path));
    }
}
