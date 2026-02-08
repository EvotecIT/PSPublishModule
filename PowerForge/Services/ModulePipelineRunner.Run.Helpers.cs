using System.Collections.Generic;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private void EnsureBuildDependenciesInstalledIfNeeded(ModulePipelinePlan plan)
    {
        if (!plan.InstallMissingModules) return;

        try
        {
            _ = EnsureBuildDependenciesInstalled(plan);
        }
        catch (Exception ex)
        {
            _logger.Error($"Dependency installation failed. {ex.Message}");
            if (_logger.IsVerbose) _logger.Verbose(ex.ToString());
            throw;
        }
    }

    private static void SafeStart(IModulePipelineProgressReporter reporter, HashSet<string> startedKeys, ModulePipelineStep? step)
    {
        if (step is null) return;
        if (!string.IsNullOrWhiteSpace(step.Key)) startedKeys.Add(step.Key);
        try { reporter.StepStarting(step); } catch { /* best effort */ }
    }

    private static void SafeDone(IModulePipelineProgressReporter reporter, ModulePipelineStep? step)
    {
        if (step is null) return;
        try { reporter.StepCompleted(step); } catch { /* best effort */ }
    }

    private static void SafeFail(IModulePipelineProgressReporter reporter, ModulePipelineStep? step, Exception ex)
    {
        if (step is null) return;
        try { reporter.StepFailed(step, ex); } catch { /* best effort */ }
    }

    private static ModulePipelineResult BuildPipelineResult(
        ModulePipelinePlan plan,
        ModuleBuildResult buildResult,
        ModuleInstallerResult? installResult,
        DocumentationBuildResult? documentationResult,
        ProjectConsistencyReport? fileConsistencyReport,
        CheckStatus? fileConsistencyStatus,
        ProjectConversionResult? fileConsistencyEncodingFix,
        ProjectConversionResult? fileConsistencyLineEndingFix,
        PowerShellCompatibilityReport? compatibilityReport,
        ModuleValidationReport? validationReport,
        ModulePublishResult[] publishResults,
        ArtefactBuildResult[] artefactResults,
        FormatterResult[]? formattingStagingResults,
        FormatterResult[]? formattingProjectResults,
        ProjectConsistencyReport? projectRootFileConsistencyReport,
        CheckStatus? projectRootFileConsistencyStatus,
        ProjectConversionResult? projectRootFileConsistencyEncodingFix,
        ProjectConversionResult? projectRootFileConsistencyLineEndingFix,
        ModuleSigningResult? signingResult)
    {
        return new ModulePipelineResult(
            plan,
            buildResult,
            installResult,
            documentationResult,
            fileConsistencyReport,
            fileConsistencyStatus,
            fileConsistencyEncodingFix,
            fileConsistencyLineEndingFix,
            compatibilityReport,
            validationReport,
            publishResults,
            artefactResults,
            formattingStagingResults,
            formattingProjectResults,
            projectRootFileConsistencyReport,
            projectRootFileConsistencyStatus,
            projectRootFileConsistencyEncodingFix,
            projectRootFileConsistencyLineEndingFix,
            signingResult);
    }

    private static void NotifySkippedStepsOnFailure(
        IModulePipelineProgressReporterV2? reporterV2,
        IEnumerable<ModulePipelineStep> steps,
        HashSet<string> startedKeys)
    {
        if (reporterV2 is null) return;

        foreach (var step in steps)
        {
            if (step is null) continue;
            if (string.IsNullOrWhiteSpace(step.Key)) continue;
            if (startedKeys.Contains(step.Key)) continue;
            try { reporterV2.StepSkipped(step); } catch { /* best effort */ }
        }
    }
}
