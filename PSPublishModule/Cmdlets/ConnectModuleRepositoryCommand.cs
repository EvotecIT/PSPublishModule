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
/// <summary>Connect to a saved Azure Artifacts profile using Entra-backed credential provider login</summary>
/// <code>Connect-ModuleRepository -ProfileName 'Company' -InstallPrerequisites</code>
/// </example>
[Cmdlet(VerbsCommunications.Connect, "ModuleRepository", DefaultParameterSetName = ParameterSetAzureArtifacts, SupportsShouldProcess = true)]
[Alias("Connect-Gallery")]
[OutputType(typeof(ModuleRepositoryRegistrationResult))]
public sealed class ConnectModuleRepositoryCommand : PSCmdlet
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

    /// <summary>Registers and probes Microsoft Artifact Registry as a PSResourceGet repository for Microsoft-owned packages.</summary>
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

    /// <summary>Installs missing private-gallery prerequisites before connecting, including PSResourceGet requirements and, for Azure Artifacts, the credential provider.</summary>
    [Parameter]
    public SwitchParameter InstallPrerequisites { get; set; }

    /// <summary>Executes the connect/login workflow.</summary>
    protected override void ProcessRecord()
    {
        var host = new CmdletPrivateGalleryHost(this);
        var service = new PrivateGalleryService(host);

        if (ParameterSetName == ParameterSetMicrosoftArtifactRegistry)
        {
            var prerequisites = service.EnsureMicrosoftArtifactRegistryPrerequisites(
                InstallPrerequisites.IsPresent);
            var marResult = service.EnsureMicrosoftArtifactRegistryRegistered(
                Name,
                Tool,
                Trusted,
                Priority,
                prerequisites.Status,
                Tool == RepositoryRegistrationTool.Auto
                    ? "Connect Microsoft Artifact Registry using PSResourceGet"
                    : $"Connect Microsoft Artifact Registry using {Tool}");
            marResult.InstalledPrerequisites = prerequisites.InstalledPrerequisites;
            marResult.PrerequisiteInstallMessages = prerequisites.Messages;

            if (marResult.RegistrationPerformed)
            {
                var marProbe = service.ProbeRepositoryAccess(marResult, credential: null);
                marResult.AccessProbePerformed = true;
                marResult.AccessProbeSucceeded = marProbe.Succeeded;
                marResult.AccessProbeTool = marProbe.Tool;
                marResult.AccessProbeMessage = marProbe.Message;
            }

            service.WriteRegistrationSummary(marResult);
            var marCmdletResult = ModuleRepositoryRegistrationResultMapper.ToCmdletResult(marResult);
            if (marResult.RegistrationPerformed && !marResult.AccessProbeSucceeded)
            {
                ThrowTerminatingError(new ErrorRecord(
                    new InvalidOperationException($"Microsoft Artifact Registry could not be reached via {marResult.AccessProbeTool}. {marResult.AccessProbeMessage}".Trim()),
                    "ConnectModuleRepositoryProbeFailed",
                    ErrorCategory.OpenError,
                    marCmdletResult.RepositoryName));
                return;
            }

            WriteObject(marCmdletResult);
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
            includeAzureArtifactsCredentialProvider: provider == PrivateGalleryProvider.AzureArtifacts);
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
                ? "Connect module repository using Auto (prefer PSResourceGet, fall back to PowerShellGet)"
                : $"Connect module repository using {tool}",
            provider: provider,
            repository: repository,
            repositoryUri: repositoryUri,
            repositorySourceUri: repositorySourceUri,
            repositoryPublishUri: repositoryPublishUri,
            jfrogBaseUri: jfrogBaseUri,
            jfrogRepository: jfrogRepository);
        result.InstalledPrerequisites = prerequisiteInstall.InstalledPrerequisites;
        result.PrerequisiteInstallMessages = prerequisiteInstall.Messages;

        if (!result.RegistrationPerformed)
        {
            service.WriteRegistrationSummary(result);
            WriteObject(ModuleRepositoryRegistrationResultMapper.ToCmdletResult(result));
            return;
        }

        var probe = service.ProbeRepositoryAccessWithOptionalSessionPrime(
            result,
            credentialResolution.Credential,
            allowInteractiveCredentialProviderPrime: !host.IsWhatIfRequested);
        result.AccessProbePerformed = true;
        result.AccessProbeSucceeded = probe.Succeeded;
        result.AccessProbeTool = probe.Tool;
        result.AccessProbeMessage = probe.Message;

        service.WriteRegistrationSummary(result);
        var cmdletResult = ModuleRepositoryRegistrationResultMapper.ToCmdletResult(result);

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
                cmdletResult.RepositoryName));
            return;
        }

        WriteObject(cmdletResult);
    }
}
