using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Microsoft.Win32;

namespace PowerForge;

internal sealed class PrivateGalleryService
{
    private const string MinimumPSResourceGetVersion = "1.1.1";
    private const string MinimumPSResourceGetExistingSessionVersion = "1.2.0";
    private const string CredentialProviderTimeoutMinutesEnvironmentVariable = "POWERFORGE_AZURE_ARTIFACTS_CREDENTIAL_PROVIDER_TIMEOUT_MINUTES";
    private const string JFrogCliTimeoutMinutesEnvironmentVariable = "POWERFORGE_JFROG_CLI_LOGIN_TIMEOUT_MINUTES";
    private const int MissingDotNetRuntimeExitCode = -2147450749;
    private const string NetFrameworkReleaseKey = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full";

    private readonly IPrivateGalleryHost _host;
    private readonly IProcessRunner _processRunner;
    private readonly Func<int?> _netFrameworkReleaseProvider;

    public PrivateGalleryService(IPrivateGalleryHost host, IProcessRunner? processRunner = null, Func<int?>? netFrameworkReleaseProvider = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _processRunner = processRunner ?? new ProcessRunner();
        _netFrameworkReleaseProvider = netFrameworkReleaseProvider ?? GetNetFrameworkRelease;
    }

    public void EnsureProviderSupported(PrivateGalleryProvider provider)
    {
        if (provider is not (PrivateGalleryProvider.AzureArtifacts or PrivateGalleryProvider.JFrog or PrivateGalleryProvider.GitHubPackages or PrivateGalleryProvider.NuGet))
            throw new ArgumentException($"Provider '{provider}' is not supported. Supported values: AzureArtifacts, JFrog, GitHubPackages, NuGet.", nameof(provider));
    }

    public IReadOnlyList<ModuleDependency> BuildDependencies(
        IEnumerable<string> names,
        IReadOnlyDictionary<string, string>? requiredVersions = null,
        IReadOnlyDictionary<string, string>? minimumVersions = null,
        IReadOnlyDictionary<string, bool>? minimumVersionInclusivity = null,
        IReadOnlyDictionary<string, string>? maximumVersions = null,
        IReadOnlyDictionary<string, bool>? maximumVersionInclusivity = null,
        string? installScope = null,
        IReadOnlyDictionary<string, string>? installScopes = null)
    {
        var dependencies = (names ?? Array.Empty<string>())
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => new ModuleDependency(
                name,
                requiredVersion: requiredVersions is not null && requiredVersions.TryGetValue(name, out var requiredVersion)
                    ? requiredVersion
                    : null,
                minimumVersion: minimumVersions is not null && minimumVersions.TryGetValue(name, out var minimumVersion)
                    ? minimumVersion
                    : null,
                maximumVersion: maximumVersions is not null && maximumVersions.TryGetValue(name, out var maximumVersion)
                    ? maximumVersion
                    : null,
                installScope: installScopes is not null && installScopes.TryGetValue(name, out var moduleScope)
                    ? moduleScope
                    : installScope,
                minimumVersionInclusive: minimumVersionInclusivity is null ||
                                         !minimumVersionInclusivity.TryGetValue(name, out var minimumInclusive) ||
                                         minimumInclusive,
                maximumVersionInclusive: maximumVersionInclusivity is null ||
                                         !maximumVersionInclusivity.TryGetValue(name, out var maximumInclusive) ||
                                         maximumInclusive))
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
        bool allowInteractivePrompt = true,
        PrivateGalleryProvider provider = PrivateGalleryProvider.AzureArtifacts)
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

        if (bootstrapMode == PrivateGalleryBootstrapMode.JFrogCli &&
            (promptForCredential || hasExplicitCredential))
        {
            throw new ArgumentException("BootstrapMode JFrogCli cannot be combined with interactive or explicit credential parameters. Use CredentialPrompt for token/basic authentication.", nameof(bootstrapMode));
        }

        if (bootstrapMode == PrivateGalleryBootstrapMode.JFrogCli &&
            provider != PrivateGalleryProvider.JFrog)
        {
            throw new ArgumentException("BootstrapMode JFrogCli can only be used with the JFrog private gallery provider.", nameof(bootstrapMode));
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
                : provider == PrivateGalleryProvider.AzureArtifacts
                    ? PrivateGalleryVersionPolicy.GetRecommendedBootstrapMode(detectedPrerequisites)
                    : PrivateGalleryBootstrapMode.CredentialPrompt;

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

        if (effectiveMode == PrivateGalleryBootstrapMode.JFrogCli)
        {
            return new CredentialResolutionResult(
                credential: null,
                bootstrapModeUsed: PrivateGalleryBootstrapMode.JFrogCli,
                credentialSource: PrivateGalleryCredentialSource.JFrogCli);
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

        var promptedCredential = _host.PromptForCredential("Private gallery authentication", $"Enter private gallery credentials or token for '{repositoryName}'.");
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
        string shouldProcessAction,
        PrivateGalleryProvider provider = PrivateGalleryProvider.AzureArtifacts,
        string? repository = null,
        string? repositoryUri = null,
        string? repositorySourceUri = null,
        string? repositoryPublishUri = null,
        string? jfrogBaseUri = null,
        string? jfrogRepository = null,
        RepositoryApiVersion apiVersion = RepositoryApiVersion.Auto)
    {
        var endpoint = PrivateGalleryRepositoryEndpoints.Create(
            provider,
            azureDevOpsOrganization,
            azureDevOpsProject,
            azureArtifactsFeed,
            repositoryName,
            repository,
            repositoryUri,
            repositorySourceUri,
            repositoryPublishUri,
            jfrogBaseUri,
            jfrogRepository);

        var effectiveTool = tool;
        var effectiveApiVersion = apiVersion;
        var result = new ModuleRepositoryRegistrationResult
        {
            RepositoryName = endpoint.RepositoryName,
            Provider = endpoint.Provider.ToString(),
            BootstrapModeRequested = bootstrapModeRequested,
            BootstrapModeUsed = bootstrapModeUsed,
            CredentialSource = credentialSource,
            AzureDevOpsOrganization = endpoint.AzureDevOpsOrganization ?? string.Empty,
            AzureDevOpsProject = endpoint.AzureDevOpsProject,
            AzureArtifactsFeed = endpoint.Repository,
            PowerShellGetSourceUri = endpoint.PowerShellGetSourceUri,
            PowerShellGetPublishUri = endpoint.PowerShellGetPublishUri,
            PSResourceGetUri = endpoint.PSResourceGetUri,
            Trusted = trusted,
            Priority = priority,
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

        if (effectiveTool == RepositoryRegistrationTool.PSResourceGet &&
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

        result.Tool = effectiveTool;

        if (!_host.ShouldProcess(result.RepositoryName, shouldProcessAction))
            return result;

        result.RegistrationPerformed = true;
        var runner = new PowerShellRunner();
        var logger = new PrivateGalleryHostLogger(_host);
        var unavailableTools = new List<string>(2);
        var messages = new List<string>(8);
        var failures = new List<string>(2);

        void RegisterPSResourceGet()
        {
            try
            {
                var client = new PSResourceGetClient(runner, logger);
                var created = client.EnsureRepositoryRegistered(
                    result.RepositoryName,
                    ResolvePSResourceGetRegistrationUri(endpoint, effectiveApiVersion),
                    trusted,
                    priority,
                    apiVersion: effectiveApiVersion,
                    timeout: TimeSpan.FromMinutes(2));

                result.PSResourceGetRegistered = true;
                result.PSResourceGetCreated = created;
                messages.Add(created
                    ? $"Registered PSResourceGet repository '{result.RepositoryName}'."
                    : $"PSResourceGet repository '{result.RepositoryName}' already existed and was verified.");
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

        void RegisterPowerShellGet()
        {
            try
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
                    : $"PowerShellGet repository '{result.RepositoryName}' already existed and was verified.");
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
            .OrderBy(static toolName => toolName, StringComparer.OrdinalIgnoreCase)
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

    private static string ResolvePSResourceGetRegistrationUri(PrivateGalleryRepositoryEndpoint endpoint, RepositoryApiVersion apiVersion)
        => apiVersion == RepositoryApiVersion.V2
            ? endpoint.PowerShellGetSourceUri
            : endpoint.PSResourceGetUri;

    public ModuleRepositoryRegistrationResult EnsureMicrosoftArtifactRegistryRegistered(
        string? repositoryName,
        RepositoryRegistrationTool tool,
        bool trusted,
        int? priority,
        BootstrapPrerequisiteStatus prerequisiteStatus,
        string shouldProcessAction)
    {
        if (tool is RepositoryRegistrationTool.PowerShellGet or RepositoryRegistrationTool.Both)
            throw new InvalidOperationException("Microsoft Artifact Registry requires PSResourceGet. PowerShellGet does not support container-registry PowerShell repositories.");

        var resolvedName = string.IsNullOrWhiteSpace(repositoryName)
            ? MicrosoftArtifactRegistryRepository.DefaultName
            : repositoryName!.Trim();
        var result = new ModuleRepositoryRegistrationResult
        {
            RepositoryName = resolvedName,
            Provider = "MicrosoftArtifactRegistry",
            BootstrapModeRequested = PrivateGalleryBootstrapMode.ExistingSession,
            BootstrapModeUsed = PrivateGalleryBootstrapMode.ExistingSession,
            CredentialSource = PrivateGalleryCredentialSource.None,
            PSResourceGetUri = MicrosoftArtifactRegistryRepository.DefaultUri,
            Trusted = trusted,
            CredentialUsed = false,
            ToolRequested = tool,
            Tool = RepositoryRegistrationTool.PSResourceGet,
            PSResourceGetAvailable = prerequisiteStatus.PSResourceGetAvailable,
            PSResourceGetVersion = prerequisiteStatus.PSResourceGetVersion,
            PSResourceGetMeetsMinimumVersion = prerequisiteStatus.PSResourceGetMeetsMinimumVersion,
            PSResourceGetSupportsExistingSessionBootstrap = prerequisiteStatus.PSResourceGetSupportsExistingSessionBootstrap,
            PowerShellGetAvailable = prerequisiteStatus.PowerShellGetAvailable,
            PowerShellGetVersion = prerequisiteStatus.PowerShellGetVersion,
            ReadinessMessages = prerequisiteStatus.ReadinessMessages
        };

        if (!prerequisiteStatus.PSResourceGetAvailable || !prerequisiteStatus.PSResourceGetMeetsMinimumVersion)
        {
            throw new InvalidOperationException($"PSResourceGet {MinimumPSResourceGetVersion}+ is required to register Microsoft Artifact Registry. Detected version: {prerequisiteStatus.PSResourceGetVersion ?? "not installed"}.");
        }

        if (!_host.ShouldProcess(result.RepositoryName, shouldProcessAction))
            return result;

        result.RegistrationPerformed = true;
        var runner = new PowerShellRunner();
        var logger = new PrivateGalleryHostLogger(_host);
        var messages = new List<string>(2);

        try
        {
            var client = new PSResourceGetClient(runner, logger);
            var created = client.EnsureMicrosoftArtifactRegistryRegistered(
                resolvedName,
                trusted,
                priority,
                timeout: TimeSpan.FromMinutes(2));

            result.PSResourceGetRegistered = true;
            result.PSResourceGetCreated = created;
            result.ToolUsed = RepositoryRegistrationTool.PSResourceGet;
            messages.Add(created
                ? $"Registered PSResourceGet repository '{result.RepositoryName}' for Microsoft Artifact Registry."
                : $"PSResourceGet repository '{result.RepositoryName}' for Microsoft Artifact Registry already existed and was refreshed.");
        }
        catch (PowerShellToolNotAvailableException ex)
        {
            result.UnavailableTools = new[] { "PSResourceGet" };
            messages.Add(ex.Message);
        }
        catch (Exception ex)
        {
            messages.Add($"Microsoft Artifact Registry registration failed: {ex.Message}");
        }

        result.Messages = messages
            .Where(static message => !string.IsNullOrWhiteSpace(message))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (!result.PSResourceGetRegistered)
        {
            var message = result.Messages.Length > 0
                ? string.Join(" ", result.Messages)
                : $"Microsoft Artifact Registry repository '{resolvedName}' could not be registered.";
            throw new InvalidOperationException(message);
        }

        return result;
    }

    public BootstrapPrerequisiteInstallResult EnsureBootstrapPrerequisites(
        bool installPrerequisites,
        PrivateGalleryBootstrapMode bootstrapMode = PrivateGalleryBootstrapMode.Auto,
        bool forceInstall = false,
        bool includeAzureArtifactsCredentialProvider = true,
        string? artefactsRepositoryName = null,
        string? artefactsPSResourceGetUri = null,
        string? artefactsPowerShellGetSourceUri = null)
    {
        var initialStatus = GetBootstrapPrerequisiteStatus();
        if (!installPrerequisites)
            return new BootstrapPrerequisiteInstallResult(Array.Empty<string>(), Array.Empty<string>(), initialStatus);

        var installed = new List<string>(2);
        var messages = new List<string>(4);
        var runner = new PowerShellRunner();
        var logger = new PrivateGalleryHostLogger(_host);
        var requiredPSResourceGetVersion = GetRequiredPSResourceGetVersion(
            bootstrapMode,
            includeAzureArtifactsCredentialProvider);

        if (!initialStatus.PSResourceGetAvailable ||
            !PrivateGalleryVersionPolicy.VersionMeetsMinimum(initialStatus.PSResourceGetVersion, requiredPSResourceGetVersion) ||
            forceInstall)
        {
            if (_host.ShouldProcess("Microsoft.PowerShell.PSResourceGet", "Install private-gallery prerequisite"))
            {
                var installer = new ModuleDependencyInstaller(runner, logger);
                var results = installer.EnsureInstalled(
                    new[] { new ModuleDependency("Microsoft.PowerShell.PSResourceGet", minimumVersion: requiredPSResourceGetVersion) },
                    force: forceInstall,
                    prerelease: IsPrereleaseVersion(requiredPSResourceGetVersion),
                    timeoutPerModule: TimeSpan.FromMinutes(10));

                var result = results.FirstOrDefault();
                if (result is null || result.Status == ModuleDependencyInstallStatus.Failed)
                {
                    var failure = result?.Message ?? "PSResourceGet prerequisite installation did not return a result.";
                    throw new InvalidOperationException($"Failed to install PSResourceGet prerequisite. {failure}".Trim());
                }

                installed.Add("PSResourceGet");
                var resolvedVersion = string.IsNullOrWhiteSpace(result.ResolvedVersion) ? "unknown version" : result.ResolvedVersion;
                messages.Add($"PSResourceGet prerequisite handled via {result.Installer ?? "module installer"} ({result.Status}, required {requiredPSResourceGetVersion}, resolved {resolvedVersion}).");
            }
        }

        var statusAfterPsResourceGet = GetBootstrapPrerequisiteStatus();
        if (installed.Contains("PSResourceGet", StringComparer.OrdinalIgnoreCase) &&
            (!statusAfterPsResourceGet.PSResourceGetAvailable ||
             !PrivateGalleryVersionPolicy.VersionMeetsMinimum(statusAfterPsResourceGet.PSResourceGetVersion, requiredPSResourceGetVersion)))
        {
            throw new InvalidOperationException($"PSResourceGet prerequisite installation completed, but version {statusAfterPsResourceGet.PSResourceGetVersion ?? "unknown"} does not satisfy minimum {requiredPSResourceGetVersion}.");
        }

        if (includeAzureArtifactsCredentialProvider &&
            !statusAfterPsResourceGet.CredentialProviderDetection.IsDetected)
        {
            if (Path.DirectorySeparatorChar == '\\')
            {
                if (_host.ShouldProcess("Azure Artifacts Credential Provider", "Install private-gallery prerequisite"))
                {
                    var installer = new AzureArtifactsCredentialProviderInstaller(
                        runner,
                        logger,
                        preferredRepositoryName: artefactsRepositoryName,
                        preferredPSResourceGetUri: artefactsPSResourceGetUri,
                        preferredPowerShellGetSourceUri: artefactsPowerShellGetSourceUri);
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

    public BootstrapPrerequisiteInstallResult EnsureMicrosoftArtifactRegistryPrerequisites(bool installPrerequisites)
        => EnsureBootstrapPrerequisites(
            installPrerequisites,
            PrivateGalleryBootstrapMode.Auto,
            includeAzureArtifactsCredentialProvider: false);

    internal static string GetRequiredPSResourceGetVersion(
        PrivateGalleryBootstrapMode bootstrapMode,
        bool includeAzureArtifactsCredentialProvider)
        => includeAzureArtifactsCredentialProvider &&
           PrivateGalleryVersionPolicy.RequiresExistingSessionBootstrap(bootstrapMode)
            ? MinimumPSResourceGetExistingSessionVersion
            : MinimumPSResourceGetVersion;

    public RepositoryAccessProbeResult ProbeRepositoryAccess(ModuleRepositoryRegistrationResult registration, RepositoryCredential? credential)
    {
        if (registration is null)
            throw new ArgumentNullException(nameof(registration));

        const string probeName = "__PowerForgePrivateGalleryConnectionProbe__";
        var runner = new PowerShellRunner();
        var logger = new NullLogger();
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
            if (IsMissingProbePackageMessage(ex.Message, probeName))
            {
                return new RepositoryAccessProbeResult(
                    true,
                    tool,
                    $"Repository access probe reached '{registration.RepositoryName}' via {tool}; the synthetic probe package was not present.");
            }

            return new RepositoryAccessProbeResult(false, tool, ex.Message);
        }
    }

    public RepositoryAccessProbeResult ProbeRepositoryAccessWithOptionalSessionPrime(
        ModuleRepositoryRegistrationResult registration,
        RepositoryCredential? credential,
        bool allowInteractiveCredentialProviderPrime)
    {
        if (registration is null)
            throw new ArgumentNullException(nameof(registration));

        var probe = ProbeRepositoryAccess(registration, credential);
        if (!probe.Succeeded &&
            credential is null &&
            allowInteractiveCredentialProviderPrime &&
            string.Equals(registration.Provider, "JFrog", StringComparison.OrdinalIgnoreCase) &&
            registration.BootstrapModeUsed == PrivateGalleryBootstrapMode.JFrogCli &&
            registration.PSResourceGetRegistered)
        {
            var login = RunJFrogCliLogin(registration);
            registration.JFrogCliLoginAttempted = login.Attempted;
            registration.JFrogCliLoginSucceeded = login.Succeeded;
            registration.JFrogCliLoginSkipped = login.Skipped;
            registration.JFrogCliPath = login.ExecutablePath;
            registration.JFrogCliLoginMessage = login.Message;

            if (!login.Succeeded)
            {
                var loginMessage = string.IsNullOrWhiteSpace(login.Message)
                    ? "JFrog CLI browser login did not complete successfully."
                    : login.Message;
                return new RepositoryAccessProbeResult(
                    false,
                    probe.Tool,
                    $"Repository access probe failed before/after JFrog CLI login. {loginMessage} Original probe: {probe.Message}".Trim());
            }

            var jfrogRetry = ProbeRepositoryAccess(registration, credential);
            if (jfrogRetry.Succeeded)
                return jfrogRetry;

            var jfrogMessage = string.IsNullOrWhiteSpace(jfrogRetry.Message)
                ? "Repository access probe still failed after JFrog CLI browser login."
                : $"Repository access probe still failed after JFrog CLI browser login. {jfrogRetry.Message}";
            return new RepositoryAccessProbeResult(
                false,
                jfrogRetry.Tool,
                jfrogMessage + " This means JFrog CLI SSO succeeded, but PSResourceGet/PowerShellGet did not consume the JFrog CLI session. Use CredentialPrompt/token authentication or configure a NuGet-compatible credential bridge.");
        }

        if (probe.Succeeded ||
            credential is not null ||
            !allowInteractiveCredentialProviderPrime ||
            !string.Equals(registration.Provider, "AzureArtifacts", StringComparison.OrdinalIgnoreCase) ||
            registration.BootstrapModeUsed != PrivateGalleryBootstrapMode.ExistingSession ||
            !registration.PSResourceGetRegistered ||
            !registration.ExistingSessionBootstrapReady)
        {
            return probe;
        }

        var prime = PrimeAzureArtifactsCredentialProviderSession(registration);
        registration.CredentialProviderSessionPrimeAttempted = prime.Attempted;
        registration.CredentialProviderSessionPrimeSucceeded = prime.Succeeded;
        registration.CredentialProviderSessionPrimeSkipped = prime.Skipped;
        registration.CredentialProviderSessionPrimePath = prime.ProviderPath;
        registration.CredentialProviderSessionPrimeMessage = prime.Message;

        if (!prime.Succeeded)
            return probe;

        var retry = ProbeRepositoryAccess(registration, credential);
        if (retry.Succeeded)
            return retry;

        var message = string.IsNullOrWhiteSpace(retry.Message)
            ? "Repository access probe still failed after Azure Artifacts Credential Provider session priming."
            : $"Repository access probe still failed after Azure Artifacts Credential Provider session priming. {retry.Message}";
        return new RepositoryAccessProbeResult(false, retry.Tool, message);
    }

    internal CredentialProviderSessionPrimeResult PrimeAzureArtifactsCredentialProviderSession(
        ModuleRepositoryRegistrationResult registration,
        TimeSpan? timeout = null)
    {
        if (registration is null)
            throw new ArgumentNullException(nameof(registration));

        if (string.IsNullOrWhiteSpace(registration.PSResourceGetUri))
        {
            return new CredentialProviderSessionPrimeResult(
                attempted: false,
                succeeded: false,
                skipped: true,
                providerPath: null,
                message: "Azure Artifacts Credential Provider session priming was skipped because the PSResourceGet feed URI is empty.");
        }

        if (IsHeadlessAutomation())
        {
            return new CredentialProviderSessionPrimeResult(
                attempted: false,
                succeeded: false,
                skipped: true,
                providerPath: null,
                message: "Azure Artifacts Credential Provider session priming was skipped because the current process appears to be CI/headless. Use a pre-cached provider session or configure ARTIFACTS_CREDENTIALPROVIDER_EXTERNAL_FEED_ENDPOINTS / ARTIFACTS_CREDENTIALPROVIDER_FEED_ENDPOINTS for unattended validation.");
        }

        var providerPaths = SelectCredentialProviderPaths(registration.AzureArtifactsCredentialProviderPaths);
        if (providerPaths.Length == 0)
        {
            return new CredentialProviderSessionPrimeResult(
                attempted: false,
                succeeded: false,
                skipped: true,
                providerPath: null,
                message: "Azure Artifacts Credential Provider session priming was skipped because no credential-provider executable or DLL was detected.");
        }

        if (!_host.ShouldProcess(registration.RepositoryName, "Prime Azure Artifacts Credential Provider session"))
        {
            return new CredentialProviderSessionPrimeResult(
                attempted: false,
                succeeded: false,
                skipped: true,
                providerPath: providerPaths[0],
                message: "Azure Artifacts Credential Provider session priming was skipped by ShouldProcess.");
        }

        _host.WriteWarning("The Azure Artifacts access probe failed without an explicit credential. PSPublishModule will invoke the Azure Artifacts Credential Provider so you can complete the Entra/MFA sign-in and cache a session token for this feed.");

        var effectiveTimeout = timeout ?? GetCredentialProviderSessionPrimeTimeout();
        var previousDeviceFlowTimeout = Environment.GetEnvironmentVariable("NUGET_CREDENTIALPROVIDER_VSTS_DEVICEFLOWTIMEOUTSECONDS");
        if (string.IsNullOrWhiteSpace(previousDeviceFlowTimeout))
        {
            Environment.SetEnvironmentVariable(
                "NUGET_CREDENTIALPROVIDER_VSTS_DEVICEFLOWTIMEOUTSECONDS",
                Math.Ceiling(effectiveTimeout.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        ProcessRunResult? lastResult = null;
        string? lastProviderPath = null;
        string? lastFailure = null;
        try
        {
            foreach (var providerExecutablePath in providerPaths)
            {
                lastProviderPath = providerExecutablePath;
                var prerequisiteFailure = GetCredentialProviderRuntimePrerequisiteFailure(providerExecutablePath);
                if (!string.IsNullOrWhiteSpace(prerequisiteFailure))
                {
                    if (!string.Equals(providerExecutablePath, providerPaths[providerPaths.Length - 1], StringComparison.OrdinalIgnoreCase))
                    {
                        _host.WriteWarning(prerequisiteFailure! + " Trying another detected provider.");
                        continue;
                    }

                    lastFailure = prerequisiteFailure;
                    break;
                }

                var result = RunCredentialProviderSessionPrime(
                    providerExecutablePath,
                    registration.PSResourceGetUri!,
                    effectiveTimeout);
                lastResult = result;

                if (result.Succeeded)
                {
                    return new CredentialProviderSessionPrimeResult(
                        attempted: true,
                        succeeded: true,
                        skipped: false,
                        providerPath: providerExecutablePath,
                        message: "Azure Artifacts Credential Provider session priming completed successfully.");
                }

                if (!result.TimedOut &&
                    !result.Succeeded &&
                    !string.Equals(providerExecutablePath, providerPaths[providerPaths.Length - 1], StringComparison.OrdinalIgnoreCase))
                {
                    var reason = IsMissingDotNetRuntimeFailure(result)
                        ? "requires a missing .NET runtime"
                        : $"failed with exit code {result.ExitCode}";
                    _host.WriteWarning($"Azure Artifacts Credential Provider '{providerExecutablePath}' {reason}. Trying another detected provider.");
                    continue;
                }

                break;
            }
        }
        finally
        {
            if (string.IsNullOrWhiteSpace(previousDeviceFlowTimeout))
                Environment.SetEnvironmentVariable("NUGET_CREDENTIALPROVIDER_VSTS_DEVICEFLOWTIMEOUTSECONDS", null);
        }

        var failure = lastResult is null
            ? lastFailure ?? "Azure Artifacts Credential Provider session priming did not run."
            : lastResult.TimedOut
            ? "Azure Artifacts Credential Provider session priming timed out."
            : IsMissingDotNetRuntimeFailure(lastResult)
                ? $"Azure Artifacts Credential Provider session priming failed because '{lastProviderPath}' requires a .NET runtime that is not installed. Reinstall prerequisites so PSPublishModule can use the self-contained Microsoft credential-provider package, or install the required .NET runtime."
                : $"Azure Artifacts Credential Provider session priming failed with exit code {lastResult.ExitCode}.";
        return new CredentialProviderSessionPrimeResult(
            attempted: true,
            succeeded: false,
            skipped: false,
            providerPath: lastProviderPath,
            message: failure);
    }

    private ProcessRunResult RunCredentialProviderSessionPrime(
        string providerExecutablePath,
        string feedUri,
        TimeSpan timeout)
    {
        var fileName = providerExecutablePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? "dotnet" : providerExecutablePath;
        var arguments = new List<string>();
        if (providerExecutablePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            arguments.Add(providerExecutablePath);

        arguments.Add("-I");
        arguments.Add("-U");
        arguments.Add(feedUri);
        arguments.Add("-F");
        arguments.Add("Json");
        arguments.Add("-C");
        arguments.Add("True");

        return _processRunner.RunAsync(
            new ProcessRunRequest(
                fileName,
                Environment.CurrentDirectory,
                arguments,
                timeout,
                captureOutput: false,
                captureError: false)).GetAwaiter().GetResult();
    }

    private string? GetCredentialProviderRuntimePrerequisiteFailure(string providerExecutablePath)
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? GetNetFxCredentialProviderPrerequisiteFailure(providerExecutablePath, _netFrameworkReleaseProvider())
            : null;

    internal static string? GetNetFxCredentialProviderPrerequisiteFailure(string providerExecutablePath, int? installedRelease)
    {
        if (!IsNetFxCredentialProviderPath(providerExecutablePath))
            return null;

        var targetFramework = TryReadNetFxTargetFramework(providerExecutablePath);
        var requiredRelease = GetNetFrameworkMinimumRelease(targetFramework);
        if (!installedRelease.HasValue)
        {
            return $"Azure Artifacts Credential Provider '{providerExecutablePath}' requires {targetFramework}, but .NET Framework 4.5+ was not detected in the registry. Install {targetFramework} or use the self-contained Microsoft.win-* credential-provider package.";
        }

        if (requiredRelease.HasValue && installedRelease.Value < requiredRelease.Value)
        {
            return $"Azure Artifacts Credential Provider '{providerExecutablePath}' requires {targetFramework}, but the installed .NET Framework Release DWORD is {installedRelease.Value}. Install {targetFramework} or use the self-contained Microsoft.win-* credential-provider package.";
        }

        return null;
    }

    private static int? GetNetFrameworkRelease()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return null;

        try
        {
            var value = Registry.GetValue(NetFrameworkReleaseKey, "Release", null);
            if (value is int release)
                return release;

            return value is string text && int.TryParse(text, out var parsed) ? parsed : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsNetFxCredentialProviderPath(string providerExecutablePath)
        => providerExecutablePath.EndsWith("CredentialProvider.Microsoft.exe", StringComparison.OrdinalIgnoreCase) &&
           HasPathSegment(providerExecutablePath, "netfx");

    private static string TryReadNetFxTargetFramework(string providerExecutablePath)
    {
        var configPath = providerExecutablePath + ".config";
        if (!File.Exists(configPath))
            return ".NET Framework 4.8.1";

        try
        {
            var document = XDocument.Load(configPath);
            var sku = document.Descendants()
                .FirstOrDefault(static element => string.Equals(element.Name.LocalName, "supportedRuntime", StringComparison.OrdinalIgnoreCase))
                ?.Attribute("sku")
                ?.Value;

            return FormatTargetFrameworkSku(sku);
        }
        catch
        {
            return ".NET Framework 4.8.1";
        }
    }

    private static string FormatTargetFrameworkSku(string? sku)
    {
        var value = (sku ?? string.Empty).Trim();
        if (value.Length == 0)
            return ".NET Framework 4.8.1";

        const string netFrameworkPrefix = ".NETFramework,Version=v";
        if (value.StartsWith(netFrameworkPrefix, StringComparison.OrdinalIgnoreCase))
            return ".NET Framework " + value.Substring(netFrameworkPrefix.Length);

        return value.Replace(".NETFramework", ".NET Framework");
    }

    private static int? GetNetFrameworkMinimumRelease(string targetFramework)
    {
        var version = ParseNetFrameworkVersion(targetFramework);
        if (version is null)
            return 533320;

        if (version >= new Version(4, 8, 1))
            return 533320;
        if (version >= new Version(4, 8))
            return 528040;
        if (version >= new Version(4, 7, 2))
            return 461808;
        if (version >= new Version(4, 7, 1))
            return 461308;
        if (version >= new Version(4, 7))
            return 460798;
        if (version >= new Version(4, 6, 2))
            return 394802;

        return null;
    }

    private static Version? ParseNetFrameworkVersion(string targetFramework)
    {
        if (string.IsNullOrWhiteSpace(targetFramework))
            return null;

        var markerIndex = targetFramework.IndexOf("4.", StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
            return null;

        var versionText = new string(targetFramework
            .Substring(markerIndex)
            .TakeWhile(static c => char.IsDigit(c) || c == '.')
            .ToArray());
        return Version.TryParse(versionText, out var version) ? version : null;
    }

    internal static bool IsMissingProbePackageMessage(string? message, string probeName)
    {
        if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(probeName))
            return false;

        var text = message!;
        return text.IndexOf(probeName, StringComparison.OrdinalIgnoreCase) >= 0 &&
               (text.IndexOf("could not be found", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("no match", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("no packages found", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("no results", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    internal JFrogCliLoginResult RunJFrogCliLogin(
        ModuleRepositoryRegistrationResult registration,
        TimeSpan? timeout = null)
    {
        if (registration is null)
            throw new ArgumentNullException(nameof(registration));

        if (!string.Equals(registration.Provider, "JFrog", StringComparison.OrdinalIgnoreCase))
        {
            return new JFrogCliLoginResult(
                attempted: false,
                succeeded: false,
                skipped: true,
                executablePath: null,
                message: "JFrog CLI login is only used for JFrog private galleries.");
        }

        if (IsHeadlessAutomation())
        {
            return new JFrogCliLoginResult(
                attempted: false,
                succeeded: false,
                skipped: true,
                executablePath: null,
                message: "JFrog CLI browser login was skipped because the current process appears to be CI/headless.");
        }

        var executable = ResolveOnPath(Path.DirectorySeparatorChar == '\\' ? "jf.exe" : "jf") ??
                         ResolveOnPath("jf");
        if (string.IsNullOrWhiteSpace(executable))
        {
            return new JFrogCliLoginResult(
                attempted: false,
                succeeded: false,
                skipped: true,
                executablePath: null,
                message: "JFrog CLI executable 'jf' was not found on PATH.");
        }

        if (!_host.ShouldProcess(registration.RepositoryName, "Run JFrog CLI browser login"))
        {
            return new JFrogCliLoginResult(
                attempted: false,
                succeeded: false,
                skipped: true,
                executablePath: executable,
                message: "JFrog CLI browser login was skipped by ShouldProcess.");
        }

        _host.WriteWarning("PSPublishModule will run 'jf login'. Complete the JFrog browser/SSO flow, then PSPublishModule will retry the repository probe to see whether PSResourceGet can use that session.");
        var effectiveTimeout = timeout ?? GetJFrogCliLoginTimeout();
        var result = _processRunner.RunAsync(
            new ProcessRunRequest(
                executable!,
                Environment.CurrentDirectory,
                new[] { "login" },
                effectiveTimeout,
                captureOutput: true,
                captureError: true)).GetAwaiter().GetResult();

        var message = BuildProcessMessage(result);
        if (result.Succeeded)
        {
            return new JFrogCliLoginResult(
                attempted: true,
                succeeded: true,
                skipped: false,
                executable,
                string.IsNullOrWhiteSpace(message)
                    ? "JFrog CLI browser login completed successfully."
                    : message);
        }

        return new JFrogCliLoginResult(
            attempted: true,
            succeeded: false,
            skipped: false,
            executable,
            string.IsNullOrWhiteSpace(message)
                ? $"JFrog CLI browser login failed with exit code {result.ExitCode}."
                : message);
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

        if (result.CredentialProviderSessionPrimeAttempted && result.CredentialProviderSessionPrimeSucceeded)
            _host.WriteVerbose(result.CredentialProviderSessionPrimeMessage ?? "Azure Artifacts Credential Provider session priming succeeded.");
        else if (result.CredentialProviderSessionPrimeAttempted)
            _host.WriteWarning(result.CredentialProviderSessionPrimeMessage ?? "Azure Artifacts Credential Provider session priming did not succeed.");
        else if (result.CredentialProviderSessionPrimeSkipped && !string.IsNullOrWhiteSpace(result.CredentialProviderSessionPrimeMessage))
            _host.WriteVerbose(result.CredentialProviderSessionPrimeMessage!);

        if (result.JFrogCliLoginAttempted && result.JFrogCliLoginSucceeded)
            _host.WriteVerbose(result.JFrogCliLoginMessage ?? "JFrog CLI browser login succeeded.");
        else if (result.JFrogCliLoginAttempted)
            _host.WriteWarning(result.JFrogCliLoginMessage ?? "JFrog CLI browser login did not succeed.");
        else if (result.JFrogCliLoginSkipped && !string.IsNullOrWhiteSpace(result.JFrogCliLoginMessage))
            _host.WriteVerbose(result.JFrogCliLoginMessage!);

        _host.WriteVerbose($"Bootstrap mode used: {result.BootstrapModeUsed}; credential source: {result.CredentialSource}.");
        _host.WriteVerbose($"Repository registration requested {result.ToolRequested}; successful path: {result.ToolUsed}.");

        if (result.ToolRequested == RepositoryRegistrationTool.Auto &&
            result.ToolUsed == RepositoryRegistrationTool.PowerShellGet)
        {
            _host.WriteVerbose("Auto registration fell back to PowerShellGet, so Install-Module is the current native path on this machine.");
        }

        if (result.BootstrapModeRequested == PrivateGalleryBootstrapMode.ExistingSession &&
            string.Equals(result.Provider, "AzureArtifacts", StringComparison.OrdinalIgnoreCase) &&
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

    private static bool IsPrereleaseVersion(string version)
        => !string.IsNullOrWhiteSpace(version) &&
           version.IndexOf("-", StringComparison.Ordinal) >= 0;

    private static bool IsHeadlessAutomation()
    {
        if (!Environment.UserInteractive)
            return true;

        foreach (var name in new[] { "CI", "GITHUB_ACTIONS", "TF_BUILD", "BUILD_BUILDID" })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "1", StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(name, "BUILD_BUILDID", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(value))
                return true;
        }

        return false;
    }

    private static TimeSpan GetCredentialProviderSessionPrimeTimeout()
    {
        var raw = Environment.GetEnvironmentVariable(CredentialProviderTimeoutMinutesEnvironmentVariable);
        if (int.TryParse(raw, out var minutes) && minutes > 0)
            return TimeSpan.FromMinutes(Math.Min(minutes, 1440));

        return TimeSpan.FromMinutes(10);
    }

    private static TimeSpan GetJFrogCliLoginTimeout()
    {
        var raw = Environment.GetEnvironmentVariable(JFrogCliTimeoutMinutesEnvironmentVariable);
        if (int.TryParse(raw, out var minutes) && minutes > 0)
            return TimeSpan.FromMinutes(Math.Min(minutes, 1440));

        return TimeSpan.FromMinutes(7);
    }

    private static string? ResolveOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory, fileName);
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
            catch
            {
                // Ignore malformed PATH segments.
            }
        }

        return File.Exists(fileName) ? Path.GetFullPath(fileName) : null;
    }

    private static string BuildProcessMessage(ProcessRunResult result)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(result.StdOut))
            parts.Add(result.StdOut.Trim());
        if (!string.IsNullOrWhiteSpace(result.StdErr))
            parts.Add(result.StdErr.Trim());
        if (result.TimedOut)
            parts.Add("Process timed out.");
        if (!result.Succeeded)
            parts.Add($"ExitCode={result.ExitCode}.");

        return string.Join(" ", parts)
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Trim();
    }

    internal static bool IsMissingDotNetRuntimeFailure(ProcessRunResult result)
    {
        if (result is null || result.Succeeded || result.TimedOut)
            return false;

        if (result.ExitCode == MissingDotNetRuntimeExitCode)
            return true;

        var message = $"{result.StdOut} {result.StdErr}";
        return message.IndexOf("You must install .NET to run this application", StringComparison.OrdinalIgnoreCase) >= 0 ||
               message.IndexOf("apphost_version", StringComparison.OrdinalIgnoreCase) >= 0 ||
               message.IndexOf("missing_runtime=true", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    internal static string[] SelectCredentialProviderPaths(IEnumerable<string>? paths)
    {
        var candidates = (paths ?? Array.Empty<string>())
            .Where(static path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(GetCredentialProviderPathRank)
            .ThenBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return candidates;
    }

    private static int GetCredentialProviderPathRank(string path)
    {
        if (path.EndsWith("CredentialProvider.Microsoft.exe", StringComparison.OrdinalIgnoreCase) &&
            HasPathSegment(path, "netcore"))
        {
            return 0;
        }

        if (path.EndsWith("CredentialProvider.Microsoft.exe", StringComparison.OrdinalIgnoreCase) &&
            HasPathSegment(path, "netfx"))
        {
            return 1;
        }

        if (path.EndsWith("CredentialProvider.Microsoft.exe", StringComparison.OrdinalIgnoreCase))
            return 2;

        if (path.EndsWith("CredentialProvider.Microsoft.dll", StringComparison.OrdinalIgnoreCase))
            return 3;

        return 4;
    }

    private static bool HasPathSegment(string path, string segment)
        => path.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
            .Any(part => string.Equals(part, segment, StringComparison.OrdinalIgnoreCase));
}
