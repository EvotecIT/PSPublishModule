using System;
using System.Collections.Generic;

namespace PowerForge;

internal sealed class PrivateModuleWorkflowService
{
    private readonly IPrivateGalleryHost _host;
    private readonly PrivateGalleryService _privateGalleryService;
    private readonly ILogger _logger;
    private readonly Func<PrivateModuleDependencyExecutionRequest, IReadOnlyList<ModuleDependencyInstallResult>> _dependencyExecutor;

    public PrivateModuleWorkflowService(
        IPrivateGalleryHost host,
        PrivateGalleryService privateGalleryService,
        ILogger logger,
        Func<PrivateModuleDependencyExecutionRequest, IReadOnlyList<ModuleDependencyInstallResult>>? dependencyExecutor = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _privateGalleryService = privateGalleryService ?? throw new ArgumentNullException(nameof(privateGalleryService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dependencyExecutor = dependencyExecutor ?? ExecuteDependencies;
    }

    public PrivateModuleWorkflowResult Execute(PrivateModuleWorkflowRequest request, Func<string, string, bool> shouldProcess)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (shouldProcess is null)
            throw new ArgumentNullException(nameof(shouldProcess));

        var modules = _privateGalleryService.BuildDependencies(request.ModuleNames);
        var repositoryName = request.RepositoryName;
        RepositoryCredential? credential = null;
        var preferPowerShellGet = false;

        if (request.UseAzureArtifacts)
        {
            _privateGalleryService.EnsureProviderSupported(request.Provider);

            var endpoint = AzureArtifactsRepositoryEndpoints.Create(
                request.AzureDevOpsOrganization,
                request.AzureDevOpsProject,
                request.AzureArtifactsFeed,
                request.RepositoryName);
            var prerequisiteInstall = _privateGalleryService.EnsureBootstrapPrerequisites(request.InstallPrerequisites);
            repositoryName = endpoint.RepositoryName;

            var credentialResolution = _privateGalleryService.ResolveCredential(
                repositoryName,
                request.BootstrapMode,
                request.CredentialUserName,
                request.CredentialSecret,
                request.CredentialSecretFilePath,
                request.PromptForCredential,
                prerequisiteInstall.Status,
                !_host.IsWhatIfRequested);
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
                shouldProcessAction: GetRepositoryAction(request.Operation, request.Tool));
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

            _privateGalleryService.WriteRegistrationSummary(registration);
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
        else
        {
            credential = _privateGalleryService.ResolveOptionalCredential(
                repositoryName,
                request.CredentialUserName,
                request.CredentialSecret,
                request.CredentialSecretFilePath,
                request.PromptForCredential);
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
        var verb = operation == PrivateModuleWorkflowOperation.Install ? "Register" : "Update";
        return tool == RepositoryRegistrationTool.Auto
            ? $"{verb} module repository using Auto (prefer PSResourceGet, fall back to PowerShellGet)"
            : $"{verb} module repository using {tool}";
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
