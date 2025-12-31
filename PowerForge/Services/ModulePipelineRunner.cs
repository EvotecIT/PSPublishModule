using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PowerForge;

/// <summary>
/// Plans and executes a configuration-driven module build workflow using <see cref="ModuleBuildPipeline"/>.
/// </summary>
public sealed class ModulePipelineRunner
{
    private readonly ILogger _logger;

    private sealed class RequiredModuleDraft
    {
        public string ModuleName { get; }
        public string? ModuleVersion { get; }
        public string? RequiredVersion { get; }
        public string? Guid { get; }

        public RequiredModuleDraft(string moduleName, string? moduleVersion, string? requiredVersion, string? guid)
        {
            ModuleName = moduleName;
            ModuleVersion = moduleVersion;
            RequiredVersion = requiredVersion;
            Guid = guid;
        }
    }

    /// <summary>
    /// Creates a new instance using the provided logger.
    /// </summary>
    public ModulePipelineRunner(ILogger logger) => _logger = logger;

    /// <summary>
    /// Computes an execution plan from <paramref name="spec"/> by overlaying configuration segments on top of the
    /// base build settings.
    /// </summary>
    public ModulePipelinePlan Plan(ModulePipelineSpec spec)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        if (spec.Build is null) throw new ArgumentException("Build is required.", nameof(spec));

        var moduleName = spec.Build.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(moduleName))
            throw new ArgumentException("Build.Name is required.", nameof(spec));

        var projectRoot = spec.Build.SourcePath?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(projectRoot))
            throw new ArgumentException("Build.SourcePath is required.", nameof(spec));
        projectRoot = Path.GetFullPath(projectRoot);

        // Aggregated values from segments (last-wins for scalars, last-wins per module for required modules).
        string? expectedVersion = null;
        string[] compatible = Array.Empty<string>();
        string? preRelease = null;

        string? author = null;
        string? companyName = null;
        string? description = null;
        string[]? tags = null;
        string? iconUri = null;
        string? projectUri = null;

        bool localVersioning = false;
        InstallationStrategy? installStrategyFromSegments = null;
        int? keepVersionsFromSegments = null;

        string? dotnetConfigFromSegments = null;
        string[]? dotnetFrameworksFromSegments = null;
        string? netProjectName = null;
        string? netProjectPath = null;
        string[]? exportAssembliesFromSegments = null;
        bool? disableBinaryCmdletScanFromSegments = null;

        InformationConfiguration? information = null;
        DocumentationConfiguration? documentation = null;
        BuildDocumentationConfiguration? documentationBuild = null;
        CompatibilitySettings? compatibilitySettings = null;
        FileConsistencySettings? fileConsistencySettings = null;
        ConfigurationFormattingSegment? formatting = null;
        var artefacts = new List<ConfigurationArtefactSegment>();
        var publishes = new List<ConfigurationPublishSegment>();

        var requiredModulesDraft = new List<RequiredModuleDraft>();
        var requiredIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var requiredModulesDraftForPackaging = new List<RequiredModuleDraft>();
        var requiredPackagingIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var segment in (spec.Segments ?? Array.Empty<IConfigurationSegment>()).Where(s => s is not null))
        {
            switch (segment)
            {
                case ConfigurationManifestSegment manifest:
                {
                    var m = manifest.Configuration;
                    if (!string.IsNullOrWhiteSpace(m.ModuleVersion)) expectedVersion = m.ModuleVersion;
                    if (m.CompatiblePSEditions is { Length: > 0 }) compatible = m.CompatiblePSEditions;
                    if (!string.IsNullOrWhiteSpace(m.Prerelease)) preRelease = m.Prerelease;

                    if (!string.IsNullOrWhiteSpace(m.Author)) author = m.Author;
                    if (!string.IsNullOrWhiteSpace(m.CompanyName)) companyName = m.CompanyName;
                    if (!string.IsNullOrWhiteSpace(m.Description)) description = m.Description;
                    if (m.Tags is { Length: > 0 }) tags = m.Tags;
                    if (!string.IsNullOrWhiteSpace(m.IconUri)) iconUri = m.IconUri;
                    if (!string.IsNullOrWhiteSpace(m.ProjectUri)) projectUri = m.ProjectUri;
                    break;
                }
                case ConfigurationBuildSegment build:
                {
                    var b = build.BuildModule;
                    if (b.LocalVersion.HasValue) localVersioning = b.LocalVersion.Value;
                    if (b.VersionedInstallStrategy.HasValue) installStrategyFromSegments = b.VersionedInstallStrategy.Value;
                    if (b.VersionedInstallKeep.HasValue) keepVersionsFromSegments = b.VersionedInstallKeep.Value;
                    break;
                }
                case ConfigurationBuildLibrariesSegment buildLibraries:
                {
                    var bl = buildLibraries.BuildLibraries;
                    if (!string.IsNullOrWhiteSpace(bl.Configuration)) dotnetConfigFromSegments = bl.Configuration;
                    if (bl.Framework is { Length: > 0 }) dotnetFrameworksFromSegments = bl.Framework;
                    if (!string.IsNullOrWhiteSpace(bl.ProjectName)) netProjectName = bl.ProjectName;
                    if (!string.IsNullOrWhiteSpace(bl.NETProjectPath)) netProjectPath = bl.NETProjectPath;
                    if (bl.BinaryModule is { Length: > 0 }) exportAssembliesFromSegments = bl.BinaryModule;
                    if (bl.BinaryModuleCmdletScanDisabled.HasValue) disableBinaryCmdletScanFromSegments = bl.BinaryModuleCmdletScanDisabled.Value;
                    break;
                }
                case ConfigurationModuleSegment moduleSeg:
                {
                    if (moduleSeg.Kind is not (ModuleDependencyKind.RequiredModule or ModuleDependencyKind.ExternalModule))
                        break;

                    var md = moduleSeg.Configuration;
                    if (string.IsNullOrWhiteSpace(md.ModuleName)) break;
                    var name = md.ModuleName.Trim();
                    var draft = new RequiredModuleDraft(
                        moduleName: name,
                        moduleVersion: md.ModuleVersion,
                        requiredVersion: md.RequiredVersion,
                        guid: md.Guid);

                    if (requiredIndex.TryGetValue(name, out var idx))
                        requiredModulesDraft[idx] = draft;
                    else
                    {
                        requiredIndex[name] = requiredModulesDraft.Count;
                        requiredModulesDraft.Add(draft);
                    }

                    if (moduleSeg.Kind == ModuleDependencyKind.RequiredModule)
                    {
                        if (requiredPackagingIndex.TryGetValue(name, out var pidx))
                            requiredModulesDraftForPackaging[pidx] = draft;
                        else
                        {
                            requiredPackagingIndex[name] = requiredModulesDraftForPackaging.Count;
                            requiredModulesDraftForPackaging.Add(draft);
                        }
                    }
                    break;
                }
                case ConfigurationInformationSegment info:
                {
                    information = info.Configuration;
                    break;
                }
                case ConfigurationDocumentationSegment docs:
                {
                    documentation = docs.Configuration;
                    break;
                }
                case ConfigurationBuildDocumentationSegment buildDocs:
                {
                    documentationBuild = buildDocs.Configuration;
                    break;
                }
                case ConfigurationCompatibilitySegment compatibility:
                {
                    compatibilitySettings = compatibility.Settings;
                    break;
                }
                case ConfigurationFileConsistencySegment fileConsistency:
                {
                    fileConsistencySettings = fileConsistency.Settings;   
                    break;
                }
                case ConfigurationFormattingSegment formattingSegment:
                {
                    formatting = formattingSegment;
                    break;
                }
                case ConfigurationPublishSegment publish:
                {
                    publishes.Add(publish);
                    break;
                }
                case ConfigurationArtefactSegment artefact:
                {
                    artefacts.Add(artefact);
                    break;
                }
            }
        }

        expectedVersion ??= spec.Build.Version;
        if (IsAutoVersion(expectedVersion))
        {
            var psd1 = Path.Combine(projectRoot, $"{moduleName}.psd1");
            try
            {
                if (File.Exists(psd1) &&
                    ManifestEditor.TryGetTopLevelString(psd1, "ModuleVersion", out var v) &&
                    !string.IsNullOrWhiteSpace(v))
                {
                    expectedVersion = v;
                }
                else
                {
                    _logger.Warn($"Build.Version was 'auto' but ModuleVersion could not be read from: {psd1}. Falling back to 1.0.0.");
                    expectedVersion = "1.0.0";
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to read ModuleVersion from manifest: {psd1}. Falling back to 1.0.0. Error: {ex.Message}");
                expectedVersion = "1.0.0";
            }
        }

        var expectedVersionResolved = string.IsNullOrWhiteSpace(expectedVersion) ? "1.0.0" : expectedVersion!;

        var localPsd1 = localVersioning ? Path.Combine(projectRoot, $"{moduleName}.psd1") : null;
        var stepper = new ModuleVersionStepper(_logger);
        var resolved = stepper.Step(expectedVersionResolved, moduleName, localPsd1Path: localPsd1).Version;

        // Resolve .csproj path: explicit build setting wins, otherwise derive from BuildLibraries NETProjectPath/ProjectName.
        var csproj = !string.IsNullOrWhiteSpace(spec.Build.CsprojPath)
            ? spec.Build.CsprojPath
            : TryResolveCsprojPath(projectRoot, moduleName, netProjectPath, netProjectName);

        var dotnetConfig = !string.IsNullOrWhiteSpace(dotnetConfigFromSegments)
            ? dotnetConfigFromSegments!
            : (string.IsNullOrWhiteSpace(spec.Build.Configuration) ? "Release" : spec.Build.Configuration);

        var frameworks = dotnetFrameworksFromSegments is { Length: > 0 }
            ? dotnetFrameworksFromSegments
            : (spec.Build.Frameworks ?? Array.Empty<string>());

        var buildSpec = new ModuleBuildSpec
        {
            Name = moduleName,
            SourcePath = projectRoot,
            StagingPath = spec.Build.StagingPath,
            CsprojPath = csproj,
            Version = resolved,
            Configuration = dotnetConfig,
            Frameworks = frameworks,
            Author = author ?? spec.Build.Author,
            CompanyName = companyName ?? spec.Build.CompanyName,
            Description = description ?? spec.Build.Description,
            Tags = tags ?? spec.Build.Tags ?? Array.Empty<string>(),
            IconUri = iconUri ?? spec.Build.IconUri,
            ProjectUri = projectUri ?? spec.Build.ProjectUri,
            ExcludeDirectories = spec.Build.ExcludeDirectories ?? Array.Empty<string>(),
            ExcludeFiles = spec.Build.ExcludeFiles ?? Array.Empty<string>(),
            ExportAssemblies = exportAssembliesFromSegments ?? spec.Build.ExportAssemblies ?? Array.Empty<string>(),
            DisableBinaryCmdletScan = disableBinaryCmdletScanFromSegments ?? spec.Build.DisableBinaryCmdletScan,
            KeepStaging = spec.Build.KeepStaging
        };

        var stagingWasGenerated = string.IsNullOrWhiteSpace(spec.Build.StagingPath);
        var deleteAfter = stagingWasGenerated && !spec.Build.KeepStaging;

        var installEnabled = spec.Install?.Enabled ?? true;
        var strategy = spec.Install?.Strategy
                       ?? installStrategyFromSegments
                       ?? InstallationStrategy.AutoRevision;
        var keep = spec.Install?.KeepVersions
                   ?? keepVersionsFromSegments
                   ?? 3;
        if (keep < 1) keep = 1;

        var roots = (spec.Install?.Roots ?? Array.Empty<string>())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .ToArray();

        if (roots.Length == 0 && compatible is { Length: > 0 })
            roots = ResolveInstallRootsFromCompatiblePSEditions(compatible);

        var requiredModules = ResolveRequiredModules(requiredModulesDraft);
        var requiredModulesForPackaging = ResolveRequiredModules(requiredModulesDraftForPackaging);
        var enabledArtefacts = artefacts
            .Where(a => a is not null && a.Configuration?.Enabled == true)      
            .ToArray();
        var enabledPublishes = publishes
            .Where(p => p is not null && p.Configuration?.Enabled == true)
            .ToArray();

        return new ModulePipelinePlan(
            moduleName: moduleName,
            projectRoot: projectRoot,
            expectedVersion: expectedVersionResolved,
            resolvedVersion: resolved,
            preRelease: preRelease,
            buildSpec: buildSpec,
            compatiblePSEditions: compatible,
            requiredModules: requiredModules,
            requiredModulesForPackaging: requiredModulesForPackaging,
            information: information,
            documentation: documentation,
            documentationBuild: documentationBuild,
            compatibilitySettings: compatibilitySettings,
            fileConsistencySettings: fileConsistencySettings,
            formatting: formatting,
            publishes: enabledPublishes,
            artefacts: enabledArtefacts,
            installEnabled: installEnabled,
            installStrategy: strategy,
            installKeepVersions: keep,
            installRoots: roots,
            stagingWasGenerated: stagingWasGenerated,
            deleteGeneratedStagingAfterRun: deleteAfter);
    }

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

        var reporter = progress ?? NullModulePipelineProgressReporter.Instance;
        var steps = ModulePipelineStep.Create(plan);

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
        var fileConsistencyStep = steps.FirstOrDefault(s => string.Equals(s.Key, "validate:fileconsistency", StringComparison.OrdinalIgnoreCase));
        var projectFileConsistencyStep = steps.FirstOrDefault(s => string.Equals(s.Key, "validate:fileconsistency-project", StringComparison.OrdinalIgnoreCase));
        var compatibilityStep = steps.FirstOrDefault(s => string.Equals(s.Key, "validate:compatibility", StringComparison.OrdinalIgnoreCase));
        var installStep = steps.FirstOrDefault(s => s.Kind == ModulePipelineStepKind.Install);
        var cleanupStep = steps.FirstOrDefault(s => s.Kind == ModulePipelineStepKind.Cleanup);

        void SafeStart(ModulePipelineStep? step)
        {
            if (step is null) return;
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

        ModuleBuildPipeline.StagingResult staged;
        SafeStart(stageStep);
        try
        {
            staged = pipeline.StageToStaging(plan.BuildSpec);
            SafeDone(stageStep);
        }
        catch (Exception ex)
        {
            SafeFail(stageStep, ex);
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

        SafeStart(manifestStep);
        try
        {
            if (plan.CompatiblePSEditions is { Length: > 0 })
                ManifestEditor.TrySetTopLevelStringArray(buildResult.ManifestPath, "CompatiblePSEditions", plan.CompatiblePSEditions);

            if (plan.RequiredModules is { Length: > 0 })
                ManifestEditor.TrySetRequiredModules(buildResult.ManifestPath, plan.RequiredModules);

            if (!string.IsNullOrWhiteSpace(plan.PreRelease))
                ManifestEditor.TrySetTopLevelString(buildResult.ManifestPath, "Prerelease", plan.PreRelease!);

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
                    SafeDone(formatProjectStep);
                }
                catch (Exception ex)
                {
                    SafeFail(formatProjectStep, ex);
                    throw;
                }
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

            SafeStart(fileConsistencyStep);
            try
            {
                var excludeDirs = MergeExcludeDirectories(
                    s.ExcludeDirectories,
                    new[] { ".git", ".vs", "bin", "obj", "packages", "node_modules", ".vscode", "Artefacts", "Ignore", "Lib", "Modules" });

                var enumeration = new ProjectEnumeration(
                    rootPath: buildResult.StagingPath,
                    kind: ProjectKind.Mixed,
                    customExtensions: null,
                    excludeDirectories: excludeDirs);

                var recommendedEncoding = MapEncoding(s.RequiredEncoding);
                var exportPath = s.ExportReport
                    ? BuildArtefactsReportPath(plan.ProjectRoot, s.ReportFileName, fallbackFileName: "FileConsistencyReport.csv")
                    : null;

                var analyzer = new ProjectConsistencyAnalyzer(_logger);
                fileConsistencyReport = analyzer.Analyze(
                    enumeration: enumeration,
                    projectType: "Mixed",
                    recommendedEncoding: recommendedEncoding,
                    recommendedLineEnding: s.RequiredLineEnding,
                    includeDetails: false,
                    exportPath: exportPath);

                if (s.AutoFix)
                {
                    var enc = new EncodingConverter();
                    fileConsistencyEncodingFix = enc.Convert(new EncodingConversionOptions(
                        enumeration: enumeration,
                        sourceEncoding: TextEncodingKind.Any,
                        targetEncoding: recommendedEncoding,
                        createBackups: s.CreateBackups,
                        backupDirectory: null,
                        force: false,
                        noRollbackOnMismatch: false,
                        preferUtf8BomForPowerShell: s.RequiredEncoding == FileConsistencyEncoding.UTF8BOM));

                    var le = new LineEndingConverter();
                    var target = s.RequiredLineEnding == FileConsistencyLineEnding.CRLF ? LineEnding.CRLF : LineEnding.LF;
                    fileConsistencyLineEndingFix = le.Convert(new LineEndingConversionOptions(
                        enumeration: enumeration,
                        target: target,
                        createBackups: s.CreateBackups,
                        backupDirectory: null,
                        force: false,
                        onlyMixed: false,
                        ensureFinalNewline: s.CheckMissingFinalNewline,
                        onlyMissingNewline: false,
                        preferUtf8BomForPowerShell: s.RequiredEncoding == FileConsistencyEncoding.UTF8BOM));

                    fileConsistencyReport = analyzer.Analyze(
                        enumeration: enumeration,
                        projectType: "Mixed",
                        recommendedEncoding: recommendedEncoding,
                        recommendedLineEnding: s.RequiredLineEnding,
                        includeDetails: false,
                        exportPath: exportPath);
                }

                var finalReport = fileConsistencyReport ?? throw new InvalidOperationException("File consistency analysis produced no report.");
                fileConsistencyStatus = EvaluateFileConsistency(finalReport, s);
                if (s.FailOnInconsistency && fileConsistencyStatus == CheckStatus.Fail)
                    throw new InvalidOperationException($"File consistency check failed. {BuildFileConsistencyMessage(finalReport, s)}");

                SafeDone(fileConsistencyStep);
            }
            catch (Exception ex)
            {
                SafeFail(fileConsistencyStep, ex);
                throw;
            }

            if (s.UpdateProjectRoot)
            {
                SafeStart(projectFileConsistencyStep);
                try
                {
                    var excludeDirs = MergeExcludeDirectories(
                        s.ExcludeDirectories,
                        new[] { ".git", ".vs", "bin", "obj", "packages", "node_modules", ".vscode", "Artefacts", "Ignore", "Lib", "Modules" });

                    var enumeration = new ProjectEnumeration(
                        rootPath: plan.ProjectRoot,
                        kind: ProjectKind.Mixed,
                        customExtensions: null,
                        excludeDirectories: excludeDirs);

                    var recommendedEncoding = MapEncoding(s.RequiredEncoding);
                    var exportPath = s.ExportReport
                        ? BuildArtefactsReportPath(plan.ProjectRoot, AddFileNameSuffix(s.ReportFileName, "Project"), fallbackFileName: "FileConsistencyReport.Project.csv")
                        : null;

                    var analyzer = new ProjectConsistencyAnalyzer(_logger);
                    projectFileConsistencyReport = analyzer.Analyze(
                        enumeration: enumeration,
                        projectType: "Mixed",
                        recommendedEncoding: recommendedEncoding,
                        recommendedLineEnding: s.RequiredLineEnding,
                        includeDetails: false,
                        exportPath: exportPath);

                    if (s.AutoFix)
                    {
                        var enc = new EncodingConverter();
                        projectFileConsistencyEncodingFix = enc.Convert(new EncodingConversionOptions(
                            enumeration: enumeration,
                            sourceEncoding: TextEncodingKind.Any,
                            targetEncoding: recommendedEncoding,
                            createBackups: s.CreateBackups,
                            backupDirectory: null,
                            force: false,
                            noRollbackOnMismatch: false,
                            preferUtf8BomForPowerShell: s.RequiredEncoding == FileConsistencyEncoding.UTF8BOM));

                        var le = new LineEndingConverter();
                        var target = s.RequiredLineEnding == FileConsistencyLineEnding.CRLF ? LineEnding.CRLF : LineEnding.LF;
                        projectFileConsistencyLineEndingFix = le.Convert(new LineEndingConversionOptions(
                            enumeration: enumeration,
                            target: target,
                            createBackups: s.CreateBackups,
                            backupDirectory: null,
                            force: false,
                            onlyMixed: false,
                            ensureFinalNewline: s.CheckMissingFinalNewline,
                            onlyMissingNewline: false,
                            preferUtf8BomForPowerShell: s.RequiredEncoding == FileConsistencyEncoding.UTF8BOM));

                        projectFileConsistencyReport = analyzer.Analyze(
                            enumeration: enumeration,
                            projectType: "Mixed",
                            recommendedEncoding: recommendedEncoding,
                            recommendedLineEnding: s.RequiredLineEnding,
                            includeDetails: false,
                            exportPath: exportPath);
                    }

                    var finalReport = projectFileConsistencyReport ?? throw new InvalidOperationException("Project-root file consistency analysis produced no report.");
                    projectFileConsistencyStatus = EvaluateFileConsistency(finalReport, s);
                    if (s.FailOnInconsistency && projectFileConsistencyStatus == CheckStatus.Fail)
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
                var excludeDirs = MergeExcludeDirectories(
                    s.ExcludeDirectories,
                    new[] { ".git", ".vs", "bin", "obj", "packages", "node_modules", ".vscode", "Artefacts", "Ignore", "Lib", "Modules" });

                var exportPath = s.ExportReport
                    ? BuildArtefactsReportPath(plan.ProjectRoot, s.ReportFileName, fallbackFileName: "PowerShellCompatibilityReport.csv")
                    : null;

                var analyzer = new PowerShellCompatibilityAnalyzer(_logger);
                var specCompat = new PowerShellCompatibilitySpec(buildResult.StagingPath, recurse: true, excludeDirectories: excludeDirs);
                var raw = analyzer.Analyze(specCompat, progress: null, exportPath: exportPath);
                var adjusted = ApplyCompatibilitySettings(raw, s);
                compatibilityReport = adjusted;

                if (s.FailOnIncompatibility && adjusted.Summary.Status == CheckStatus.Fail)
                    throw new InvalidOperationException($"PowerShell compatibility check failed. {adjusted.Summary.Message}");

                SafeDone(compatibilityStep);
            }
            catch (Exception ex)
            {
                SafeFail(compatibilityStep, ex);
                throw;
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
                        requiredModules: plan.RequiredModulesForPackaging,
                        information: plan.Information));
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
            try
            {
                var installSpec = new ModuleInstallSpec
                {
                    Name = plan.ModuleName,
                    Version = plan.ResolvedVersion,
                    StagingPath = buildResult.StagingPath,
                    Strategy = plan.InstallStrategy,
                    KeepVersions = plan.InstallKeepVersions,
                    Roots = plan.InstallRoots
                };
                installResult = pipeline.InstallFromStaging(installSpec);
                SafeDone(installStep);
            }
            catch (Exception ex)
            {
                SafeFail(installStep, ex);
                throw;
            }
        }

        if (plan.DeleteGeneratedStagingAfterRun)
        {
            SafeStart(cleanupStep);
            try { Directory.Delete(buildResult.StagingPath, recursive: true); }
            catch (Exception ex) { _logger.Warn($"Failed to delete staging folder: {ex.Message}"); }
            SafeDone(cleanupStep);
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
            publishResults.ToArray(),
            artefactResults.ToArray(),
            formattingStagingResults,
            formattingProjectResults,
            projectFileConsistencyReport,
            projectFileConsistencyStatus,
            projectFileConsistencyEncodingFix,
            projectFileConsistencyLineEndingFix);
    }

    private void SyncGeneratedDocumentationToProjectRoot(ModulePipelinePlan plan, DocumentationBuildResult documentationResult)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (documentationResult is null) throw new ArgumentNullException(nameof(documentationResult));
        if (!documentationResult.Succeeded) return;
        if (plan.Documentation is null) return;
        if (plan.DocumentationBuild is null) return;

        var projectRoot = Path.GetFullPath(plan.ProjectRoot);

        var sourceDocs = documentationResult.DocsPath;
        if (string.IsNullOrWhiteSpace(sourceDocs) || !Directory.Exists(sourceDocs))
        {
            _logger.Warn("Documentation generation succeeded, but DocsPath does not exist; skipping project doc sync.");
            return;
        }

        var targetDocs = ResolvePath(projectRoot, plan.Documentation.Path);
        var targetReadme = ResolvePath(projectRoot, plan.Documentation.PathReadme);

        var fullTargetDocs = Path.GetFullPath(targetDocs).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullProjectRoot = projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(fullTargetDocs, fullProjectRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Documentation.Path resolves to the project root. Refusing to sync documentation to avoid overwriting project files. Set Documentation.Path to a folder (e.g. 'Docs').");

        if (plan.DocumentationBuild.StartClean)
        {
            try
            {
                if (Directory.Exists(fullTargetDocs))
                    Directory.Delete(fullTargetDocs, recursive: true);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to clean project docs folder '{fullTargetDocs}'. Error: {ex.Message}");
            }
        }

        DirectoryCopy(sourceDocs, fullTargetDocs);

        if (!string.IsNullOrWhiteSpace(documentationResult.ReadmePath) &&
            File.Exists(documentationResult.ReadmePath) &&
            !string.IsNullOrWhiteSpace(targetReadme))
        {
            var destDir = Path.GetDirectoryName(Path.GetFullPath(targetReadme));
            if (!string.IsNullOrWhiteSpace(destDir))
                Directory.CreateDirectory(destDir);
            File.Copy(documentationResult.ReadmePath, targetReadme, overwrite: true);
        }

        if (plan.DocumentationBuild.GenerateExternalHelp &&
            !string.IsNullOrWhiteSpace(documentationResult.ExternalHelpFilePath) &&
            File.Exists(documentationResult.ExternalHelpFilePath))
        {
            var externalHelpDir = Path.GetDirectoryName(Path.GetFullPath(documentationResult.ExternalHelpFilePath));
            var cultureFolder = string.IsNullOrWhiteSpace(externalHelpDir)
                ? plan.DocumentationBuild.ExternalHelpCulture
                : new DirectoryInfo(externalHelpDir).Name;

            var targetCultureDir = Path.Combine(projectRoot, cultureFolder);
            Directory.CreateDirectory(targetCultureDir);

            var targetHelpFile = Path.Combine(targetCultureDir, Path.GetFileName(documentationResult.ExternalHelpFilePath));
            File.Copy(documentationResult.ExternalHelpFilePath, targetHelpFile, overwrite: true);
        }

        _logger.Success($"Updated project documentation at '{targetDocs}'.");
    }

    private static string ResolvePath(string baseDir, string path)
    {
        var p = (path ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(p)) return Path.GetFullPath(baseDir);
        if (Path.IsPathRooted(p)) return Path.GetFullPath(p);
        return Path.GetFullPath(Path.Combine(baseDir, p));
    }

    private static void DirectoryCopy(string sourceDir, string destDir)
    {
        var source = Path.GetFullPath(sourceDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var dest = Path.GetFullPath(destDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"Source directory not found: {source}");

        Directory.CreateDirectory(dest);

        var sourcePrefix = source + Path.DirectorySeparatorChar;
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var full = Path.GetFullPath(dir);
            var rel = full.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase)
                ? full.Substring(sourcePrefix.Length)
                : Path.GetFileName(full) ?? full;
            Directory.CreateDirectory(Path.Combine(dest, rel));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var full = Path.GetFullPath(file);
            var rel = full.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase)
                ? full.Substring(sourcePrefix.Length)
                : Path.GetFileName(full) ?? full;

            var target = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(full, target, overwrite: true);
        }
    }

    private sealed class NullModulePipelineProgressReporter : IModulePipelineProgressReporter
    {
        public static readonly NullModulePipelineProgressReporter Instance = new();

        private NullModulePipelineProgressReporter() { }

        public void StepStarting(ModulePipelineStep step) { }
        public void StepCompleted(ModulePipelineStep step) { }
        public void StepFailed(ModulePipelineStep step, Exception error) { }
    }

    private static bool IsAutoVersion(string? value)
        => string.IsNullOrWhiteSpace(value) ||
           value!.Trim().Equals("Auto", StringComparison.OrdinalIgnoreCase);

    private ManifestEditor.RequiredModule[] ResolveRequiredModules(IReadOnlyList<RequiredModuleDraft> drafts)
    {
        var list = (drafts ?? Array.Empty<RequiredModuleDraft>())
            .Where(d => d is not null && !string.IsNullOrWhiteSpace(d.ModuleName))
            .ToArray();
        if (list.Length == 0) return Array.Empty<ManifestEditor.RequiredModule>();

        var moduleNames = list.Select(d => d.ModuleName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var installed = TryGetLatestInstalledModuleInfo(moduleNames);

        var results = new List<ManifestEditor.RequiredModule>(list.Length);
        foreach (var d in list)
        {
            installed.TryGetValue(d.ModuleName, out var info);

            var required = ResolveAutoOrLatest(d.RequiredVersion, info.Version);
            var moduleVersion = ResolveAutoOrLatest(d.ModuleVersion, info.Version);
            var guid = ResolveAutoGuid(d.Guid, info.Guid);

            // RequiredVersion is exact; do not also emit ModuleVersion when present.
            if (!string.IsNullOrWhiteSpace(required)) moduleVersion = null;

            results.Add(new ManifestEditor.RequiredModule(d.ModuleName, moduleVersion: moduleVersion, requiredVersion: required, guid: guid));
        }

        return results.ToArray();
    }

    private static string? ResolveAutoOrLatest(string? value, string? installedVersion)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value!.Trim();
        if (trimmed.Equals("Latest", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(installedVersion) ? null : installedVersion;
        }
        return trimmed;
    }

    private static string? ResolveAutoGuid(string? value, string? installedGuid)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value!.Trim();
        if (trimmed.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrWhiteSpace(installedGuid) ? null : installedGuid;
        return trimmed;
    }

    private Dictionary<string, (string? Version, string? Guid)> TryGetLatestInstalledModuleInfo(IReadOnlyList<string> names)
    {
        var list = (names ?? Array.Empty<string>())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (list.Length == 0) return new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase);

        var runner = new PowerShellRunner();
        var script = BuildGetInstalledModuleInfoScript();
        var args = new List<string>(1) { EncodeLines(list) };

        var result = RunScript(runner, script, args, TimeSpan.FromMinutes(2));
        if (result.ExitCode != 0)
        {
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdOut)) _logger.Verbose(result.StdOut.Trim());
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdErr)) _logger.Verbose(result.StdErr.Trim());
            return new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase);
        }

        var map = new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in SplitLines(result.StdOut))
        {
            if (!line.StartsWith("PFMODINFO::ITEM::", StringComparison.Ordinal)) continue;
            var parts = line.Split(new[] { "::" }, StringSplitOptions.None);
            if (parts.Length < 5) continue;

            var name = Decode(parts[2]);
            var ver = EmptyToNull(Decode(parts[3]));
            var guid = EmptyToNull(Decode(parts[4]));
            if (string.IsNullOrWhiteSpace(name)) continue;
            map[name] = (ver, guid);
        }

        foreach (var n in list)
            if (!map.ContainsKey(n)) map[n] = (null, null);

        return map;
    }

    private static PowerShellRunResult RunScript(IPowerShellRunner runner, string scriptText, IReadOnlyList<string> args, TimeSpan timeout)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "PowerForge", "modulepipeline");
        Directory.CreateDirectory(tempDir);
        var scriptPath = Path.Combine(tempDir, $"modulepipeline_{Guid.NewGuid():N}.ps1");
        File.WriteAllText(scriptPath, scriptText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        try
        {
            return runner.Run(new PowerShellRunRequest(scriptPath, args, timeout, preferPwsh: true));
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { /* ignore */ }
        }
    }

    private static string BuildGetInstalledModuleInfoScript()
    {
        return @"
param(
  [string]$NamesB64
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function DecodeLines([string]$b64) {
  if ([string]::IsNullOrWhiteSpace($b64)) { return @() }
  $text = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($b64))
  return $text -split ""`n"" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
}

$names = DecodeLines $NamesB64
foreach ($n in $names) {
  try {
    $m = Get-Module -ListAvailable -Name $n | Sort-Object Version -Descending | Select-Object -First 1
    $ver = if ($m) { [string]$m.Version } else { '' }
    $guid = if ($m) { [string]$m.Guid } else { '' }
    $fields = @($n, $ver, $guid) | ForEach-Object { [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes([string]$_)) }
    Write-Output ('PFMODINFO::ITEM::' + ($fields -join '::'))
  } catch {
    $fields = @($n, '', '') | ForEach-Object { [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes([string]$_)) }
    Write-Output ('PFMODINFO::ITEM::' + ($fields -join '::'))
  }
}
exit 0
";
    }

    private static IEnumerable<string> SplitLines(string? text)
        => (text ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

    private static string EncodeLines(IEnumerable<string> lines)
    {
        var joined = string.Join("\n", lines ?? Array.Empty<string>());
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(joined));
    }

    private static string Decode(string? b64)
    {
        if (string.IsNullOrWhiteSpace(b64)) return string.Empty;
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(b64)); }
        catch { return string.Empty; }
    }

    private static string? EmptyToNull(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s;

    private static string? TryResolveCsprojPath(string projectRoot, string moduleName, string? netProjectPath, string? netProjectName)
    {
        if (string.IsNullOrWhiteSpace(netProjectPath))
            return null;

        var projectName = string.IsNullOrWhiteSpace(netProjectName) ? moduleName : netProjectName!.Trim();
        var rawPath = netProjectPath!.Trim().Trim('"');

        var basePath = Path.IsPathRooted(rawPath)
            ? Path.GetFullPath(rawPath)
            : Path.GetFullPath(Path.Combine(projectRoot, rawPath));

        if (basePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            return basePath;

        return Path.Combine(basePath, projectName + ".csproj");
    }

    private static string[] ResolveInstallRootsFromCompatiblePSEditions(string[] compatiblePSEditions)
    {
        var compatible = compatiblePSEditions ?? Array.Empty<string>();
        if (compatible.Length == 0) return Array.Empty<string>();

        var hasDesktop = compatible.Any(s => string.Equals(s, "Desktop", StringComparison.OrdinalIgnoreCase));
        var hasCore = compatible.Any(s => string.Equals(s, "Core", StringComparison.OrdinalIgnoreCase));

        var roots = new List<string>();
        if (Path.DirectorySeparatorChar == '\\')
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(docs))
                docs = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (!string.IsNullOrWhiteSpace(docs))
            {
                if (hasCore) roots.Add(Path.Combine(docs, "PowerShell", "Modules"));
                if (hasDesktop) roots.Add(Path.Combine(docs, "WindowsPowerShell", "Modules"));
            }
        }
        else
        {
            var home = Environment.GetEnvironmentVariable("HOME");
            if (string.IsNullOrWhiteSpace(home))
                home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            var dataHome = !string.IsNullOrWhiteSpace(xdgDataHome)
                ? xdgDataHome
                : (!string.IsNullOrWhiteSpace(home)
                    ? Path.Combine(home!, ".local", "share")
                    : null);

            if (!string.IsNullOrWhiteSpace(dataHome))
                roots.Add(Path.Combine(dataHome!, "powershell", "Modules"));
        }

        return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static TextEncodingKind MapEncoding(FileConsistencyEncoding encoding)
    {
        return encoding switch
        {
            FileConsistencyEncoding.ASCII => TextEncodingKind.Ascii,
            FileConsistencyEncoding.UTF8 => TextEncodingKind.UTF8,
            FileConsistencyEncoding.UTF8BOM => TextEncodingKind.UTF8BOM,
            FileConsistencyEncoding.Unicode => TextEncodingKind.Unicode,
            FileConsistencyEncoding.BigEndianUnicode => TextEncodingKind.BigEndianUnicode,
            FileConsistencyEncoding.UTF7 => TextEncodingKind.UTF7,
            FileConsistencyEncoding.UTF32 => TextEncodingKind.UTF32,
            _ => TextEncodingKind.UTF8BOM
        };
    }

    private static string BuildArtefactsReportPath(string projectRoot, string? reportFileName, string fallbackFileName)
    {
        var name = string.IsNullOrWhiteSpace(reportFileName) ? fallbackFileName : reportFileName!.Trim();
        var artefacts = Path.Combine(projectRoot, "Artefacts");
        Directory.CreateDirectory(artefacts);
        return Path.GetFullPath(Path.Combine(artefacts, name));
    }

    private static string? AddFileNameSuffix(string? fileName, string suffix)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        var trimmedSuffix = (suffix ?? string.Empty).Trim().Trim('.');
        var trimmed = fileName!.Trim();
        if (string.IsNullOrWhiteSpace(trimmedSuffix)) return trimmed;

        var ext = Path.GetExtension(trimmed);
        if (string.IsNullOrWhiteSpace(ext)) return trimmed + "." + trimmedSuffix;

        var baseName = trimmed.Substring(0, trimmed.Length - ext.Length);
        return baseName + "." + trimmedSuffix + ext;
    }

    private static string[] MergeExcludeDirectories(IEnumerable<string>? primary, IEnumerable<string>? extra)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in (primary ?? Array.Empty<string>()))
            if (!string.IsNullOrWhiteSpace(s)) set.Add(s.Trim());
        foreach (var s in (extra ?? Array.Empty<string>()))
            if (!string.IsNullOrWhiteSpace(s)) set.Add(s.Trim());
        return set.ToArray();
    }

    private static FormatterResult[] FormatPowerShellTree(
        string rootPath,
        string moduleName,
        string manifestPath,
        bool includeMergeFormatting,
        ConfigurationFormattingSegment formatting,
        FormattingPipeline pipeline)
    {
        if (pipeline is null) throw new ArgumentNullException(nameof(pipeline));
        if (formatting is null) return Array.Empty<FormatterResult>();

        var cfg = formatting.Options ?? new FormattingOptions();

        var excludeDirs = MergeExcludeDirectories(
            primary: null,
            extra: new[] { ".git", ".vs", "bin", "obj", "packages", "node_modules", ".vscode", "Artefacts", "Ignore", "Lib", "Modules" });

        var enumeration = new ProjectEnumeration(
            rootPath: rootPath,
            kind: ProjectKind.PowerShell,
            customExtensions: null,
            excludeDirectories: excludeDirs);

        var all = ProjectFileEnumerator.Enumerate(enumeration)
            .Where(IsPowerShellSourceFile)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var rootPsm1 = Path.Combine(rootPath, $"{moduleName}.psm1");

        string[] psFiles = all.Where(p => HasExtension(p, ".ps1", ".psm1")).ToArray();
        string[] psd1Files = all.Where(p => HasExtension(p, ".psd1")).ToArray();

        // Avoid formatting the same output file twice when merge settings are enabled.
        if (includeMergeFormatting)
        {
            if (cfg.Merge.FormatCodePSM1?.Enabled == true)
                psFiles = psFiles.Where(p => !string.Equals(p, rootPsm1, StringComparison.OrdinalIgnoreCase)).ToArray();

            if (cfg.Merge.FormatCodePSD1?.Enabled == true)
                psd1Files = psd1Files.Where(p => !string.Equals(p, manifestPath, StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        var results = new List<FormatterResult>(all.Length + 4);

        if (cfg.Standard.FormatCodePSM1?.Enabled == true && psFiles.Length > 0)
            results.AddRange(pipeline.Run(psFiles, BuildFormatOptions(cfg.Standard.FormatCodePSM1)));

        if (cfg.Standard.FormatCodePSD1?.Enabled == true && psd1Files.Length > 0)
            results.AddRange(pipeline.Run(psd1Files, BuildFormatOptions(cfg.Standard.FormatCodePSD1)));

        if (includeMergeFormatting)
        {
            if (cfg.Merge.FormatCodePSM1?.Enabled == true && File.Exists(rootPsm1))
                results.AddRange(pipeline.Run(new[] { rootPsm1 }, BuildFormatOptions(cfg.Merge.FormatCodePSM1)));

            if (cfg.Merge.FormatCodePSD1?.Enabled == true && File.Exists(manifestPath))
                results.AddRange(pipeline.Run(new[] { manifestPath }, BuildFormatOptions(cfg.Merge.FormatCodePSD1)));
        }

        return results.ToArray();

        static bool IsPowerShellSourceFile(string path)
            => HasExtension(path, ".ps1", ".psm1", ".psd1");

        static bool HasExtension(string path, params string[] extensions)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            var ext = Path.GetExtension(path);
            if (string.IsNullOrWhiteSpace(ext)) return false;

            foreach (var e in extensions)
            {
                if (ext.Equals(e, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }
    }

    private static FormatOptions BuildFormatOptions(FormatCodeOptions formatCode)
        => new()
        {
            RemoveCommentsInParamBlock = formatCode.RemoveCommentsInParamBlock,
            RemoveCommentsBeforeParamBlock = formatCode.RemoveCommentsBeforeParamBlock,
            RemoveAllEmptyLines = formatCode.RemoveAllEmptyLines,
            RemoveEmptyLines = formatCode.RemoveEmptyLines,
            PssaSettingsJson = PssaFormattingDefaults.SerializeSettings(formatCode.FormatterSettings),
            TimeoutSeconds = 120,
            LineEnding = LineEnding.CRLF,
            Utf8Bom = true
        };

    private static CheckStatus EvaluateFileConsistency(ProjectConsistencyReport report, FileConsistencySettings settings)
    {
        var total = report.Summary.TotalFiles;
        if (total <= 0) return CheckStatus.Pass;

        var filesWithIssues = CountFileConsistencyIssues(report, settings);

        if (filesWithIssues == 0) return CheckStatus.Pass;

        var max = Clamp(settings.MaxInconsistencyPercentage, 0, 100);
        var percent = (filesWithIssues / (double)total) * 100.0;
        return percent <= max ? CheckStatus.Warning : CheckStatus.Fail;
    }

    private static string BuildFileConsistencyMessage(ProjectConsistencyReport report, FileConsistencySettings settings)
    {
        var total = report.Summary.TotalFiles;
        var max = Clamp(settings.MaxInconsistencyPercentage, 0, 100);
        var issues = CountFileConsistencyIssues(report, settings);
        var percent = total <= 0 ? 0.0 : Math.Round((issues / (double)total) * 100.0, 1);
        return $"{issues}/{total} files have issues ({percent:0.0}%, max allowed {max}%).";
    }

    private static int CountFileConsistencyIssues(ProjectConsistencyReport report, FileConsistencySettings settings)
    {
        int filesWithIssues = 0;
        foreach (var f in report.ProblematicFiles)
        {
            if (f.NeedsEncodingConversion || f.NeedsLineEndingConversion)
            {
                filesWithIssues++;
                continue;
            }

            if (settings.CheckMissingFinalNewline && f.MissingFinalNewline)
            {
                filesWithIssues++;
                continue;
            }

            if (settings.CheckMixedLineEndings && f.HasMixedLineEndings)
            {
                filesWithIssues++;
            }
        }

        return filesWithIssues;
    }

    private static PowerShellCompatibilityReport ApplyCompatibilitySettings(PowerShellCompatibilityReport report, CompatibilitySettings settings)
    {
        if (report is null) throw new ArgumentNullException(nameof(report));
        if (settings is null) return report;

        var s = report.Summary;
        if (!settings.RequireCrossCompatibility && !settings.RequirePS51Compatibility && !settings.RequirePS7Compatibility)
            return report;

        if (s.TotalFiles == 0)
            return report;

        var failures = new List<string>();

        if (settings.RequirePS51Compatibility && s.PowerShell51Compatible != s.TotalFiles)
            failures.Add($"PS 5.1 compatible {s.PowerShell51Compatible}/{s.TotalFiles}");

        if (settings.RequirePS7Compatibility && s.PowerShell7Compatible != s.TotalFiles)
            failures.Add($"PS 7 compatible {s.PowerShell7Compatible}/{s.TotalFiles}");

        if (settings.RequireCrossCompatibility)
        {
            var min = Clamp(settings.MinimumCompatibilityPercentage, 0, 100);
            if (s.CrossCompatibilityPercentage < min)
                failures.Add($"Cross-compatible {s.CrossCompatibilityPercentage:0.0}% (< {min}%)");
        }

        var status = failures.Count > 0
            ? CheckStatus.Fail
            : s.FilesWithIssues > 0 ? CheckStatus.Warning : CheckStatus.Pass;

        var message = status switch
        {
            CheckStatus.Pass => $"All {s.TotalFiles} files meet compatibility requirements",
            CheckStatus.Warning => $"{s.FilesWithIssues} files have compatibility issues but requirements are met",
            _ => $"Compatibility requirements not met: {string.Join(", ", failures)}"
        };

        var adjusted = new PowerShellCompatibilitySummary(
            status: status,
            analysisDate: s.AnalysisDate,
            totalFiles: s.TotalFiles,
            powerShell51Compatible: s.PowerShell51Compatible,
            powerShell7Compatible: s.PowerShell7Compatible,
            crossCompatible: s.CrossCompatible,
            filesWithIssues: s.FilesWithIssues,
            crossCompatibilityPercentage: s.CrossCompatibilityPercentage,
            message: message,
            recommendations: s.Recommendations);

        return new PowerShellCompatibilityReport(adjusted, report.Files, report.ExportPath);
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min) return min;
        return value > max ? max : value;
    }
}
