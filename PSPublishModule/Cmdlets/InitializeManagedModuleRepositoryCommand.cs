using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Performs one-command onboarding for managed module repository profiles.
/// </summary>
/// <remarks>
/// <para>
/// This cmdlet is the workstation or build-agent onboarding entry point for managed module repositories. It can use an
/// existing saved profile, import a non-secret profile JSON file, or create a profile from feed details. Unless
/// <c>-SkipConnect</c> is used, it then installs requested prerequisites, registers/probes native provider state where
/// needed, and validates authenticated access through the selected bootstrap mode. It can also write a distributable
/// non-secret bootstrap package for other machines.
/// </para>
/// </remarks>
/// <example>
/// <summary>Onboard from a managed profile file</summary>
/// <code>Initialize-ManagedModuleRepository -Path .\Company.repository.json -ProfileName Company -Overwrite -InstallPrerequisites</code>
/// <para>Imports the non-secret profile, installs/refreshes prerequisites, registers the repository, and triggers the Azure Artifacts credential-provider login flow when needed.</para>
/// </example>
/// <example>
/// <summary>Create and connect an Entra-first Azure Artifacts profile</summary>
/// <code>Initialize-ManagedModuleRepository -ProfileName Company -Organization contoso -Project Platform -Feed Modules -InstallPrerequisites</code>
/// <para>Saves an Entra-first profile and connects the workstation in one command.</para>
/// </example>
/// <example>
/// <summary>Verify profile metadata without connecting</summary>
/// <code>Initialize-ManagedModuleRepository -ProfileName Company -SkipConnect</code>
/// <para>Returns profile and local prerequisite readiness without registering or probing the repository.</para>
/// </example>
[Cmdlet(VerbsData.Initialize, "ManagedModuleRepository", DefaultParameterSetName = ParameterSetProfile, SupportsShouldProcess = true)]
[OutputType(typeof(ModuleRepositoryOnboardingResult))]
[OutputType(typeof(ModuleRepositoryRegistrationResult))]
public sealed class InitializeManagedModuleRepositoryCommand : PSCmdlet
{
    private const string ParameterSetProfile = "Profile";
    private const string ParameterSetImport = "Import";
    private const string ParameterSetRepository = "Repository";
    private const string ParameterSetMicrosoftArtifactRegistry = "MicrosoftArtifactRegistry";

    /// <summary>Saved repository profile name. When used with Path, selects one imported profile from the file. When used with Azure Artifacts feed details, creates that profile name.</summary>
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = ParameterSetProfile)]
    [Parameter(ParameterSetName = ParameterSetImport)]
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = ParameterSetRepository)]
    [Alias("Name", "Profile")]
    [ValidateNotNullOrEmpty]
    public string? ProfileName { get; set; }

    /// <summary>Source JSON profile file exported with Get-ManagedModuleRepository -ExportPath.</summary>
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = ParameterSetImport)]
    [ValidateNotNullOrEmpty]
    public string? Path { get; set; }

    /// <summary>Replace saved profiles with matching names when importing from Path.</summary>
    [Parameter(ParameterSetName = ParameterSetImport)]
    public SwitchParameter Overwrite { get; set; }

    /// <summary>Private gallery provider.</summary>
    [Parameter(ParameterSetName = ParameterSetRepository)]
    public PrivateGalleryProvider Provider { get; set; } = PrivateGalleryProvider.AzureArtifacts;

    /// <summary>Initializes the public Microsoft Artifact Registry PowerShell repository.</summary>
    [Parameter(Mandatory = true, ParameterSetName = ParameterSetMicrosoftArtifactRegistry)]
    public SwitchParameter MicrosoftArtifactRegistry { get; set; }

    /// <summary>Azure DevOps organization name.</summary>
    [Parameter(ParameterSetName = ParameterSetRepository)]
    [Alias("Organization")]
    [ValidateNotNullOrEmpty]
    public string AzureDevOpsOrganization { get; set; } = string.Empty;

    /// <summary>Optional Azure DevOps project name for project-scoped feeds.</summary>
    [Parameter(ParameterSetName = ParameterSetRepository)]
    [Alias("Project")]
    public string? AzureDevOpsProject { get; set; }

    /// <summary>Azure Artifacts feed name.</summary>
    [Parameter(ParameterSetName = ParameterSetRepository)]
    [Alias("Feed")]
    [ValidateNotNullOrEmpty]
    public string AzureArtifactsFeed { get; set; } = string.Empty;

    /// <summary>Provider repository/feed id. For Azure this is the feed when AzureArtifactsFeed is omitted; for JFrog this is the Artifactory NuGet repository key.</summary>
    [Parameter(ParameterSetName = ParameterSetRepository)]
    public string? Repository { get; set; }

    /// <summary>Optional local repository name override. Defaults to the profile name for new Azure Artifacts profiles.</summary>
    [Parameter(ParameterSetName = ParameterSetRepository)]
    [Parameter(ParameterSetName = ParameterSetMicrosoftArtifactRegistry)]
    public string? RepositoryName { get; set; }

    /// <summary>PSResourceGet v3 repository URI for generic/JFrog feeds.</summary>
    [Parameter(ParameterSetName = ParameterSetRepository)]
    public string? RepositoryUri { get; set; }

    /// <summary>PowerShellGet source URI for generic/JFrog feeds.</summary>
    [Parameter(ParameterSetName = ParameterSetRepository)]
    public string? RepositorySourceUri { get; set; }

    /// <summary>PowerShellGet publish URI for generic/JFrog feeds.</summary>
    [Parameter(ParameterSetName = ParameterSetRepository)]
    public string? RepositoryPublishUri { get; set; }

    /// <summary>JFrog Artifactory base URI, for example https://company.jfrog.io/artifactory.</summary>
    [Parameter(ParameterSetName = ParameterSetRepository)]
    public string? JFrogBaseUri { get; set; }

    /// <summary>JFrog NuGet repository key. Defaults from Repository when omitted.</summary>
    [Parameter(ParameterSetName = ParameterSetRepository)]
    public string? JFrogRepository { get; set; }

    /// <summary>GitHub user or organization namespace for GitHub Packages. Defaults from Repository when omitted.</summary>
    [Parameter(ParameterSetName = ParameterSetRepository)]
    [Alias("Owner", "Namespace")]
    public string? GitHubOwner { get; set; }

    /// <summary>Registration strategy saved in a new profile. Defaults to PSResourceGet for Entra-first Azure Artifacts use.</summary>
    [Parameter(ParameterSetName = ParameterSetRepository)]
    [Parameter(ParameterSetName = ParameterSetMicrosoftArtifactRegistry)]
    public RepositoryRegistrationTool Tool { get; set; } = RepositoryRegistrationTool.PSResourceGet;

    /// <summary>Bootstrap/authentication mode saved in a new profile. Defaults to ExistingSession for Azure Artifacts Credential Provider login.</summary>
    [Parameter(ParameterSetName = ParameterSetRepository)]
    [Alias("Mode")]
    public PrivateGalleryBootstrapMode BootstrapMode { get; set; } = PrivateGalleryBootstrapMode.ExistingSession;

    /// <summary>When true, marks the repository as trusted during registration.</summary>
    [Parameter(ParameterSetName = ParameterSetRepository)]
    [Parameter(ParameterSetName = ParameterSetMicrosoftArtifactRegistry)]
    public bool Trusted { get; set; } = true;

    /// <summary>Optional PSResourceGet repository priority.</summary>
    [Parameter(ParameterSetName = ParameterSetRepository)]
    [Parameter(ParameterSetName = ParameterSetMicrosoftArtifactRegistry)]
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

    /// <summary>Optional output directory for a non-secret onboarding package that imports the selected profiles and can install starter modules.</summary>
    [Parameter]
    public string? BootstrapPath { get; set; }

    /// <summary>Generated bootstrap script file name when BootstrapPath is used.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string BootstrapScriptName { get; set; } = "Initialize-ManagedModuleRepository.ps1";

    /// <summary>Generated profile JSON file name when BootstrapPath is used.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string BootstrapProfileFileName { get; set; } = "repositories.json";

    /// <summary>Optional module names written into the bootstrap script as starter managed installs.</summary>
    [Parameter]
    [Alias("ModuleName")]
    public string[]? InstallModule { get; set; }

    /// <summary>Overwrite existing bootstrap files when BootstrapPath is used.</summary>
    [Parameter]
    public SwitchParameter BootstrapForce { get; set; }

    /// <summary>Save/import/test the profile but do not register, connect, or probe the repository.</summary>
    [Parameter]
    public SwitchParameter SkipConnect { get; set; }

    /// <summary>Profile store scope. Existing profiles default to user-then-machine lookup; profile creation/import defaults to the current user's store unless Machine is specified.</summary>
    [Parameter]
    public ModuleRepositoryProfileScope Scope { get; set; } = ModuleRepositoryProfileScope.User;

    /// <summary>Runs the onboarding workflow.</summary>
    protected override void ProcessRecord()
    {
        var host = new CmdletPrivateGalleryHost(this);
        var service = new PrivateGalleryService(host);

        if (ParameterSetName == ParameterSetMicrosoftArtifactRegistry)
        {
            InitializeMicrosoftArtifactRegistry(service);
            return;
        }

        var profileWriteState = ResolveProfiles(out var importedFromPath);
        var prerequisiteStatus = service.GetBootstrapPrerequisiteStatus();

        WriteBootstrapPackageIfRequested(profileWriteState);

        foreach (var state in profileWriteState)
        {
            var readiness = ModuleRepositoryProfileReadinessMapper.ToCmdletResult(
                state.Profile,
                state.Store.Path,
                prerequisiteStatus,
                state.Store.Scope);
            var profileResult = ModuleRepositoryProfileResultMapper.ToCmdletResult(state.Profile, state.Store.Path, state.Store.Scope);
            var result = new ModuleRepositoryOnboardingResult
            {
                ProfileName = state.Profile.Name,
                ProfileFound = true,
                ProfileWritten = state.Written,
                ProfileStorePath = state.Store.Path,
                Scope = state.Store.Scope,
                ImportedFromPath = importedFromPath,
                Profile = profileResult,
                Readiness = readiness,
                RecommendedInstallCommand = $"Install-ManagedModule -ProfileName '{state.Profile.Name}' -Name <ModuleName>",
                RecommendedUpdateCommand = $"Update-ManagedModule -ProfileName '{state.Profile.Name}' -Name <ModuleName>",
                Messages = BuildProfileMessages(state, readiness).ToArray()
            };

            if (SkipConnect.IsPresent || !state.ShouldConnect)
            {
                result.ConnectSkipped = true;
                result.Succeeded = readiness.ProfileFound;
                WriteObject(result);
                continue;
            }

            result.ConnectAttempted = true;
            var connection = ConnectProfile(service, host, state.Profile);
            result.Connection = ModuleRepositoryRegistrationResultMapper.ToCmdletResult(connection);
            var refreshedStatus = service.GetBootstrapPrerequisiteStatus();
            var refreshedReadiness = ModuleRepositoryProfileReadinessMapper.ToCmdletResult(
                state.Profile,
                state.Store.Path,
                refreshedStatus,
                state.Store.Scope);
            result.Readiness = refreshedReadiness;
            result.ConnectSkipped = !connection.RegistrationPerformed;
            result.Succeeded = connection.RegistrationPerformed
                ? connection.AccessProbeSucceeded
                : host.IsWhatIfRequested;
            result.Messages = result.Messages
                .Concat(connection.Messages)
                .Concat(connection.PrerequisiteInstallMessages)
                .Concat(refreshedReadiness.ReadinessMessages)
                .Where(static message => !string.IsNullOrWhiteSpace(message))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            WriteObject(result);
        }
    }

    private ProfileWriteState[] ResolveProfiles(out string? importedFromPath)
    {
        importedFromPath = null;
        var scopeWasBound = MyInvocation.BoundParameters.ContainsKey(nameof(Scope));

        if (ParameterSetName == ParameterSetProfile)
        {
            var resolvedScope = scopeWasBound ? Scope : ModuleRepositoryProfileScope.All;
            var resolved = ModuleRepositoryProfileCommandSupport.ResolveRequiredWithStore(ProfileName!, resolvedScope);
            return new[] { new ProfileWriteState(resolved.Profile, resolved.Store, written: false, shouldConnect: true) };
        }

        if (Scope == ModuleRepositoryProfileScope.All)
            throw new ArgumentException("Initialize-ManagedModuleRepository requires User or Machine scope when creating or importing profiles.", nameof(Scope));

        var store = new ModuleRepositoryProfileStore(Scope);

        if (ParameterSetName == ParameterSetRepository)
        {
            ValidateRepositoryProfile();
            var azureArtifactsFeed = ResolveAzureArtifactsFeed();
            var profile = ModuleRepositoryProfileStore.Normalize(new ModuleRepositoryProfile
            {
                Name = ProfileName!,
                Provider = Provider,
                AzureDevOpsOrganization = AzureDevOpsOrganization,
                AzureDevOpsProject = AzureDevOpsProject,
                AzureArtifactsFeed = azureArtifactsFeed,
                Repository = Repository ?? string.Empty,
                RepositoryName = RepositoryName ?? ProfileName ?? string.Empty,
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

            if (!ShouldProcess(profile.Name, "Save and initialize module repository profile"))
                return new[] { new ProfileWriteState(profile, store, written: false, shouldConnect: false) };

            var saved = store.SaveProfile(profile);
            return new[] { new ProfileWriteState(saved, store, written: true, shouldConnect: true) };
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
            return profiles.Select(profile => new ProfileWriteState(profile, store, written: false, shouldConnect: false)).ToArray();

        var imported = store.ImportProfiles(profiles, Overwrite);
        return imported.Select(profile => new ProfileWriteState(profile, store, written: true, shouldConnect: true)).ToArray();
    }

    private void InitializeMicrosoftArtifactRegistry(PrivateGalleryService service)
    {
        var prerequisiteInstall = service.EnsureMicrosoftArtifactRegistryPrerequisites(InstallPrerequisites.IsPresent);
        var registration = service.EnsureMicrosoftArtifactRegistryRegistered(
            RepositoryName,
            Tool,
            Trusted,
            Priority,
            prerequisiteInstall.Status,
            shouldProcessAction: "Initialize Microsoft Artifact Registry repository using PSResourceGet");
        registration.InstalledPrerequisites = prerequisiteInstall.InstalledPrerequisites;
        registration.PrerequisiteInstallMessages = prerequisiteInstall.Messages;
        service.WriteRegistrationSummary(registration);
        WriteObject(ModuleRepositoryRegistrationResultMapper.ToCmdletResult(registration));
    }

    private void ValidateRepositoryProfile()
    {
        if (Provider == PrivateGalleryProvider.AzureArtifacts &&
            (string.IsNullOrWhiteSpace(AzureDevOpsOrganization) || string.IsNullOrWhiteSpace(ResolveAzureArtifactsFeed())))
        {
            throw new ArgumentException("Azure Artifacts managed module repositories require AzureDevOpsOrganization and AzureArtifactsFeed or Repository.");
        }

        if (Provider == PrivateGalleryProvider.JFrog &&
            string.IsNullOrWhiteSpace(JFrogBaseUri) &&
            string.IsNullOrWhiteSpace(RepositoryUri))
        {
            throw new ArgumentException("JFrog managed module repositories require JFrogBaseUri or RepositoryUri.");
        }

        if (Provider == PrivateGalleryProvider.NuGet &&
            string.IsNullOrWhiteSpace(RepositoryUri) &&
            string.IsNullOrWhiteSpace(RepositorySourceUri))
        {
            throw new ArgumentException("NuGet managed module repositories require RepositoryUri or RepositorySourceUri.");
        }

        if (Provider == PrivateGalleryProvider.GitHubPackages &&
            string.IsNullOrWhiteSpace(GitHubOwner) &&
            string.IsNullOrWhiteSpace(Repository))
        {
            throw new ArgumentException("GitHub Packages managed module repositories require GitHubOwner or Repository owner metadata.");
        }
    }

    private string ResolveAzureArtifactsFeed()
        => !string.IsNullOrWhiteSpace(AzureArtifactsFeed)
            ? AzureArtifactsFeed
            : Repository ?? string.Empty;

    private void WriteBootstrapPackageIfRequested(ProfileWriteState[] profileWriteState)
    {
        if (string.IsNullOrWhiteSpace(BootstrapPath))
            return;

        var profiles = profileWriteState
            .Select(static state => state.Profile)
            .ToArray();
        if (profiles.Length == 0)
            return;

        var resolvedOutputDirectory = SessionState.Path.GetUnresolvedProviderPathFromPSPath(BootstrapPath!);
        if (!ShouldProcess(resolvedOutputDirectory, $"Create managed module repository bootstrap package for {profiles.Length} profile(s)"))
            return;

        var package = ModuleRepositoryBootstrapScriptBuilder.WritePackage(new ModuleRepositoryBootstrapScriptOptions
        {
            OutputDirectory = resolvedOutputDirectory,
            ScriptName = BootstrapScriptName,
            ProfileFileName = BootstrapProfileFileName,
            Profiles = profiles,
            InstallModules = InstallModule ?? Array.Empty<string>(),
            Force = BootstrapForce.IsPresent
        });

        WriteObject(package);
    }

    private PowerForge.ModuleRepositoryRegistrationResult ConnectProfile(
        PrivateGalleryService service,
        CmdletPrivateGalleryHost host,
        ModuleRepositoryProfile profile)
    {
        service.EnsureProviderSupported(profile.Provider);

        var endpoint = PrivateGalleryRepositoryEndpoints.Create(
            profile.Provider,
            profile.AzureDevOpsOrganization,
            profile.AzureDevOpsProject,
            profile.AzureArtifactsFeed,
            profile.RepositoryName,
            profile.Repository,
            profile.RepositoryUri,
            profile.RepositorySourceUri,
            profile.RepositoryPublishUri,
            profile.JFrogBaseUri,
            profile.JFrogRepository,
            profile.GitHubOwner);
        var prerequisiteInstall = service.EnsureBootstrapPrerequisites(
            InstallPrerequisites.IsPresent,
            profile.BootstrapMode,
            includeAzureArtifactsCredentialProvider: profile.Provider == PrivateGalleryProvider.AzureArtifacts,
            artefactsRepositoryName: endpoint.RepositoryName,
            artefactsPSResourceGetUri: endpoint.PSResourceGetUri,
            artefactsPowerShellGetSourceUri: endpoint.PowerShellGetSourceUri);
        var credentialResolution = service.ResolveCredential(
            endpoint.RepositoryName,
            profile.BootstrapMode,
            CredentialUserName,
            CredentialSecret,
            CredentialSecretFilePath,
            PromptForCredential,
            prerequisiteInstall.Status,
            !host.IsWhatIfRequested,
            endpoint.Provider);

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
                : $"Initialize module repository using {profile.Tool}",
            provider: profile.Provider,
            repository: profile.Repository,
            repositoryUri: profile.RepositoryUri,
            repositorySourceUri: profile.RepositorySourceUri,
            repositoryPublishUri: profile.RepositoryPublishUri,
            jfrogBaseUri: profile.JFrogBaseUri,
            jfrogRepository: profile.JFrogRepository);
        registration.InstalledPrerequisites = prerequisiteInstall.InstalledPrerequisites;
        registration.PrerequisiteInstallMessages = prerequisiteInstall.Messages;

        if (!registration.RegistrationPerformed)
        {
            service.WriteRegistrationSummary(registration);
            return registration;
        }

        var probe = service.ProbeRepositoryAccessWithOptionalSessionPrime(
            registration,
            credentialResolution.Credential,
            allowInteractiveCredentialProviderPrime: !host.IsWhatIfRequested);
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
        internal ProfileWriteState(ModuleRepositoryProfile profile, ModuleRepositoryProfileStore store, bool written, bool shouldConnect)
        {
            Profile = profile;
            Store = store;
            Written = written;
            ShouldConnect = shouldConnect;
        }

        internal ModuleRepositoryProfile Profile { get; }
        internal ModuleRepositoryProfileStore Store { get; }
        internal bool Written { get; }
        internal bool ShouldConnect { get; }
    }
}
