using System;
using System.Collections.Generic;

namespace PowerForge;

internal sealed class PrivateModuleWorkflowService
{
    private readonly IPrivateGalleryHost _host;
    private readonly PrivateGalleryService _privateGalleryService;
    private readonly ILogger _logger;
    private readonly Func<PrivateModuleDependencyExecutionRequest, IReadOnlyList<ModuleDependencyInstallResult>> _dependencyExecutor;
    private readonly Func<ModuleRepositoryRegistrationResult, RepositoryCredential?, bool, RepositoryAccessProbeResult> _accessProbeExecutor;

    public PrivateModuleWorkflowService(
        IPrivateGalleryHost host,
        PrivateGalleryService privateGalleryService,
        ILogger logger,
        Func<PrivateModuleDependencyExecutionRequest, IReadOnlyList<ModuleDependencyInstallResult>>? dependencyExecutor = null,
        Func<ModuleRepositoryRegistrationResult, RepositoryCredential?, bool, RepositoryAccessProbeResult>? accessProbeExecutor = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _privateGalleryService = privateGalleryService ?? throw new ArgumentNullException(nameof(privateGalleryService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dependencyExecutor = dependencyExecutor ?? ExecuteDependencies;
        _accessProbeExecutor = accessProbeExecutor ?? _privateGalleryService.ProbeRepositoryAccessWithOptionalSessionPrime;
    }

    public PrivateModuleWorkflowResult Execute(PrivateModuleWorkflowRequest request, Func<string, string, bool> shouldProcess)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (shouldProcess is null)
            throw new ArgumentNullException(nameof(shouldProcess));

        var modules = _privateGalleryService.BuildDependencies(
            request.ModuleNames,
            request.RequiredVersions,
            request.MinimumVersions,
            request.MinimumVersionInclusivity,
            request.MaximumVersions,
            request.MaximumVersionInclusivity,
            request.InstallScope,
            request.InstallScopes);
        var repositoryName = request.RepositoryName;
        RepositoryCredential? credential = null;
        var preferPowerShellGet = false;
        var usePrivateGallery = request.UseAzureArtifacts;
        var useMicrosoftArtifactRegistry = request.UseMicrosoftArtifactRegistry;
        var managedRepositorySource = request.ManagedRepositorySource;
        var effectiveTransport = request.DeliveryTransport;

        if (usePrivateGallery && useMicrosoftArtifactRegistry)
            throw new ArgumentException("Choose either a private gallery provider or Microsoft Artifact Registry, not both.", nameof(request));

        if (usePrivateGallery && UsesManagedPrivateGalleryPath(request, managedRepositorySource))
        {
            _privateGalleryService.EnsureProviderSupported(request.Provider);

            var endpoint = PrivateGalleryRepositoryEndpoints.Create(
                request.Provider,
                request.AzureDevOpsOrganization,
                request.AzureDevOpsProject,
                request.AzureArtifactsFeed,
                request.RepositoryName,
                request.Repository,
                request.RepositoryUri,
                request.RepositorySourceUri,
                request.RepositoryPublishUri,
                request.JFrogBaseUri,
                request.JFrogRepository);

            repositoryName = endpoint.RepositoryName;
            managedRepositorySource = endpoint.PSResourceGetUri;
            effectiveTransport = ModuleStateDeliveryTransport.ManagedModule;
            credential = _privateGalleryService.ResolveOptionalCredential(
                repositoryName,
                request.CredentialUserName,
                request.CredentialSecret,
                request.CredentialSecretFilePath,
                request.PromptForCredential);
        }
        else if (usePrivateGallery)
        {
            _privateGalleryService.EnsureProviderSupported(request.Provider);

            var endpoint = PrivateGalleryRepositoryEndpoints.Create(
                request.Provider,
                request.AzureDevOpsOrganization,
                request.AzureDevOpsProject,
                request.AzureArtifactsFeed,
                request.RepositoryName,
                request.Repository,
                request.RepositoryUri,
                request.RepositorySourceUri,
                request.RepositoryPublishUri,
                request.JFrogBaseUri,
                request.JFrogRepository);
            managedRepositorySource = endpoint.PSResourceGetUri;
            var prerequisiteInstall = _privateGalleryService.EnsureBootstrapPrerequisites(
                request.InstallPrerequisites,
                request.BootstrapMode,
                includeAzureArtifactsCredentialProvider: endpoint.Provider == PrivateGalleryProvider.AzureArtifacts,
                artefactsRepositoryName: endpoint.RepositoryName,
                artefactsPSResourceGetUri: endpoint.PSResourceGetUri,
                artefactsPowerShellGetSourceUri: endpoint.PowerShellGetSourceUri);
            repositoryName = endpoint.RepositoryName;

            var credentialResolution = _privateGalleryService.ResolveCredential(
                repositoryName,
                request.BootstrapMode,
                request.CredentialUserName,
                request.CredentialSecret,
                request.CredentialSecretFilePath,
                request.PromptForCredential,
                prerequisiteInstall.Status,
                !_host.IsWhatIfRequested,
                endpoint.Provider);
            credential = credentialResolution.Credential;

            var registration = _privateGalleryService.EnsureAzureArtifactsRepositoryRegistered(
                request.AzureDevOpsOrganization,
                request.AzureDevOpsProject,
                request.AzureArtifactsFeed,
                request.RepositoryName,
                request.Tool,
                request.Trusted,
                request.Priority,
                request.BootstrapMode,
                credentialResolution.BootstrapModeUsed,
                credentialResolution.CredentialSource,
                credential,
                prerequisiteInstall.Status,
                shouldProcessAction: GetRepositoryAction(request.Operation, request.Tool),
                provider: endpoint.Provider,
                repository: request.Repository,
                repositoryUri: request.RepositoryUri,
                repositorySourceUri: request.RepositorySourceUri,
                repositoryPublishUri: request.RepositoryPublishUri,
                jfrogBaseUri: request.JFrogBaseUri,
                jfrogRepository: request.JFrogRepository);
            registration.InstalledPrerequisites = prerequisiteInstall.InstalledPrerequisites;
            registration.PrerequisiteInstallMessages = prerequisiteInstall.Messages;

            if (!registration.RegistrationPerformed)
            {
                _host.WriteWarning(GetSkippedRegistrationMessage(request.Operation, registration.RepositoryName));
                return new PrivateModuleWorkflowResult
                {
                    OperationPerformed = false,
                    RepositoryName = registration.RepositoryName,
                    DependencyResults = Array.Empty<ModuleDependencyInstallResult>()
                };
            }

            var probe = _accessProbeExecutor(registration, credential, !_host.IsWhatIfRequested);
            registration.AccessProbePerformed = true;
            registration.AccessProbeSucceeded = probe.Succeeded;
            registration.AccessProbeTool = probe.Tool;
            registration.AccessProbeMessage = probe.Message;

            _privateGalleryService.WriteRegistrationSummary(registration);

            if (!probe.Succeeded)
            {
                var message = string.IsNullOrWhiteSpace(probe.Message)
                    ? $"Repository access probe failed via {probe.Tool}."
                    : $"Repository access probe failed via {probe.Tool}: {probe.Message}";
                var refreshCommand = string.IsNullOrWhiteSpace(request.ProfileName)
                    ? "Initialize-ModuleRepository for this private gallery"
                    : $"Initialize-ModuleRepository -ProfileName '{request.ProfileName}' -InstallPrerequisites";
                throw new InvalidOperationException($"{message} Re-run {refreshCommand} or retry the command with valid private gallery credentials.");
            }

            _host.WriteVerbose($"Repository '{registration.RepositoryName}' is ready for {GetOperationNoun(request.Operation)}.");

            if (credential is null &&
                !registration.InstallPSResourceReady &&
                !registration.InstallModuleReady)
            {
                var hint = string.IsNullOrWhiteSpace(registration.RecommendedBootstrapCommand)
                    ? string.Empty
                    : $" Recommended next step: {registration.RecommendedBootstrapCommand}";
                throw new InvalidOperationException(
                    $"Repository '{registration.RepositoryName}' was registered, but no native {GetOperationNoun(request.Operation)} path is ready for bootstrap mode {registration.BootstrapModeUsed}.{hint}");
            }

            preferPowerShellGet = credential is null &&
                                  string.Equals(registration.PreferredInstallCommand, "Install-Module", StringComparison.OrdinalIgnoreCase);
        }
        else if (useMicrosoftArtifactRegistry)
        {
            var prerequisiteInstall = _privateGalleryService.EnsureMicrosoftArtifactRegistryPrerequisites(
                request.InstallPrerequisites);
            var registration = _privateGalleryService.EnsureMicrosoftArtifactRegistryRegistered(
                request.RepositoryName,
                request.Tool,
                request.Trusted,
                request.Priority,
                prerequisiteInstall.Status,
                shouldProcessAction: GetRepositoryAction(request.Operation, RepositoryRegistrationTool.PSResourceGet));
            registration.InstalledPrerequisites = prerequisiteInstall.InstalledPrerequisites;
            registration.PrerequisiteInstallMessages = prerequisiteInstall.Messages;
            repositoryName = registration.RepositoryName;
            managedRepositorySource = registration.PSResourceGetUri;

            if (!registration.RegistrationPerformed)
            {
                _host.WriteWarning(GetSkippedRegistrationMessage(request.Operation, registration.RepositoryName));
                return new PrivateModuleWorkflowResult
                {
                    OperationPerformed = false,
                    RepositoryName = registration.RepositoryName,
                    DependencyResults = Array.Empty<ModuleDependencyInstallResult>()
                };
            }

            var probe = _accessProbeExecutor(registration, credential, false);
            registration.AccessProbePerformed = true;
            registration.AccessProbeSucceeded = probe.Succeeded;
            registration.AccessProbeTool = probe.Tool;
            registration.AccessProbeMessage = probe.Message;

            _privateGalleryService.WriteRegistrationSummary(registration);

            if (!probe.Succeeded)
            {
                var message = string.IsNullOrWhiteSpace(probe.Message)
                    ? $"Microsoft Artifact Registry access probe failed via {probe.Tool}."
                    : $"Microsoft Artifact Registry access probe failed via {probe.Tool}: {probe.Message}";
                throw new InvalidOperationException(message);
            }
        }
        if (effectiveTransport == ModuleStateDeliveryTransport.Auto)
        {
            effectiveTransport = usePrivateGallery || useMicrosoftArtifactRegistry || string.IsNullOrWhiteSpace(managedRepositorySource)
                ? ModuleStateDeliveryTransport.PrivateModule
                : ModuleStateDeliveryTransport.ManagedModule;
        }

        if (effectiveTransport == ModuleStateDeliveryTransport.PrivateModule)
        {
            if (!string.IsNullOrWhiteSpace(request.VersionPolicy))
            {
                throw new InvalidOperationException(
                    "VersionPolicy requires -Transport ManagedModule or Auto with a repository source URI/path. The private-module compatibility transport supports RequiredVersion, MinimumVersion, and MaximumVersion.");
            }

            ThrowIfManagedOnlyOptionsRequested(request);
        }

        if (!shouldProcess(
                $"{modules.Count} module(s) from repository '{repositoryName}'",
                GetFinalAction(request.Operation, request.Force)))
        {
            return new PrivateModuleWorkflowResult
            {
                OperationPerformed = false,
                RepositoryName = repositoryName,
                DependencyResults = Array.Empty<ModuleDependencyInstallResult>()
            };
        }

        if (!usePrivateGallery && !useMicrosoftArtifactRegistry)
        {
            credential = _privateGalleryService.ResolveOptionalCredential(
                repositoryName,
                request.CredentialUserName,
                request.CredentialSecret,
                request.CredentialSecretFilePath,
                request.PromptForCredential);
        }

        if (effectiveTransport == ModuleStateDeliveryTransport.ManagedModule)
        {
            var managedResults = ExecuteManagedDependencies(request, modules, repositoryName, managedRepositorySource, credential);
            return new PrivateModuleWorkflowResult
            {
                OperationPerformed = true,
                RepositoryName = repositoryName,
                DependencyResults = managedResults
            };
        }

        var results = _dependencyExecutor(new PrivateModuleDependencyExecutionRequest
        {
            Operation = request.Operation,
            Modules = modules,
            RepositoryName = repositoryName,
            Credential = credential,
            Prerelease = request.Prerelease,
            Force = request.Force,
            PreferPowerShellGet = preferPowerShellGet
        });

        return new PrivateModuleWorkflowResult
        {
            OperationPerformed = true,
            RepositoryName = repositoryName,
            DependencyResults = results
        };
    }

    private IReadOnlyList<ModuleDependencyInstallResult> ExecuteManagedDependencies(
        PrivateModuleWorkflowRequest request,
        IReadOnlyList<ModuleDependency> modules,
        string repositoryName,
        string? repositorySource,
        RepositoryCredential? credential)
    {
        if (string.IsNullOrWhiteSpace(repositorySource))
        {
            throw new InvalidOperationException(
                "Managed private module delivery requires a repository source URI or local feed path. Use RepositoryUri/RepositorySourceUri, a saved profile with a source URI, or pass a local/URI Repository value.");
        }

        var repository = new ManagedModuleRepository(repositoryName, repositorySource!);
        var installService = new ManagedModuleInstallService(_logger);
        var updateService = new ManagedModuleUpdateService(_logger);
        var results = new List<ModuleDependencyInstallResult>(modules.Count);
        foreach (var module in modules)
        {
            results.Add(request.Operation == PrivateModuleWorkflowOperation.Install
                ? ExecuteManagedInstall(request, module, repository, credential, installService)
                : ExecuteManagedUpdate(request, module, repository, credential, updateService));
        }

        return results;
    }

    private static ModuleDependencyInstallResult ExecuteManagedInstall(
        PrivateModuleWorkflowRequest workflow,
        ModuleDependency module,
        ManagedModuleRepository repository,
        RepositoryCredential? credential,
        ManagedModuleInstallService installService)
    {
        var result = installService.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = repository,
            Name = module.Name,
            Version = module.RequiredVersion,
            MinimumVersion = module.MinimumVersion,
            MaximumVersion = module.MaximumVersion,
            VersionPolicy = workflow.VersionPolicy,
            IncludePrerelease = workflow.Prerelease,
            Scope = ResolveManagedScope(workflow),
            ShellEdition = workflow.ManagedShellEdition,
            ModuleRoot = workflow.ManagedModuleRoot,
            PackageCacheDirectory = workflow.ManagedPackageCacheDirectory,
            Credential = credential,
            Force = workflow.Force,
            AllowClobber = workflow.ManagedAllowClobber,
            AcceptLicense = workflow.ManagedAcceptLicense,
            SkipDependencyCheck = workflow.ManagedSkipDependencyCheck
        }).GetAwaiter().GetResult();

        return MapInstallResult(result);
    }

    private static ModuleDependencyInstallResult ExecuteManagedUpdate(
        PrivateModuleWorkflowRequest workflow,
        ModuleDependency module,
        ManagedModuleRepository repository,
        RepositoryCredential? credential,
        ManagedModuleUpdateService updateService)
    {
        var result = updateService.UpdateAsync(new ManagedModuleUpdateRequest
        {
            Repository = repository,
            Name = module.Name,
            Version = module.RequiredVersion,
            MinimumVersion = module.MinimumVersion,
            MaximumVersion = module.MaximumVersion,
            VersionPolicy = workflow.VersionPolicy,
            IncludePrerelease = workflow.Prerelease,
            Scope = ResolveManagedScope(workflow),
            ShellEdition = workflow.ManagedShellEdition,
            ModuleRoot = workflow.ManagedModuleRoot,
            PackageCacheDirectory = workflow.ManagedPackageCacheDirectory,
            Credential = credential,
            Force = workflow.Force,
            AllowClobber = workflow.ManagedAllowClobber,
            AcceptLicense = workflow.ManagedAcceptLicense,
            SkipDependencyCheck = workflow.ManagedSkipDependencyCheck,
            SourcePolicy = workflow.ManagedRequireSourceMatch ? new ManagedModuleSourcePolicy() : null,
            AllowLoadedModuleUpdate = workflow.ManagedAllowLoadedModuleUpdate
        }).GetAwaiter().GetResult();

        return MapUpdateResult(result);
    }

    private static ManagedModuleInstallScope ResolveManagedScope(PrivateModuleWorkflowRequest workflow)
        => string.IsNullOrWhiteSpace(workflow.ManagedModuleRoot)
            ? workflow.ManagedScope
            : ManagedModuleInstallScope.Custom;

    private static bool UsesManagedPrivateGalleryPath(PrivateModuleWorkflowRequest request, string? managedRepositorySource)
    {
        if (request.DeliveryTransport == ModuleStateDeliveryTransport.ManagedModule)
            return true;
        if (request.DeliveryTransport != ModuleStateDeliveryTransport.Auto)
            return false;

        return request.Provider == PrivateGalleryProvider.NuGet ||
               HasManagedOnlyOptionsRequested(request) ||
               !string.IsNullOrWhiteSpace(managedRepositorySource);
    }

    private static ModuleDependencyInstallResult MapInstallResult(ManagedModuleInstallResult result)
        => new(
            result.Name,
            result.Status == ManagedModuleInstallStatus.AlreadyInstalled ? result.Version : null,
            result.Version,
            ResolveRequestedVersion(result.RequestedVersion, result.MinimumVersion, result.MaximumVersion, result.VersionPolicy),
            result.Status == ManagedModuleInstallStatus.AlreadyInstalled
                ? ModuleDependencyInstallStatus.Satisfied
                : ModuleDependencyInstallStatus.Installed,
            "ManagedModule",
            null);

    private static ModuleDependencyInstallResult MapUpdateResult(ManagedModuleUpdateResult result)
        => new(
            result.Name,
            result.PreviousVersion,
            result.TargetVersion,
            ResolveRequestedVersion(result.RequestedVersion, result.MinimumVersion, result.MaximumVersion, result.VersionPolicy),
            result.Status switch
            {
                ManagedModuleUpdateStatus.UpToDate => ModuleDependencyInstallStatus.Satisfied,
                ManagedModuleUpdateStatus.InstalledMissing => ModuleDependencyInstallStatus.Installed,
                _ => ModuleDependencyInstallStatus.Updated
            },
            "ManagedModule",
            result.SourcePolicyReason);

    private static string? ResolveRequestedVersion(string? exact, string? minimum, string? maximum, string? policy)
    {
        if (!string.IsNullOrWhiteSpace(exact))
            return exact;
        if (!string.IsNullOrWhiteSpace(policy))
            return policy;
        if (!string.IsNullOrWhiteSpace(minimum) || !string.IsNullOrWhiteSpace(maximum))
            return $"{minimum ?? string.Empty}..{maximum ?? string.Empty}";
        return null;
    }

    private static void ThrowIfManagedOnlyOptionsRequested(PrivateModuleWorkflowRequest request)
    {
        var managedOnly = CollectManagedOnlyOptions(request);
        if (managedOnly.Count == 0)
            return;

        throw new InvalidOperationException(
            $"{string.Join(", ", managedOnly)} requires -Transport ManagedModule or Auto with a repository source URI/path.");
    }

    private static bool HasManagedOnlyOptionsRequested(PrivateModuleWorkflowRequest request)
        => CollectManagedOnlyOptions(request).Count > 0;

    private static List<string> CollectManagedOnlyOptions(PrivateModuleWorkflowRequest request)
    {
        var managedOnly = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.ManagedModuleRoot))
            managedOnly.Add("ModuleRoot");
        if (!string.IsNullOrWhiteSpace(request.ManagedPackageCacheDirectory))
            managedOnly.Add("PackageCacheDirectory");
        if (!string.IsNullOrWhiteSpace(request.ManagedRepositorySource))
            managedOnly.Add("Repository source URI/path");
        if (request.ManagedAllowClobber)
            managedOnly.Add("AllowClobber");
        if (request.ManagedAcceptLicense)
            managedOnly.Add("AcceptLicense");
        if (request.ManagedSkipDependencyCheck)
            managedOnly.Add("SkipDependencyCheck");
        if (request.ManagedRequireSourceMatch)
            managedOnly.Add("RequireSourceMatch");
        if (request.ManagedAllowLoadedModuleUpdate)
            managedOnly.Add("AllowLoadedModuleUpdate");

        return managedOnly;
    }

    private IReadOnlyList<ModuleDependencyInstallResult> ExecuteDependencies(PrivateModuleDependencyExecutionRequest request)
    {
        var installer = new ModuleDependencyInstaller(new PowerShellRunner(), _logger);
        return request.Operation == PrivateModuleWorkflowOperation.Install
            ? installer.EnsureInstalled(
                request.Modules,
                force: request.Force,
                repository: request.RepositoryName,
                credential: request.Credential,
                prerelease: request.Prerelease,
                preferPowerShellGet: request.PreferPowerShellGet,
                timeoutPerModule: TimeSpan.FromMinutes(10))
            : installer.EnsureUpdated(
                request.Modules,
                repository: request.RepositoryName,
                credential: request.Credential,
                prerelease: request.Prerelease,
                preferPowerShellGet: request.PreferPowerShellGet,
                timeoutPerModule: TimeSpan.FromMinutes(10));
    }

    private static string GetRepositoryAction(PrivateModuleWorkflowOperation operation, RepositoryRegistrationTool tool)
    {
        var verb = operation == PrivateModuleWorkflowOperation.Install ? "Ensure" : "Update";
        return tool == RepositoryRegistrationTool.Auto
            ? $"{verb} module repository registration using Auto (prefer PSResourceGet, fall back to PowerShellGet)"
            : $"{verb} module repository registration using {tool}";
    }

    private static string GetSkippedRegistrationMessage(PrivateModuleWorkflowOperation operation, string repositoryName)
    {
        return operation == PrivateModuleWorkflowOperation.Install
            ? $"Repository '{repositoryName}' was not registered because the operation was skipped. Module installation was not attempted."
            : $"Repository '{repositoryName}' was not refreshed because the operation was skipped. Module update was not attempted.";
    }

    private static string GetOperationNoun(PrivateModuleWorkflowOperation operation)
        => operation == PrivateModuleWorkflowOperation.Install ? "installation" : "update";

    private static string GetFinalAction(PrivateModuleWorkflowOperation operation, bool force)
    {
        if (operation == PrivateModuleWorkflowOperation.Install)
            return force ? "Install or reinstall private modules" : "Install private modules";

        return "Update private modules";
    }
}
