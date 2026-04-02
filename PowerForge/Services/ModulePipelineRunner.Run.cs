using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using System.Text.Json;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private static readonly IScriptFunctionExportDetector ScriptFunctionExportDetector = new PowerShellScriptFunctionExportDetector();

    /// <summary>
    /// Executes the pipeline described by <paramref name="spec"/>.
    /// </summary>
    public ModulePipelineResult Run(ModulePipelineSpec spec)
    {
        var plan = Plan(spec);
        return Run(spec, plan, progress: null);
    }

    /// <summary>
    /// Executes the pipeline described by <paramref name="spec"/> using a precomputed <paramref name="plan"/>.
    /// </summary>
    public ModulePipelineResult Run(ModulePipelineSpec spec, ModulePipelinePlan plan, IModulePipelineProgressReporter? progress = null)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        if (plan is null) throw new ArgumentNullException(nameof(plan));

        var manifestRequiredModules = ResolveOutputRequiredModules(plan.RequiredModules, plan.MergeMissing, plan.ApprovedModules);
        var packagingRequiredModules = ResolveOutputRequiredModules(plan.RequiredModulesForPackaging, plan.MergeMissing, plan.ApprovedModules);
        var manifestExternalModuleDependencies = plan.ExternalModuleDependencies ?? Array.Empty<string>();

        var session = ModulePipelineExecutionSession.Create(plan, progress);
        var reporter = session.Reporter;
        var stageStep = session.StageStep;
        var buildStep = session.BuildStep;
        var manifestStep = session.ManifestStep;
        var docsExtractStep = session.DocsExtractStep;
        var docsWriteStep = session.DocsWriteStep;
        var docsMamlStep = session.DocsMamlStep;
        var formatStagingStep = session.FormatStagingStep;
        var formatProjectStep = session.FormatProjectStep;
        var signStep = session.SignStep;
        var fileConsistencyStep = session.FileConsistencyStep;
        var projectFileConsistencyStep = session.ProjectFileConsistencyStep;
        var compatibilityStep = session.CompatibilityStep;
        var moduleValidationStep = session.ModuleValidationStep;
        var binaryConflictAnalysisStep = session.BinaryConflictAnalysisStep;
        var binaryDependenciesStep = session.BinaryDependenciesStep;
        var importModulesStep = session.ImportModulesStep;
        var testSteps = session.TestSteps;
        var installStep = session.InstallStep;
        var cleanupStep = session.CleanupStep;

        var pipeline = ModuleBuildPipelineFactory.Create(_logger);
        string? stagingPathForCleanup = plan.BuildSpec.StagingPath;
        Exception? pipelineFailure = null;

        try
        {
            var dependencyInstallResults = EnsureBuildDependenciesInstalledIfNeeded(plan);
            SyncSourceProjectVersionIfRequested(plan);

            ModuleBuildPipeline.StagingResult staged;
            session.Start(stageStep);
            try
            {
                staged = pipeline.StageToStaging(plan.BuildSpec);
                stagingPathForCleanup = staged.StagingPath;
                session.Done(stageStep);
            }
            catch (Exception ex)
            {
                session.Fail(stageStep, ex);
                stagingPathForCleanup ??= plan.BuildSpec.StagingPath;
                throw;
            }

            ModuleBuildResult buildResult;
            session.Start(buildStep);
            try
            {
                buildResult = pipeline.BuildInStaging(plan.BuildSpec, staged.StagingPath);
                session.Done(buildStep);
            }
            catch (Exception ex)
            {
                session.Fail(buildStep, ex);
                throw;
            }

            var mergeExecution = plan.BuildSpec.RefreshManifestOnly ? MergeExecutionResult.None : ApplyMerge(plan, buildResult);
            var mergedScripts = mergeExecution.MergedModule;
            if (!plan.BuildSpec.RefreshManifestOnly)
                ApplyPlaceholders(plan, buildResult);

            session.Start(manifestStep);
            try
            {
                RefreshManifestFromPlan(plan, buildResult, manifestRequiredModules, manifestExternalModuleDependencies);

                if (plan.Delivery is not null && plan.Delivery.Enable)
                {
                    ApplyDeliveryMetadata(buildResult.ManifestPath, plan.Delivery);

                    if (plan.Delivery.GenerateInstallCommand || plan.Delivery.GenerateUpdateCommand)
                    {
                        var generator = new DeliveryCommandGenerator(_logger);
                        var generated = generator.Generate(buildResult.StagingPath, plan.ModuleName, plan.Delivery);

                        if (generated.Length > 0)
                        {
                            try
                            {
                                var publicFolder = Path.Combine(buildResult.StagingPath, "Public");
                                if (Directory.Exists(publicFolder))
                                {
                                    var scripts = Directory.GetFiles(publicFolder, "*.ps1", SearchOption.AllDirectories);
                                    var functions = ScriptFunctionExportDetector.DetectScriptFunctions(scripts);
                                    BuildServices.SetManifestExports(buildResult.ManifestPath, functions, cmdlets: null, aliases: null);
                                }

                                if (mergedScripts)
                                    SyncMergedPsm1WithGeneratedScripts(buildResult.ManifestPath, buildResult.StagingPath, plan.ModuleName, generated.Select(g => g.ScriptPath));
                            }
                            catch (Exception ex)
                            {
                                _logger.Warn($"Failed to update manifest exports after generating delivery commands. Error: {ex.Message}");
                                if (_logger.IsVerbose) _logger.Verbose(ex.ToString());
                            }
                        }
                    }
                }

                if (!mergedScripts && !plan.BuildSpec.RefreshManifestOnly)
                    TryRegenerateBootstrapperFromManifest(buildResult, plan.ModuleName, plan.BuildSpec.ExportAssemblies);

                session.Done(manifestStep);
            }
            catch (Exception ex)
            {
                session.Fail(manifestStep, ex);
                throw;
            }

            DocumentationBuildResult? documentationResult = null;
            if (plan.Documentation is not null && plan.DocumentationBuild?.Enable == true)
            {
                try
                {
                    documentationResult = _hostedOperations.BuildDocumentation(
                        moduleName: plan.ModuleName,
                        stagingPath: buildResult.StagingPath,
                        moduleManifestPath: buildResult.ManifestPath,
                        documentation: plan.Documentation,
                        buildDocumentation: plan.DocumentationBuild!,
                        progress: reporter,
                        extractStep: docsExtractStep,
                        writeStep: docsWriteStep,
                        externalHelpStep: docsMamlStep);

                    if (documentationResult is not null && !documentationResult.Succeeded)
                        throw new InvalidOperationException($"Documentation generation failed. {documentationResult.ErrorMessage}");

                    // Legacy: "UpdateWhenNew" historically updated documentation in the project folder.
                    // When enabled, keep the repo Docs/Readme.md and external help in sync (not just staging).
                    if (documentationResult is not null &&
                        documentationResult.Succeeded &&
                        plan.DocumentationBuild?.UpdateWhenNew == true)
                    {
                        try
                        {
                            SyncGeneratedDocumentationToProjectRoot(plan, documentationResult);
                        }
                        catch (Exception ex)
                        {
                            _logger.Warn($"Failed to update project docs folder. Error: {ex.Message}");
                            if (_logger.IsVerbose) _logger.Verbose(ex.ToString());
                        }
                    }
                }
                catch (Exception ex)
                {
                    session.Fail(docsExtractStep, ex);
                    session.Fail(docsWriteStep, ex);
                    session.Fail(docsMamlStep, ex);
                    throw;
                }
            }

            FormatterResult[] formattingStagingResults = Array.Empty<FormatterResult>();
            FormatterResult[] formattingProjectResults = Array.Empty<FormatterResult>();
            ModuleSigningResult? signingResult = null;
            ModuleValidationReport? validationReport = null;
            BuildDiagnostic[] automaticBinaryConflictDiagnostics = Array.Empty<BuildDiagnostic>();

            if (plan.Formatting is not null)
            {
                var formattingPipeline = new FormattingPipeline(_logger);       

                session.Start(formatStagingStep);
                try
                {
                    formattingStagingResults = FormatPowerShellTree(
                        rootPath: buildResult.StagingPath,
                        moduleName: plan.ModuleName,
                        manifestPath: buildResult.ManifestPath,
                        includeMergeFormatting: true,
                        formatting: plan.Formatting,
                        pipeline: formattingPipeline);

                    var stagingFmt = FormattingSummary.FromResults(formattingStagingResults);
                    if (stagingFmt.Status == CheckStatus.Fail)
                    {
                        LogFormattingIssues(buildResult.StagingPath, formattingStagingResults, "staging root");
                        throw new InvalidOperationException(
                            BuildFormattingFailureMessage("staging root", buildResult.StagingPath, stagingFmt, formattingStagingResults));
                    }
                    session.Done(formatStagingStep);
                }
                catch (Exception ex)
                {
                    session.Fail(formatStagingStep, ex);
                    throw;
                }

                if (plan.Formatting.Options.UpdateProjectRoot)
                {
                    session.Start(formatProjectStep);
                    try
                    {
                        var projectManifest = Path.Combine(plan.ProjectRoot, $"{plan.ModuleName}.psd1");
                        formattingProjectResults = FormatPowerShellTree(        
                            rootPath: plan.ProjectRoot,
                            moduleName: plan.ModuleName,
                            manifestPath: projectManifest,
                            includeMergeFormatting: false,
                            formatting: plan.Formatting,
                            pipeline: formattingPipeline);

                        var projectFmt = FormattingSummary.FromResults(formattingProjectResults);
                        if (projectFmt.Status == CheckStatus.Fail)
                        {
                            LogFormattingIssues(plan.ProjectRoot, formattingProjectResults, "project root");
                            throw new InvalidOperationException(
                                BuildFormattingFailureMessage("project root", plan.ProjectRoot, projectFmt, formattingProjectResults));
                        }
                        session.Done(formatProjectStep);
                    }
                    catch (Exception ex)
                    {
                        session.Fail(formatProjectStep, ex);
                        throw;
                    }
                }
            }

            try
            {
                RefreshManifestFromPlan(plan, buildResult, manifestRequiredModules, manifestExternalModuleDependencies);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Post-format manifest patch failed. {ex.Message}");
                if (_logger.IsVerbose) _logger.Verbose(ex.ToString());
            }

            if (plan.SignModule)
            {
                session.Start(signStep);
                try
                {
                    signingResult = SignBuiltModuleOutput(
                        moduleName: plan.ModuleName,
                        rootPath: buildResult.StagingPath,
                        signing: plan.Signing);
                    session.Done(signStep);
                }
                catch (Exception ex)
                {
                    session.Fail(signStep, ex);
                    throw;
                }
            }

        ProjectConsistencyReport? fileConsistencyReport = null;
        CheckStatus? fileConsistencyStatus = null;
        ProjectConversionResult? fileConsistencyEncodingFix = null;
        ProjectConversionResult? fileConsistencyLineEndingFix = null;
        ProjectConsistencyReport? projectFileConsistencyReport = null;
        CheckStatus? projectFileConsistencyStatus = null;
        ProjectConversionResult? projectFileConsistencyEncodingFix = null;
        ProjectConversionResult? projectFileConsistencyLineEndingFix = null;
        PowerShellCompatibilityReport? compatibilityReport = null;        

        if (plan.FileConsistencySettings?.Enable == true)
        {
            var s = plan.FileConsistencySettings;
            var scope = s.ResolveScope();
            var runStaging = scope != FileConsistencyScope.ProjectOnly;
            var runProject = scope != FileConsistencyScope.StagingOnly;

            var fileConsistencySeverity = ResolveFileConsistencySeverity(s);

            if (runStaging)
            {
                session.Start(fileConsistencyStep);
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
                    fileConsistencyReport = analyzer.Analyze(
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
                        fileConsistencyEncodingFix = enc.Convert(encOptions);

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
                        fileConsistencyLineEndingFix = le.Convert(lineEndingOptions);

                        fileConsistencyReport = analyzer.Analyze(
                            enumeration: enumeration,
                            projectType: kind.ToString(),
                            recommendedEncoding: recommendedEncoding,
                            recommendedLineEnding: s.RequiredLineEnding,
                            includeDetails: false,
                            exportPath: exportPath,
                            encodingOverrides: encodingOverrides,
                            lineEndingOverrides: lineEndingOverrides);
                    }

                    var finalReport = fileConsistencyReport ?? throw new InvalidOperationException("File consistency analysis produced no report.");
                    fileConsistencyStatus = EvaluateFileConsistency(finalReport, s, fileConsistencySeverity);
                    if (fileConsistencySeverity != ValidationSeverity.Off)
                        LogFileConsistencyIssues(finalReport, s, "staging", fileConsistencyStatus ?? CheckStatus.Warning);
                    if (fileConsistencySeverity == ValidationSeverity.Error && fileConsistencyStatus == CheckStatus.Fail)
                        throw new InvalidOperationException($"File consistency check failed. {BuildFileConsistencyMessage(finalReport, s)}");

                    session.Done(fileConsistencyStep);
                }
                catch (Exception ex)
                {
                    session.Fail(fileConsistencyStep, ex);
                    throw;
                }
            }

            if (runProject)
            {
                session.Start(projectFileConsistencyStep);
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
                    projectFileConsistencyReport = analyzer.Analyze(
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
                        projectFileConsistencyEncodingFix = enc.Convert(encOptions);

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
                        projectFileConsistencyLineEndingFix = le.Convert(lineEndingOptions);

                        projectFileConsistencyReport = analyzer.Analyze(
                            enumeration: enumeration,
                            projectType: kind.ToString(),
                            recommendedEncoding: recommendedEncoding,
                            recommendedLineEnding: s.RequiredLineEnding,
                            includeDetails: false,
                            exportPath: exportPath,
                            encodingOverrides: encodingOverrides,
                            lineEndingOverrides: lineEndingOverrides);
                    }

                    var finalReport = projectFileConsistencyReport ?? throw new InvalidOperationException("Project-root file consistency analysis produced no report.");
                    projectFileConsistencyStatus = EvaluateFileConsistency(finalReport, s, fileConsistencySeverity);
                    if (fileConsistencySeverity != ValidationSeverity.Off)
                        LogFileConsistencyIssues(finalReport, s, "project", projectFileConsistencyStatus ?? CheckStatus.Warning);
                    if (fileConsistencySeverity == ValidationSeverity.Error && projectFileConsistencyStatus == CheckStatus.Fail)
                        throw new InvalidOperationException($"File consistency (project) check failed. {BuildFileConsistencyMessage(finalReport, s)}");

                    session.Done(projectFileConsistencyStep);
                }
                catch (Exception ex)
                {
                    session.Fail(projectFileConsistencyStep, ex);
                    throw;
                }
            }
        }

        if (plan.CompatibilitySettings?.Enable == true)
        {
            session.Start(compatibilityStep);
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
                compatibilityReport = adjusted;

                if (compatibilitySeverity == ValidationSeverity.Error && adjusted.Summary.Status == CheckStatus.Fail)
                    throw new InvalidOperationException($"PowerShell compatibility check failed. {adjusted.Summary.Message}");

                session.Done(compatibilityStep);
            }
            catch (Exception ex)
            {
                session.Fail(compatibilityStep, ex);
                throw;
            }
        }

        if (plan.ValidationSettings?.Enable == true)
        {
            session.Start(moduleValidationStep);
            try
            {
                validationReport = _hostedOperations.ValidateModule(new ModuleValidationSpec
                {
                    ProjectRoot = plan.ProjectRoot,
                    StagingPath = buildResult.StagingPath,
                    ModuleName = plan.ModuleName,
                    ManifestPath = buildResult.ManifestPath,
                    BuildSpec = plan.BuildSpec,
                    Settings = plan.ValidationSettings ?? new ModuleValidationSettings()
                });

                if (validationReport.Status == CheckStatus.Fail)
                    throw new InvalidOperationException($"Module validation failed ({validationReport.Summary}).");

                session.Done(moduleValidationStep);
            }
            catch (Exception ex)
            {
                session.Fail(moduleValidationStep, ex);
                throw;
            }
        }

        if (plan.ImportModules is not null &&
            (plan.ImportModules.Self == true || plan.ImportModules.RequiredModules == true))
        {
            if (plan.ImportModules.RequiredModules == true &&
                ShouldAnalyzeBinaryConflicts(plan.ImportModules, importRequired: true))
            {
                session.Start(binaryConflictAnalysisStep);
                try
                {
                    automaticBinaryConflictDiagnostics = AnalyzeAutomaticBinaryConflicts(plan, buildResult);
                    LogAutomaticBinaryConflictDiagnostics(automaticBinaryConflictDiagnostics);
                    session.Done(binaryConflictAnalysisStep);
                }
                catch (Exception ex)
                {
                    session.Fail(binaryConflictAnalysisStep, ex);
                    throw;
                }
            }

            if (plan.ImportModules.Self == true && plan.ImportModules.SkipBinaryDependencyCheck != true)
            {
                session.Start(binaryDependenciesStep);
                try
                {
                    RunBinaryDependencyPreflight(plan, buildResult);
                    session.Done(binaryDependenciesStep);
                }
                catch (Exception ex)
                {
                    session.Fail(binaryDependenciesStep, ex);
                    throw;
                }
            }

            session.Start(importModulesStep);
            try
            {
                RunImportModules(plan, buildResult);
                session.Done(importModulesStep);
            }
            catch (Exception ex)
            {
                session.Fail(importModulesStep, ex);
                throw;
            }
        }

        if (plan.TestsAfterMerge is { Length: > 0 })
        {
            for (int i = 0; i < plan.TestsAfterMerge.Length; i++)
            {
                var cfg = plan.TestsAfterMerge[i];
                var step = testSteps.Length > i ? testSteps[i] : null;
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

        var artefactResults = new List<ArtefactBuildResult>();
        if (plan.Artefacts is { Length: > 0 })
        {
            var builder = new ArtefactBuilder(_logger);
            foreach (var artefact in plan.Artefacts)
            {
                var step = session.GetArtefactStep(artefact);
                session.Start(step);
                try
                {
                    artefactResults.Add(builder.Build(
                        segment: artefact,
                        projectRoot: plan.ProjectRoot,
                        stagingPath: buildResult.StagingPath,
                        moduleName: plan.ModuleName,
                        moduleVersion: plan.ResolvedVersion,
                        preRelease: plan.PreRelease,
                        requiredModules: packagingRequiredModules,
                        information: plan.Information,
                        delivery: plan.Delivery,
                        includeScriptFolders: !mergedScripts));
                    session.Done(step);
                }
                catch (Exception ex)
                {
                    session.Fail(step, ex);
                    throw;
                }
            }
        }

        var publishResults = new List<ModulePublishResult>();
        if (plan.Publishes is { Length: > 0 })
        {
            foreach (var publish in plan.Publishes)
            {
                var step = session.GetPublishStep(publish);
                session.Start(step);
                try
                {
                    publishResults.Add(_hostedOperations.PublishModule(publish.Configuration, plan, buildResult, artefactResults, includeScriptFolders: !mergedScripts));
                    session.Done(step);
                }
                catch (Exception ex)
                {
                    session.Fail(step, ex);
                    throw;
                }
            }
        }

        ModuleInstallerResult? installResult = null;
            if (plan.InstallEnabled)
            {
                session.Start(installStep);
            string? installPackagePath = null;
            try
            {
                // Install should reflect the packaged module layout (not the full staged repo copy).
                // This prevents shipping repo metadata (e.g., .github/.editorconfig/Sources) to end users.
                installPackagePath = Path.Combine(Path.GetTempPath(), "PowerForge", "install", $"{plan.ModuleName}_{Guid.NewGuid():N}");
                Directory.CreateDirectory(installPackagePath);
                ArtefactBuilder.CopyModulePackageForInstall(
                    buildResult.StagingPath,
                    installPackagePath,
                    plan.Information,
                    plan.Delivery,
                    includeScriptFolders: !mergedScripts);

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
                installResult = pipeline.InstallFromStaging(installSpec);
                session.Done(installStep);
            }
            catch (Exception ex)
            {
                session.Fail(installStep, ex);
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

        var projectManifestSyncMessage = SyncBuildManifestToProjectRoot(plan);

        return BuildPipelineResult(
            spec,
            plan,
            buildResult,
            automaticBinaryConflictDiagnostics,
            installResult,
            documentationResult,
            fileConsistencyReport,
            fileConsistencyStatus,
            fileConsistencyEncodingFix,
            fileConsistencyLineEndingFix,
            compatibilityReport,
            validationReport,
            publishResults.ToArray(),
            artefactResults.ToArray(),
            formattingStagingResults,
            formattingProjectResults,
            projectFileConsistencyReport,
            projectFileConsistencyStatus,
            projectFileConsistencyEncodingFix,
            projectFileConsistencyLineEndingFix,
            signingResult,
            dependencyInstallResults,
            staged,
            mergeExecution,
            projectManifestSyncMessage);
        }
        catch (Exception ex)
        {
            pipelineFailure = ex;
            throw;
        }
        finally
        {
            if (plan.DeleteGeneratedStagingAfterRun)
            {
                session.Start(cleanupStep);
                try { DeleteDirectoryWithRetries(stagingPathForCleanup); }
                catch (Exception ex) { _logger.Warn($"Failed to delete staging folder: {ex.Message}"); }
                session.Done(cleanupStep);
            }

            if (pipelineFailure is not null)
                session.NotifySkippedOnFailure();
        }
    }

}
