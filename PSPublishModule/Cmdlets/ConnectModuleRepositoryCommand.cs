using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Registers an Azure Artifacts repository if needed and validates authenticated access for the selected bootstrap mode.
/// </summary>
/// <remarks>
/// <para>
/// This cmdlet is the explicit "connect/login" companion to <c>Register-ModuleRepository</c>. It ensures the
/// repository registration exists and then performs a lightweight authenticated probe so callers know whether
/// the chosen bootstrap path can actually access the private feed.
/// </para>
/// </remarks>
/// <example>
/// <summary>Connect to an Azure Artifacts repository using the best available bootstrap mode</summary>
/// <code>Connect-ModuleRepository -AzureDevOpsOrganization 'contoso' -AzureDevOpsProject 'Platform' -AzureArtifactsFeed 'Modules' -InstallPrerequisites</code>
/// </example>
[Cmdlet(VerbsCommunications.Connect, "ModuleRepository", SupportsShouldProcess = true)]
[Alias("Connect-Gallery")]
[OutputType(typeof(ModuleRepositoryRegistrationResult))]
public sealed class ConnectModuleRepositoryCommand : PSCmdlet
{
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

    /// <summary>Optional repository name override. Defaults to the feed name.</summary>
    [Parameter]
    [Alias("Repository")]
    public string? Name { get; set; }

    /// <summary>Registration strategy. Auto prefers PSResourceGet and falls back to PowerShellGet when needed.</summary>
    [Parameter]
    public RepositoryRegistrationTool Tool { get; set; } = RepositoryRegistrationTool.Auto;

    /// <summary>Bootstrap/authentication mode. Auto uses supplied or prompted credentials when requested; otherwise it prefers ExistingSession when Azure Artifacts prerequisites are ready and falls back to CredentialPrompt when they are not.</summary>
    [Parameter]
    [Alias("Mode")]
    public PrivateGalleryBootstrapMode BootstrapMode { get; set; } = PrivateGalleryBootstrapMode.Auto;

    /// <summary>When true, marks the repository as trusted.</summary>
    [Parameter]
    public bool Trusted { get; set; } = true;

    /// <summary>Optional PSResourceGet repository priority.</summary>
    [Parameter]
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

    /// <summary>Installs missing private-gallery prerequisites such as PSResourceGet and the Azure Artifacts credential provider before connecting.</summary>
    [Parameter]
    public SwitchParameter InstallPrerequisites { get; set; }

    /// <summary>Executes the connect/login workflow.</summary>
    protected override void ProcessRecord()
    {
        PrivateGalleryCommandSupport.EnsureProviderSupported(Provider);

        var endpoint = AzureArtifactsRepositoryEndpoints.Create(
            AzureDevOpsOrganization,
            AzureDevOpsProject,
            AzureArtifactsFeed,
            Name);
        var prerequisiteInstall = PrivateGalleryCommandSupport.EnsureBootstrapPrerequisites(this, InstallPrerequisites.IsPresent);

        var credentialResolution = PrivateGalleryCommandSupport.ResolveCredential(
            this,
            endpoint.RepositoryName,
            BootstrapMode,
            CredentialUserName,
            CredentialSecret,
            CredentialSecretFilePath,
            PromptForCredential);

        var result = PrivateGalleryCommandSupport.EnsureAzureArtifactsRepositoryRegistered(
            this,
            AzureDevOpsOrganization,
            AzureDevOpsProject,
            AzureArtifactsFeed,
            Name,
            Tool,
            Trusted,
            Priority,
            BootstrapMode,
            credentialResolution.BootstrapModeUsed,
            credentialResolution.CredentialSource,
            credentialResolution.Credential,
            shouldProcessAction: Tool == RepositoryRegistrationTool.Auto
                ? "Connect module repository using Auto (prefer PSResourceGet, fall back to PowerShellGet)"
                : $"Connect module repository using {Tool}");
        result.InstalledPrerequisites = prerequisiteInstall.InstalledPrerequisites;
        result.PrerequisiteInstallMessages = prerequisiteInstall.Messages;

        if (!result.RegistrationPerformed)
        {
            PrivateGalleryCommandSupport.WriteRegistrationSummary(this, result);
            WriteObject(result);
            return;
        }

        var probe = PrivateGalleryCommandSupport.ProbeRepositoryAccess(result, credentialResolution.Credential);
        result.AccessProbePerformed = true;
        result.AccessProbeSucceeded = probe.Succeeded;
        result.AccessProbeTool = probe.Tool;
        result.AccessProbeMessage = probe.Message;

        PrivateGalleryCommandSupport.WriteRegistrationSummary(this, result);

        if (!probe.Succeeded)
        {
            var hint = string.IsNullOrWhiteSpace(result.RecommendedBootstrapCommand)
                ? string.Empty
                : $" Recommended next step: {result.RecommendedBootstrapCommand}";
            var exception = new InvalidOperationException(
                $"Repository '{result.RepositoryName}' could not be connected via {probe.Tool}. {probe.Message}{hint}".Trim());
            ThrowTerminatingError(new ErrorRecord(
                exception,
                "ConnectModuleRepositoryProbeFailed",
                ErrorCategory.OpenError,
                result.RepositoryName));
            return;
        }

        WriteObject(result);
    }
}
