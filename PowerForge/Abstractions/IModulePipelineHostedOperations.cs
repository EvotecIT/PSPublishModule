using System.Collections.Generic;

namespace PowerForge;

internal interface IModulePipelineHostedOperations
{
    IReadOnlyList<ModuleDependencyInstallResult> EnsureDependenciesInstalled(
        ModuleDependency[] dependencies,
        ModuleSkipConfiguration? skipModules,
        bool force,
        string? repository,
        RepositoryCredential? credential,
        bool prerelease);

    DocumentationBuildResult BuildDocumentation(
        string moduleName,
        string stagingPath,
        string moduleManifestPath,
        DocumentationConfiguration documentation,
        BuildDocumentationConfiguration buildDocumentation,
        IModulePipelineProgressReporter progress,
        ModulePipelineStep? extractStep,
        ModulePipelineStep? writeStep,
        ModulePipelineStep? externalHelpStep);

    ModuleValidationReport ValidateModule(ModuleValidationSpec spec);

    void EnsureBinaryDependenciesValid(string moduleRoot, string powerShellEdition, string? modulePath, string? validationTarget);

    ModuleTestSuiteResult RunModuleTestSuite(ModuleTestSuiteSpec spec);

    ModulePublishResult PublishModule(
        PublishConfiguration publish,
        ModulePipelinePlan plan,
        ModuleBuildResult buildResult,
        IReadOnlyList<ArtefactBuildResult> artefactResults,
        bool includeScriptFolders);
}
