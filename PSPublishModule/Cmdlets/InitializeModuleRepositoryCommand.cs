using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Performs one-command enterprise onboarding for a private module repository profile.
/// </summary>
/// <remarks>
/// <para>
/// This cmdlet is the managed-workstation entry point for private gallery onboarding. It can use an existing saved
/// profile, import a non-secret profile JSON file, or create an Azure Artifacts profile from feed details. Unless
/// <c>-SkipConnect</c> is used, it then installs requested prerequisites, registers the repository, and validates
/// authenticated access through the selected bootstrap mode.
/// </para>
/// </remarks>
/// <example>
/// <summary>Onboard from a managed profile file</summary>
/// <code>Initialize-ModuleRepository -Path .\Company.profile.json -ProfileName Company -Overwrite -InstallPrerequisites</code>
/// <para>Imports the non-secret profile, installs/refreshes prerequisites, registers the repository, and triggers the Azure Artifacts credential-provider login flow when needed.</para>
/// </example>
/// <example>
/// <summary>Create and connect an Entra-first Azure Artifacts profile</summary>
/// <code>Initialize-ModuleRepository -ProfileName Company -Organization contoso -Project Platform -Feed Modules -InstallPrerequisites</code>
/// <para>Saves an Entra-first profile and connects the workstation in one command.</para>
/// </example>
/// <example>
/// <summary>Verify profile metadata without connecting</summary>
/// <code>Initialize-ModuleRepository -ProfileName Company -SkipConnect</code>
/// <para>Returns profile and local prerequisite readiness without registering or probing the repository.</para>
/// </example>
[Cmdlet(VerbsData.Initialize, "ModuleRepository", DefaultParameterSetName = ParameterSetProfile, SupportsShouldProcess = true)]
[Alias("Initialize-Gallery")]
[OutputType(typeof(ModuleRepositoryOnboardingResult))]
public sealed class InitializeModuleRepositoryCommand : PSCmdlet
{
    private const string ParameterSetProfile = "Profile";
    private const string ParameterSetImport = "Import";
    private const string ParameterSetAzureArtifacts = "AzureArtifacts";

    /// <summary>Saved repository profile name. When used with Path, selects one imported profile from the file. When used with Azure Artifacts feed details, creates that profile name.</summary>
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = ParameterSetProfile)]
    [Parameter(ParameterSetName = ParameterSetImport)]
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = ParameterSetAzureArtifacts)]
    [Alias("Name", "Profile")]
    [ValidateNotNullOrEmpty]
    public string? ProfileName { get; set; }

    /// <summary>Source JSON profile file exported with Export-ModuleRepositoryProfile.</summary>
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = ParameterSetImport)]
    [ValidateNotNullOrEmpty]
    public string? Path { get; set; }

    /// <summary>Replace saved profiles with matching names when importing from Path.</summary>
    [Parameter(ParameterSetName = ParameterSetImport)]
    public SwitchParameter Overwrite { get; set; }

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

    /// <summary>Optional local repository name override. Defaults to the feed name.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    [Alias("Repository")]
    public string? RepositoryName { get; set; }

    /// <summary>Registration strategy saved in a new profile. Defaults to PSResourceGet for Entra-first Azure Artifacts use.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    public RepositoryRegistrationTool Tool { get; set; } = RepositoryRegistrationTool.PSResourceGet;

    /// <summary>Bootstrap/authentication mode saved in a new profile. Defaults to ExistingSession for Azure Artifacts Credential Provider login.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    [Alias("Mode")]
    public PrivateGalleryBootstrapMode BootstrapMode { get; set; } = PrivateGalleryBootstrapMode.ExistingSession;

    /// <summary>When true, marks the repository as trusted during registration.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    public bool Trusted { get; set; } = true;

    /// <summary>Optional PSResourceGet repository priority.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    public int? Priority { get; set; }

    /// <summary>Optional repository credential username for credential-prompt fallback environments.</summary>
    [Parameter]
    [Alias("UserName")]
    public string? CredentialUserName { get; set; }

    /// <summary>Optional repository credential secret for credential-prompt fallback environments.</summary>
    [Parameter]
    [Alias("Password", "Token")]
    public string? CredentialSecret { get; set; }

    /// <summary>Optional path to a file containing the repository credential secret.</summary>
    [Parameter]
    [Alias("CredentialPath", "TokenPath")]
    public string? CredentialSecretFilePath { get; set; }

    /// <summary>Prompts interactively for repository credentials in credential-prompt fallback environments.</summary>
    [Parameter]
    [Alias("Interactive")]
    public SwitchParameter PromptForCredential { get; set; }

    /// <summary>Installs missing private-gallery prerequisites before connecting, including the PSResourceGet version required by the selected bootstrap mode and the Azure Artifacts credential provider.</summary>
    [Parameter]
    public SwitchParameter InstallPrerequisites { get; set; }

    /// <summary>Save/import/test the profile but do not register, connect, or probe the repository.</summary>
    [Parameter]
    public SwitchParameter SkipConnect { get; set; }

    /// <summary>Runs the onboarding workflow.</summary>
    protected override void ProcessRecord()
    {
        var store = new ModuleRepositoryProfileStore();
        var host = new CmdletPrivateGalleryHost(this);
        var service = new PrivateGalleryService(host);
        var profileWriteState = ResolveProfiles(store, out var importedFromPath);
        var prerequisiteStatus = service.GetBootstrapPrerequisiteStatus();

        foreach (var state in profileWriteState)
        {
            var readiness = ModuleRepositoryProfileReadinessMapper.ToCmdletResult(
                state.Profile,
                store.Path,
                prerequisiteStatus);
            var profileResult = ModuleRepositoryProfileResultMapper.ToCmdletResult(state.Profile, store.Path);
            var result = new ModuleRepositoryOnboardingResult
            {
                ProfileName = state.Profile.Name,
                ProfileFound = true,
                ProfileWritten = state.Written,
                ProfileStorePath = store.Path,
                ImportedFromPath = importedFromPath,
                Profile = profileResult,
                Readiness = readiness,
                RecommendedInstallCommand = $"Install-PrivateModule -ProfileName '{state.Profile.Name}' -Name <ModuleName>",
                RecommendedUpdateCommand = $"Update-PrivateModule -ProfileName '{state.Profile.Name}' -Name <ModuleName>",
                Messages = BuildProfileMessages(state, readiness).ToArray()
            };

            if (SkipConnect.IsPresent)
            {
                result.ConnectSkipped = true;
                result.Succeeded = readiness.ProfileFound;
                WriteObject(result);
                continue;
            }

            result.ConnectAttempted = true;
            var connection = ConnectProfile(service, host, state.Profile);
            result.Connection = ModuleRepositoryRegistrationResultMapper.ToCmdletResult(connection);
            result.ConnectSkipped = !connection.RegistrationPerformed;
            result.Succeeded = connection.RegistrationPerformed
                ? connection.AccessProbeSucceeded
                : host.IsWhatIfRequested;
            result.Messages = result.Messages
                .Concat(connection.Messages)
                .Concat(connection.PrerequisiteInstallMessages)
                .Where(static message => !string.IsNullOrWhiteSpace(message))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            WriteObject(result);
        }
    }

    private ProfileWriteState[] ResolveProfiles(ModuleRepositoryProfileStore store, out string? importedFromPath)
    {
        importedFromPath = null;

        if (ParameterSetName == ParameterSetProfile)
        {
            var profile = ModuleRepositoryProfileCommandSupport.ResolveRequired(ProfileName!);
            return new[] { new ProfileWriteState(profile, written: false) };
        }

        if (ParameterSetName == ParameterSetAzureArtifacts)
        {
            var profile = ModuleRepositoryProfileStore.Normalize(new ModuleRepositoryProfile
            {
                Name = ProfileName!,
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

            if (!ShouldProcess(profile.Name, "Save and initialize module repository profile"))
                return new[] { new ProfileWriteState(profile, written: false) };

            var saved = store.SaveProfile(profile);
            return new[] { new ProfileWriteState(saved, written: true) };
        }

        var resolvedPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(Path!);
        importedFromPath = resolvedPath;
        var profiles = ModuleRepositoryProfileStore.ReadProfilesFile(resolvedPath);
        if (!string.IsNullOrWhiteSpace(ProfileName))
        {
            var selected = profiles
                .Where(profile => string.Equals(profile.Name, ProfileName!.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (selected.Length == 0)
                throw new InvalidOperationException($"Module repository profile '{ProfileName}' was not found in '{resolvedPath}'.");

            profiles = selected;
        }

        if (profiles.Length == 0)
            return Array.Empty<ProfileWriteState>();

        if (!ShouldProcess(store.Path, $"Import and initialize {profiles.Length} module repository profile(s) from '{resolvedPath}'"))
            return profiles.Select(static profile => new ProfileWriteState(profile, written: false)).ToArray();

        var imported = store.ImportProfiles(profiles, Overwrite);
        return imported.Select(static profile => new ProfileWriteState(profile, written: true)).ToArray();
    }

    private PowerForge.ModuleRepositoryRegistrationResult ConnectProfile(
        PrivateGalleryService service,
        CmdletPrivateGalleryHost host,
        ModuleRepositoryProfile profile)
    {
        service.EnsureProviderSupported(profile.Provider);

        var prerequisiteInstall = service.EnsureBootstrapPrerequisites(
            InstallPrerequisites.IsPresent,
            profile.BootstrapMode);
        var endpoint = AzureArtifactsRepositoryEndpoints.Create(
            profile.AzureDevOpsOrganization,
            profile.AzureDevOpsProject,
            profile.AzureArtifactsFeed,
            profile.RepositoryName);
        var credentialResolution = service.ResolveCredential(
            endpoint.RepositoryName,
            profile.BootstrapMode,
            CredentialUserName,
            CredentialSecret,
            CredentialSecretFilePath,
            PromptForCredential,
            prerequisiteInstall.Status,
            !host.IsWhatIfRequested);

        var registration = service.EnsureAzureArtifactsRepositoryRegistered(
            profile.AzureDevOpsOrganization,
            profile.AzureDevOpsProject,
            profile.AzureArtifactsFeed,
            profile.RepositoryName,
            profile.Tool,
            profile.Trusted,
            profile.Priority,
            profile.BootstrapMode,
            credentialResolution.BootstrapModeUsed,
            credentialResolution.CredentialSource,
            credentialResolution.Credential,
            prerequisiteInstall.Status,
            shouldProcessAction: profile.Tool == RepositoryRegistrationTool.Auto
                ? "Initialize module repository using Auto (prefer PSResourceGet, fall back to PowerShellGet)"
                : $"Initialize module repository using {profile.Tool}");
        registration.InstalledPrerequisites = prerequisiteInstall.InstalledPrerequisites;
        registration.PrerequisiteInstallMessages = prerequisiteInstall.Messages;

        if (!registration.RegistrationPerformed)
        {
            service.WriteRegistrationSummary(registration);
            return registration;
        }

        var probe = service.ProbeRepositoryAccess(registration, credentialResolution.Credential);
        registration.AccessProbePerformed = true;
        registration.AccessProbeSucceeded = probe.Succeeded;
        registration.AccessProbeTool = probe.Tool;
        registration.AccessProbeMessage = probe.Message;

        service.WriteRegistrationSummary(registration);
        if (!probe.Succeeded)
        {
            var hint = string.IsNullOrWhiteSpace(registration.RecommendedBootstrapCommand)
                ? string.Empty
                : $" Recommended next step: {registration.RecommendedBootstrapCommand}";
            throw new InvalidOperationException(
                $"Repository '{registration.RepositoryName}' could not be initialized via {probe.Tool}. {probe.Message}{hint}".Trim());
        }

        return registration;
    }

    private static IEnumerable<string> BuildProfileMessages(ProfileWriteState state, ModuleRepositoryProfileReadinessResult readiness)
    {
        yield return state.Written
            ? $"Profile '{state.Profile.Name}' was saved to the PSPublishModule profile store."
            : $"Profile '{state.Profile.Name}' was resolved without writing the profile store.";

        foreach (var message in readiness.ReadinessMessages)
            yield return message;
    }

    private readonly struct ProfileWriteState
    {
        internal ProfileWriteState(ModuleRepositoryProfile profile, bool written)
        {
            Profile = profile;
            Written = written;
        }

        internal ModuleRepositoryProfile Profile { get; }
        internal bool Written { get; }
    }
}
