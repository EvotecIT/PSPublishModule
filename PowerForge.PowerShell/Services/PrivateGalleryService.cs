using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

internal sealed class PrivateGalleryService
{
    private const string MinimumPSResourceGetVersion = "1.1.1";
    private const string MinimumPSResourceGetExistingSessionVersion = "1.2.0-preview5";

    private readonly IPrivateGalleryHost _host;

    public PrivateGalleryService(IPrivateGalleryHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public void EnsureProviderSupported(PrivateGalleryProvider provider)
    {
        if (provider != PrivateGalleryProvider.AzureArtifacts)
            throw new ArgumentException($"Provider '{provider}' is not supported yet. Supported value: AzureArtifacts.", nameof(provider));
    }

    public IReadOnlyList<ModuleDependency> BuildDependencies(IEnumerable<string> names)
    {
        var dependencies = (names ?? Array.Empty<string>())
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static name => new ModuleDependency(name))
            .ToArray();

        if (dependencies.Length == 0)
            throw new ArgumentException("At least one module name must be provided.", nameof(names));

        return dependencies;
    }

    public CredentialResolutionResult ResolveCredential(
        string repositoryName,
        PrivateGalleryBootstrapMode bootstrapMode,
        string? credentialUserName,
        string? credentialSecret,
        string? credentialSecretFilePath,
        bool promptForCredential,
        BootstrapPrerequisiteStatus? prerequisiteStatus = null,
        bool allowInteractivePrompt = true)
    {
        var hasCredentialSecretFile = !string.IsNullOrWhiteSpace(credentialSecretFilePath);
        var hasCredentialSecret = !string.IsNullOrWhiteSpace(credentialSecret);
        var hasCredentialUser = !string.IsNullOrWhiteSpace(credentialUserName);
        var hasAnyCredentialSecret = hasCredentialSecretFile || hasCredentialSecret;
        var hasExplicitCredential = hasCredentialUser && hasAnyCredentialSecret;

        if (hasAnyCredentialSecret && !hasCredentialUser)
            throw new ArgumentException("CredentialUserName is required when CredentialSecret/CredentialSecretFilePath is provided.", nameof(credentialUserName));

        if (promptForCredential && (hasAnyCredentialSecret || hasCredentialUser))
            throw new ArgumentException("PromptForCredential cannot be combined with CredentialUserName/CredentialSecret/CredentialSecretFilePath.", nameof(promptForCredential));

        if (bootstrapMode == PrivateGalleryBootstrapMode.ExistingSession &&
            (promptForCredential || hasExplicitCredential))
        {
            throw new ArgumentException("BootstrapMode ExistingSession cannot be combined with interactive or explicit credential parameters.", nameof(bootstrapMode));
        }

        var resolvedSecret = string.Empty;
        if (hasCredentialSecretFile)
        {
            resolvedSecret = File.ReadAllText(credentialSecretFilePath!).Trim();
        }
        else if (hasCredentialSecret)
        {
            resolvedSecret = credentialSecret!.Trim();
        }

        var effectiveMode = bootstrapMode;
        if (bootstrapMode == PrivateGalleryBootstrapMode.Auto)
        {
            var detectedPrerequisites = prerequisiteStatus ?? GetBootstrapPrerequisiteStatus();
            effectiveMode = promptForCredential || hasExplicitCredential
                ? PrivateGalleryBootstrapMode.CredentialPrompt
                : PrivateGalleryVersionPolicy.GetRecommendedBootstrapMode(detectedPrerequisites);

            if (effectiveMode == PrivateGalleryBootstrapMode.Auto)
            {
                if (!allowInteractivePrompt)
                    effectiveMode = PrivateGalleryBootstrapMode.CredentialPrompt;
                else
                    throw new InvalidOperationException(PrivateGalleryVersionPolicy.BuildBootstrapUnavailableMessage(repositoryName, detectedPrerequisites));
            }
        }

        if (effectiveMode == PrivateGalleryBootstrapMode.ExistingSession)
        {
            return new CredentialResolutionResult(
                credential: null,
                bootstrapModeUsed: PrivateGalleryBootstrapMode.ExistingSession,
                credentialSource: PrivateGalleryCredentialSource.None);
        }

        if (hasExplicitCredential)
        {
            return new CredentialResolutionResult(
                credential: new RepositoryCredential
                {
                    UserName = credentialUserName!.Trim(),
                    Secret = resolvedSecret
                },
                bootstrapModeUsed: PrivateGalleryBootstrapMode.CredentialPrompt,
                credentialSource: PrivateGalleryCredentialSource.Supplied);
        }

        if (!allowInteractivePrompt)
        {
            return new CredentialResolutionResult(
                credential: null,
                bootstrapModeUsed: PrivateGalleryBootstrapMode.CredentialPrompt,
                credentialSource: PrivateGalleryCredentialSource.None);
        }

        var promptedCredential = _host.PromptForCredential("Private gallery authentication", $"Enter Azure Artifacts credentials or PAT for '{repositoryName}'.");
        if (promptedCredential is null)
        {
            return new CredentialResolutionResult(
                credential: null,
                bootstrapModeUsed: PrivateGalleryBootstrapMode.CredentialPrompt,
                credentialSource: PrivateGalleryCredentialSource.None);
        }

        return new CredentialResolutionResult(
            credential: promptedCredential,
            bootstrapModeUsed: PrivateGalleryBootstrapMode.CredentialPrompt,
            credentialSource: PrivateGalleryCredentialSource.Prompt);
    }

    public RepositoryCredential? ResolveOptionalCredential(
        string repositoryName,
        string? credentialUserName,
        string? credentialSecret,
        string? credentialSecretFilePath,
        bool promptForCredential)
    {
        var hasCredentialUser = !string.IsNullOrWhiteSpace(credentialUserName);
        var hasCredentialSecret = !string.IsNullOrWhiteSpace(credentialSecret);
        var hasCredentialSecretFile = !string.IsNullOrWhiteSpace(credentialSecretFilePath);

        if (!promptForCredential && !hasCredentialUser && !hasCredentialSecret && !hasCredentialSecretFile)
            return null;

        if (!promptForCredential && hasCredentialUser && !hasCredentialSecret && !hasCredentialSecretFile)
            throw new ArgumentException("CredentialSecret/CredentialSecretFilePath or PromptForCredential is required when CredentialUserName is provided.", nameof(credentialUserName));

        return ResolveCredential(
            repositoryName,
            PrivateGalleryBootstrapMode.CredentialPrompt,
            credentialUserName,
            credentialSecret,
            credentialSecretFilePath,
            promptForCredential).Credential;
    }

    public ModuleRepositoryRegistrationResult EnsureAzureArtifactsRepositoryRegistered(
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
        var messages = new List<string>(8);
        var unavailableTools = new List<string>(2);
        var result = new ModuleRepositoryRegistrationResult
        {
            RepositoryName = endpoint.RepositoryName,
            Provider = "AzureArtifacts",
            BootstrapModeRequested = bootstrapModeRequested,
            BootstrapModeUsed = bootstrapModeUsed,
            CredentialSource = credentialSource,
            AzureDevOpsOrganization = endpoint.Organization,
            AzureDevOpsProject = endpoint.Project,
            AzureArtifactsFeed = endpoint.Feed,
            PowerShellGetSourceUri = endpoint.PowerShellGetSourceUri,
            PowerShellGetPublishUri = endpoint.PowerShellGetPublishUri,
            PSResourceGetUri = endpoint.PSResourceGetUri,
            Trusted = trusted,
            CredentialUsed = credential is not null,
            ToolRequested = tool,
            Tool = tool,
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
        };

        if (effectiveTool == RepositoryRegistrationTool.Auto)
        {
            if (prerequisiteStatus.PSResourceGetAvailable && prerequisiteStatus.PSResourceGetMeetsMinimumVersion)
            {
                effectiveTool = RepositoryRegistrationTool.PSResourceGet;
            }
            else if (prerequisiteStatus.PowerShellGetAvailable)
            {
                effectiveTool = RepositoryRegistrationTool.PowerShellGet;
                if (!prerequisiteStatus.PSResourceGetAvailable)
                    unavailableTools.Add("PSResourceGet");
            }
            else
            {
                unavailableTools.Add("PSResourceGet");
                unavailableTools.Add("PowerShellGet");
                throw new InvalidOperationException($"Neither PSResourceGet {MinimumPSResourceGetVersion}+ nor PowerShellGet are available for repository registration. Install prerequisites with -InstallPrerequisites or install PowerShellGet manually.");
            }
        }
        else if (effectiveTool == RepositoryRegistrationTool.PSResourceGet &&
                 (!prerequisiteStatus.PSResourceGetAvailable || !prerequisiteStatus.PSResourceGetMeetsMinimumVersion))
        {
            throw new InvalidOperationException($"PSResourceGet {MinimumPSResourceGetVersion}+ is required when Tool is PSResourceGet. Detected version: {prerequisiteStatus.PSResourceGetVersion ?? "not installed"}.");
        }
        else if (effectiveTool == RepositoryRegistrationTool.PowerShellGet &&
                 !prerequisiteStatus.PowerShellGetAvailable)
        {
            throw new InvalidOperationException("PowerShellGet is required when Tool is PowerShellGet.");
        }
        else if (effectiveTool == RepositoryRegistrationTool.Both &&
                 (!prerequisiteStatus.PSResourceGetAvailable ||
                  !prerequisiteStatus.PSResourceGetMeetsMinimumVersion ||
                  !prerequisiteStatus.PowerShellGetAvailable))
        {
            throw new InvalidOperationException(
                $"Both PSResourceGet {MinimumPSResourceGetVersion}+ and PowerShellGet are required when Tool is Both. " +
                $"Detected PSResourceGet: {prerequisiteStatus.PSResourceGetVersion ?? "not installed"}, PowerShellGet: {prerequisiteStatus.PowerShellGetVersion ?? "not installed"}.");
        }

        result.UnavailableTools = unavailableTools
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static toolName => toolName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        result.Tool = effectiveTool;

        if (!_host.ShouldProcess(result.RepositoryName, shouldProcessAction))
            return result;

        result.RegistrationPerformed = true;
        var runner = new PowerShellRunner();
        var logger = new PrivateGalleryHostLogger(_host);

        if (effectiveTool == RepositoryRegistrationTool.Auto)
            throw new InvalidOperationException("Repository registration tool could not be resolved.");

        if (effectiveTool is RepositoryRegistrationTool.PSResourceGet or RepositoryRegistrationTool.Both)
        {
            var client = new PSResourceGetClient(runner, logger);
            var created = client.EnsureRepositoryRegistered(
                result.RepositoryName,
                endpoint.PSResourceGetUri,
                trusted,
                priority,
                apiVersion: RepositoryApiVersion.V3,
                timeout: TimeSpan.FromMinutes(2));

            result.PSResourceGetRegistered = true;
            result.PSResourceGetCreated = created;
            messages.Add(created
                ? $"Registered PSResourceGet repository '{result.RepositoryName}'."
                : $"PSResourceGet repository '{result.RepositoryName}' already existed and was refreshed.");
        }

        if (effectiveTool is RepositoryRegistrationTool.PowerShellGet or RepositoryRegistrationTool.Both)
        {
            var client = new PowerShellGetClient(runner, logger);
            var created = client.EnsureRepositoryRegistered(
                result.RepositoryName,
                endpoint.PowerShellGetSourceUri,
                endpoint.PowerShellGetPublishUri,
                trusted,
                credential,
                timeout: TimeSpan.FromMinutes(2));

            result.PowerShellGetRegistered = true;
            result.PowerShellGetCreated = created;
            messages.Add(created
                ? $"Registered PowerShellGet repository '{result.RepositoryName}'."
                : $"PowerShellGet repository '{result.RepositoryName}' already existed and was refreshed.");
        }

        if (tool == RepositoryRegistrationTool.Auto &&
            effectiveTool == RepositoryRegistrationTool.PSResourceGet &&
            prerequisiteStatus.PowerShellGetAvailable)
        {
            try
            {
                var powerShellGetClient = new PowerShellGetClient(runner, logger);
                var created = powerShellGetClient.EnsureRepositoryRegistered(
                    result.RepositoryName,
                    endpoint.PowerShellGetSourceUri,
                    endpoint.PowerShellGetPublishUri,
                    trusted,
                    credential,
                    timeout: TimeSpan.FromMinutes(2));

                result.PowerShellGetRegistered = true;
                result.PowerShellGetCreated = created;
                messages.Add(created
                    ? $"Registered PowerShellGet repository '{result.RepositoryName}' for compatibility."
                    : $"PowerShellGet repository '{result.RepositoryName}' was already present for compatibility.");
                result.ToolUsed = RepositoryRegistrationTool.Both;
            }
            catch (Exception ex)
            {
                messages.Add($"PowerShellGet registration was skipped after PSResourceGet succeeded: {ex.Message}");
                result.ToolUsed = result.PSResourceGetRegistered ? RepositoryRegistrationTool.PSResourceGet : RepositoryRegistrationTool.PowerShellGet;
            }
        }
        else
        {
            result.ToolUsed = effectiveTool;
        }

        result.Messages = messages.Where(static message => !string.IsNullOrWhiteSpace(message)).ToArray();
        return result;
    }

    public BootstrapPrerequisiteInstallResult EnsureBootstrapPrerequisites(bool installPrerequisites, bool forceInstall = false)
    {
        var initialStatus = GetBootstrapPrerequisiteStatus();
        if (!installPrerequisites)
            return new BootstrapPrerequisiteInstallResult(Array.Empty<string>(), Array.Empty<string>(), initialStatus);

        var installed = new List<string>(2);
        var messages = new List<string>(4);
        var runner = new PowerShellRunner();
        var logger = new PrivateGalleryHostLogger(_host);

        if (!initialStatus.PSResourceGetAvailable || !initialStatus.PSResourceGetMeetsMinimumVersion || forceInstall)
        {
            if (_host.ShouldProcess("Microsoft.PowerShell.PSResourceGet", "Install private-gallery prerequisite"))
            {
                var installer = new ModuleDependencyInstaller(runner, logger);
                var results = installer.EnsureInstalled(
                    new[] { new ModuleDependency("Microsoft.PowerShell.PSResourceGet", minimumVersion: MinimumPSResourceGetVersion) },
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
            throw new InvalidOperationException($"PSResourceGet prerequisite installation completed, but version {statusAfterPsResourceGet.PSResourceGetVersion ?? "unknown"} does not satisfy minimum {MinimumPSResourceGetVersion}.");
        }

        if (!statusAfterPsResourceGet.CredentialProviderDetection.IsDetected)
        {
            if (Path.DirectorySeparatorChar == '\\')
            {
                if (_host.ShouldProcess("Azure Artifacts Credential Provider", "Install private-gallery prerequisite"))
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
                        throw new InvalidOperationException("Azure Artifacts Credential Provider installation completed, but the provider was still not detected afterwards.");
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
            installed.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static item => item, StringComparer.OrdinalIgnoreCase).ToArray(),
            messages.Where(static message => !string.IsNullOrWhiteSpace(message)).Distinct(StringComparer.Ordinal).ToArray(),
            finalStatus);
    }

    public RepositoryAccessProbeResult ProbeRepositoryAccess(ModuleRepositoryRegistrationResult registration, RepositoryCredential? credential)
    {
        if (registration is null)
            throw new ArgumentNullException(nameof(registration));

        const string probeName = "__PowerForgePrivateGalleryConnectionProbe__";
        var runner = new PowerShellRunner();
        var logger = new PrivateGalleryHostLogger(_host);
        var tool = PrivateGalleryVersionPolicy.SelectAccessProbeTool(registration, credential);

        try
        {
            if (tool == "PSResourceGet")
            {
                var client = new PSResourceGetClient(runner, logger);
                client.Find(new PSResourceFindOptions(new[] { probeName }, null, false, new[] { registration.RepositoryName }, credential), timeout: TimeSpan.FromMinutes(2));
            }
            else
            {
                var client = new PowerShellGetClient(runner, logger);
                client.Find(new PowerShellGetFindOptions(new[] { probeName }, false, new[] { registration.RepositoryName }, credential), timeout: TimeSpan.FromMinutes(2));
            }

            return new RepositoryAccessProbeResult(true, tool, $"Repository access probe completed successfully via {tool}.");
        }
        catch (Exception ex)
        {
            return new RepositoryAccessProbeResult(false, tool, ex.Message);
        }
    }

    public void WriteRegistrationSummary(ModuleRepositoryRegistrationResult result)
    {
        if (result is null)
            return;

        foreach (var message in result.Messages.Where(static message => !string.IsNullOrWhiteSpace(message)))
        {
            if (result.UnavailableTools.Length > 0 &&
                result.UnavailableTools.Any(tool => message.IndexOf(tool, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                _host.WriteWarning(message);
            }
            else
            {
                _host.WriteVerbose(message);
            }
        }

        foreach (var message in result.ReadinessMessages.Where(static message => !string.IsNullOrWhiteSpace(message)))
            _host.WriteVerbose(message);
        foreach (var message in result.PrerequisiteInstallMessages.Where(static message => !string.IsNullOrWhiteSpace(message)))
            _host.WriteVerbose(message);

        var ready = result.ReadyCommands;
        if (ready.Length > 0)
            _host.WriteVerbose($"Repository '{result.RepositoryName}' is ready for {string.Join(", ", ready)}.");

        _host.WriteVerbose($"Bootstrap readiness: ExistingSession={result.ExistingSessionBootstrapReady}; CredentialPrompt={result.CredentialPromptBootstrapReady}.");
        if (!string.IsNullOrWhiteSpace(result.PSResourceGetVersion))
            _host.WriteVerbose($"Detected PSResourceGet version: {result.PSResourceGetVersion} (meets minimum {MinimumPSResourceGetVersion}: {result.PSResourceGetMeetsMinimumVersion}; supports ExistingSession {MinimumPSResourceGetExistingSessionVersion}+: {result.PSResourceGetSupportsExistingSessionBootstrap}).");
        if (!string.IsNullOrWhiteSpace(result.PowerShellGetVersion))
            _host.WriteVerbose($"Detected PowerShellGet version: {result.PowerShellGetVersion}.");
        if (!string.IsNullOrWhiteSpace(result.AzureArtifactsCredentialProviderVersion))
            _host.WriteVerbose($"Detected Azure Artifacts Credential Provider version: {result.AzureArtifactsCredentialProviderVersion}.");

        if (result.InstalledPrerequisites.Length > 0)
            _host.WriteVerbose($"Installed prerequisites: {string.Join(", ", result.InstalledPrerequisites)}.");

        if (result.AccessProbePerformed)
        {
            if (result.AccessProbeSucceeded)
                _host.WriteVerbose(result.AccessProbeMessage ?? $"Repository access probe succeeded via {result.AccessProbeTool ?? "unknown"}.");
            else if (!string.IsNullOrWhiteSpace(result.AccessProbeMessage))
                _host.WriteWarning($"Repository access probe failed via {result.AccessProbeTool ?? "unknown"}: {result.AccessProbeMessage}");
        }

        _host.WriteVerbose($"Bootstrap mode used: {result.BootstrapModeUsed}; credential source: {result.CredentialSource}.");
        _host.WriteVerbose($"Repository registration requested {result.ToolRequested}; successful path: {result.ToolUsed}.");

        if (result.ToolRequested == RepositoryRegistrationTool.Auto &&
            result.ToolUsed == RepositoryRegistrationTool.PowerShellGet)
        {
            _host.WriteVerbose("Auto registration fell back to PowerShellGet, so Install-Module is the current native path on this machine.");
        }

        if (result.BootstrapModeRequested == PrivateGalleryBootstrapMode.ExistingSession &&
            !result.ExistingSessionBootstrapReady)
        {
            _host.WriteWarning($"ExistingSession bootstrap was requested, but Azure Artifacts ExistingSession support requires PSResourceGet {MinimumPSResourceGetExistingSessionVersion}+ and a detected Azure Artifacts Credential Provider.");
        }

        if (!string.IsNullOrWhiteSpace(result.RecommendedBootstrapCommand))
            _host.WriteVerbose($"Bootstrap recommendation: {result.RecommendedBootstrapCommand}");
        if (!string.IsNullOrWhiteSpace(result.RecommendedNativeInstallCommand))
            _host.WriteVerbose($"Native install example: {result.RecommendedNativeInstallCommand}");
        _host.WriteVerbose($"Wrapper install example: {result.RecommendedWrapperInstallCommand}");
    }

    public BootstrapPrerequisiteStatus GetBootstrapPrerequisiteStatus()
    {
        var runner = new PowerShellRunner();
        var logger = new NullLogger();
        var psResourceGet = new PSResourceGetClient(runner, logger);
        var powerShellGet = new PowerShellGetClient(runner, logger);

        var psResourceGetAvailability = psResourceGet.GetAvailability();
        var powerShellGetAvailability = powerShellGet.GetAvailability();
        var credentialProviderDetection = AzureArtifactsCredentialProviderLocator.Detect();
        var psResourceGetMeetsMinimumVersion = PrivateGalleryVersionPolicy.VersionMeetsMinimum(psResourceGetAvailability.Version, MinimumPSResourceGetVersion);
        var psResourceGetSupportsExistingSessionBootstrap = PrivateGalleryVersionPolicy.VersionMeetsMinimum(psResourceGetAvailability.Version, MinimumPSResourceGetExistingSessionVersion);

        var readinessMessages = new List<string>(6);
        if (psResourceGetAvailability.Available)
        {
            if (psResourceGetMeetsMinimumVersion)
            {
                readinessMessages.Add($"PSResourceGet is available for private-gallery bootstrap (version {psResourceGetAvailability.Version ?? "unknown"}).");
                if (!psResourceGetSupportsExistingSessionBootstrap)
                    readinessMessages.Add($"PSResourceGet version {psResourceGetAvailability.Version ?? "unknown"} supports credential-prompt installs, but Azure Artifacts ExistingSession bootstrap requires {MinimumPSResourceGetExistingSessionVersion} or newer.");
            }
            else
            {
                readinessMessages.Add($"PSResourceGet is installed, but version {psResourceGetAvailability.Version ?? "unknown"} is below the private-gallery minimum {MinimumPSResourceGetVersion}.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(psResourceGetAvailability.Message))
        {
            readinessMessages.Add(psResourceGetAvailability.Message!);
        }

        if (powerShellGetAvailability.Available)
            readinessMessages.Add($"PowerShellGet is available for compatibility/fallback registration (version {powerShellGetAvailability.Version ?? "unknown"}).");
        else if (!string.IsNullOrWhiteSpace(powerShellGetAvailability.Message))
            readinessMessages.Add(powerShellGetAvailability.Message!);

        if (credentialProviderDetection.IsDetected)
            readinessMessages.Add($"Azure Artifacts Credential Provider detected ({credentialProviderDetection.Paths.Length} path(s), version {credentialProviderDetection.Version ?? "unknown"}).");
        else
            readinessMessages.Add("Azure Artifacts Credential Provider was not detected in NUGET_PLUGIN_PATHS, %UserProfile%\\.nuget\\plugins, or Visual Studio NuGet plugin locations.");

        return new BootstrapPrerequisiteStatus(
            psResourceGetAvailability.Available,
            psResourceGetAvailability.Version,
            psResourceGetMeetsMinimumVersion,
            psResourceGetSupportsExistingSessionBootstrap,
            psResourceGetAvailability.Message,
            powerShellGetAvailability.Available,
            powerShellGetAvailability.Version,
            powerShellGetAvailability.Message,
            credentialProviderDetection,
            readinessMessages.ToArray());
    }
}
