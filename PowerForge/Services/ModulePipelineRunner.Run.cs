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
        var manifestExternalModuleDependencies = plan.ExternalModuleDependencies ?? Array.Empty<string>();

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

        var pipeline = new ModuleBuildPipeline(_logger);
        string? stagingPathForCleanup = plan.BuildSpec.StagingPath;
        Exception? pipelineFailure = null;

        try
        {
            EnsureBuildDependenciesInstalledIfNeeded(plan);

            ModuleBuildPipeline.StagingResult staged;
            SafeStart(reporter, startedKeys, stageStep);
            try
            {
                staged = pipeline.StageToStaging(plan.BuildSpec);
                stagingPathForCleanup = staged.StagingPath;
                SafeDone(reporter, stageStep);
            }
            catch (Exception ex)
            {
                SafeFail(reporter, stageStep, ex);
                stagingPathForCleanup ??= plan.BuildSpec.StagingPath;
                throw;
            }

            ModuleBuildResult buildResult;
            SafeStart(reporter, startedKeys, buildStep);
            try
            {
                buildResult = pipeline.BuildInStaging(plan.BuildSpec, staged.StagingPath);
                SafeDone(reporter, buildStep);
            }
            catch (Exception ex)
            {
                SafeFail(reporter, buildStep, ex);
                throw;
            }

            var mergedScripts = plan.BuildSpec.RefreshManifestOnly ? false : ApplyMerge(plan, buildResult);
            if (!plan.BuildSpec.RefreshManifestOnly)
                ApplyPlaceholders(plan, buildResult);

            SafeStart(reporter, startedKeys, manifestStep);
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

                if (!mergedScripts && !plan.BuildSpec.RefreshManifestOnly)
                    TryRegenerateBootstrapperFromManifest(buildResult, plan.ModuleName, plan.BuildSpec.ExportAssemblies);

                SafeDone(reporter, manifestStep);
            }
            catch (Exception ex)
            {
                SafeFail(reporter, manifestStep, ex);
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
                    SafeFail(reporter, docsExtractStep, ex);
                    SafeFail(reporter, docsWriteStep, ex);
                    SafeFail(reporter, docsMamlStep, ex);
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

                SafeStart(reporter, startedKeys, formatStagingStep);
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
                    SafeDone(reporter, formatStagingStep);
                }
                catch (Exception ex)
                {
                    SafeFail(reporter, formatStagingStep, ex);
                    throw;
                }

                if (plan.Formatting.Options.UpdateProjectRoot)
                {
                    SafeStart(reporter, startedKeys, formatProjectStep);
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
                        SafeDone(reporter, formatProjectStep);
                    }
                    catch (Exception ex)
                    {
                        SafeFail(reporter, formatProjectStep, ex);
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
                SafeStart(reporter, startedKeys, signStep);
                try
                {
                    signingResult = SignBuiltModuleOutput(
                        moduleName: plan.ModuleName,
                        rootPath: buildResult.StagingPath,
                        signing: plan.Signing);
                    SafeDone(reporter, signStep);
                }
                catch (Exception ex)
                {
                    SafeFail(reporter, signStep, ex);
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
                SafeStart(reporter, startedKeys, fileConsistencyStep);
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

                    SafeDone(reporter, fileConsistencyStep);
                }
                catch (Exception ex)
                {
                    SafeFail(reporter, fileConsistencyStep, ex);
                    throw;
                }
            }

            if (runProject)
            {
                SafeStart(reporter, startedKeys, projectFileConsistencyStep);
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

                    SafeDone(reporter, projectFileConsistencyStep);
                }
                catch (Exception ex)
                {
                    SafeFail(reporter, projectFileConsistencyStep, ex);
                    throw;
                }
            }
        }

        if (plan.CompatibilitySettings?.Enable == true)
        {
            SafeStart(reporter, startedKeys, compatibilityStep);
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

                SafeDone(reporter, compatibilityStep);
            }
            catch (Exception ex)
            {
                SafeFail(reporter, compatibilityStep, ex);
                throw;
            }
        }

        if (plan.ValidationSettings?.Enable == true)
        {
            SafeStart(reporter, startedKeys, moduleValidationStep);
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

                SafeDone(reporter, moduleValidationStep);
            }
            catch (Exception ex)
            {
                SafeFail(reporter, moduleValidationStep, ex);
                throw;
            }
        }

        if (plan.ImportModules is not null &&
            (plan.ImportModules.Self == true || plan.ImportModules.RequiredModules == true))
        {
            SafeStart(reporter, startedKeys, importModulesStep);
            try
            {
                RunImportModules(plan, buildResult);
                SafeDone(reporter, importModulesStep);
            }
            catch (Exception ex)
            {
                SafeFail(reporter, importModulesStep, ex);
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
                SafeStart(reporter, startedKeys, step);
                try
                {
                    RunTestsAfterMerge(plan, buildResult, cfg, testService);
                    SafeDone(reporter, step);
                }
                catch (Exception ex)
                {
                    SafeFail(reporter, step, ex);
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
                SafeStart(reporter, startedKeys, step);
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
                    SafeDone(reporter, step);
                }
                catch (Exception ex)
                {
                    SafeFail(reporter, step, ex);
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
                SafeStart(reporter, startedKeys, step);
                try
                {
                    publishResults.Add(publisher.Publish(publish.Configuration, plan, buildResult, artefactResults, includeScriptFolders: !mergedScripts));
                    SafeDone(reporter, step);
                }
                catch (Exception ex)
                {
                    SafeFail(reporter, step, ex);
                    throw;
                }
            }
        }

        ModuleInstallerResult? installResult = null;
            if (plan.InstallEnabled)
            {
                SafeStart(reporter, startedKeys, installStep);
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
                    UpdateManifestToResolvedVersion = spec.Install?.UpdateManifestToResolvedVersion ?? true,
                    LegacyFlatHandling = plan.InstallLegacyFlatHandling,
                    PreserveVersions = plan.InstallPreserveVersions
                };
                installResult = pipeline.InstallFromStaging(installSpec);
                SafeDone(reporter, installStep);
            }
            catch (Exception ex)
            {
                SafeFail(reporter, installStep, ex);
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

        return BuildPipelineResult(
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
                SafeStart(reporter, startedKeys, cleanupStep);
                try { DeleteDirectoryWithRetries(stagingPathForCleanup); }
                catch (Exception ex) { _logger.Warn($"Failed to delete staging folder: {ex.Message}"); }
                SafeDone(reporter, cleanupStep);
            }

            if (pipelineFailure is not null)
                NotifySkippedStepsOnFailure(reporterV2, steps, startedKeys);
        }
    }

}
