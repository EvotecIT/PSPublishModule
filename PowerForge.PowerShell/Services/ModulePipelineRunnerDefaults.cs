using System;

namespace PowerForge;

internal static class ModulePipelineRunnerDefaults
{
    internal delegate ProjectBuildHostExecutionResult ModulePackageBuildExecutor(
        ProjectBuildHostRequest request,
        ProjectBuildConfiguration? configuration,
        string? configPath);

    internal delegate GitHubReleasePublishResult ModuleGitHubReleasePublisher(GitHubReleasePublishRequest request);

    internal static ModulePipelineRunnerServices Create(
        ILogger logger,
        IPowerShellRunner? powerShellRunner,
        IModuleDependencyMetadataProvider? moduleDependencyMetadataProvider,
        IModulePipelineHostedOperations? hostedOperations,
        IModuleManifestMutator? manifestMutator,
        IMissingFunctionAnalysisService? missingFunctionAnalysisService,
        IScriptFunctionExportDetector? scriptFunctionExportDetector,
        ModulePackageBuildExecutor? packageBuildExecutor = null,
        ModuleGitHubReleasePublisher? gitHubReleasePublisher = null)
    {
        if (logger is null)
            throw new ArgumentNullException(nameof(logger));

        var resolvedRunner = powerShellRunner ?? new PowerShellRunner();
        return new ModulePipelineRunnerServices(
            resolvedRunner,
            moduleDependencyMetadataProvider ?? new PowerShellModuleDependencyMetadataProvider(resolvedRunner, logger),
            hostedOperations ?? new PowerShellModulePipelineHostedOperations(resolvedRunner, logger),
            manifestMutator ?? new AstModuleManifestMutator(),
            missingFunctionAnalysisService ?? new PowerShellMissingFunctionAnalysisService(),
            scriptFunctionExportDetector ?? new PowerShellScriptFunctionExportDetector(),
            packageBuildExecutor ?? ((request, configuration, configPath) =>
            {
                var service = new ProjectBuildHostService(logger);
                return configuration is null
                    ? service.Execute(request)
                    : service.Execute(request, configuration, configPath ?? request.ConfigPath);
            }),
            gitHubReleasePublisher ?? (request => new GitHubReleasePublisher(logger).PublishRelease(request)));
    }
}

internal sealed class ModulePipelineRunnerServices
{
    internal ModulePipelineRunnerServices(
        IPowerShellRunner powerShellRunner,
        IModuleDependencyMetadataProvider moduleDependencyMetadataProvider,
        IModulePipelineHostedOperations hostedOperations,
        IModuleManifestMutator manifestMutator,
        IMissingFunctionAnalysisService missingFunctionAnalysisService,
        IScriptFunctionExportDetector scriptFunctionExportDetector,
        ModulePipelineRunnerDefaults.ModulePackageBuildExecutor packageBuildExecutor,
        ModulePipelineRunnerDefaults.ModuleGitHubReleasePublisher gitHubReleasePublisher)
    {
        PowerShellRunner = powerShellRunner ?? throw new ArgumentNullException(nameof(powerShellRunner));
        ModuleDependencyMetadataProvider = moduleDependencyMetadataProvider ?? throw new ArgumentNullException(nameof(moduleDependencyMetadataProvider));
        HostedOperations = hostedOperations ?? throw new ArgumentNullException(nameof(hostedOperations));
        ManifestMutator = manifestMutator ?? throw new ArgumentNullException(nameof(manifestMutator));
        MissingFunctionAnalysisService = missingFunctionAnalysisService ?? throw new ArgumentNullException(nameof(missingFunctionAnalysisService));
        ScriptFunctionExportDetector = scriptFunctionExportDetector ?? throw new ArgumentNullException(nameof(scriptFunctionExportDetector));
        PackageBuildExecutor = packageBuildExecutor ?? throw new ArgumentNullException(nameof(packageBuildExecutor));
        GitHubReleasePublisher = gitHubReleasePublisher ?? throw new ArgumentNullException(nameof(gitHubReleasePublisher));
    }

    internal IPowerShellRunner PowerShellRunner { get; }
    internal IModuleDependencyMetadataProvider ModuleDependencyMetadataProvider { get; }
    internal IModulePipelineHostedOperations HostedOperations { get; }
    internal IModuleManifestMutator ManifestMutator { get; }
    internal IMissingFunctionAnalysisService MissingFunctionAnalysisService { get; }
    internal IScriptFunctionExportDetector ScriptFunctionExportDetector { get; }
    internal ModulePipelineRunnerDefaults.ModulePackageBuildExecutor PackageBuildExecutor { get; }
    internal ModulePipelineRunnerDefaults.ModuleGitHubReleasePublisher GitHubReleasePublisher { get; }
}
