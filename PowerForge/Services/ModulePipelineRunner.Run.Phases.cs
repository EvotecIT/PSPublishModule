using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private void ExecuteValidationPhases(
        ModulePipelinePlan plan,
        ModulePipelineExecutionSession session,
        ModulePipelineRunState state)
    {
        var buildResult = state.RequireBuildResult();

        if (plan.FileConsistencySettings?.Enable == true)
        {
            var s = plan.FileConsistencySettings;
            var scope = s.ResolveScope();
            var runStaging = scope != FileConsistencyScope.ProjectOnly;
            var runProject = scope != FileConsistencyScope.StagingOnly;

            var fileConsistencySeverity = ResolveFileConsistencySeverity(s);

            if (runStaging)
            {
                session.Start(session.FileConsistencyStep);
                try
                {
                    var excludeDirs = MergeExcludeDirectories(
                        s.ExcludeDirectories,
                        new[] { ".git", ".vs", "bin", "obj", "packages", "node_modules", ".vscode", "Artefacts", "Ignore", "Lib", "Modules" });
                    var excludeFiles = s.ExcludeFiles ?? Array.Empty<string>();
                    var kind = s.ProjectKind ?? ProjectKind.Mixed;
                    var includePatterns = s.IncludePatterns is { Length: > 0 }
                        ? s.IncludePatterns.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToArray()
                        : null;

                    var enumeration = new ProjectEnumeration(
                        rootPath: buildResult.StagingPath,
                        kind: kind,
                        customExtensions: includePatterns,
                        excludeDirectories: excludeDirs,
                        excludeFiles: excludeFiles);

                    var encodingOverrides = s.EncodingOverrides;
                    var lineEndingOverrides = s.LineEndingOverrides;
                    var recommendedEncoding = s.RequiredEncoding.ToTextEncodingKind();
                    var exportPath = s.ExportReport
                        ? BuildArtefactsReportPath(plan.ProjectRoot, s.ReportFileName, fallbackFileName: "FileConsistencyReport.csv")
                        : null;

                    var analyzer = new ProjectConsistencyAnalyzer(_logger);
                    state.FileConsistencyReport = analyzer.Analyze(
                        enumeration: enumeration,
                        projectType: kind.ToString(),
                        recommendedEncoding: recommendedEncoding,
                        recommendedLineEnding: s.RequiredLineEnding,
                        includeDetails: false,
                        exportPath: exportPath,
                        encodingOverrides: encodingOverrides,
                        lineEndingOverrides: lineEndingOverrides);

                    if (s.AutoFix)
                    {
                        var enc = new EncodingConverter();
                        var encOptions = new EncodingConversionOptions(
                            enumeration: enumeration,
                            sourceEncoding: TextEncodingKind.Any,
                            targetEncoding: recommendedEncoding,
                            createBackups: s.CreateBackups,
                            backupDirectory: null,
                            force: false,
                            noRollbackOnMismatch: false,
                            preferUtf8BomForPowerShell: s.RequiredEncoding == FileConsistencyEncoding.UTF8BOM);
                        if (encodingOverrides is { Count: > 0 })
                        {
                            encOptions.TargetEncodingResolver = path =>
                            {
                                var rel = ProjectTextInspection.ComputeRelativePath(enumeration.RootPath, path);
                                var overrideEncoding = FileConsistencyOverrideResolver.ResolveEncodingOverride(rel, encodingOverrides);
                                return overrideEncoding?.ToTextEncodingKind();
                            };
                        }
                        state.FileConsistencyEncodingFix = enc.Convert(encOptions);

                        var le = new LineEndingConverter();
                        var target = s.RequiredLineEnding.ToLineEnding();
                        var lineEndingOptions = new LineEndingConversionOptions(
                            enumeration: enumeration,
                            target: target,
                            createBackups: s.CreateBackups,
                            backupDirectory: null,
                            force: false,
                            onlyMixed: false,
                            ensureFinalNewline: s.CheckMissingFinalNewline,
                            onlyMissingNewline: false,
                            preferUtf8BomForPowerShell: s.RequiredEncoding == FileConsistencyEncoding.UTF8BOM);
                        if (lineEndingOverrides is { Count: > 0 })
                        {
                            lineEndingOptions.TargetResolver = path =>
                            {
                                var rel = ProjectTextInspection.ComputeRelativePath(enumeration.RootPath, path);
                                var overrideLineEnding = FileConsistencyOverrideResolver.ResolveLineEndingOverride(rel, lineEndingOverrides);
                                return overrideLineEnding?.ToLineEnding();
                            };
                        }
                        state.FileConsistencyLineEndingFix = le.Convert(lineEndingOptions);

                        state.FileConsistencyReport = analyzer.Analyze(
                            enumeration: enumeration,
                            projectType: kind.ToString(),
                            recommendedEncoding: recommendedEncoding,
                            recommendedLineEnding: s.RequiredLineEnding,
                            includeDetails: false,
                            exportPath: exportPath,
                            encodingOverrides: encodingOverrides,
                            lineEndingOverrides: lineEndingOverrides);
                    }

                    var finalReport = state.FileConsistencyReport ?? throw new InvalidOperationException("File consistency analysis produced no report.");
                    state.FileConsistencyStatus = EvaluateFileConsistency(finalReport, s, fileConsistencySeverity);
                    if (fileConsistencySeverity != ValidationSeverity.Off)
                        LogFileConsistencyIssues(finalReport, s, "staging", state.FileConsistencyStatus ?? CheckStatus.Warning);
                    if (fileConsistencySeverity == ValidationSeverity.Error && state.FileConsistencyStatus == CheckStatus.Fail)
                        throw new InvalidOperationException($"File consistency check failed. {BuildFileConsistencyMessage(finalReport, s)}");

                    session.Done(session.FileConsistencyStep);
                }
                catch (Exception ex)
                {
                    session.Fail(session.FileConsistencyStep, ex);
                    throw;
                }
            }

            if (runProject)
            {
                session.Start(session.ProjectFileConsistencyStep);
                try
                {
                    var excludeDirs = MergeExcludeDirectories(
                        s.ExcludeDirectories,
                        new[] { ".git", ".vs", "bin", "obj", "packages", "node_modules", ".vscode", "Artefacts", "Ignore", "Lib", "Modules" });
                    var excludeFiles = s.ExcludeFiles ?? Array.Empty<string>();
                    var kind = s.ProjectKind ?? ProjectKind.Mixed;
                    var includePatterns = s.IncludePatterns is { Length: > 0 }
                        ? s.IncludePatterns.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToArray()
                        : null;

                    var enumeration = new ProjectEnumeration(
                        rootPath: plan.ProjectRoot,
                        kind: kind,
                        customExtensions: includePatterns,
                        excludeDirectories: excludeDirs,
                        excludeFiles: excludeFiles);

                    var encodingOverrides = s.EncodingOverrides;
                    var lineEndingOverrides = s.LineEndingOverrides;
                    var recommendedEncoding = s.RequiredEncoding.ToTextEncodingKind();
                    var exportPath = s.ExportReport
                        ? BuildArtefactsReportPath(plan.ProjectRoot, AddFileNameSuffix(s.ReportFileName, "Project"), fallbackFileName: "FileConsistencyReport.Project.csv")
                        : null;

                    var analyzer = new ProjectConsistencyAnalyzer(_logger);
                    state.ProjectFileConsistencyReport = analyzer.Analyze(
                        enumeration: enumeration,
                        projectType: kind.ToString(),
                        recommendedEncoding: recommendedEncoding,
                        recommendedLineEnding: s.RequiredLineEnding,
                        includeDetails: false,
                        exportPath: exportPath,
                        encodingOverrides: encodingOverrides,
                        lineEndingOverrides: lineEndingOverrides);

                    if (s.AutoFix)
                    {
                        var enc = new EncodingConverter();
                        var encOptions = new EncodingConversionOptions(
                            enumeration: enumeration,
                            sourceEncoding: TextEncodingKind.Any,
                            targetEncoding: recommendedEncoding,
                            createBackups: s.CreateBackups,
                            backupDirectory: null,
                            force: false,
                            noRollbackOnMismatch: false,
                            preferUtf8BomForPowerShell: s.RequiredEncoding == FileConsistencyEncoding.UTF8BOM);
                        if (encodingOverrides is { Count: > 0 })
                        {
                            encOptions.TargetEncodingResolver = path =>
                            {
                                var rel = ProjectTextInspection.ComputeRelativePath(enumeration.RootPath, path);
                                var overrideEncoding = FileConsistencyOverrideResolver.ResolveEncodingOverride(rel, encodingOverrides);
                                return overrideEncoding?.ToTextEncodingKind();
                            };
                        }
                        state.ProjectFileConsistencyEncodingFix = enc.Convert(encOptions);

                        var le = new LineEndingConverter();
                        var target = s.RequiredLineEnding.ToLineEnding();
                        var lineEndingOptions = new LineEndingConversionOptions(
                            enumeration: enumeration,
                            target: target,
                            createBackups: s.CreateBackups,
                            backupDirectory: null,
                            force: false,
                            onlyMixed: false,
                            ensureFinalNewline: s.CheckMissingFinalNewline,
                            onlyMissingNewline: false,
                            preferUtf8BomForPowerShell: s.RequiredEncoding == FileConsistencyEncoding.UTF8BOM);
                        if (lineEndingOverrides is { Count: > 0 })
                        {
                            lineEndingOptions.TargetResolver = path =>
                            {
                                var rel = ProjectTextInspection.ComputeRelativePath(enumeration.RootPath, path);
                                var overrideLineEnding = FileConsistencyOverrideResolver.ResolveLineEndingOverride(rel, lineEndingOverrides);
                                return overrideLineEnding?.ToLineEnding();
                            };
                        }
                        state.ProjectFileConsistencyLineEndingFix = le.Convert(lineEndingOptions);

                        state.ProjectFileConsistencyReport = analyzer.Analyze(
                            enumeration: enumeration,
                            projectType: kind.ToString(),
                            recommendedEncoding: recommendedEncoding,
                            recommendedLineEnding: s.RequiredLineEnding,
                            includeDetails: false,
                            exportPath: exportPath,
                            encodingOverrides: encodingOverrides,
                            lineEndingOverrides: lineEndingOverrides);
                    }

                    var finalReport = state.ProjectFileConsistencyReport ?? throw new InvalidOperationException("Project-root file consistency analysis produced no report.");
                    state.ProjectFileConsistencyStatus = EvaluateFileConsistency(finalReport, s, fileConsistencySeverity);
                    if (fileConsistencySeverity != ValidationSeverity.Off)
                        LogFileConsistencyIssues(finalReport, s, "project", state.ProjectFileConsistencyStatus ?? CheckStatus.Warning);
                    if (fileConsistencySeverity == ValidationSeverity.Error && state.ProjectFileConsistencyStatus == CheckStatus.Fail)
                        throw new InvalidOperationException($"File consistency (project) check failed. {BuildFileConsistencyMessage(finalReport, s)}");

                    session.Done(session.ProjectFileConsistencyStep);
                }
                catch (Exception ex)
                {
                    session.Fail(session.ProjectFileConsistencyStep, ex);
                    throw;
                }
            }
        }

        if (plan.CompatibilitySettings?.Enable == true)
        {
            session.Start(session.CompatibilityStep);
            try
            {
                var s = plan.CompatibilitySettings;
                var compatibilitySeverity = ResolveCompatibilitySeverity(s);
                var excludeDirs = MergeExcludeDirectories(
                    s.ExcludeDirectories,
                    new[] { ".git", ".vs", "bin", "obj", "packages", "node_modules", ".vscode", "Artefacts", "Ignore", "Lib", "Modules" });

                var exportPath = s.ExportReport
                    ? BuildArtefactsReportPath(plan.ProjectRoot, s.ReportFileName, fallbackFileName: "PowerShellCompatibilityReport.csv")
                    : null;

                var analyzer = new PowerShellCompatibilityAnalyzer(_logger);
                var specCompat = new PowerShellCompatibilitySpec(buildResult.StagingPath, recurse: true, excludeDirectories: excludeDirs);
                var raw = analyzer.Analyze(specCompat, progress: null, exportPath: exportPath);
                var adjusted = ApplyCompatibilitySettings(raw, s, compatibilitySeverity);
                state.CompatibilityReport = adjusted;

                if (compatibilitySeverity == ValidationSeverity.Error && adjusted.Summary.Status == CheckStatus.Fail)
                    throw new InvalidOperationException($"PowerShell compatibility check failed. {adjusted.Summary.Message}");

                session.Done(session.CompatibilityStep);
            }
            catch (Exception ex)
            {
                session.Fail(session.CompatibilityStep, ex);
                throw;
            }
        }

        if (plan.ValidationSettings?.Enable == true)
        {
            session.Start(session.ModuleValidationStep);
            try
            {
                state.ValidationReport = _hostedOperations.ValidateModule(new ModuleValidationSpec
                {
                    ProjectRoot = plan.ProjectRoot,
                    StagingPath = buildResult.StagingPath,
                    ModuleName = plan.ModuleName,
                    ManifestPath = buildResult.ManifestPath,
                    BuildSpec = plan.BuildSpec,
                    Settings = plan.ValidationSettings ?? new ModuleValidationSettings()
                });

                if (state.ValidationReport.Status == CheckStatus.Fail)
                    throw new InvalidOperationException($"Module validation failed ({state.ValidationReport.Summary}).");

                session.Done(session.ModuleValidationStep);
            }
            catch (Exception ex)
            {
                session.Fail(session.ModuleValidationStep, ex);
                throw;
            }
        }
    }

    private void ExecuteTestPhases(
        ModulePipelinePlan plan,
        ModulePipelineExecutionSession session,
        ModulePipelineRunState state)
    {
        var buildResult = state.RequireBuildResult();

        if (plan.ImportModules is not null &&
            (plan.ImportModules.Self == true || plan.ImportModules.RequiredModules == true))
        {
            if (plan.ImportModules.RequiredModules == true &&
                ShouldAnalyzeBinaryConflicts(plan.ImportModules, importRequired: true))
            {
                session.Start(session.BinaryConflictAnalysisStep);
                try
                {
                    state.AutomaticBinaryConflictDiagnostics = AnalyzeAutomaticBinaryConflicts(plan, buildResult);
                    LogAutomaticBinaryConflictDiagnostics(state.AutomaticBinaryConflictDiagnostics);
                    session.Done(session.BinaryConflictAnalysisStep);
                }
                catch (Exception ex)
                {
                    session.Fail(session.BinaryConflictAnalysisStep, ex);
                    throw;
                }
            }

            if (plan.ImportModules.Self == true && plan.ImportModules.SkipBinaryDependencyCheck != true)
            {
                session.Start(session.BinaryDependenciesStep);
                try
                {
                    RunBinaryDependencyPreflight(plan, buildResult);
                    session.Done(session.BinaryDependenciesStep);
                }
                catch (Exception ex)
                {
                    session.Fail(session.BinaryDependenciesStep, ex);
                    throw;
                }
            }

            session.Start(session.ImportModulesStep);
            try
            {
                RunImportModules(plan, buildResult);
                session.Done(session.ImportModulesStep);
            }
            catch (Exception ex)
            {
                session.Fail(session.ImportModulesStep, ex);
                throw;
            }
        }

        if (plan.TestsAfterMerge is { Length: > 0 })
        {
            for (int i = 0; i < plan.TestsAfterMerge.Length; i++)
            {
                var cfg = plan.TestsAfterMerge[i];
                var step = session.TestSteps.Length > i ? session.TestSteps[i] : null;
                session.Start(step);
                try
                {
                    RunTestsAfterMerge(plan, buildResult, cfg);
                    session.Done(step);
                }
                catch (Exception ex)
                {
                    session.Fail(step, ex);
                    throw;
                }
            }
        }
    }

    private void ExecutePackagingPublishAndInstallPhases(
        ModulePipelineSpec spec,
        ModulePipelinePlan plan,
        ModulePipelineExecutionSession session,
        RequiredModuleReference[] packagingRequiredModules,
        ModuleBuildPipeline pipeline,
        ModulePipelineRunState state)
    {
        var buildResult = state.RequireBuildResult();

        if (plan.Artefacts is { Length: > 0 })
        {
            var builder = new ArtefactBuilder(_logger);
            foreach (var artefact in plan.Artefacts)
            {
                var step = session.GetArtefactStep(artefact);
                session.Start(step);
                try
                {
                    state.ArtefactResults.Add(builder.Build(
                        segment: artefact,
                        projectRoot: plan.ProjectRoot,
                        stagingPath: buildResult.StagingPath,
                        moduleName: plan.ModuleName,
                        moduleVersion: plan.ResolvedVersion,
                        preRelease: plan.PreRelease,
                        requiredModules: packagingRequiredModules,
                        information: plan.Information,
                        delivery: plan.Delivery,
                        includeScriptFolders: !state.MergedScripts));
                    session.Done(step);
                }
                catch (Exception ex)
                {
                    session.Fail(step, ex);
                    throw;
                }
            }
        }

        if (plan.Publishes is { Length: > 0 })
        {
            foreach (var publish in plan.Publishes)
            {
                var step = session.GetPublishStep(publish);
                session.Start(step);
                try
                {
                    state.PublishResults.Add(_hostedOperations.PublishModule(
                        publish.Configuration,
                        plan,
                        buildResult,
                        state.ArtefactResults,
                        includeScriptFolders: !state.MergedScripts));
                    session.Done(step);
                }
                catch (Exception ex)
                {
                    session.Fail(step, ex);
                    throw;
                }
            }
        }

        if (plan.InstallEnabled)
        {
            session.Start(session.InstallStep);
            string? installPackagePath = null;
            try
            {
                installPackagePath = Path.Combine(Path.GetTempPath(), "PowerForge", "install", $"{plan.ModuleName}_{Guid.NewGuid():N}");
                Directory.CreateDirectory(installPackagePath);
                ArtefactBuilder.CopyModulePackageForInstall(
                    buildResult.StagingPath,
                    installPackagePath,
                    plan.Information,
                    plan.Delivery,
                    includeScriptFolders: !state.MergedScripts);

                var installSpec = new ModuleInstallSpec
                {
                    Name = plan.ModuleName,
                    Version = plan.ResolvedVersion,
                    StagingPath = installPackagePath,
                    Strategy = plan.InstallStrategy,
                    KeepVersions = plan.InstallKeepVersions,
                    Roots = plan.InstallRoots,
                    UpdateManifestToResolvedVersion = spec.Install?.UpdateManifestToResolvedVersion ?? true,
                    LegacyFlatHandling = plan.InstallLegacyFlatHandling,
                    PreserveVersions = plan.InstallPreserveVersions
                };
                state.InstallResult = pipeline.InstallFromStaging(installSpec);
                session.Done(session.InstallStep);
            }
            catch (Exception ex)
            {
                session.Fail(session.InstallStep, ex);
                throw;
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(installPackagePath))
                {
                    try { DeleteDirectoryWithRetries(installPackagePath); }
                    catch (Exception ex) { _logger.Warn($"Failed to delete install package folder: {ex.Message}"); }
                }
            }
        }
    }
}
