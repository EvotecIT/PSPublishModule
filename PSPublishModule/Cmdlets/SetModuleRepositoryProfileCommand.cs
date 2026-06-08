using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates or updates a saved private module repository profile.
/// </summary>
/// <remarks>
/// <para>
/// Profiles store the non-secret Azure Artifacts feed settings used by <c>Connect-ModuleRepository</c>,
/// <c>Install-PrivateModule</c>, <c>Update-PrivateModule</c>, <c>New-ConfigurationPublish</c>, and
/// <c>Publish-NugetPackage</c>. Azure Artifacts profiles default to PSResourceGet with the Azure Artifacts
/// Credential Provider so Entra ID/MFA is handled by the provider instead of storing PATs in PSPublishModule.
/// </para>
/// </remarks>
/// <example>
/// <summary>Create an Entra-first Azure Artifacts profile</summary>
/// <code>Set-ModuleRepositoryProfile -Name Company -AzureDevOpsOrganization contoso -AzureDevOpsProject Platform -AzureArtifactsFeed Modules</code>
/// <para>Saves a user-local profile that later commands can reference with <c>-ProfileName Company</c>.</para>
/// </example>
/// <example>
/// <summary>Use a different local repository name</summary>
/// <code>Set-ModuleRepositoryProfile -Name Finance -AzureDevOpsOrganization contoso -AzureDevOpsProject Platform -AzureArtifactsFeed InternalModules -RepositoryName CompanyModules -Priority 20</code>
/// <para>Stores the Azure Artifacts feed identity while registering it locally as <c>CompanyModules</c>.</para>
/// </example>
[Cmdlet(VerbsCommon.Set, "ModuleRepositoryProfile", SupportsShouldProcess = true)]
[Alias("Set-GalleryProfile")]
[OutputType(typeof(ModuleRepositoryProfileResult))]
public sealed class SetModuleRepositoryProfileCommand : PSCmdlet
{
    /// <summary>Profile name used by connect, install, update, and publish commands.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [Alias("ProfileName")]
    [ValidateNotNullOrEmpty]
    public string Name { get; set; } = string.Empty;

    /// <summary>Private gallery provider.</summary>
    [Parameter]
    public PrivateGalleryProvider Provider { get; set; } = PrivateGalleryProvider.AzureArtifacts;

    /// <summary>Azure DevOps organization name.</summary>
    [Parameter]
    [Alias("Organization")]
    [ValidateNotNullOrEmpty]
    public string AzureDevOpsOrganization { get; set; } = string.Empty;

    /// <summary>Optional Azure DevOps project name for project-scoped feeds.</summary>
    [Parameter]
    [Alias("Project")]
    public string? AzureDevOpsProject { get; set; }

    /// <summary>Azure Artifacts feed name.</summary>
    [Parameter]
    [Alias("Feed")]
    [ValidateNotNullOrEmpty]
    public string AzureArtifactsFeed { get; set; } = string.Empty;

    /// <summary>Provider repository/feed id. For Azure this is the feed when AzureArtifactsFeed is omitted; for JFrog this is the Artifactory NuGet repository key.</summary>
    [Parameter]
    public string? Repository { get; set; }

    /// <summary>Optional local repository name override. Defaults to the provider repository/feed id.</summary>
    [Parameter]
    public string? RepositoryName { get; set; }

    /// <summary>PSResourceGet v3 repository URI for generic/JFrog feeds.</summary>
    [Parameter]
    public string? RepositoryUri { get; set; }

    /// <summary>PowerShellGet source URI for generic/JFrog feeds.</summary>
    [Parameter]
    public string? RepositorySourceUri { get; set; }

    /// <summary>PowerShellGet publish URI for generic/JFrog feeds.</summary>
    [Parameter]
    public string? RepositoryPublishUri { get; set; }

    /// <summary>JFrog Artifactory base URI, for example https://company.jfrog.io/artifactory.</summary>
    [Parameter]
    public string? JFrogBaseUri { get; set; }

    /// <summary>JFrog NuGet repository key. Defaults from Repository when omitted.</summary>
    [Parameter]
    public string? JFrogRepository { get; set; }

    /// <summary>GitHub user or organization namespace for GitHub Packages. Defaults from Repository when omitted.</summary>
    [Parameter]
    [Alias("Owner", "Namespace")]
    public string? GitHubOwner { get; set; }

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

    /// <summary>Profile store scope to write. Use Machine from an elevated/admin deployment to share non-secret feed settings with all users.</summary>
    [Parameter]
    public ModuleRepositoryProfileScope Scope { get; set; } = ModuleRepositoryProfileScope.User;

    /// <summary>Saves the profile.</summary>
    protected override void ProcessRecord()
    {
        if (Scope == ModuleRepositoryProfileScope.All)
            throw new ArgumentException("Set-ModuleRepositoryProfile requires User or Machine scope.", nameof(Scope));

        var store = new ModuleRepositoryProfileStore(Scope);
        var profile = ModuleRepositoryProfileStore.Normalize(new ModuleRepositoryProfile
        {
            Name = Name,
            Provider = Provider,
            AzureDevOpsOrganization = AzureDevOpsOrganization,
            AzureDevOpsProject = AzureDevOpsProject,
            AzureArtifactsFeed = AzureArtifactsFeed,
            Repository = Repository ?? string.Empty,
            RepositoryName = RepositoryName ?? string.Empty,
            RepositoryUri = RepositoryUri ?? string.Empty,
            RepositorySourceUri = RepositorySourceUri ?? string.Empty,
            RepositoryPublishUri = RepositoryPublishUri ?? string.Empty,
            JFrogBaseUri = JFrogBaseUri ?? string.Empty,
            JFrogRepository = JFrogRepository ?? string.Empty,
            GitHubOwner = GitHubOwner ?? string.Empty,
            Tool = Tool,
            BootstrapMode = BootstrapMode,
            Trusted = Trusted,
            Priority = Priority,
            AuthenticationMode = Provider == PrivateGalleryProvider.AzureArtifacts
                ? "AzureArtifactsCredentialProvider"
                : "CredentialPrompt"
        });

        if (!ShouldProcess(profile.Name, "Save module repository profile"))
            return;

        var saved = store.SaveProfile(profile);
        WriteObject(ModuleRepositoryProfileResultMapper.ToCmdletResult(saved, store.Path, store.Scope));
    }
}
