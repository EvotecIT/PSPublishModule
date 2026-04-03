using System;

namespace PowerForge;

internal static class ModulePipelineRunnerDefaults
{
    internal static ModulePipelineRunnerServices Create(
        ILogger logger,
        IPowerShellRunner? powerShellRunner,
        IModuleDependencyMetadataProvider? moduleDependencyMetadataProvider,
        IModulePipelineHostedOperations? hostedOperations,
        IModuleManifestMutator? manifestMutator,
        IMissingFunctionAnalysisService? missingFunctionAnalysisService,
        IScriptFunctionExportDetector? scriptFunctionExportDetector)
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
            scriptFunctionExportDetector ?? new PowerShellScriptFunctionExportDetector());
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
        IScriptFunctionExportDetector scriptFunctionExportDetector)
    {
        PowerShellRunner = powerShellRunner ?? throw new ArgumentNullException(nameof(powerShellRunner));
        ModuleDependencyMetadataProvider = moduleDependencyMetadataProvider ?? throw new ArgumentNullException(nameof(moduleDependencyMetadataProvider));
        HostedOperations = hostedOperations ?? throw new ArgumentNullException(nameof(hostedOperations));
        ManifestMutator = manifestMutator ?? throw new ArgumentNullException(nameof(manifestMutator));
        MissingFunctionAnalysisService = missingFunctionAnalysisService ?? throw new ArgumentNullException(nameof(missingFunctionAnalysisService));
        ScriptFunctionExportDetector = scriptFunctionExportDetector ?? throw new ArgumentNullException(nameof(scriptFunctionExportDetector));
    }

    internal IPowerShellRunner PowerShellRunner { get; }
    internal IModuleDependencyMetadataProvider ModuleDependencyMetadataProvider { get; }
    internal IModulePipelineHostedOperations HostedOperations { get; }
    internal IModuleManifestMutator ManifestMutator { get; }
    internal IMissingFunctionAnalysisService MissingFunctionAnalysisService { get; }
    internal IScriptFunctionExportDetector ScriptFunctionExportDetector { get; }
}
