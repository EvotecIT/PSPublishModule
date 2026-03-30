using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private ModuleDependencyInstallResult[] EnsureBuildDependenciesInstalledIfNeeded(ModulePipelinePlan plan)
    {
        if (!plan.InstallMissingModules) return Array.Empty<ModuleDependencyInstallResult>();

        try
        {
            return EnsureBuildDependenciesInstalled(plan);
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
        ModuleSigningResult? signingResult,
        ModuleDependencyInstallResult[] dependencyInstallResults,
        ModuleBuildPipeline.StagingResult stagingResult,
        MergeExecutionResult mergeExecution,
        string? projectManifestSyncMessage)
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
            signingResult,
            BuildOwnerNotes(
                plan,
                buildResult,
                documentationResult,
                dependencyInstallResults,
                stagingResult,
                mergeExecution,
                projectManifestSyncMessage));

        if (diagnosticsPolicy?.PolicyViolated == true)
            throw new ModulePipelineDiagnosticsPolicyException(result, diagnosticsPolicy, diagnosticsPolicy.FailureReason);

        return result;
    }

    private ModuleOwnerNote[] BuildOwnerNotes(
        ModulePipelinePlan plan,
        ModuleBuildResult buildResult,
        DocumentationBuildResult? documentationResult,
        ModuleDependencyInstallResult[]? dependencyInstallResults,
        ModuleBuildPipeline.StagingResult? stagingResult,
        MergeExecutionResult? mergeExecution,
        string? projectManifestSyncMessage)
    {
        var notes = new List<ModuleOwnerNote>();

        if (dependencyInstallResults is { Length: > 0 })
        {
            var installed = dependencyInstallResults.Count(static result => result.Status == ModuleDependencyInstallStatus.Installed);
            var updated = dependencyInstallResults.Count(static result => result.Status == ModuleDependencyInstallStatus.Updated);
            var satisfied = dependencyInstallResults.Count(static result => result.Status == ModuleDependencyInstallStatus.Satisfied);
            var skipped = dependencyInstallResults.Count(static result => result.Status == ModuleDependencyInstallStatus.Skipped);
            var listed = string.Join(", ", dependencyInstallResults.Select(static result => result.Name).Where(static name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase));

            notes.Add(new ModuleOwnerNote(
                "Dependencies",
                ModuleOwnerNoteSeverity.Info,
                summary: $"Checked {dependencyInstallResults.Length} dependency module(s): {listed}.",
                nextStep: installed > 0 || updated > 0
                    ? "If the build environment changed, re-run the module import step if you are troubleshooting dependency-related behavior."
                    : string.Empty,
                details: new[]
                {
                    $"{installed} installed, {updated} updated, {satisfied} satisfied, {skipped} skipped."
                }));
        }

        if (stagingResult is not null && (stagingResult.NormalizedLineEndingsCount > 0 || stagingResult.LineEndingNormalizationErrors > 0))
        {
            var lines = new List<string>();
            if (stagingResult.NormalizedLineEndingsCount > 0)
                lines.Add($"Normalized staged line endings to CRLF for {stagingResult.NormalizedLineEndingsCount} file(s).");
            if (stagingResult.LineEndingNormalizationErrors > 0)
                lines.Add($"Failed to normalize {stagingResult.LineEndingNormalizationErrors} staged file(s).");

            notes.Add(new ModuleOwnerNote(
                "Staging",
                stagingResult.LineEndingNormalizationErrors > 0 ? ModuleOwnerNoteSeverity.Warning : ModuleOwnerNoteSeverity.Info,
                summary: stagingResult.LineEndingNormalizationErrors > 0
                    ? $"Normalized {stagingResult.NormalizedLineEndingsCount} staged file(s) and failed to normalize {stagingResult.LineEndingNormalizationErrors}."
                    : $"Normalized staged line endings to CRLF for {stagingResult.NormalizedLineEndingsCount} file(s).",
                whyItMatters: stagingResult.LineEndingNormalizationErrors > 0
                    ? "A normalization failure means staged content may not match the expected packaging format."
                    : string.Empty,
                nextStep: stagingResult.LineEndingNormalizationErrors > 0
                    ? "Review the affected staged files before publishing."
                    : string.Empty,
                details: lines.ToArray()));
        }

        if (buildResult.BuildNotes is { Length: > 0 })
            notes.AddRange(buildResult.BuildNotes);

        if (mergeExecution is not null &&
            (mergeExecution.RequiredModules.Length > 0 ||
             mergeExecution.ApprovedModules.Length > 0 ||
             mergeExecution.DependentModules.Length > 0 ||
             mergeExecution.TopLevelInlinedFunctions > 0 ||
             mergeExecution.TotalInlinedFunctions > 0 ||
             mergeExecution.UsedExistingPsm1 ||
             mergeExecution.RetainedBootstrapperBecauseBinaryOutputsDetected ||
             mergeExecution.MergedModule))
        {
            notes.Add(new ModuleOwnerNote(
                "Module Entry Script",
                ModuleOwnerNoteSeverity.Info,
                summary: mergeExecution.RetainedBootstrapperBecauseBinaryOutputsDetected
                    ? "Build kept the existing .psm1 entry script because the module contains binaries."
                    : mergeExecution.UsedExistingPsm1 && !mergeExecution.HasScriptSources
                        ? "Build reused the existing .psm1 entry script because there were no script sources to merge."
                        : mergeExecution.MergedModule
                            ? $"Build wrote a merged .psm1 entry script from {mergeExecution.ScriptFilesDetected} script source file(s)."
                            : "Build entry script did not require changes.",
                details: BuildMergeExecutionOwnerDetails(
                    mergeExecution.RequiredModules,
                    mergeExecution.ApprovedModules,
                    mergeExecution.DependentModules,
                    mergeExecution.TopLevelInlinedFunctions,
                    mergeExecution.TotalInlinedFunctions)));
        }

        if (documentationResult is not null &&
            documentationResult.Succeeded &&
            plan.DocumentationBuild?.UpdateWhenNew == true &&
            plan.Documentation is not null)
        {
            var docsPath = ResolvePath(plan.ProjectRoot, plan.Documentation.Path);
            notes.Add(new ModuleOwnerNote(
                "Documentation",
                ModuleOwnerNoteSeverity.Info,
                summary: $"Updated project documentation at '{docsPath}'.",
                details: new[]
                {
                    $"Generated {documentationResult.MarkdownFiles} markdown help file(s)."
                }));
        }

        if (!string.IsNullOrWhiteSpace(projectManifestSyncMessage))
        {
            var manifestSyncMessage = projectManifestSyncMessage!;
            notes.Add(new ModuleOwnerNote(
                "Manifest",
                ModuleOwnerNoteSeverity.Info,
                summary: manifestSyncMessage.Trim(),
                details: new[]
                {
                    $"Project manifest now tracks the resolved build version {plan.ResolvedVersion}."
                }));
        }

        return notes.ToArray();
    }

    internal static string[] BuildMergeExecutionOwnerDetails(
        IReadOnlyList<RequiredModuleReference>? requiredModules,
        IReadOnlyList<string>? approvedModules,
        IReadOnlyList<string>? dependentModules,
        int topLevelInlinedFunctions,
        int totalInlinedFunctions)
    {
        var details = new List<string>();

        var formattedRequiredModules = (requiredModules ?? Array.Empty<RequiredModuleReference>())
            .Where(static module => module is not null && !string.IsNullOrWhiteSpace(module.ModuleName))
            .OrderBy(static module => module.ModuleName, StringComparer.OrdinalIgnoreCase)
            .Select(FormatRequiredModule)
            .Where(static module => !string.IsNullOrWhiteSpace(module))
            .ToArray();
        if (formattedRequiredModules.Length > 0)
            details.Add($"Required modules ({formattedRequiredModules.Length}): {string.Join(", ", formattedRequiredModules)}");

        var orderedApprovedModules = (approvedModules ?? Array.Empty<string>())
            .Where(static module => !string.IsNullOrWhiteSpace(module))
            .Select(static module => module.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static module => module, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (orderedApprovedModules.Length > 0)
            details.Add($"Approved modules ({orderedApprovedModules.Length}): {string.Join(", ", orderedApprovedModules)}");

        var orderedDependentModules = (dependentModules ?? Array.Empty<string>())
            .Where(static module => !string.IsNullOrWhiteSpace(module))
            .Select(static module => module.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static module => module, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (orderedDependentModules.Length > 0)
            details.Add($"Dependent modules ({orderedDependentModules.Length}): {string.Join(", ", orderedDependentModules)}");

        if (topLevelInlinedFunctions > 0 || totalInlinedFunctions > 0)
            details.Add($"MergeMissing: {topLevelInlinedFunctions} top-level function(s) inlined (total {totalInlinedFunctions} including dependencies).");

        return details.ToArray();
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
