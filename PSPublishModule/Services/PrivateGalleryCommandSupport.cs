using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

internal static partial class PrivateGalleryCommandSupport
{
    private const string MinimumPSResourceGetVersion = "1.1.1";
    private const string MinimumPSResourceGetExistingSessionVersion = "1.2.0-preview5";
    internal const string ReservedPowerShellGalleryRepositoryName = "PSGallery";

    internal readonly struct CredentialResolutionResult
    {
        internal CredentialResolutionResult(
            RepositoryCredential? Credential,
            PrivateGalleryBootstrapMode BootstrapModeUsed,
            PrivateGalleryCredentialSource CredentialSource)
        {
            this.Credential = Credential;
            this.BootstrapModeUsed = BootstrapModeUsed;
            this.CredentialSource = CredentialSource;
        }

        internal RepositoryCredential? Credential { get; }
        internal PrivateGalleryBootstrapMode BootstrapModeUsed { get; }
        internal PrivateGalleryCredentialSource CredentialSource { get; }
    }

    internal readonly struct BootstrapPrerequisiteStatus
    {
        internal BootstrapPrerequisiteStatus(
            bool PSResourceGetAvailable,
            string? PSResourceGetVersion,
            bool PSResourceGetMeetsMinimumVersion,
            bool PSResourceGetSupportsExistingSessionBootstrap,
            string? PSResourceGetMessage,
            bool PowerShellGetAvailable,
            string? PowerShellGetVersion,
            string? PowerShellGetMessage,
            AzureArtifactsCredentialProviderDetectionResult CredentialProviderDetection,
            string[] ReadinessMessages)
        {
            this.PSResourceGetAvailable = PSResourceGetAvailable;
            this.PSResourceGetVersion = PSResourceGetVersion;
            this.PSResourceGetMeetsMinimumVersion = PSResourceGetMeetsMinimumVersion;
            this.PSResourceGetSupportsExistingSessionBootstrap = PSResourceGetSupportsExistingSessionBootstrap;
            this.PSResourceGetMessage = PSResourceGetMessage;
            this.PowerShellGetAvailable = PowerShellGetAvailable;
            this.PowerShellGetVersion = PowerShellGetVersion;
            this.PowerShellGetMessage = PowerShellGetMessage;
            this.CredentialProviderDetection = CredentialProviderDetection;
            this.ReadinessMessages = ReadinessMessages;
        }

        internal bool PSResourceGetAvailable { get; }
        internal string? PSResourceGetVersion { get; }
        internal bool PSResourceGetMeetsMinimumVersion { get; }
        internal bool PSResourceGetSupportsExistingSessionBootstrap { get; }
        internal string? PSResourceGetMessage { get; }
        internal bool PowerShellGetAvailable { get; }
        internal string? PowerShellGetVersion { get; }
        internal string? PowerShellGetMessage { get; }
        internal AzureArtifactsCredentialProviderDetectionResult CredentialProviderDetection { get; }
        internal string[] ReadinessMessages { get; }
    }

    internal readonly struct BootstrapPrerequisiteInstallResult
    {
        internal BootstrapPrerequisiteInstallResult(
            string[] InstalledPrerequisites,
            string[] Messages,
            BootstrapPrerequisiteStatus Status)
        {
            this.InstalledPrerequisites = InstalledPrerequisites;
            this.Messages = Messages;
            this.Status = Status;
        }

        internal string[] InstalledPrerequisites { get; }
        internal string[] Messages { get; }
        internal BootstrapPrerequisiteStatus Status { get; }
    }

    internal readonly struct RepositoryAccessProbeResult
    {
        internal RepositoryAccessProbeResult(bool Succeeded, string Tool, string? Message)
        {
            this.Succeeded = Succeeded;
            this.Tool = Tool;
            this.Message = Message;
        }

        internal bool Succeeded { get; }
        internal string Tool { get; }
        internal string? Message { get; }
    }

    internal static void EnsureProviderSupported(PrivateGalleryProvider provider)
    {
        if (provider != PrivateGalleryProvider.AzureArtifacts)
            throw new PSArgumentException($"Provider '{provider}' is not supported yet. Supported value: AzureArtifacts.");
    }

    internal static bool IsWhatIfRequested(PSCmdlet cmdlet)
    {
        return cmdlet.MyInvocation.BoundParameters.TryGetValue("WhatIf", out var whatIfValue) &&
               whatIfValue is SwitchParameter switchParameter &&
               switchParameter.IsPresent;
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

}
