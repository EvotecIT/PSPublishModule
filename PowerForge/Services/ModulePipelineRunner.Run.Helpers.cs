using System;
using System.Collections.Generic;
using System.Linq;

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

    private ModulePipelineResult BuildPipelineResult(
        ModulePipelineSpec spec,
        ModulePipelinePlan plan,
        ModuleBuildResult buildResult,
        BuildDiagnostic[] automaticBinaryConflictDiagnostics,
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
        var diagnostics = new List<BuildDiagnostic>(BuildDiagnosticsFactory.CreatePipelineDiagnostics(
            fileConsistencyReport,
            plan.FileConsistencySettings,
            projectRootFileConsistencyReport,
            compatibilityReport,
            validationReport));
        diagnostics.AddRange(automaticBinaryConflictDiagnostics ?? Array.Empty<BuildDiagnostic>());
        diagnostics.AddRange(CreateBinaryConflictDiagnostics(spec.Diagnostics, plan, buildResult));
        var diagnosticsBaseline = BuildDiagnosticsBaselineStore.Evaluate(
            plan.ProjectRoot,
            spec.Diagnostics,
            diagnostics.ToArray());
        var diagnosticsPolicy = BuildDiagnosticsPolicyEvaluator.Evaluate(
            spec.Diagnostics,
            diagnostics.ToArray(),
            diagnosticsBaseline);

        var result = new ModulePipelineResult(
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
            diagnostics.ToArray(),
            diagnosticsBaseline,
            diagnosticsPolicy,
            publishResults,
            artefactResults,
            formattingStagingResults,
            formattingProjectResults,
            projectRootFileConsistencyReport,
            projectRootFileConsistencyStatus,
            projectRootFileConsistencyEncodingFix,
            projectRootFileConsistencyLineEndingFix,
            signingResult);

        if (diagnosticsPolicy?.PolicyViolated == true)
            throw new ModulePipelineDiagnosticsPolicyException(result, diagnosticsPolicy, diagnosticsPolicy.FailureReason);

        return result;
    }

    private BuildDiagnostic[] CreateBinaryConflictDiagnostics(
        ModulePipelineDiagnosticsOptions? options,
        ModulePipelinePlan plan,
        ModuleBuildResult buildResult)
    {
        var roots = (options?.BinaryConflictSearchRoots ?? Array.Empty<string>())
            .Where(static root => !string.IsNullOrWhiteSpace(root))
            .Select(static root => root.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (roots.Length == 0)
            return Array.Empty<BuildDiagnostic>();

        var editions = (plan.CompatiblePSEditions ?? Array.Empty<string>())
            .Where(static edition => !string.IsNullOrWhiteSpace(edition))
            .Select(static edition => edition.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (editions.Length == 0)
            editions = new[] { "Core" };

        var detector = new BinaryConflictDetectionService(_logger);
        var diagnostics = new List<BuildDiagnostic>();
        foreach (var edition in editions)
        {
            var result = detector.Analyze(
                buildResult.StagingPath,
                edition,
                currentModuleName: plan.ModuleName,
                searchRoots: roots);
            diagnostics.AddRange(BuildDiagnosticsFactory.CreateBinaryConflictDiagnostics(result));
        }

        return diagnostics.ToArray();
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
