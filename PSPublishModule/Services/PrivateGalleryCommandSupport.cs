using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Result returned when registering or refreshing a private module repository.
/// </summary>
public sealed class ModuleRepositoryRegistrationResult
{
    /// <summary>Repository name used for registration.</summary>
    public string RepositoryName { get; set; } = string.Empty;

    /// <summary>Provider used for registration.</summary>
    public string Provider { get; set; } = "AzureArtifacts";

    /// <summary>Bootstrap mode requested by the caller.</summary>
    public PrivateGalleryBootstrapMode BootstrapModeRequested { get; set; }

    /// <summary>Bootstrap mode actually used during registration.</summary>
    public PrivateGalleryBootstrapMode BootstrapModeUsed { get; set; }

    /// <summary>Source of the credential used during bootstrap.</summary>
    public PrivateGalleryCredentialSource CredentialSource { get; set; }

    /// <summary>Azure DevOps organization name.</summary>
    public string AzureDevOpsOrganization { get; set; } = string.Empty;

    /// <summary>Optional Azure DevOps project name.</summary>
    public string? AzureDevOpsProject { get; set; }

    /// <summary>Azure Artifacts feed name.</summary>
    public string AzureArtifactsFeed { get; set; } = string.Empty;

    /// <summary>Resolved PowerShellGet source URI.</summary>
    public string PowerShellGetSourceUri { get; set; } = string.Empty;

    /// <summary>Resolved PowerShellGet publish URI.</summary>
    public string PowerShellGetPublishUri { get; set; } = string.Empty;

    /// <summary>Resolved PSResourceGet URI.</summary>
    public string PSResourceGetUri { get; set; } = string.Empty;

    /// <summary>Selected repository registration tool.</summary>
    public RepositoryRegistrationTool Tool { get; set; }

    /// <summary>Repository registration strategy requested by the caller.</summary>
    public RepositoryRegistrationTool ToolRequested { get; set; }

    /// <summary>Repository registration path that completed successfully.</summary>
    public RepositoryRegistrationTool ToolUsed { get; set; }

    /// <summary>Whether PowerShellGet registration created the repository (false means it was updated).</summary>
    public bool PowerShellGetCreated { get; set; }

    /// <summary>Whether PSResourceGet registration created the repository (false means it was updated).</summary>
    public bool PSResourceGetCreated { get; set; }

    /// <summary>Whether the repository is trusted.</summary>
    public bool Trusted { get; set; }

    /// <summary>Whether a credential was supplied for registration.</summary>
    public bool CredentialUsed { get; set; }

    /// <summary>Whether the registration action was executed.</summary>
    public bool RegistrationPerformed { get; set; }

    /// <summary>Whether PSResourceGet registration completed successfully.</summary>
    public bool PSResourceGetRegistered { get; set; }

    /// <summary>Whether PowerShellGet registration completed successfully.</summary>
    public bool PowerShellGetRegistered { get; set; }

    /// <summary>Whether PSResourceGet is available locally for bootstrap/use.</summary>
    public bool PSResourceGetAvailable { get; set; }

    /// <summary>Detected PSResourceGet version when available.</summary>
    public string? PSResourceGetVersion { get; set; }

    /// <summary>Whether the detected PSResourceGet version satisfies the private-gallery minimum.</summary>
    public bool PSResourceGetMeetsMinimumVersion { get; set; }

    /// <summary>Whether the detected PSResourceGet version supports Azure Artifacts ExistingSession bootstrap.</summary>
    public bool PSResourceGetSupportsExistingSessionBootstrap { get; set; }

    /// <summary>Whether PowerShellGet is available locally for bootstrap/use.</summary>
    public bool PowerShellGetAvailable { get; set; }

    /// <summary>Detected PowerShellGet version when available.</summary>
    public string? PowerShellGetVersion { get; set; }

    /// <summary>Whether Azure Artifacts Credential Provider was detected from standard NuGet plugin locations.</summary>
    public bool AzureArtifactsCredentialProviderDetected { get; set; }

    /// <summary>Detected Azure Artifacts credential-provider file paths.</summary>
    public string[] AzureArtifactsCredentialProviderPaths { get; set; } = Array.Empty<string>();

    /// <summary>Detected Azure Artifacts credential-provider version when available.</summary>
    public string? AzureArtifactsCredentialProviderVersion { get; set; }

    /// <summary>Readiness/preflight messages collected before registration.</summary>
    public string[] ReadinessMessages { get; set; } = Array.Empty<string>();

    /// <summary>Names of prerequisites installed by the current command execution.</summary>
    public string[] InstalledPrerequisites { get; set; } = Array.Empty<string>();

    /// <summary>Messages emitted while installing prerequisites.</summary>
    public string[] PrerequisiteInstallMessages { get; set; } = Array.Empty<string>();

    /// <summary>Tool names skipped because they were not available locally.</summary>
    public string[] UnavailableTools { get; set; } = Array.Empty<string>();

    /// <summary>Non-fatal messages collected during repository registration.</summary>
    public string[] Messages { get; set; } = Array.Empty<string>();

    /// <summary>Whether an authenticated repository probe was attempted.</summary>
    public bool AccessProbePerformed { get; set; }

    /// <summary>Whether the authenticated repository probe succeeded.</summary>
    public bool AccessProbeSucceeded { get; set; }

    /// <summary>Tool used for the repository access probe.</summary>
    public string? AccessProbeTool { get; set; }

    /// <summary>Outcome message returned from the repository access probe.</summary>
    public string? AccessProbeMessage { get; set; }

    /// <summary>Whether the existing-session/device-login bootstrap path is ready for Azure Artifacts.</summary>
    public bool ExistingSessionBootstrapReady => PSResourceGetSupportsExistingSessionBootstrap && AzureArtifactsCredentialProviderDetected;

    /// <summary>Whether the credential-prompt bootstrap path is available.</summary>
    public bool CredentialPromptBootstrapReady => (PSResourceGetAvailable && PSResourceGetMeetsMinimumVersion) || PowerShellGetAvailable;

    /// <summary>Whether the repository bootstrap recommendation should include prerequisite installation.</summary>
    public bool InstallPrerequisitesRecommended
        => !PSResourceGetAvailable || !PSResourceGetMeetsMinimumVersion || !AzureArtifactsCredentialProviderDetected;

    /// <summary>Suggested bootstrap mode based on detected prerequisites.</summary>
    public PrivateGalleryBootstrapMode RecommendedBootstrapMode
        => ExistingSessionBootstrapReady
            ? PrivateGalleryBootstrapMode.ExistingSession
            : CredentialPromptBootstrapReady
                ? PrivateGalleryBootstrapMode.CredentialPrompt
                : PrivateGalleryBootstrapMode.Auto;

    /// <summary>Whether native Install-PSResource is ready to use with this repository.</summary>
    public bool InstallPSResourceReady => PSResourceGetRegistered && ExistingSessionBootstrapReady;

    /// <summary>Whether native Install-Module is ready to use with this repository.</summary>
    public bool InstallModuleReady => PowerShellGetRegistered;

    /// <summary>Names of the native commands that are ready for this repository.</summary>
    public string[] ReadyCommands
    {
        get
        {
            var ready = new List<string>(2);
            if (InstallPSResourceReady) ready.Add("Install-PSResource");
            if (InstallModuleReady) ready.Add("Install-Module");
            return ready.ToArray();
        }
    }

    /// <summary>Preferred native install command for this repository.</summary>
    public string PreferredInstallCommand
        => InstallPSResourceReady
            ? "Install-PSResource"
            : InstallModuleReady
                ? "Install-Module"
                : string.Empty;

    /// <summary>Recommended wrapper command for installing modules from this repository.</summary>
    public string RecommendedWrapperInstallCommand
        => string.IsNullOrWhiteSpace(RepositoryName)
            ? "Install-PrivateModule -Name <ModuleName>"
            : $"Install-PrivateModule -Name <ModuleName> -Repository '{RepositoryName}'";

    /// <summary>Recommended native command for installing modules from this repository.</summary>
    public string RecommendedNativeInstallCommand
    {
        get
        {
            if (string.IsNullOrWhiteSpace(RepositoryName) || string.IsNullOrWhiteSpace(PreferredInstallCommand))
                return string.Empty;

            return PreferredInstallCommand == "Install-PSResource"
                ? $"Install-PSResource -Name <ModuleName> -Repository '{RepositoryName}'"
                : $"Install-Module -Name <ModuleName> -Repository '{RepositoryName}'";
        }
    }

    /// <summary>Recommended bootstrap command based on detected prerequisites.</summary>
    public string RecommendedBootstrapCommand
    {
        get
        {
            if (string.IsNullOrWhiteSpace(AzureDevOpsOrganization) || string.IsNullOrWhiteSpace(AzureArtifactsFeed))
                return string.Empty;

            var parts = new List<string>
            {
                "Register-ModuleRepository",
                $"-AzureDevOpsOrganization '{AzureDevOpsOrganization}'"
            };

            if (!string.IsNullOrWhiteSpace(AzureDevOpsProject))
                parts.Add($"-AzureDevOpsProject '{AzureDevOpsProject}'");

            parts.Add($"-AzureArtifactsFeed '{AzureArtifactsFeed}'");

            if (!string.IsNullOrWhiteSpace(RepositoryName) &&
                !string.Equals(RepositoryName, AzureArtifactsFeed, StringComparison.OrdinalIgnoreCase))
            {
                parts.Add($"-Name '{RepositoryName}'");
            }

            if (InstallPrerequisitesRecommended)
                parts.Add("-InstallPrerequisites");

            if (RecommendedBootstrapMode == PrivateGalleryBootstrapMode.ExistingSession)
            {
                parts.Add("-BootstrapMode ExistingSession");
            }
            else if (RecommendedBootstrapMode == PrivateGalleryBootstrapMode.CredentialPrompt)
            {
                parts.Add("-BootstrapMode CredentialPrompt");
                parts.Add("-Interactive");
            }

            return string.Join(" ", parts);
        }
    }
}

internal static class PrivateGalleryCommandSupport
{
    private const string MinimumPSResourceGetVersion = "1.1.1";
    private const string MinimumPSResourceGetExistingSessionVersion = "1.2.0-preview5";
    internal const string ReservedPowerShellGalleryRepositoryName = "PSGallery";

    internal readonly record struct CredentialResolutionResult(
        RepositoryCredential? Credential,
        PrivateGalleryBootstrapMode BootstrapModeUsed,
        PrivateGalleryCredentialSource CredentialSource);

    internal readonly record struct BootstrapPrerequisiteStatus(
        bool PSResourceGetAvailable,
        string? PSResourceGetVersion,
        bool PSResourceGetMeetsMinimumVersion,
        bool PSResourceGetSupportsExistingSessionBootstrap,
        string? PSResourceGetMessage,
        bool PowerShellGetAvailable,
        string? PowerShellGetVersion,
        string? PowerShellGetMessage,
        AzureArtifactsCredentialProviderDetectionResult CredentialProviderDetection,
        string[] ReadinessMessages);

    internal readonly record struct BootstrapPrerequisiteInstallResult(
        string[] InstalledPrerequisites,
        string[] Messages,
        BootstrapPrerequisiteStatus Status);

    internal readonly record struct RepositoryAccessProbeResult(
        bool Succeeded,
        string Tool,
        string? Message);

    internal static void EnsureProviderSupported(PrivateGalleryProvider provider)
    {
        if (provider != PrivateGalleryProvider.AzureArtifacts)
            throw new PSArgumentException($"Provider '{provider}' is not supported yet. Supported value: AzureArtifacts.");
    }

    internal static CredentialResolutionResult ResolveCredential(
        PSCmdlet cmdlet,
        string repositoryName,
        PrivateGalleryBootstrapMode bootstrapMode,
        string? credentialUserName,
        string? credentialSecret,
        string? credentialSecretFilePath,
        SwitchParameter promptForCredential,
        BootstrapPrerequisiteStatus? prerequisiteStatus = null,
        bool allowInteractivePrompt = true)
    {
        var hasCredentialSecretFile = !string.IsNullOrWhiteSpace(credentialSecretFilePath);
        var hasCredentialSecret = !string.IsNullOrWhiteSpace(credentialSecret);
        var hasCredentialUser = !string.IsNullOrWhiteSpace(credentialUserName);
        var hasExplicitCredential = hasCredentialUser && (hasCredentialSecretFile || hasCredentialSecret);

        var resolvedSecret = string.Empty;
        if (hasCredentialSecretFile)
        {
            resolvedSecret = File.ReadAllText(credentialSecretFilePath!).Trim();
        }
        else if (hasCredentialSecret)
        {
            resolvedSecret = credentialSecret!.Trim();
        }

        if (!string.IsNullOrWhiteSpace(resolvedSecret) &&
            !hasCredentialUser)
        {
            throw new PSArgumentException("CredentialUserName is required when CredentialSecret/CredentialSecretFilePath is provided.");
        }

        if (promptForCredential.IsPresent)
        {
            if (!string.IsNullOrWhiteSpace(resolvedSecret) || hasCredentialUser)
                throw new PSArgumentException("PromptForCredential cannot be combined with CredentialUserName/CredentialSecret/CredentialSecretFilePath.");
        }

        if (bootstrapMode == PrivateGalleryBootstrapMode.ExistingSession &&
            (promptForCredential.IsPresent || hasExplicitCredential))
        {
            throw new PSArgumentException("BootstrapMode ExistingSession cannot be combined with interactive or explicit credential parameters.");
        }

        var effectiveMode = bootstrapMode;
        if (bootstrapMode == PrivateGalleryBootstrapMode.Auto)
        {
            var detectedPrerequisites = prerequisiteStatus ?? GetBootstrapPrerequisiteStatus();
            effectiveMode = promptForCredential.IsPresent || hasExplicitCredential
                ? PrivateGalleryBootstrapMode.CredentialPrompt
                : GetRecommendedBootstrapMode(detectedPrerequisites);

            if (effectiveMode == PrivateGalleryBootstrapMode.Auto)
            {
                if (!allowInteractivePrompt)
                    effectiveMode = PrivateGalleryBootstrapMode.CredentialPrompt;
                else
                    throw new InvalidOperationException(BuildBootstrapUnavailableMessage(repositoryName, detectedPrerequisites));
            }
        }

        if (effectiveMode == PrivateGalleryBootstrapMode.ExistingSession)
        {
            return new CredentialResolutionResult(
                Credential: null,
                BootstrapModeUsed: PrivateGalleryBootstrapMode.ExistingSession,
                CredentialSource: PrivateGalleryCredentialSource.None);
        }

        if (hasExplicitCredential)
        {
            return new CredentialResolutionResult(
                Credential: new RepositoryCredential
                {
                    UserName = credentialUserName!.Trim(),
                    Secret = resolvedSecret
                },
                BootstrapModeUsed: PrivateGalleryBootstrapMode.CredentialPrompt,
                CredentialSource: PrivateGalleryCredentialSource.Supplied);
        }

        if (!allowInteractivePrompt)
        {
            return new CredentialResolutionResult(
                Credential: null,
                BootstrapModeUsed: PrivateGalleryBootstrapMode.CredentialPrompt,
                CredentialSource: PrivateGalleryCredentialSource.None);
        }

        var caption = cmdlet.MyInvocation.MyCommand.Name;
        var message = $"Enter Azure Artifacts credentials or PAT for '{repositoryName}'.";
        var promptCredential = cmdlet.Host.UI.PromptForCredential(caption, message, string.Empty, string.Empty);
        if (promptCredential is null)
        {
            return new CredentialResolutionResult(
                Credential: null,
                BootstrapModeUsed: PrivateGalleryBootstrapMode.CredentialPrompt,
                CredentialSource: PrivateGalleryCredentialSource.None);
        }

        return new CredentialResolutionResult(
            Credential: new RepositoryCredential
            {
                UserName = promptCredential.UserName,
                Secret = promptCredential.GetNetworkCredential().Password
            },
            BootstrapModeUsed: PrivateGalleryBootstrapMode.CredentialPrompt,
            CredentialSource: PrivateGalleryCredentialSource.Prompt);
    }

    internal static RepositoryCredential? ResolveOptionalCredential(
        PSCmdlet cmdlet,
        string repositoryName,
        string? credentialUserName,
        string? credentialSecret,
        string? credentialSecretFilePath,
        SwitchParameter promptForCredential)
    {
        var hasCredentialUser = !string.IsNullOrWhiteSpace(credentialUserName);
        var hasCredentialSecret = !string.IsNullOrWhiteSpace(credentialSecret);
        var hasCredentialSecretFile = !string.IsNullOrWhiteSpace(credentialSecretFilePath);

        if (!promptForCredential.IsPresent &&
            !hasCredentialUser &&
            !hasCredentialSecret &&
            !hasCredentialSecretFile)
        {
            return null;
        }

        if (!promptForCredential.IsPresent &&
            hasCredentialUser &&
            !hasCredentialSecret &&
            !hasCredentialSecretFile)
        {
            throw new PSArgumentException("CredentialSecret/CredentialSecretFilePath or PromptForCredential is required when CredentialUserName is provided.");
        }

        return ResolveCredential(
            cmdlet,
            repositoryName,
            PrivateGalleryBootstrapMode.CredentialPrompt,
            credentialUserName,
            credentialSecret,
            credentialSecretFilePath,
            promptForCredential).Credential;
    }

    internal static ModuleRepositoryRegistrationResult EnsureAzureArtifactsRepositoryRegistered(
        PSCmdlet cmdlet,
        string azureDevOpsOrganization,
        string? azureDevOpsProject,
        string azureArtifactsFeed,
        string? repositoryName,
        RepositoryRegistrationTool tool,
        bool trusted,
        int? priority,
        PrivateGalleryBootstrapMode bootstrapModeRequested,
        PrivateGalleryBootstrapMode bootstrapModeUsed,
        PrivateGalleryCredentialSource credentialSource,
        RepositoryCredential? credential,
        BootstrapPrerequisiteStatus prerequisiteStatus,
        string shouldProcessAction)
    {
        var endpoint = AzureArtifactsRepositoryEndpoints.Create(
            azureDevOpsOrganization,
            azureDevOpsProject,
            azureArtifactsFeed,
            repositoryName);

        var effectiveTool = tool;

        var result = new ModuleRepositoryRegistrationResult
        {
            RepositoryName = endpoint.RepositoryName,
            AzureDevOpsOrganization = endpoint.Organization,
            AzureDevOpsProject = endpoint.Project,
            AzureArtifactsFeed = endpoint.Feed,
            PowerShellGetSourceUri = endpoint.PowerShellGetSourceUri,
            PowerShellGetPublishUri = endpoint.PowerShellGetPublishUri,
            PSResourceGetUri = endpoint.PSResourceGetUri,
            Trusted = trusted,
            CredentialUsed = credential is not null,
            BootstrapModeRequested = bootstrapModeRequested,
            BootstrapModeUsed = bootstrapModeUsed,
            CredentialSource = credentialSource,
            PSResourceGetAvailable = prerequisiteStatus.PSResourceGetAvailable,
            PSResourceGetVersion = prerequisiteStatus.PSResourceGetVersion,
            PSResourceGetMeetsMinimumVersion = prerequisiteStatus.PSResourceGetMeetsMinimumVersion,
            PSResourceGetSupportsExistingSessionBootstrap = prerequisiteStatus.PSResourceGetSupportsExistingSessionBootstrap,
            PowerShellGetAvailable = prerequisiteStatus.PowerShellGetAvailable,
            PowerShellGetVersion = prerequisiteStatus.PowerShellGetVersion,
            AzureArtifactsCredentialProviderDetected = prerequisiteStatus.CredentialProviderDetection.IsDetected,
            AzureArtifactsCredentialProviderPaths = prerequisiteStatus.CredentialProviderDetection.Paths,
            AzureArtifactsCredentialProviderVersion = prerequisiteStatus.CredentialProviderDetection.Version,
            ReadinessMessages = prerequisiteStatus.ReadinessMessages,
            Tool = effectiveTool,
            ToolRequested = effectiveTool,
            ToolUsed = effectiveTool
        };

        if (!cmdlet.ShouldProcess(endpoint.RepositoryName, shouldProcessAction))
            return result;

        result.RegistrationPerformed = true;
        var runner = new PowerShellRunner();
        var logger = new CmdletLogger(cmdlet, cmdlet.MyInvocation.BoundParameters.ContainsKey("Verbose"));
        var unavailableTools = new List<string>(2);
        var messages = new List<string>(4);
        var failures = new List<string>(2);

        void RegisterPowerShellGet()
        {
            try
            {
                var powerShellGet = new PowerShellGetClient(runner, logger);
                result.PowerShellGetCreated = powerShellGet.EnsureRepositoryRegistered(
                    endpoint.RepositoryName,
                    endpoint.PowerShellGetSourceUri,
                    endpoint.PowerShellGetPublishUri,
                    trusted: trusted,
                    credential: credential,
                    timeout: TimeSpan.FromMinutes(2));
                result.PowerShellGetRegistered = true;
            }
            catch (PowerShellToolNotAvailableException ex)
            {
                unavailableTools.Add("PowerShellGet");
                messages.Add(ex.Message);
            }
            catch (Exception ex)
            {
                failures.Add($"PowerShellGet registration failed: {ex.Message}");
            }
        }

        void RegisterPSResourceGet()
        {
            try
            {
                var psResourceGet = new PSResourceGetClient(runner, logger);
                result.PSResourceGetCreated = psResourceGet.EnsureRepositoryRegistered(
                    endpoint.RepositoryName,
                    endpoint.PSResourceGetUri,
                    trusted: trusted,
                    priority: priority,
                    apiVersion: RepositoryApiVersion.V3,
                    timeout: TimeSpan.FromMinutes(2));
                result.PSResourceGetRegistered = true;
            }
            catch (PowerShellToolNotAvailableException ex)
            {
                unavailableTools.Add("PSResourceGet");
                messages.Add(ex.Message);
            }
            catch (Exception ex)
            {
                failures.Add($"PSResourceGet registration failed: {ex.Message}");
            }
        }

        if (effectiveTool == RepositoryRegistrationTool.Auto)
        {
            RegisterPSResourceGet();
            if (!result.PSResourceGetRegistered)
                RegisterPowerShellGet();
        }
        else
        {
            if (effectiveTool is RepositoryRegistrationTool.PowerShellGet or RepositoryRegistrationTool.Both)
                RegisterPowerShellGet();

            if (effectiveTool is RepositoryRegistrationTool.PSResourceGet or RepositoryRegistrationTool.Both)
                RegisterPSResourceGet();
        }

        result.UnavailableTools = unavailableTools
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static tool => tool, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        result.Messages = messages
            .Concat(failures)
            .Where(static message => !string.IsNullOrWhiteSpace(message))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (!result.PSResourceGetRegistered && !result.PowerShellGetRegistered)
        {
            var message = result.Messages.Length > 0
                ? string.Join(" ", result.Messages)
                : $"No repository registration path succeeded for '{endpoint.RepositoryName}'.";
            throw new InvalidOperationException(message);
        }

        result.ToolUsed = result.PSResourceGetRegistered && result.PowerShellGetRegistered
            ? RepositoryRegistrationTool.Both
            : result.PSResourceGetRegistered
                ? RepositoryRegistrationTool.PSResourceGet
                : RepositoryRegistrationTool.PowerShellGet;

        return result;
    }

    internal static BootstrapPrerequisiteInstallResult EnsureBootstrapPrerequisites(
        PSCmdlet cmdlet,
        bool installPrerequisites,
        bool forceInstall = false)
    {
        var initialStatus = GetBootstrapPrerequisiteStatus();
        if (!installPrerequisites)
        {
            return new BootstrapPrerequisiteInstallResult(
                Array.Empty<string>(),
                Array.Empty<string>(),
                initialStatus);
        }

        var installed = new List<string>(2);
        var messages = new List<string>(4);
        var runner = new PowerShellRunner();
        var logger = new CmdletLogger(cmdlet, cmdlet.MyInvocation.BoundParameters.ContainsKey("Verbose"));

        if (!initialStatus.PSResourceGetAvailable || !initialStatus.PSResourceGetMeetsMinimumVersion || forceInstall)
        {
            if (cmdlet.ShouldProcess("Microsoft.PowerShell.PSResourceGet", "Install private-gallery prerequisite"))
            {
                var installer = new ModuleDependencyInstaller(runner, logger);
                var results = installer.EnsureInstalled(
                    new[] { new ModuleDependency("Microsoft.PowerShell.PSResourceGet", minimumVersion: "1.1.1") },
                    force: forceInstall,
                    prerelease: false,
                    timeoutPerModule: TimeSpan.FromMinutes(10));

                var result = results.FirstOrDefault();
                if (result is null || result.Status == ModuleDependencyInstallStatus.Failed)
                {
                    var failure = result?.Message ?? "PSResourceGet prerequisite installation did not return a result.";
                    throw new InvalidOperationException($"Failed to install PSResourceGet prerequisite. {failure}".Trim());
                }

                installed.Add("PSResourceGet");
                var resolvedVersion = string.IsNullOrWhiteSpace(result.ResolvedVersion) ? "unknown version" : result.ResolvedVersion;
                messages.Add($"PSResourceGet prerequisite handled via {result.Installer ?? "module installer"} ({result.Status}, resolved {resolvedVersion}).");
            }
        }

        var statusAfterPsResourceGet = GetBootstrapPrerequisiteStatus();
        if (installed.Contains("PSResourceGet", StringComparer.OrdinalIgnoreCase) &&
            (!statusAfterPsResourceGet.PSResourceGetAvailable || !statusAfterPsResourceGet.PSResourceGetMeetsMinimumVersion))
        {
            throw new InvalidOperationException(
                $"PSResourceGet prerequisite installation completed, but version {statusAfterPsResourceGet.PSResourceGetVersion ?? "unknown"} does not satisfy minimum {MinimumPSResourceGetVersion}.");
        }
        if (!statusAfterPsResourceGet.CredentialProviderDetection.IsDetected)
        {
            if (Path.DirectorySeparatorChar == '\\')
            {
                if (cmdlet.ShouldProcess("Azure Artifacts Credential Provider", "Install private-gallery prerequisite"))
                {
                    var installer = new AzureArtifactsCredentialProviderInstaller(runner, logger);
                    var result = installer.InstallForCurrentUser(includeNetFx: true, installNet8: true, force: forceInstall);
                    if (!result.Succeeded)
                        throw new InvalidOperationException("Azure Artifacts Credential Provider installation did not succeed.");

                    installed.Add("AzureArtifactsCredentialProvider");
                    messages.AddRange(result.Messages);

                    var statusAfterCredentialProvider = GetBootstrapPrerequisiteStatus();
                    if (!statusAfterCredentialProvider.CredentialProviderDetection.IsDetected)
                    {
                        throw new InvalidOperationException(
                            "Azure Artifacts Credential Provider installation completed, but the provider was still not detected afterwards.");
                    }
                }
            }
            else
            {
                messages.Add("Automatic Azure Artifacts Credential Provider installation is currently supported on Windows only.");
            }
        }

        var finalStatus = GetBootstrapPrerequisiteStatus();
        return new BootstrapPrerequisiteInstallResult(
            installed
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            messages
                .Where(static message => !string.IsNullOrWhiteSpace(message))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            finalStatus);
    }

    internal static RepositoryAccessProbeResult ProbeRepositoryAccess(
        ModuleRepositoryRegistrationResult registration,
        RepositoryCredential? credential)
    {
        if (registration is null)
            throw new ArgumentNullException(nameof(registration));

        const string probeName = "__PowerForgePrivateGalleryConnectionProbe__";
        var runner = new PowerShellRunner();
        var logger = new NullLogger();
        var tool = SelectAccessProbeTool(registration, credential);

        try
        {
            if (tool == "PSResourceGet")
            {
                var client = new PSResourceGetClient(runner, logger);
                client.Find(
                    new PSResourceFindOptions(
                        names: new[] { probeName },
                        version: null,
                        prerelease: false,
                        repositories: new[] { registration.RepositoryName },
                        credential: credential),
                    timeout: TimeSpan.FromMinutes(2));
            }
            else
            {
                var client = new PowerShellGetClient(runner, logger);
                client.Find(
                    new PowerShellGetFindOptions(
                        names: new[] { probeName },
                        prerelease: false,
                        repositories: new[] { registration.RepositoryName },
                        credential: credential),
                    timeout: TimeSpan.FromMinutes(2));
            }

            return new RepositoryAccessProbeResult(
                Succeeded: true,
                Tool: tool,
                Message: $"Repository access probe completed successfully via {tool}.");
        }
        catch (Exception ex)
        {
            return new RepositoryAccessProbeResult(
                Succeeded: false,
                Tool: tool,
                Message: ex.Message);
        }
    }

    internal static void WriteRegistrationSummary(PSCmdlet cmdlet, ModuleRepositoryRegistrationResult result)
    {
        if (cmdlet is null || result is null)
            return;

        foreach (var message in result.Messages.Where(static message => !string.IsNullOrWhiteSpace(message)))
        {
            if (result.UnavailableTools.Length > 0 &&
                result.UnavailableTools.Any(tool => message.IndexOf(tool, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                cmdlet.WriteWarning(message);
            }
            else
            {
                cmdlet.WriteVerbose(message);
            }
        }

        foreach (var message in result.ReadinessMessages.Where(static message => !string.IsNullOrWhiteSpace(message)))
        {
            cmdlet.WriteVerbose(message);
        }

        foreach (var message in result.PrerequisiteInstallMessages.Where(static message => !string.IsNullOrWhiteSpace(message)))
        {
            cmdlet.WriteVerbose(message);
        }

        var ready = result.ReadyCommands;
        if (ready.Length > 0)
        {
            cmdlet.WriteVerbose(
                $"Repository '{result.RepositoryName}' is ready for {string.Join(", ", ready)}.");
        }

        cmdlet.WriteVerbose(
            $"Bootstrap readiness: ExistingSession={result.ExistingSessionBootstrapReady}; CredentialPrompt={result.CredentialPromptBootstrapReady}.");
        if (!string.IsNullOrWhiteSpace(result.PSResourceGetVersion))
        {
            cmdlet.WriteVerbose(
                $"Detected PSResourceGet version: {result.PSResourceGetVersion} (meets minimum {MinimumPSResourceGetVersion}: {result.PSResourceGetMeetsMinimumVersion}; supports ExistingSession {MinimumPSResourceGetExistingSessionVersion}+: {result.PSResourceGetSupportsExistingSessionBootstrap}).");
        }
        if (!string.IsNullOrWhiteSpace(result.PowerShellGetVersion))
        {
            cmdlet.WriteVerbose(
                $"Detected PowerShellGet version: {result.PowerShellGetVersion}.");
        }
        if (!string.IsNullOrWhiteSpace(result.AzureArtifactsCredentialProviderVersion))
        {
            cmdlet.WriteVerbose(
                $"Detected Azure Artifacts Credential Provider version: {result.AzureArtifactsCredentialProviderVersion}.");
        }

        if (result.InstalledPrerequisites.Length > 0)
        {
            cmdlet.WriteVerbose(
                $"Installed prerequisites: {string.Join(", ", result.InstalledPrerequisites)}.");
        }

        if (result.AccessProbePerformed)
        {
            if (result.AccessProbeSucceeded)
            {
                cmdlet.WriteVerbose(result.AccessProbeMessage ?? $"Repository access probe succeeded via {result.AccessProbeTool ?? "unknown"}.");
            }
            else if (!string.IsNullOrWhiteSpace(result.AccessProbeMessage))
            {
                cmdlet.WriteWarning($"Repository access probe failed via {result.AccessProbeTool ?? "unknown"}: {result.AccessProbeMessage}");
            }
        }

        cmdlet.WriteVerbose(
            $"Bootstrap mode used: {result.BootstrapModeUsed}; credential source: {result.CredentialSource}.");

        cmdlet.WriteVerbose(
            $"Repository registration requested {result.ToolRequested}; successful path: {result.ToolUsed}.");

        if (result.ToolRequested == RepositoryRegistrationTool.Auto &&
            result.ToolUsed == RepositoryRegistrationTool.PowerShellGet)
        {
            cmdlet.WriteVerbose(
                "Auto registration fell back to PowerShellGet, so Install-Module is the current native path on this machine.");
        }

        if (result.BootstrapModeRequested == PrivateGalleryBootstrapMode.ExistingSession &&
            !result.ExistingSessionBootstrapReady)
        {
            cmdlet.WriteWarning(
                $"ExistingSession bootstrap was requested, but Azure Artifacts ExistingSession support requires PSResourceGet {MinimumPSResourceGetExistingSessionVersion}+ and a detected Azure Artifacts Credential Provider.");
        }

        if (!string.IsNullOrWhiteSpace(result.RecommendedBootstrapCommand))
        {
            cmdlet.WriteVerbose(
                $"Bootstrap recommendation: {result.RecommendedBootstrapCommand}");
        }

        if (!string.IsNullOrWhiteSpace(result.RecommendedNativeInstallCommand))
        {
            cmdlet.WriteVerbose(
                $"Native install example: {result.RecommendedNativeInstallCommand}");
        }

        cmdlet.WriteVerbose(
            $"Wrapper install example: {result.RecommendedWrapperInstallCommand}");
    }

    internal static IReadOnlyList<ModuleDependency> BuildDependencies(IEnumerable<string> names)
    {
        var dependencies = (names ?? Array.Empty<string>())
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static name => new ModuleDependency(name))
            .ToArray();

        if (dependencies.Length == 0)
            throw new PSArgumentException("At least one module name must be provided.");

        return dependencies;
    }

    internal static BootstrapPrerequisiteStatus GetBootstrapPrerequisiteStatus()
    {
        var runner = new PowerShellRunner();
        var logger = new NullLogger();
        var psResourceGet = new PSResourceGetClient(runner, logger);
        var powerShellGet = new PowerShellGetClient(runner, logger);

        var psResourceGetAvailability = psResourceGet.GetAvailability();
        var powerShellGetAvailability = powerShellGet.GetAvailability();
        var psResourceGetAvailable = psResourceGetAvailability.Available;
        var psResourceGetMessage = psResourceGetAvailability.Message;
        var powerShellGetAvailable = powerShellGetAvailability.Available;
        var powerShellGetMessage = powerShellGetAvailability.Message;
        var credentialProviderDetection = AzureArtifactsCredentialProviderLocator.Detect();
        var psResourceGetMeetsMinimumVersion = VersionMeetsMinimum(psResourceGetAvailability.Version, MinimumPSResourceGetVersion);
        var psResourceGetSupportsExistingSessionBootstrap = VersionMeetsMinimum(psResourceGetAvailability.Version, MinimumPSResourceGetExistingSessionVersion);

        var readinessMessages = new List<string>(6);
        if (psResourceGetAvailable)
        {
            if (psResourceGetMeetsMinimumVersion)
            {
                readinessMessages.Add($"PSResourceGet is available for private-gallery bootstrap (version {psResourceGetAvailability.Version ?? "unknown"}).");
                if (!psResourceGetSupportsExistingSessionBootstrap)
                {
                    readinessMessages.Add(
                        $"PSResourceGet version {psResourceGetAvailability.Version ?? "unknown"} supports credential-prompt installs, but Azure Artifacts ExistingSession bootstrap requires {MinimumPSResourceGetExistingSessionVersion} or newer.");
                }
            }
            else
            {
                readinessMessages.Add($"PSResourceGet is installed, but version {psResourceGetAvailability.Version ?? "unknown"} is below the private-gallery minimum {MinimumPSResourceGetVersion}.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(psResourceGetMessage))
        {
            readinessMessages.Add(psResourceGetMessage!);
        }

        if (powerShellGetAvailable)
        {
            readinessMessages.Add($"PowerShellGet is available for compatibility/fallback registration (version {powerShellGetAvailability.Version ?? "unknown"}).");
        }
        else if (!string.IsNullOrWhiteSpace(powerShellGetMessage))
        {
            readinessMessages.Add(powerShellGetMessage!);
        }

        if (credentialProviderDetection.IsDetected)
        {
            readinessMessages.Add(
                $"Azure Artifacts Credential Provider detected ({credentialProviderDetection.Paths.Length} path(s), version {credentialProviderDetection.Version ?? "unknown"}).");
        }
        else
        {
            readinessMessages.Add(
                "Azure Artifacts Credential Provider was not detected in NUGET_PLUGIN_PATHS, %UserProfile%\\.nuget\\plugins, or Visual Studio NuGet plugin locations.");
        }

        return new BootstrapPrerequisiteStatus(
            psResourceGetAvailable,
            psResourceGetAvailability.Version,
            psResourceGetMeetsMinimumVersion,
            psResourceGetSupportsExistingSessionBootstrap,
            psResourceGetMessage,
            powerShellGetAvailable,
            powerShellGetAvailability.Version,
            powerShellGetMessage,
            credentialProviderDetection,
            readinessMessages.ToArray());
    }

    private static PrivateGalleryBootstrapMode GetRecommendedBootstrapMode(BootstrapPrerequisiteStatus status)
        => IsExistingSessionBootstrapReady(status)
            ? PrivateGalleryBootstrapMode.ExistingSession
            : IsCredentialPromptBootstrapReady(status)
                ? PrivateGalleryBootstrapMode.CredentialPrompt
                : PrivateGalleryBootstrapMode.Auto;

    private static string BuildBootstrapUnavailableMessage(string repositoryName, BootstrapPrerequisiteStatus status)
    {
        var message = $"No supported private-gallery bootstrap path is ready for repository '{repositoryName}'.";
        var reasons = status.ReadinessMessages
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (reasons.Length > 0)
            message += " " + string.Join(" ", reasons);

        message += " Install prerequisites with -InstallPrerequisites or ensure PowerShellGet/PSResourceGet availability before retrying.";
        return message;
    }

    private static bool IsExistingSessionBootstrapReady(BootstrapPrerequisiteStatus status)
        => status.PSResourceGetSupportsExistingSessionBootstrap && status.CredentialProviderDetection.IsDetected;

    private static bool IsCredentialPromptBootstrapReady(BootstrapPrerequisiteStatus status)
        => (status.PSResourceGetAvailable && status.PSResourceGetMeetsMinimumVersion) || status.PowerShellGetAvailable;

    private static string SelectAccessProbeTool(ModuleRepositoryRegistrationResult registration, RepositoryCredential? credential)
    {
        if (credential is null)
        {
            if (registration.InstallPSResourceReady)
                return "PSResourceGet";
            if (registration.InstallModuleReady)
                return "PowerShellGet";

            throw new InvalidOperationException(
                $"Repository '{registration.RepositoryName}' does not currently have a native authenticated access path. {registration.RecommendedBootstrapCommand}".Trim());
        }

        if (registration.PSResourceGetRegistered)
            return "PSResourceGet";
        if (registration.PowerShellGetRegistered)
            return "PowerShellGet";

        throw new InvalidOperationException(
            $"Repository '{registration.RepositoryName}' is not registered for PSResourceGet or PowerShellGet.");
    }

    internal static bool VersionMeetsMinimum(string? versionText, string minimumVersion)
    {
        if (string.IsNullOrWhiteSpace(versionText) || string.IsNullOrWhiteSpace(minimumVersion))
            return false;

        return TryParseVersionStamp(versionText, out var version) &&
               TryParseVersionStamp(minimumVersion, out var minimum) &&
               CompareVersionStamps(version, minimum) >= 0;
    }

    private static bool TryParseVersionStamp(string? versionText, out (Version Version, string[] PreRelease) version)
    {
        if (string.IsNullOrWhiteSpace(versionText))
        {
            version = (new Version(0, 0), Array.Empty<string>());
            return false;
        }

        var raw = versionText!.Trim();
        var plusIndex = raw.IndexOf('+');
        if (plusIndex >= 0)
            raw = raw.Substring(0, plusIndex);

        string[] preRelease = Array.Empty<string>();
        var dashIndex = raw.IndexOf('-');
        if (dashIndex >= 0)
        {
            preRelease = raw.Substring(dashIndex + 1)
                .Split(new[] { '.', '-' }, StringSplitOptions.RemoveEmptyEntries);
            raw = raw.Substring(0, dashIndex);
        }

        if (Version.TryParse(raw, out var parsed) && parsed is not null)
        {
            version = (parsed, preRelease);
            return true;
        }

        version = (new Version(0, 0), Array.Empty<string>());
        return false;
    }

    private static int CompareVersionStamps((Version Version, string[] PreRelease) left, (Version Version, string[] PreRelease) right)
    {
        var versionCompare = left.Version.CompareTo(right.Version);
        if (versionCompare != 0)
            return versionCompare;

        var leftHasPreRelease = left.PreRelease.Length > 0;
        var rightHasPreRelease = right.PreRelease.Length > 0;
        if (!leftHasPreRelease && !rightHasPreRelease)
            return 0;
        if (!leftHasPreRelease)
            return 1;
        if (!rightHasPreRelease)
            return -1;

        var count = Math.Max(left.PreRelease.Length, right.PreRelease.Length);
        for (var index = 0; index < count; index++)
        {
            if (index >= left.PreRelease.Length)
                return -1;
            if (index >= right.PreRelease.Length)
                return 1;

            var segmentCompare = ComparePreReleaseSegment(left.PreRelease[index], right.PreRelease[index]);
            if (segmentCompare != 0)
                return segmentCompare;
        }

        return 0;
    }

    private static int ComparePreReleaseSegment(string left, string right)
    {
        if (TrySplitAlphaNumeric(left, out var leftPrefix, out var leftNumber) &&
            TrySplitAlphaNumeric(right, out var rightPrefix, out var rightNumber))
        {
            var prefixCompare = string.Compare(leftPrefix, rightPrefix, StringComparison.OrdinalIgnoreCase);
            if (prefixCompare != 0)
                return prefixCompare;

            return leftNumber.CompareTo(rightNumber);
        }

        var leftIsNumeric = int.TryParse(left, out var leftNumeric);
        var rightIsNumeric = int.TryParse(right, out var rightNumeric);
        if (leftIsNumeric && rightIsNumeric)
            return leftNumeric.CompareTo(rightNumeric);
        if (leftIsNumeric)
            return -1;
        if (rightIsNumeric)
            return 1;

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TrySplitAlphaNumeric(string value, out string prefix, out int number)
    {
        prefix = string.Empty;
        number = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var index = value.Length;
        while (index > 0 && char.IsDigit(value[index - 1]))
            index--;

        if (index <= 0 || index >= value.Length)
            return false;

        prefix = value.Substring(0, index);
        return int.TryParse(value.Substring(index), out number);
    }
}
