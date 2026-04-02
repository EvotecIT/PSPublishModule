using System.Collections.Generic;

namespace PowerForge;

internal sealed class PowerShellModulePipelineHostedOperations : IModulePipelineHostedOperations
{
    private readonly ILogger _logger;

    internal PowerShellModulePipelineHostedOperations(ILogger logger)
    {
        _logger = logger ?? new NullLogger();
    }

    public IReadOnlyList<ModuleDependencyInstallResult> EnsureDependenciesInstalled(
        ModuleDependency[] dependencies,
        ModuleSkipConfiguration? skipModules,
        bool force,
        string? repository,
        RepositoryCredential? credential,
        bool prerelease)
    {
        var installer = new ModuleDependencyInstaller(new PowerShellRunner(), _logger);
        return installer.EnsureInstalled(
            dependencies: dependencies,
            skipModules: skipModules?.IgnoreModuleName,
            force: force,
            repository: repository,
            credential: credential,
            prerelease: prerelease);
    }

    public DocumentationBuildResult BuildDocumentation(
        string moduleName,
        string stagingPath,
        string moduleManifestPath,
        DocumentationConfiguration documentation,
        BuildDocumentationConfiguration buildDocumentation,
        IModulePipelineProgressReporter progress,
        ModulePipelineStep? extractStep,
        ModulePipelineStep? writeStep,
        ModulePipelineStep? externalHelpStep)
    {
        var engine = new DocumentationEngine(new PowerShellRunner(), _logger);
        return engine.BuildWithProgress(
            moduleName: moduleName,
            stagingPath: stagingPath,
            moduleManifestPath: moduleManifestPath,
            documentation: documentation,
            buildDocumentation: buildDocumentation,
            timeout: null,
            progress: progress,
            extractStep: extractStep,
            writeStep: writeStep,
            externalHelpStep: externalHelpStep);
    }

    public ModuleValidationReport ValidateModule(ModuleValidationSpec spec)
        => new ModuleValidationService(_logger).Run(spec);

    public void EnsureBinaryDependenciesValid(string moduleRoot, string powerShellEdition, string? modulePath, string? validationTarget)
    {
        var service = new BinaryDependencyPreflightService(_logger);
        var result = service.Analyze(moduleRoot, powerShellEdition);
        if (!result.HasIssues)
            return;

        throw new InvalidOperationException(
            BinaryDependencyPreflightService.BuildFailureMessage(
                result,
                modulePath,
                validationTarget));
    }

    public ModuleTestSuiteResult RunModuleTestSuite(ModuleTestSuiteSpec spec)
        => new ModuleTestSuiteService(new PowerShellRunner(), _logger).Run(spec);

    public ModulePublishResult PublishModule(
        PublishConfiguration publish,
        ModulePipelinePlan plan,
        ModuleBuildResult buildResult,
        IReadOnlyList<ArtefactBuildResult> artefactResults,
        bool includeScriptFolders)
        => new ModulePublisher(_logger).Publish(publish, plan, buildResult, artefactResults, includeScriptFolders);
}
