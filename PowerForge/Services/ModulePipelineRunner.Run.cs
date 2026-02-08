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

        var reporter = progress ?? NullModulePipelineProgressReporter.Instance;
        var steps = ModulePipelineStep.Create(plan);
        var reporterV2 = reporter as IModulePipelineProgressReporterV2;
        var startedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var artefactSteps = steps
            .Where(s => s.ArtefactSegment is not null)
            .ToDictionary(s => s.ArtefactSegment!, s => s);
        var publishSteps = steps
            .Where(s => s.PublishSegment is not null)
            .ToDictionary(s => s.PublishSegment!, s => s);

        var stageStep = steps.FirstOrDefault(s => string.Equals(s.Key, "build:stage", StringComparison.OrdinalIgnoreCase));
        var buildStep = steps.FirstOrDefault(s => string.Equals(s.Key, "build:build", StringComparison.OrdinalIgnoreCase));
        var manifestStep = steps.FirstOrDefault(s => string.Equals(s.Key, "build:manifest", StringComparison.OrdinalIgnoreCase));

        var docsExtractStep = steps.FirstOrDefault(s => string.Equals(s.Key, "docs:extract", StringComparison.OrdinalIgnoreCase));
        var docsWriteStep = steps.FirstOrDefault(s => string.Equals(s.Key, "docs:write", StringComparison.OrdinalIgnoreCase));
        var docsMamlStep = steps.FirstOrDefault(s => string.Equals(s.Key, "docs:maml", StringComparison.OrdinalIgnoreCase));
        var formatStagingStep = steps.FirstOrDefault(s => string.Equals(s.Key, "format:staging", StringComparison.OrdinalIgnoreCase));
        var formatProjectStep = steps.FirstOrDefault(s => string.Equals(s.Key, "format:project", StringComparison.OrdinalIgnoreCase));
        var signStep = steps.FirstOrDefault(s => string.Equals(s.Key, "sign", StringComparison.OrdinalIgnoreCase));
        var fileConsistencyStep = steps.FirstOrDefault(s => string.Equals(s.Key, "validate:fileconsistency", StringComparison.OrdinalIgnoreCase));
        var projectFileConsistencyStep = steps.FirstOrDefault(s => string.Equals(s.Key, "validate:fileconsistency-project", StringComparison.OrdinalIgnoreCase));
        var compatibilityStep = steps.FirstOrDefault(s => string.Equals(s.Key, "validate:compatibility", StringComparison.OrdinalIgnoreCase));
        var moduleValidationStep = steps.FirstOrDefault(s => string.Equals(s.Key, "validate:module", StringComparison.OrdinalIgnoreCase));
        var importModulesStep = steps.FirstOrDefault(s => string.Equals(s.Key, "tests:import-modules", StringComparison.OrdinalIgnoreCase));
        var testSteps = steps.Where(s => s.Kind == ModulePipelineStepKind.Tests && s.Key.StartsWith("tests:", StringComparison.OrdinalIgnoreCase) && !string.Equals(s.Key, "tests:import-modules", StringComparison.OrdinalIgnoreCase)).ToArray();
        var installStep = steps.FirstOrDefault(s => s.Kind == ModulePipelineStepKind.Install);
        var cleanupStep = steps.FirstOrDefault(s => s.Kind == ModulePipelineStepKind.Cleanup);

        void SafeStart(ModulePipelineStep? step)
        {
            if (step is null) return;
            if (!string.IsNullOrWhiteSpace(step.Key)) startedKeys.Add(step.Key);
            try { reporter.StepStarting(step); } catch { /* best effort */ }
        }

        void SafeDone(ModulePipelineStep? step)
        {
            if (step is null) return;
            try { reporter.StepCompleted(step); } catch { /* best effort */ }
        }

        void SafeFail(ModulePipelineStep? step, Exception ex)
        {
            if (step is null) return;
            try { reporter.StepFailed(step, ex); } catch { /* best effort */ }
        }

        var pipeline = new ModuleBuildPipeline(_logger);
        string? stagingPathForCleanup = plan.BuildSpec.StagingPath;
        Exception? pipelineFailure = null;

        try
        {
            if (plan.InstallMissingModules)
            {
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

            ModuleBuildPipeline.StagingResult staged;
            SafeStart(stageStep);
            try
            {
                staged = pipeline.StageToStaging(plan.BuildSpec);
                stagingPathForCleanup = staged.StagingPath;
                SafeDone(stageStep);
            }
            catch (Exception ex)
            {
                SafeFail(stageStep, ex);
                stagingPathForCleanup ??= plan.BuildSpec.StagingPath;
                throw;
            }

            ModuleBuildResult buildResult;
            SafeStart(buildStep);
            try
            {
                buildResult = pipeline.BuildInStaging(plan.BuildSpec, staged.StagingPath);
                SafeDone(buildStep);
            }
            catch (Exception ex)
            {
                SafeFail(buildStep, ex);
                throw;
            }

            var mergedScripts = ApplyMerge(plan, buildResult);
            ApplyPlaceholders(plan, buildResult);

            SafeStart(manifestStep);
            try
            {
                if (plan.CompatiblePSEditions is { Length: > 0 })
                    ManifestEditor.TrySetTopLevelStringArray(buildResult.ManifestPath, "CompatiblePSEditions", plan.CompatiblePSEditions);

                if (manifestRequiredModules is { Length: > 0 })
                    ManifestEditor.TrySetRequiredModules(buildResult.ManifestPath, manifestRequiredModules);
                if (plan.ExternalModuleDependencies is { Length: > 0 })
                    ManifestEditor.TrySetPsDataStringArray(buildResult.ManifestPath, "ExternalModuleDependencies", plan.ExternalModuleDependencies);
                if (plan.ExternalModuleDependencies is { Length: > 0 })
                    ManifestEditor.TrySetPsDataStringArray(buildResult.ManifestPath, "ExternalModuleDependencies", plan.ExternalModuleDependencies);

                if (!ManifestEditor.TryGetTopLevelStringArray(buildResult.ManifestPath, "ScriptsToProcess", out _) &&
                    !ManifestEditor.TryGetTopLevelString(buildResult.ManifestPath, "ScriptsToProcess", out _))
                {
                    ManifestEditor.TrySetTopLevelStringArray(buildResult.ManifestPath, "ScriptsToProcess", Array.Empty<string>());
                }

                if (plan.CommandModuleDependencies is { Count: > 0 })
                    ManifestEditor.TrySetTopLevelHashtableStringArray(buildResult.ManifestPath, "CommandModuleDependencies", plan.CommandModuleDependencies);

                if (!string.IsNullOrWhiteSpace(plan.PreRelease))
                    ManifestEditor.TrySetTopLevelString(buildResult.ManifestPath, "Prerelease", plan.PreRelease!);

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
                                    var functions = ExportDetector.DetectScriptFunctions(scripts);
                                    BuildServices.SetManifestExports(buildResult.ManifestPath, functions, cmdlets: null, aliases: null);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.Warn($"Failed to update manifest exports after generating delivery commands. Error: {ex.Message}");
                                if (_logger.IsVerbose) _logger.Verbose(ex.ToString());
                            }
                        }
                    }
                }

                if (!mergedScripts)
                    TryRegenerateBootstrapperFromManifest(buildResult, plan.ModuleName, plan.BuildSpec.ExportAssemblies);

                SafeDone(manifestStep);
            }
            catch (Exception ex)
            {
                SafeFail(manifestStep, ex);
                throw;
            }

            DocumentationBuildResult? documentationResult = null;
            if (plan.Documentation is not null && plan.DocumentationBuild?.Enable == true)
            {
                try
                {
                    var engine = new DocumentationEngine(new PowerShellRunner(), _logger);
                    documentationResult = engine.BuildWithProgress(
                        moduleName: plan.ModuleName,
                        stagingPath: buildResult.StagingPath,
                        moduleManifestPath: buildResult.ManifestPath,
                        documentation: plan.Documentation,
                        buildDocumentation: plan.DocumentationBuild!,
                        timeout: null,
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
                    SafeFail(docsExtractStep, ex);
                    SafeFail(docsWriteStep, ex);
                    SafeFail(docsMamlStep, ex);
                    throw;
                }
            }

            FormatterResult[] formattingStagingResults = Array.Empty<FormatterResult>();
            FormatterResult[] formattingProjectResults = Array.Empty<FormatterResult>();
            ModuleSigningResult? signingResult = null;
            ModuleValidationReport? validationReport = null;

            if (plan.Formatting is not null)
            {
                var formattingPipeline = new FormattingPipeline(_logger);       

                SafeStart(formatStagingStep);
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
                    SafeDone(formatStagingStep);
                }
                catch (Exception ex)
                {
                    SafeFail(formatStagingStep, ex);
                    throw;
                }

                if (plan.Formatting.Options.UpdateProjectRoot)
                {
                    SafeStart(formatProjectStep);
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
                        SafeDone(formatProjectStep);
                    }
                    catch (Exception ex)
                    {
                        SafeFail(formatProjectStep, ex);
                        throw;
                    }
                }
            }

            try
            {
                if (manifestRequiredModules is { Length: > 0 })
                    ManifestEditor.TrySetRequiredModules(buildResult.ManifestPath, manifestRequiredModules);

                if (!ManifestEditor.TryGetTopLevelStringArray(buildResult.ManifestPath, "ScriptsToProcess", out _) &&
                    !ManifestEditor.TryGetTopLevelString(buildResult.ManifestPath, "ScriptsToProcess", out _))
                {
                    ManifestEditor.TrySetTopLevelStringArray(buildResult.ManifestPath, "ScriptsToProcess", Array.Empty<string>());
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"Post-format manifest patch failed. {ex.Message}");
                if (_logger.IsVerbose) _logger.Verbose(ex.ToString());
            }

            if (plan.SignModule)
            {
                SafeStart(signStep);
                try
                {
                    signingResult = SignBuiltModuleOutput(
                        moduleName: plan.ModuleName,
                        rootPath: buildResult.StagingPath,
                        signing: plan.Signing);
                    SafeDone(signStep);
                }
                catch (Exception ex)
                {
                    SafeFail(signStep, ex);
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
                SafeStart(fileConsistencyStep);
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

                    SafeDone(fileConsistencyStep);
                }
                catch (Exception ex)
                {
                    SafeFail(fileConsistencyStep, ex);
                    throw;
                }
            }

            if (runProject)
            {
                SafeStart(projectFileConsistencyStep);
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

                    SafeDone(projectFileConsistencyStep);
                }
                catch (Exception ex)
                {
                    SafeFail(projectFileConsistencyStep, ex);
                    throw;
                }
            }
        }

        if (plan.CompatibilitySettings?.Enable == true)
        {
            SafeStart(compatibilityStep);
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

                SafeDone(compatibilityStep);
            }
            catch (Exception ex)
            {
                SafeFail(compatibilityStep, ex);
                throw;
            }
        }

        if (plan.ValidationSettings?.Enable == true)
        {
            SafeStart(moduleValidationStep);
            try
            {
                var validator = new ModuleValidationService(_logger);
                validationReport = validator.Run(new ModuleValidationSpec
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

                SafeDone(moduleValidationStep);
            }
            catch (Exception ex)
            {
                SafeFail(moduleValidationStep, ex);
                throw;
            }
        }

        if (plan.ImportModules is not null &&
            (plan.ImportModules.Self == true || plan.ImportModules.RequiredModules == true))
        {
            SafeStart(importModulesStep);
            try
            {
                RunImportModules(plan, buildResult);
                SafeDone(importModulesStep);
            }
            catch (Exception ex)
            {
                SafeFail(importModulesStep, ex);
                throw;
            }
        }

        if (plan.TestsAfterMerge is { Length: > 0 })
        {
            var testService = new ModuleTestSuiteService(new PowerShellRunner(), _logger);
            for (int i = 0; i < plan.TestsAfterMerge.Length; i++)
            {
                var cfg = plan.TestsAfterMerge[i];
                var step = testSteps.Length > i ? testSteps[i] : null;
                SafeStart(step);
                try
                {
                    RunTestsAfterMerge(plan, buildResult, cfg, testService);
                    SafeDone(step);
                }
                catch (Exception ex)
                {
                    SafeFail(step, ex);
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
                artefactSteps.TryGetValue(artefact, out var step);
                SafeStart(step);
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
                        includeScriptFolders: !mergedScripts));
                    SafeDone(step);
                }
                catch (Exception ex)
                {
                    SafeFail(step, ex);
                    throw;
                }
            }
        }

        var publishResults = new List<ModulePublishResult>();
        if (plan.Publishes is { Length: > 0 })
        {
            var publisher = new ModulePublisher(_logger);
            foreach (var publish in plan.Publishes)
            {
                publishSteps.TryGetValue(publish, out var step);
                SafeStart(step);
                try
                {
                    publishResults.Add(publisher.Publish(publish.Configuration, plan, buildResult, artefactResults));
                    SafeDone(step);
                }
                catch (Exception ex)
                {
                    SafeFail(step, ex);
                    throw;
                }
            }
        }

        ModuleInstallerResult? installResult = null;
            if (plan.InstallEnabled)
            {
                SafeStart(installStep);
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
                    includeScriptFolders: !mergedScripts);

                var installSpec = new ModuleInstallSpec
                {
                    Name = plan.ModuleName,
                    Version = plan.ResolvedVersion,
                    StagingPath = installPackagePath,
                    Strategy = plan.InstallStrategy,
                    KeepVersions = plan.InstallKeepVersions,
                    Roots = plan.InstallRoots,
                    LegacyFlatHandling = spec.Install?.LegacyFlatHandling ?? LegacyFlatModuleHandling.Warn,
                    PreserveVersions = spec.Install?.PreserveVersions ?? Array.Empty<string>()
                };
                installResult = pipeline.InstallFromStaging(installSpec);
                SafeDone(installStep);
            }
            catch (Exception ex)
            {
                SafeFail(installStep, ex);
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
                publishResults.ToArray(),
                artefactResults.ToArray(),
                formattingStagingResults,
                formattingProjectResults,
                projectFileConsistencyReport,
                projectFileConsistencyStatus,
                projectFileConsistencyEncodingFix,
                projectFileConsistencyLineEndingFix,
                signingResult);
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
                SafeStart(cleanupStep);
                try { DeleteDirectoryWithRetries(stagingPathForCleanup); }
                catch (Exception ex) { _logger.Warn($"Failed to delete staging folder: {ex.Message}"); }
                SafeDone(cleanupStep);
            }

            if (pipelineFailure is not null && reporterV2 is not null)
            {
                foreach (var step in steps)
                {
                    if (step is null) continue;
                    if (string.IsNullOrWhiteSpace(step.Key)) continue;
                    if (startedKeys.Contains(step.Key)) continue;
                    try { reporterV2.StepSkipped(step); } catch { /* best effort */ }
                }
            }
        }
    }

}
