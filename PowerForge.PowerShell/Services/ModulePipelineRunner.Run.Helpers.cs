using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private ModuleDependencyInstallResult[] EnsureBuildDependenciesInstalledIfNeeded(ModulePipelinePlan plan)
    {
        try
        {
            var results = new List<ModuleDependencyInstallResult>();
            if (plan.InstallMissingModules)
                results.AddRange(EnsureBuildDependenciesInstalled(plan));
            var packagingRequiredModules = ResolveOutputRequiredModules(plan.RequiredModulesForPackaging, plan.MergeMissing, plan.ApprovedModules);
            results.AddRange(EnsureFeatureToolDependenciesInstalled(plan, packagingRequiredModules));
            return results.ToArray();
        }
        catch (Exception ex)
        {
            _logger.Error($"Dependency installation failed. {ex.Message}");
            if (_logger.IsVerbose) _logger.Verbose(ex.ToString());
            throw;
        }
    }

    private ModulePipelineResult BuildPipelineResult(
        ModulePipelineSpec spec,
        ModulePipelinePlan plan,
        ModulePipelineRunState state)
    {
        var buildResult = state.RequireBuildResult();
        var stagingResult = state.RequireStaged();
        var diagnostics = new List<BuildDiagnostic>(BuildDiagnosticsFactory.CreatePipelineDiagnostics(
            state.FileConsistencyReport,
            plan.FileConsistencySettings,
            state.ProjectFileConsistencyReport,
            state.CompatibilityReport,
            state.ValidationReport));
        diagnostics.AddRange(state.AutomaticBinaryConflictDiagnostics ?? Array.Empty<BuildDiagnostic>());
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
            state.InstallResult,
            state.DocumentationResult,
            state.FileConsistencyReport,
            state.FileConsistencyStatus,
            state.FileConsistencyEncodingFix,
            state.FileConsistencyLineEndingFix,
            state.CompatibilityReport,
            state.ValidationReport,
            diagnostics.ToArray(),
            diagnosticsBaseline,
            diagnosticsPolicy,
            state.PublishResults.ToArray(),
            state.ArtefactResults.ToArray(),
            state.FormattingStagingResults,
            state.FormattingProjectResults,
            state.ProjectFileConsistencyReport,
            state.ProjectFileConsistencyStatus,
            state.ProjectFileConsistencyEncodingFix,
            state.ProjectFileConsistencyLineEndingFix,
            signingResult: state.SigningResult,
            xcodeProjectVersionResults: state.XcodeProjectVersionResults.ToArray(),
            appleAppResults: state.AppleAppResults.ToArray(),
            actionResults: state.ActionResults.ToArray(),
            projectBuildResults: state.ProjectBuildResults.ToArray(),
            releaseCoordinationResult: state.ReleaseCoordinationResult,
            ownerNotes: BuildOwnerNotes(
                plan,
                buildResult,
                state.DocumentationResult,
                state.DependencyInstallResults,
                state.ProjectBuildResults.ToArray(),
                state.ReleaseCoordinationResult,
                stagingResult,
                state.MergeExecution,
                state.ProjectManifestSyncMessage));

        if (diagnosticsPolicy?.PolicyViolated == true)
            throw new ModulePipelineDiagnosticsPolicyException(result, diagnosticsPolicy, diagnosticsPolicy.FailureReason);

        return result;
    }

    private ModuleOwnerNote[] BuildOwnerNotes(
        ModulePipelinePlan plan,
        ModuleBuildResult buildResult,
        DocumentationBuildResult? documentationResult,
        ModuleDependencyInstallResult[]? dependencyInstallResults,
        ProjectBuildHostExecutionResult[]? projectBuildResults,
        ModuleReleaseCoordinationResult? releaseCoordinationResult,
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

        if (projectBuildResults is { Length: > 0 })
        {
            var successful = projectBuildResults.Count(static result => result.Success);
            var failed = projectBuildResults.Length - successful;
            var configured = projectBuildResults
                .Select(static result => result.ConfigPath)
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            notes.Add(new ModuleOwnerNote(
                "Package Builds",
                failed > 0 ? ModuleOwnerNoteSeverity.Warning : ModuleOwnerNoteSeverity.Info,
                summary: $"Ran {projectBuildResults.Length} package build lane(s) before the module build.",
                nextStep: failed > 0
                    ? "Review the package build result before publishing the module or release assets."
                    : string.Empty,
                details: new[]
                {
                    $"{successful} succeeded, {failed} failed.",
                    configured.Length == 0 ? "No package build config paths were reported." : $"Configs: {string.Join(", ", configured)}"
                }));
        }

        if (releaseCoordinationResult is not null)
        {
            notes.Add(new ModuleOwnerNote(
                "Release",
                releaseCoordinationResult.GitHub is { Succeeded: false } ? ModuleOwnerNoteSeverity.Warning : ModuleOwnerNoteSeverity.Info,
                summary: $"Prepared {releaseCoordinationResult.AssetPaths.Length} unified release asset(s).",
                details: new[]
                {
                    string.IsNullOrWhiteSpace(releaseCoordinationResult.StageRoot)
                        ? "Assets were published from their original output paths."
                        : $"StageRoot: {releaseCoordinationResult.StageRoot}",
                    $"{releaseCoordinationResult.ModuleAssetPaths.Length} module asset(s), {releaseCoordinationResult.PackageAssetPaths.Length} package asset(s)."
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

        if (totalInlinedFunctions > 0)
        {
            details.Add(
                topLevelInlinedFunctions > 0
                    ? $"Functions inlined during merge: {topLevelInlinedFunctions} top-level function(s) inlined (total {totalInlinedFunctions} including dependencies)."
                    : $"Functions inlined during merge: {totalInlinedFunctions} dependency function(s) inlined (no top-level functions required).");
        }

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

}
