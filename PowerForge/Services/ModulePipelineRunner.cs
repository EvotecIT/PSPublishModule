using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using System.Text.Json;

namespace PowerForge;

/// <summary>
/// Plans and executes a configuration-driven module build workflow using <see cref="ModuleBuildPipeline"/>.
/// </summary>
/// <remarks>
/// <para>
/// The runner works in two phases:
/// </para>
/// <list type="number">
/// <item><description><see cref="Plan"/> merges the base build settings with configuration segments (last-wins) into a deterministic plan.</description></item>
/// <item><description><see cref="Run(ModulePipelineSpec)"/> executes the plan step-by-step and returns a structured result.</description></item>
/// </list>
/// <para>
/// This split enables "plan-only" scenarios such as generating a JSON configuration without performing the build.
/// </para>
/// </remarks>
/// <example>
/// <summary>Plan and execute a build</summary>
/// <code>
/// var logger = new ConsoleLogger { IsVerbose = true };
/// var runner = new ModulePipelineRunner(logger);
/// var json = File.ReadAllText(@"C:\Git\MyModule\powerforge.json");
/// var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
/// options.Converters.Add(new ConfigurationSegmentJsonConverter());
/// var spec = JsonSerializer.Deserialize&lt;ModulePipelineSpec&gt;(json, options)!;
/// var result = runner.Run(spec);
/// </code>
/// </example>
public sealed class ModulePipelineRunner
{
    private readonly ILogger _logger;

    private sealed class RequiredModuleDraft
    {
        public string ModuleName { get; }
        public string? ModuleVersion { get; }
        public string? MinimumVersion { get; }
        public string? RequiredVersion { get; }
        public string? Guid { get; }

        public RequiredModuleDraft(string moduleName, string? moduleVersion, string? minimumVersion, string? requiredVersion, string? guid)
        {
            ModuleName = moduleName;
            ModuleVersion = moduleVersion;
            MinimumVersion = minimumVersion;
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
        bool installMissingModules = false;
        bool installMissingModulesForce = false;
        bool installMissingModulesPrerelease = false;
        bool resolveMissingModulesOnline = false;
        bool warnIfRequiredModulesOutdated = false;
        string? installMissingModulesRepository = null;
        RepositoryCredential? installMissingModulesCredential = null;
        bool signModule = false;
        bool mergeModule = false;
        bool mergeModuleSet = false;
        bool mergeMissing = false;
        bool mergeMissingSet = false;
        SigningOptionsConfiguration? signing = null;

        string? dotnetConfigFromSegments = null;
        string[]? dotnetFrameworksFromSegments = null;
        string? netProjectName = null;
        string? netProjectPath = null;
        string[]? exportAssembliesFromSegments = null;
        bool? disableBinaryCmdletScanFromSegments = null;
        string? resolveBinaryConflictsProjectName = null;

        InformationConfiguration? information = null;
        DocumentationConfiguration? documentation = null;
        DeliveryOptionsConfiguration? delivery = null;
        BuildDocumentationConfiguration? documentationBuild = null;
        CompatibilitySettings? compatibilitySettings = null;
        FileConsistencySettings? fileConsistencySettings = null;
        ModuleValidationSettings? validationSettings = null;
        ConfigurationFormattingSegment? formatting = null;
        ImportModulesConfiguration? importModules = null;
        PlaceHolderOptionConfiguration? placeHolderOption = null;
        var placeHolders = new List<PlaceHolderReplacement>();
        var commandDependencies = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var testsAfterMerge = new List<TestConfiguration>();
        var artefacts = new List<ConfigurationArtefactSegment>();
        var publishes = new List<ConfigurationPublishSegment>();
        var approvedModules = new List<string>();
        var moduleSkipIgnoreModules = new List<string>();
        var moduleSkipIgnoreFunctions = new List<string>();
        bool moduleSkipForce = false;
        bool moduleSkipFailOnMissingCommands = false;

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
                    if (b.InstallMissingModules.HasValue) installMissingModules = b.InstallMissingModules.Value;
                    if (b.InstallMissingModulesForce.HasValue) installMissingModulesForce = b.InstallMissingModulesForce.Value;
                    if (b.InstallMissingModulesPrerelease.HasValue) installMissingModulesPrerelease = b.InstallMissingModulesPrerelease.Value;
                    if (b.ResolveMissingModulesOnline.HasValue) resolveMissingModulesOnline = b.ResolveMissingModulesOnline.Value;
                    if (b.WarnIfRequiredModulesOutdated.HasValue) warnIfRequiredModulesOutdated = b.WarnIfRequiredModulesOutdated.Value;
                    if (!string.IsNullOrWhiteSpace(b.InstallMissingModulesRepository)) installMissingModulesRepository = b.InstallMissingModulesRepository;
                    if (b.InstallMissingModulesCredential is not null) installMissingModulesCredential = b.InstallMissingModulesCredential;
                    if (b.SignMerged.HasValue) signModule = b.SignMerged.Value;
                    if (b.Merge.HasValue)
                    {
                        mergeModule = b.Merge.Value;
                        mergeModuleSet = true;
                    }
                    if (b.MergeMissing.HasValue)
                    {
                        mergeMissing = b.MergeMissing.Value;
                        mergeMissingSet = true;
                    }
                    if (!string.IsNullOrWhiteSpace(b.ResolveBinaryConflicts?.ProjectName))
                        resolveBinaryConflictsProjectName = b.ResolveBinaryConflicts!.ProjectName;
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
                    var md = moduleSeg.Configuration;
                    if (string.IsNullOrWhiteSpace(md.ModuleName)) break;        
                    var name = md.ModuleName.Trim();

                    if (moduleSeg.Kind == ModuleDependencyKind.ApprovedModule)
                    {
                        approvedModules.Add(name);
                        break;
                    }

                    if (moduleSeg.Kind is not (ModuleDependencyKind.RequiredModule or ModuleDependencyKind.ExternalModule))
                        break;

                    var draft = new RequiredModuleDraft(
                        moduleName: name,
                        moduleVersion: md.ModuleVersion,
                        minimumVersion: md.MinimumVersion,
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
                case ConfigurationOptionsSegment optionsSegment:
                {
                    var opts = optionsSegment.Options ?? new ConfigurationOptions();
                    if (opts.Delivery is not null && opts.Delivery.Enable)      
                        delivery = opts.Delivery;
                    if (opts.Signing is not null)
                        signing = opts.Signing;
                    break;
                }
                case ConfigurationModuleSkipSegment skipSeg:
                {
                    var cfg = skipSeg.Configuration ?? new ModuleSkipConfiguration();
                    if (cfg.IgnoreModuleName is { Length: > 0 })
                        moduleSkipIgnoreModules.AddRange(cfg.IgnoreModuleName);
                    if (cfg.IgnoreFunctionName is { Length: > 0 })
                        moduleSkipIgnoreFunctions.AddRange(cfg.IgnoreFunctionName);
                    if (cfg.Force) moduleSkipForce = true;
                    if (cfg.FailOnMissingCommands) moduleSkipFailOnMissingCommands = true;
                    break;
                }
                case ConfigurationCommandSegment commandSeg:
                {
                    var cfg = commandSeg.Configuration ?? new CommandConfiguration();
                    var commandModuleName = cfg.ModuleName?.Trim();
                    var commandNames = cfg.CommandName ?? Array.Empty<string>();
                    if (string.IsNullOrWhiteSpace(commandModuleName) || commandNames.Length == 0)
                        break;

                    var commandKey = commandModuleName!;
                    if (!commandDependencies.TryGetValue(commandKey, out var list))
                    {
                        list = new List<string>();
                        commandDependencies[commandKey] = list;
                    }

                    foreach (var name in commandNames)
                    {
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        if (list.Any(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase))) continue;
                        list.Add(name.Trim());
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
                case ConfigurationImportModulesSegment importSeg:
                {
                    var cfg = importSeg.ImportModules ?? new ImportModulesConfiguration();
                    importModules ??= new ImportModulesConfiguration();
                    if (cfg.Self.HasValue) importModules.Self = cfg.Self;
                    if (cfg.RequiredModules.HasValue) importModules.RequiredModules = cfg.RequiredModules;
                    if (cfg.Verbose.HasValue) importModules.Verbose = cfg.Verbose;
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
                    formatting = MergeFormattingSegments(formatting, formattingSegment);
                    break;
                }
                case ConfigurationPlaceHolderSegment placeHolder:
                {
                    var cfg = placeHolder.Configuration;
                    if (!string.IsNullOrWhiteSpace(cfg.Find) || !string.IsNullOrWhiteSpace(cfg.Replace))
                        placeHolders.Add(cfg);
                    break;
                }
                case ConfigurationPlaceHolderOptionSegment placeHolderOptionSeg:
                {
                    if (placeHolderOptionSeg.PlaceHolderOption?.SkipBuiltinReplacements == true)
                    {
                        placeHolderOption ??= new PlaceHolderOptionConfiguration();
                        placeHolderOption.SkipBuiltinReplacements = true;
                    }
                    break;
                }
                case ConfigurationValidationSegment validationSegment:
                {
                    validationSettings = validationSegment.Settings;
                    break;
                }
                case ConfigurationTestSegment testSeg:
                {
                    var cfg = testSeg.Configuration ?? new TestConfiguration();
                    if (!string.IsNullOrWhiteSpace(cfg.TestsPath))
                        testsAfterMerge.Add(cfg);
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

        var exportAssemblies = exportAssembliesFromSegments ?? spec.Build.ExportAssemblies ?? Array.Empty<string>();
        if (!exportAssemblies.Any(s => !string.IsNullOrWhiteSpace(s)))
        {
            // Legacy behavior: when no explicit NETBinaryModule/ExportAssemblies is set, infer the primary export
            // assembly from the build configuration (ResolveBinaryConflictsName / NETProjectName).
            var inferred =
                resolveBinaryConflictsProjectName?.Trim()
                ?? netProjectName?.Trim();

            if (!string.IsNullOrWhiteSpace(inferred))
                exportAssemblies = new[] { inferred! };
        }

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
            ExportAssemblies = exportAssemblies,
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

        var requiredModules = ResolveRequiredModules(
            requiredModulesDraft,
            resolveMissingModulesOnline,
            warnIfRequiredModulesOutdated,
            installMissingModulesPrerelease,
            installMissingModulesRepository,
            installMissingModulesCredential);
        var requiredModulesForPackaging = ResolveRequiredModules(
            requiredModulesDraftForPackaging,
            resolveMissingModulesOnline,
            warnIfRequiredModulesOutdated,
            installMissingModulesPrerelease,
            installMissingModulesRepository,
            installMissingModulesCredential);

        var approved = approvedModules
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!mergeModuleSet)
        {
            mergeModule = true;
            _logger.Info("MergeModule not explicitly set; enabling by default for legacy compatibility.");
        }

        if (!mergeMissingSet && !mergeMissing && approved.Length > 0)
        {
            mergeMissing = true;
            var context = mergeModule ? "and MergeModule is enabled" : "for approved-module inlining";
            _logger.Info($"MergeMissing not explicitly set; enabling because approved modules are configured {context}.");
        }

        ModuleSkipConfiguration? moduleSkip = null;
        if (moduleSkipForce || moduleSkipFailOnMissingCommands || moduleSkipIgnoreModules.Count > 0 || moduleSkipIgnoreFunctions.Count > 0)
        {
            moduleSkip = new ModuleSkipConfiguration
            {
                Force = moduleSkipForce,
                FailOnMissingCommands = moduleSkipFailOnMissingCommands,
                IgnoreModuleName = moduleSkipIgnoreModules
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                IgnoreFunctionName = moduleSkipIgnoreFunctions
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };
        }

        var enabledArtefacts = artefacts
            .Where(a => a is not null && a.Configuration?.Enabled == true)      
            .ToArray();
        var enabledPublishes = publishes
            .Where(p => p is not null && p.Configuration?.Enabled == true)
            .ToArray();

        var commandDeps = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in commandDependencies)
        {
            var cmds = kvp.Value
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (cmds.Length == 0) continue;
            commandDeps[kvp.Key] = cmds;
        }

        var placeHolderEntries = placeHolders
            .Where(p => p is not null && (!string.IsNullOrWhiteSpace(p.Find) || !string.IsNullOrWhiteSpace(p.Replace)))
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
            delivery: delivery,
            documentationBuild: documentationBuild,
            compatibilitySettings: compatibilitySettings,
            fileConsistencySettings: fileConsistencySettings,
            validationSettings: validationSettings,
            formatting: formatting,
            importModules: importModules,
            placeHolders: placeHolderEntries,
            placeHolderOption: placeHolderOption,
            commandModuleDependencies: commandDeps,
            testsAfterMerge: testsAfterMerge.ToArray(),
            mergeModule: mergeModule,
            mergeMissing: mergeMissing,
            approvedModules: approved,
            moduleSkip: moduleSkip,
            signModule: signModule,
            signing: signing,
            publishes: enabledPublishes,
            artefacts: enabledArtefacts,
            installEnabled: installEnabled,
            installStrategy: strategy,
            installKeepVersions: keep,
            installRoots: roots,
            installMissingModules: installMissingModules,
            installMissingModulesForce: installMissingModulesForce,
            installMissingModulesPrerelease: installMissingModulesPrerelease,
            installMissingModulesRepository: installMissingModulesRepository,
            installMissingModulesCredential: installMissingModulesCredential,
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

                if (plan.RequiredModules is { Length: > 0 })
                    ManifestEditor.TrySetRequiredModules(buildResult.ManifestPath, plan.RequiredModules);

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
                if (plan.RequiredModules is { Length: > 0 })
                    ManifestEditor.TrySetRequiredModules(buildResult.ManifestPath, plan.RequiredModules);

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
                        requiredModules: plan.RequiredModulesForPackaging,
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

    private static void DeleteDirectoryWithRetries(string? path, int maxAttempts = 10, int initialDelayMs = 250)
    {
        if (path is null) return;
        var trimmed = path.Trim().Trim('"');
        if (trimmed.Length == 0) return;

        var full = Path.GetFullPath(trimmed);
        if (!Directory.Exists(full)) return;

        maxAttempts = Math.Max(1, maxAttempts);
        initialDelayMs = Math.Max(0, initialDelayMs);

        Exception? last = null;
        var delayMs = initialDelayMs;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                Directory.Delete(full, recursive: true);
                return;
            }
            catch (IOException ex)
            {
                last = ex;
            }
            catch (UnauthorizedAccessException ex)
            {
                last = ex;
            }

            if (attempt >= maxAttempts)
                throw last ?? new IOException("Failed to delete directory.");

            // If the directory contains assemblies loaded in a collectible ALC, forcing GC helps release file locks.
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            catch
            {
                // best effort only
            }

            if (delayMs > 0)
                System.Threading.Thread.Sleep(delayMs);

            delayMs = delayMs <= 0 ? 0 : Math.Min(delayMs * 2, 3000);
        }
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
        var targetReadme = ResolvePath(projectRoot, plan.Documentation.PathReadme, optional: true);

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
            plan.DocumentationBuild.SyncExternalHelpToProjectRoot &&
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

    private static string ResolvePath(string baseDir, string path, bool optional = false)
    {
        var p = (path ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(p)) return optional ? string.Empty : Path.GetFullPath(baseDir);
        if (Path.IsPathRooted(p)) return Path.GetFullPath(p);
        return Path.GetFullPath(Path.Combine(baseDir, p));
    }

    private static ConfigurationFormattingSegment? MergeFormattingSegments(
        ConfigurationFormattingSegment? existing,
        ConfigurationFormattingSegment incoming)
    {
        if (incoming is null) return existing;
        if (existing is null) return incoming;

        existing.Options ??= new FormattingOptions();
        incoming.Options ??= new FormattingOptions();

        existing.Options.UpdateProjectRoot |= incoming.Options.UpdateProjectRoot;

        MergeTarget(existing.Options.Standard, incoming.Options.Standard);
        MergeTarget(existing.Options.Merge, incoming.Options.Merge);

        return existing;

        static void MergeTarget(FormattingTargetOptions dst, FormattingTargetOptions src)
        {
            if (src is null) return;

            if (src.FormatCodePS1 is not null) dst.FormatCodePS1 = src.FormatCodePS1;
            if (src.FormatCodePSM1 is not null) dst.FormatCodePSM1 = src.FormatCodePSM1;
            if (src.FormatCodePSD1 is not null) dst.FormatCodePSD1 = src.FormatCodePSD1;

            if (src.Style?.PSD1 is not null)
            {
                dst.Style ??= new FormattingStyleOptions();
                dst.Style.PSD1 = src.Style.PSD1;
            }
        }
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

    private static bool IsAutoOrLatest(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           (value!.Trim().Equals("Auto", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("Latest", StringComparison.OrdinalIgnoreCase));

    private static bool IsAutoGuid(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           value!.Trim().Equals("Auto", StringComparison.OrdinalIgnoreCase);

    private ManifestEditor.RequiredModule[] ResolveRequiredModules(
        IReadOnlyList<RequiredModuleDraft> drafts,
        bool resolveMissingModulesOnline,
        bool warnIfRequiredModulesOutdated,
        bool prerelease,
        string? repository,
        RepositoryCredential? credential)
    {
        var list = (drafts ?? Array.Empty<RequiredModuleDraft>())
            .Where(d => d is not null && !string.IsNullOrWhiteSpace(d.ModuleName))
            .ToArray();
        if (list.Length == 0) return Array.Empty<ManifestEditor.RequiredModule>();

        var moduleNames = list.Select(d => d.ModuleName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var installed = TryGetLatestInstalledModuleInfo(moduleNames);

        Dictionary<string, string?>? onlineVersions = null;
        if (resolveMissingModulesOnline || warnIfRequiredModulesOutdated)
        {
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in list)
            {
                if (warnIfRequiredModulesOutdated)
                {
                    candidates.Add(d.ModuleName);
                    continue;
                }

                installed.TryGetValue(d.ModuleName, out var info);
                if (!string.IsNullOrWhiteSpace(info.Version)) continue;

                var minimumSource = !string.IsNullOrWhiteSpace(d.MinimumVersion) ? d.MinimumVersion : d.ModuleVersion;
                if (IsAutoOrLatest(d.RequiredVersion) || IsAutoOrLatest(minimumSource))
                    candidates.Add(d.ModuleName);
            }

            if (candidates.Count > 0)
                onlineVersions = TryResolveLatestOnlineVersions(candidates, repository, credential, prerelease);
        }

        var resolvedOnline = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unresolvedVersion = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unresolvedGuid = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var results = new List<ManifestEditor.RequiredModule>(list.Length);
        foreach (var d in list)
        {
            installed.TryGetValue(d.ModuleName, out var info);

            var availableVersion = info.Version;
            if (string.IsNullOrWhiteSpace(availableVersion) &&
                onlineVersions is not null &&
                onlineVersions.TryGetValue(d.ModuleName, out var onlineVersion) &&
                !string.IsNullOrWhiteSpace(onlineVersion))
            {
                availableVersion = onlineVersion;
                resolvedOnline.Add(d.ModuleName);
            }

            var required = ResolveAutoOrLatest(d.RequiredVersion, availableVersion);
            var minimumSource = !string.IsNullOrWhiteSpace(d.MinimumVersion) ? d.MinimumVersion : d.ModuleVersion;
            if (!string.IsNullOrWhiteSpace(d.MinimumVersion) &&
                !string.IsNullOrWhiteSpace(d.ModuleVersion) &&
                !string.Equals(d.MinimumVersion, d.ModuleVersion, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Warn($"Module dependency '{d.ModuleName}' specifies both MinimumVersion and ModuleVersion; using MinimumVersion '{d.MinimumVersion}'.");
            }
            var moduleVersion = ResolveAutoOrLatest(minimumSource, availableVersion);
            var guid = ResolveAutoGuid(d.Guid, info.Guid);

            if (IsAutoOrLatest(d.RequiredVersion) && string.IsNullOrWhiteSpace(required))
                unresolvedVersion.Add(d.ModuleName);
            if (IsAutoOrLatest(minimumSource) && string.IsNullOrWhiteSpace(moduleVersion))
                unresolvedVersion.Add(d.ModuleName);
            if (IsAutoGuid(d.Guid) && string.IsNullOrWhiteSpace(guid))
                unresolvedGuid.Add(d.ModuleName);

            // RequiredVersion is exact; do not also emit ModuleVersion when present.
            if (!string.IsNullOrWhiteSpace(required)) moduleVersion = null;

            results.Add(new ManifestEditor.RequiredModule(d.ModuleName, moduleVersion: moduleVersion, requiredVersion: required, guid: guid));
        }

        if (resolvedOnline.Count > 0)
        {
            var listText = string.Join(", ", resolvedOnline.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
            _logger.Info($"Resolved RequiredModules from repository without installing: {listText}.");
        }

        if (warnIfRequiredModulesOutdated)
        {
            var outdated = new List<(string Name, string Installed, string Latest)>();
            var missing = new List<(string Name, string Latest)>();
            var unparsable = new List<string>();

            foreach (var name in moduleNames)
            {
                installed.TryGetValue(name, out var info);
                var installedVersion = info.Version;

                string? latestVersion = null;
                if (onlineVersions is not null)
                    onlineVersions.TryGetValue(name, out latestVersion);

                if (string.IsNullOrWhiteSpace(latestVersion))
                    continue;

                if (string.IsNullOrWhiteSpace(installedVersion))
                {
                    missing.Add((name, latestVersion!));
                    continue;
                }

                if (!TryParseVersionParts(installedVersion!, out var installedParsed, out var installedPre) ||
                    !TryParseVersionParts(latestVersion!, out var latestParsed, out var latestPre))
                {
                    unparsable.Add(name);
                    continue;
                }

                if (CompareVersionParts(latestParsed, latestPre, installedParsed, installedPre) > 0)
                    outdated.Add((name, installedVersion!, latestVersion!));
            }

            if (outdated.Count > 0)
            {
                var items = string.Join(", ", outdated
                    .OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(o => $"{o.Name} ({o.Installed} -> {o.Latest})"));
                _logger.Warn($"RequiredModules outdated compared to repository: {items}.");
            }

            if (missing.Count > 0)
            {
                var items = string.Join(", ", missing
                    .OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(o => $"{o.Name} (latest {o.Latest})"));
                _logger.Warn($"RequiredModules not installed locally (repository has newer versions): {items}.");
            }

            if (unparsable.Count > 0)
            {
                var items = string.Join(", ", unparsable.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
                _logger.Warn($"RequiredModules installed versions could not be parsed for outdated check: {items}.");
            }
        }

        if (unresolvedVersion.Count > 0)
        {
            var listText = string.Join(", ", unresolvedVersion.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
            var hint = resolveMissingModulesOnline
                ? "Module was not installed and online resolution did not return a version."
                : "Module is not installed and online resolution is disabled.";
            _logger.Warn($"RequiredModules set to Auto/Latest but version could not be resolved for: {listText}. {hint} Install it or enable InstallMissingModules to resolve versions.");
        }

        if (unresolvedGuid.Count > 0)
        {
            var listText = string.Join(", ", unresolvedGuid.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
            _logger.Warn($"RequiredModules set Guid=Auto but module not installed: {listText}. Install it or specify the Guid explicitly.");
        }

        return results.ToArray();
    }

    private static ManifestEditor.RequiredModule[] FilterRequiredModules(
        ManifestEditor.RequiredModule[] modules,
        IReadOnlyCollection<string> approvedModules)
    {
        if (modules is null || modules.Length == 0) return Array.Empty<ManifestEditor.RequiredModule>();
        if (approvedModules is null || approvedModules.Count == 0) return modules;

        var approved = new HashSet<string>(approvedModules, StringComparer.OrdinalIgnoreCase);
        return modules
            .Where(m => !string.IsNullOrWhiteSpace(m.ModuleName) && !approved.Contains(m.ModuleName!))
            .ToArray();
    }

    private ModuleDependencyInstallResult[] EnsureBuildDependenciesInstalled(ModulePipelinePlan plan)
    {
        if (plan is null) return Array.Empty<ModuleDependencyInstallResult>();

        var required = plan.RequiredModules ?? Array.Empty<ManifestEditor.RequiredModule>();
        if (required.Length == 0)
        {
            var manifestPath = Path.Combine(plan.ProjectRoot, $"{plan.ModuleName}.psd1");
            if (File.Exists(manifestPath) &&
                ManifestEditor.TryGetRequiredModules(manifestPath, out var fromManifest) &&
                fromManifest is not null)
            {
                required = fromManifest;
            }
        }

        if (required.Length == 0)
        {
            _logger.Info("InstallMissingModules enabled, but no RequiredModules were found.");
            return Array.Empty<ModuleDependencyInstallResult>();
        }

        var deps = required
            .Where(r => !string.IsNullOrWhiteSpace(r.ModuleName))
            .Select(r => new ModuleDependency(
                name: r.ModuleName.Trim(),
                requiredVersion: r.RequiredVersion,
                minimumVersion: r.ModuleVersion,
                maximumVersion: r.MaximumVersion))
            .ToArray();

        if (deps.Length == 0)
        {
            _logger.Info("InstallMissingModules enabled, but no valid module dependencies were resolved.");
            return Array.Empty<ModuleDependencyInstallResult>();
        }

        _logger.Info($"Installing missing modules ({deps.Length}): {string.Join(", ", deps.Select(d => d.Name))}");

        var installer = new ModuleDependencyInstaller(new PowerShellRunner(), _logger);
        var results = installer.EnsureInstalled(
            dependencies: deps,
            skipModules: plan.ModuleSkip?.IgnoreModuleName,
            force: plan.InstallMissingModulesForce,
            repository: plan.InstallMissingModulesRepository,
            credential: plan.InstallMissingModulesCredential,
            prerelease: plan.InstallMissingModulesPrerelease);

        var failures = results.Where(r => r.Status == ModuleDependencyInstallStatus.Failed).ToArray();
        if (failures.Length > 0)
            throw new InvalidOperationException($"Dependency installation failed for {failures.Length} module{(failures.Length == 1 ? string.Empty : "s")}.");

        if (results.Count > 0)
        {
            var installed = results.Count(r => r.Status == ModuleDependencyInstallStatus.Installed);
            var updated = results.Count(r => r.Status == ModuleDependencyInstallStatus.Updated);
            var satisfied = results.Count(r => r.Status == ModuleDependencyInstallStatus.Satisfied);
            var skipped = results.Count(r => r.Status == ModuleDependencyInstallStatus.Skipped);
            _logger.Info($"Dependency install summary: {installed} installed, {updated} updated, {satisfied} satisfied, {skipped} skipped.");
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

    private Dictionary<string, string?> TryResolveLatestOnlineVersions(
        IReadOnlyCollection<string> names,
        string? repository,
        RepositoryCredential? credential,
        bool prerelease)
    {
        var list = (names ?? Array.Empty<string>())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (list.Length == 0)
            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        var repos = ParseRepositoryList(repository);
        var runner = new PowerShellRunner();

        IReadOnlyList<PSResourceInfo> items = Array.Empty<PSResourceInfo>();
        var resolved = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var psrg = new PSResourceGetClient(runner, _logger);
            var opts = new PSResourceFindOptions(list, version: null, prerelease: prerelease, repositories: repos, credential: credential);
            items = psrg.Find(opts, timeout: TimeSpan.FromMinutes(2));
            resolved = SelectLatestVersions(items, prerelease);
            if (resolved.Count > 0) return resolved;
        }
        catch (PowerShellToolNotAvailableException)
        {
            // fall back to PowerShellGet
        }
        catch (Exception ex)
        {
            _logger.Warn($"Find-PSResource failed while resolving RequiredModules. {ex.Message}");
        }

        try
        {
            var psg = new PowerShellGetClient(runner, _logger);
            var useRepos = repos.Length == 0 ? new[] { "PSGallery" } : repos;
            var opts = new PowerShellGetFindOptions(list, prerelease: prerelease, repositories: useRepos, credential: credential);
            items = psg.Find(opts, timeout: TimeSpan.FromMinutes(2));
            resolved = SelectLatestVersions(items, prerelease);
        }
        catch (PowerShellToolNotAvailableException ex)
        {
            _logger.Warn($"PowerShellGet not available for online resolution. {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.Warn($"Find-Module failed while resolving RequiredModules. {ex.Message}");
        }

        return resolved;
    }

    private static string[] ParseRepositoryList(string? repository)
    {
        var repoText = repository ?? string.Empty;
        if (string.IsNullOrWhiteSpace(repoText)) return Array.Empty<string>();
        return repoText
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(r => r.Trim())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static Dictionary<string, string?> SelectLatestVersions(IEnumerable<PSResourceInfo> items, bool allowPrerelease)
    {
        var map = new Dictionary<string, (Version Version, string? Pre, string Raw)>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items ?? Array.Empty<PSResourceInfo>())
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.Version))
                continue;

            if (!TryParseVersionParts(item.Version, out var version, out var pre))
                continue;

            if (!allowPrerelease && !string.IsNullOrWhiteSpace(pre))
                continue;

            if (!map.TryGetValue(item.Name, out var current))
            {
                map[item.Name] = (version, pre, item.Version);
                continue;
            }

            if (CompareVersionParts(version, pre, current.Version, current.Pre) > 0)
                map[item.Name] = (version, pre, item.Version);
        }

        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in map)
            result[kvp.Key] = kvp.Value.Raw;
        return result;
    }

    private static bool TryParseVersionParts(string text, out Version version, out string? preRelease)
    {
        preRelease = null;
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;

        var trimmed = text.Trim();
        var parts = trimmed.Split(new[] { '-' }, 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        if (!Version.TryParse(parts[0], out var parsed) || parsed is null) return false;
        version = parsed;
        if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
            preRelease = parts[1].Trim();
        return true;
    }

    private static int CompareVersionParts(Version a, string? preA, Version b, string? preB)
    {
        var cmp = a.CompareTo(b);
        if (cmp != 0) return cmp;

        var hasPreA = !string.IsNullOrWhiteSpace(preA);
        var hasPreB = !string.IsNullOrWhiteSpace(preB);
        if (hasPreA == hasPreB)
        {
            if (!hasPreA) return 0;
            return string.Compare(preA, preB, StringComparison.OrdinalIgnoreCase);
        }

        // Release > prerelease when same core version
        return hasPreA ? -1 : 1;
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

    private void LogFormattingIssues(string rootPath, FormatterResult[] results, string label)
    {
        var issues = CollectFormattingIssues(rootPath, results);
        if (issues.Length == 0) return;

        var errorCount = 0;
        var skippedCount = 0;
        foreach (var i in issues)
        {
            if (i.IsError) errorCount++;
            else skippedCount++;
        }

        _logger.Error($"Formatting issues for {label}: {errorCount} error(s), {skippedCount} skipped.");

        const int maxItems = 20;
        var shown = 0;
        foreach (var issue in issues.Where(i => i.IsError).Concat(issues.Where(i => !i.IsError)))
        {
            if (shown++ >= maxItems) break;
            var prefix = issue.IsError ? "ERROR" : "SKIP";
            var line = $"{prefix}: {issue.Path} - {issue.Message}";
            if (issue.IsError) _logger.Error(line);
            else _logger.Warn(line);
        }

        if (issues.Length > maxItems)
            _logger.Warn($"Formatting issues: {issues.Length - maxItems} more not shown.");
    }

    private static string BuildFormattingFailureMessage(
        string label,
        string rootPath,
        FormattingSummary summary,
        FormatterResult[] results)
    {
        var message = $"Formatting failed for {label} (errors {summary.Errors}/{summary.Total}).";
        var issues = CollectFormattingIssues(rootPath, results, onlyErrors: true, maxItems: 3);
        if (issues.Length == 0) return message;
        var sample = string.Join(" | ", issues.Select(i => $"{i.Path}: {i.Message}"));
        return $"{message} First error(s): {sample}";
    }

    private static FormattingIssue[] CollectFormattingIssues(
        string rootPath,
        FormatterResult[] results,
        bool onlyErrors = false,
        int maxItems = 0)
    {
        if (results is null || results.Length == 0) return Array.Empty<FormattingIssue>();

        var list = new List<FormattingIssue>();
        foreach (var r in results)
        {
            if (r is null) continue;
            var msg = r.Message ?? string.Empty;
            var isError = FormattingSummary.IsErrorMessage(msg);
            var isSkipped = FormattingSummary.IsSkippedMessage(msg);
            if (!isError && !isSkipped) continue;
            if (onlyErrors && !isError) continue;

            var rel = FormatFormattingPath(rootPath, r.Path);
            var message = NormalizeFormattingMessage(msg);
            list.Add(new FormattingIssue(rel, message, isError));
        }

        if (maxItems > 0 && list.Count > maxItems)
            return list.Take(maxItems).ToArray();

        return list.ToArray();
    }

    private static string NormalizeFormattingMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "Unknown";
        var idx = message.IndexOf(';');
        if (idx < 0) return message.Trim();
        var token = message.Substring(0, idx).Trim();
        var details = message.Substring(idx + 1).Trim();
        return string.IsNullOrWhiteSpace(details) ? token : $"{token} ({details})";
    }

    private static string FormatFormattingPath(string rootPath, string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return "<unknown>";
        if (string.IsNullOrWhiteSpace(rootPath)) return fullPath;
        try
        {
            return ProjectTextInspection.ComputeRelativePath(rootPath, fullPath);
        }
        catch
        {
            return fullPath;
        }
    }

    private sealed class FormattingIssue
    {
        public string Path { get; }
        public string Message { get; }
        public bool IsError { get; }

        public FormattingIssue(string path, string message, bool isError)
        {
            Path = path ?? string.Empty;
            Message = message ?? string.Empty;
            IsError = isError;
        }
    }

    private void LogFileConsistencyIssues(
        ProjectConsistencyReport report,
        FileConsistencySettings settings,
        string label,
        CheckStatus status)
    {
        if (report is null) return;
        var problematic = report.ProblematicFiles ?? Array.Empty<ProjectConsistencyFileDetail>();
        if (problematic.Length == 0) return;

        var log = status == CheckStatus.Fail ? (Action<string>)_logger.Error : _logger.Warn;
        log($"File consistency issues for {label}: {problematic.Length} file(s).");

        if (!string.IsNullOrWhiteSpace(report.ExportPath))
            log($"File consistency report ({label}): {report.ExportPath}");

        var summary = report.Summary;
        var parts = new List<string>();
        if (summary.FilesNeedingEncodingConversion > 0)
            parts.Add($"encoding {summary.FilesNeedingEncodingConversion}");
        if (summary.FilesNeedingLineEndingConversion > 0)
            parts.Add($"line endings {summary.FilesNeedingLineEndingConversion}");
        if (settings.CheckMixedLineEndings && summary.FilesWithMixedLineEndings > 0)
            parts.Add($"mixed {summary.FilesWithMixedLineEndings}");
        if (settings.CheckMissingFinalNewline && summary.FilesMissingFinalNewline > 0)
            parts.Add($"missing newline {summary.FilesMissingFinalNewline}");
        if (parts.Count > 0)
            log($"File consistency summary ({label}): {string.Join(", ", parts)} (total {summary.TotalFiles}).");

        const int maxItems = 20;
        var shown = 0;
        foreach (var item in problematic)
        {
            var reasons = BuildFileConsistencyReasons(item, settings);
            if (reasons.Count == 0) continue;
            log($"{item.RelativePath} - {string.Join(", ", reasons)}");
            if (++shown >= maxItems) break;
        }

        if (problematic.Length > maxItems)
            _logger.Warn($"File consistency issues: {problematic.Length - maxItems} more not shown.");
    }

    private static List<string> BuildFileConsistencyReasons(
        ProjectConsistencyFileDetail file,
        FileConsistencySettings settings)
    {
        var reasons = new List<string>(4);

        if (file.NeedsEncodingConversion)
        {
            var current = file.CurrentEncoding?.ToString() ?? "Unknown";
            reasons.Add($"encoding {current} (expected {file.RecommendedEncoding})");
        }

        if (file.NeedsLineEndingConversion)
        {
            var current = file.CurrentLineEnding.ToString();
            reasons.Add($"line endings {current} (expected {file.RecommendedLineEnding})");
        }

        if (settings.CheckMixedLineEndings && file.HasMixedLineEndings)
            reasons.Add("mixed line endings");

        if (settings.CheckMissingFinalNewline && file.MissingFinalNewline)
            reasons.Add("missing final newline");

        var error = file.Error;
        if (!string.IsNullOrWhiteSpace(error))
            reasons.Add($"error: {error!.Trim()}");

        return reasons;
    }

    private bool ApplyMerge(ModulePipelinePlan plan, ModuleBuildResult buildResult)
    {
        if (plan is null || buildResult is null) return false;
        if (!plan.MergeModule && !plan.MergeMissing) return false;

        var mergeInfo = BuildMergeSources(buildResult.StagingPath, plan.ModuleName, plan.Information, buildResult.Exports);
        if (!mergeInfo.HasScripts && !File.Exists(mergeInfo.Psm1Path))
        {
            _logger.Warn("Merge requested but no script sources or PSM1 file were found.");
            return false;
        }

        string? analysisCode = mergeInfo.HasScripts ? mergeInfo.MergedScriptContent : null;
        string? analysisPath = mergeInfo.HasScripts ? null : mergeInfo.Psm1Path;

        MissingFunctionsReport? missingReport = null;
        string[] dependentRequiredModules = Array.Empty<string>();
        if (plan.MergeModule || plan.MergeMissing)
        {
            var requiredModules = GetRequiredModuleNames(plan);
            var approvedModules = plan.ApprovedModules ?? Array.Empty<string>();
            dependentRequiredModules = ResolveDependentRequiredModules(requiredModules, approvedModules);

            missingReport = AnalyzeMissingFunctions(analysisPath, analysisCode, plan);
            LogMergeSummary(plan, mergeInfo, missingReport, dependentRequiredModules);
            if (missingReport is not null)
                ValidateMissingFunctions(missingReport, plan, dependentRequiredModules);
        }

        var mergedModule = false;
        if (plan.MergeModule)
        {
            if (mergeInfo.HasLib)
            {
                _logger.Warn("MergeModuleOnBuild requested but binary outputs were detected. Keeping bootstrapper PSM1.");
            }
            else if (!mergeInfo.HasScripts)
            {
                _logger.Warn("MergeModuleOnBuild requested but no script sources were found. Skipping merge.");
            }
            else
            {
                if (File.Exists(mergeInfo.Psm1Path))
                {
                    try
                    {
                        var existing = File.ReadAllText(mergeInfo.Psm1Path);    
                        if (!string.IsNullOrWhiteSpace(existing) && !IsAutoGeneratedPsm1(existing))
                            _logger.Warn("MergeModuleOnBuild will overwrite existing PSM1 content in staging.");
                    }
                    catch
                    {
                        // best effort only
                    }
                }

                var merged = mergeInfo.MergedScriptContent;
                if (plan.MergeMissing && missingReport?.Functions is { Length: > 0 })
                    merged = PrependFunctions(missingReport.Functions, merged);

                WriteMergedPsm1(mergeInfo.Psm1Path, merged);
                mergedModule = true;
            }
        }

        if (!mergedModule && plan.MergeMissing && missingReport?.Functions is { Length: > 0 } && File.Exists(mergeInfo.Psm1Path))
        {
            var existing = File.ReadAllText(mergeInfo.Psm1Path);
            var merged = PrependFunctions(missingReport.Functions, existing);
            WriteMergedPsm1(mergeInfo.Psm1Path, merged);
        }

        return mergedModule;
    }

    private void ApplyPlaceholders(ModulePipelinePlan plan, ModuleBuildResult buildResult)
    {
        if (plan is null || buildResult is null) return;

        var psm1Path = Path.Combine(buildResult.StagingPath, $"{plan.ModuleName}.psm1");
        if (!File.Exists(psm1Path)) return;

        var moduleName = plan.ModuleName;
        var moduleVersion = plan.ResolvedVersion;
        var preRelease = plan.PreRelease;
        var moduleVersionWithPreRelease = string.IsNullOrWhiteSpace(preRelease)
            ? moduleVersion
            : $"{moduleVersion}-{preRelease}";
        var tagName = "v" + moduleVersion;
        var tagModuleVersionWithPreRelease = "v" + moduleVersionWithPreRelease;

        var replacements = new List<(string Find, string Replace)>();
        if (plan.PlaceHolderOption?.SkipBuiltinReplacements != true)
        {
            replacements.Add(("{ModuleName}", moduleName));
            replacements.Add(("<ModuleName>", moduleName));
            replacements.Add(("{ModuleVersion}", moduleVersion));
            replacements.Add(("<ModuleVersion>", moduleVersion));
            replacements.Add(("{ModuleVersionWithPreRelease}", moduleVersionWithPreRelease));
            replacements.Add(("<ModuleVersionWithPreRelease>", moduleVersionWithPreRelease));
            replacements.Add(("{TagModuleVersionWithPreRelease}", tagModuleVersionWithPreRelease));
            replacements.Add(("<TagModuleVersionWithPreRelease>", tagModuleVersionWithPreRelease));
            replacements.Add(("{TagName}", tagName));
            replacements.Add(("<TagName>", tagName));
        }

        if (plan.PlaceHolders is { Length: > 0 })
        {
            foreach (var entry in plan.PlaceHolders)
            {
                if (entry is null) continue;
                if (string.IsNullOrWhiteSpace(entry.Find)) continue;
                replacements.Add((entry.Find, entry.Replace ?? string.Empty));
            }
        }

        if (replacements.Count == 0) return;

        string content;
        try { content = File.ReadAllText(psm1Path); }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to read PSM1 for placeholder replacement: {ex.Message}");
            return;
        }

        var updated = content;
        foreach (var item in replacements)
        {
            if (string.IsNullOrEmpty(item.Find)) continue;
            updated = updated.Replace(item.Find, item.Replace ?? string.Empty);
        }

        if (string.Equals(content, updated, StringComparison.Ordinal)) return;

        try
        {
            File.WriteAllText(psm1Path, updated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to write PSM1 after placeholder replacement: {ex.Message}");
        }
    }

    private void RunImportModules(ModulePipelinePlan plan, ModuleBuildResult buildResult)
    {
        var cfg = plan.ImportModules;
        if (cfg is null) return;

        var importSelf = cfg.Self == true;
        var importRequired = cfg.RequiredModules == true;
        if (!importSelf && !importRequired) return;

        var modules = importRequired
            ? plan.RequiredModules
                .Where(m => !string.IsNullOrWhiteSpace(m.ModuleName))
                .Select(m => new ImportModuleEntry
                {
                    Name = m.ModuleName.Trim(),
                    MinimumVersion = string.IsNullOrWhiteSpace(m.ModuleVersion) ? null : m.ModuleVersion!.Trim(),
                    RequiredVersion = string.IsNullOrWhiteSpace(m.RequiredVersion) ? null : m.RequiredVersion!.Trim()
                })
                .ToArray()
            : Array.Empty<ImportModuleEntry>();

        var modulesB64 = EncodeImportModules(modules);
        var args = new List<string>(5)
        {
            modulesB64,
            importRequired ? "1" : "0",
            importSelf ? "1" : "0",
            buildResult.ManifestPath,
            cfg.Verbose == true ? "1" : "0"
        };

        var runner = new PowerShellRunner();
        var script = BuildImportModulesScript();
        var result = RunScript(runner, script, args, TimeSpan.FromMinutes(5));
        if (result.ExitCode != 0)
        {
            var message = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
            if (string.IsNullOrWhiteSpace(message)) message = "Import-Module failed.";
            throw new InvalidOperationException(message.Trim());
        }
    }

    private void RunTestsAfterMerge(
        ModulePipelinePlan plan,
        ModuleBuildResult buildResult,
        TestConfiguration testConfiguration,
        ModuleTestSuiteService service)
    {
        if (plan is null || buildResult is null || testConfiguration is null) return;

        var testsPath = testConfiguration.TestsPath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(testsPath))
            throw new InvalidOperationException("TestsAfterMerge is enabled but TestsPath is empty.");

        if (!Path.IsPathRooted(testsPath))
            testsPath = Path.GetFullPath(Path.Combine(plan.ProjectRoot, testsPath));

        var importCfg = plan.ImportModules;
        var importSelf = importCfg?.Self == true;
        var importRequired = importCfg?.RequiredModules == true;
        var importVerbose = importCfg?.Verbose == true;

        var importModules = importRequired
            ? plan.RequiredModules
                .Where(m => !string.IsNullOrWhiteSpace(m.ModuleName))
                .Select(m => new ModuleDependency(
                    name: m.ModuleName.Trim(),
                    requiredVersion: m.RequiredVersion,
                    minimumVersion: m.ModuleVersion,
                    maximumVersion: m.MaximumVersion))
                .ToArray()
            : Array.Empty<ModuleDependency>();

        var spec = new ModuleTestSuiteSpec
        {
            ProjectPath = buildResult.StagingPath,
            TestPath = testsPath,
            Force = testConfiguration.Force,
            SkipDependencies = true,
            SkipImport = !importSelf,
            ImportModules = importModules,
            ImportModulesVerbose = importVerbose
        };

        var result = service.Run(spec);
        if (result.FailedCount > 0)
        {
            if (testConfiguration.Force)
            {
                _logger.Warn($"TestsAfterMerge failed ({result.FailedCount} failed) but Force was set; continuing.");
            }
            else
            {
                throw new InvalidOperationException($"TestsAfterMerge failed ({result.FailedCount} failed).");
            }
        }
    }

    private static string BuildImportModulesScript()
    {
        return @"
param(
  [string]$ModulesB64,
  [string]$ImportRequired,
  [string]$ImportSelf,
  [string]$ModulePath,
  [string]$VerboseFlag
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$importVerbose = ($VerboseFlag -eq '1')
$VerbosePreference = if ($importVerbose) { 'Continue' } else { 'SilentlyContinue' }

function DecodeModules([string]$b64) {
  if ([string]::IsNullOrWhiteSpace($b64)) { return @() }
  $json = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($b64))
  if ([string]::IsNullOrWhiteSpace($json)) { return @() }
  try { return $json | ConvertFrom-Json } catch { return @() }
}

if ($ImportRequired -eq '1') {
  $modules = DecodeModules $ModulesB64
  foreach ($m in $modules) {
    if (-not $m -or [string]::IsNullOrWhiteSpace($m.Name)) { continue }
    if ($m.RequiredVersion) {
      Import-Module -Name $m.Name -RequiredVersion $m.RequiredVersion -Force -ErrorAction Stop -Verbose:$importVerbose
    } elseif ($m.MinimumVersion) {
      Import-Module -Name $m.Name -MinimumVersion $m.MinimumVersion -Force -ErrorAction Stop -Verbose:$importVerbose
    } else {
      Import-Module -Name $m.Name -Force -ErrorAction Stop -Verbose:$importVerbose
    }
  }
}

if ($ImportSelf -eq '1') {
  if (-not [string]::IsNullOrWhiteSpace($ModulePath)) {
    Import-Module -Name $ModulePath -Force -ErrorAction Stop -Verbose:$importVerbose
  } else {
    throw 'ModulePath is required for ImportSelf.'
  }
}
exit 0
";
    }

    private static string EncodeImportModules(IEnumerable<ImportModuleEntry> modules)
    {
        var list = modules?.Where(m => m is not null && !string.IsNullOrWhiteSpace(m.Name)).ToArray() ?? Array.Empty<ImportModuleEntry>();
        if (list.Length == 0) return string.Empty;
        var json = JsonSerializer.Serialize(list);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    private sealed class ImportModuleEntry
    {
        public string Name { get; set; } = string.Empty;
        public string? MinimumVersion { get; set; }
        public string? RequiredVersion { get; set; }
    }


    private static MergeSourceInfo BuildMergeSources(string rootPath, string moduleName, InformationConfiguration? information, ExportSet exports)
    {
        var root = Path.GetFullPath(rootPath);
        var psm1 = Path.Combine(root, $"{moduleName}.psm1");

        var dirs = ResolveMergeDirectories(information);
        var files = new List<string>();
        foreach (var dir in dirs)
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var full = Path.Combine(root, dir);
            if (!Directory.Exists(full)) continue;
            try
            {
                files.AddRange(Directory.EnumerateFiles(full, "*.ps1", SearchOption.AllDirectories));
            }
            catch
            {
                // best effort only
            }
        }

        var ordered = files
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var merged = ordered.Length > 0 ? BuildMergedScriptContent(root, ordered, exports) : string.Empty;
        var libRoot = Path.Combine(root, "Lib");
        var hasLib = Directory.Exists(libRoot) && Directory.EnumerateDirectories(libRoot).Any();

        return new MergeSourceInfo(psm1, ordered, merged, hasLib);
    }

    private static string[] ResolveMergeDirectories(InformationConfiguration? information)
    {
        var ordered = new List<string> { "Classes", "Enums", "Private", "Public" };

        if (information?.IncludePS1 is { Length: > 0 })
        {
            foreach (var entry in information.IncludePS1)
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;
                if (ordered.Any(o => string.Equals(o, entry, StringComparison.OrdinalIgnoreCase))) continue;
                ordered.Add(entry);
            }
        }

        return ordered.ToArray();
    }

    private static string BuildMergedScriptContent(string rootPath, IReadOnlyList<string> files, ExportSet exports)
    {
        var requires = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usingLines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var body = new StringBuilder(8192);

        foreach (var file in files)
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) continue;

            string[] lines;
            try { lines = File.ReadAllLines(file); }
            catch { continue; }

            var block = new List<string>();

            foreach (var line in lines)
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("#requires", StringComparison.OrdinalIgnoreCase))
                {
                    requires.Add(trimmed);
                    continue;
                }
                if (trimmed.StartsWith("using ", StringComparison.OrdinalIgnoreCase))
                {
                    usingLines.Add(trimmed);
                    continue;
                }
                block.Add(line);
            }

            if (block.Count == 0) continue;

            foreach (var line in block) body.AppendLine(line);
            body.AppendLine();
        }

        var header = new StringBuilder(1024);
        foreach (var req in requires.OrderBy(r => r, StringComparer.OrdinalIgnoreCase))
            header.AppendLine(req);
        foreach (var use in usingLines.OrderBy(r => r, StringComparer.OrdinalIgnoreCase))
            header.AppendLine(use);

        if (header.Length > 0)
            header.AppendLine();

        header.Append(body.ToString().TrimEnd());

        var merged = header.ToString().TrimEnd();
        var exportBlock = BuildExportBlock(exports);
        if (!string.IsNullOrWhiteSpace(exportBlock))
        {
            if (!string.IsNullOrWhiteSpace(merged))
                merged += Environment.NewLine + Environment.NewLine;
            merged += exportBlock.TrimEnd();
        }

        return merged;
    }

    private static string BuildExportBlock(ExportSet exports)
    {
        var sb = new StringBuilder(256);
        sb.AppendLine("$FunctionsToExport = " + FormatPsStringList(exports.Functions));
        sb.AppendLine("$CmdletsToExport = " + FormatPsStringList(exports.Cmdlets));
        sb.AppendLine("$AliasesToExport = " + FormatPsStringList(exports.Aliases));
        sb.AppendLine("Export-ModuleMember -Function $FunctionsToExport -Alias $AliasesToExport -Cmdlet $CmdletsToExport");
        return sb.ToString();
    }

    private static string FormatPsStringList(IReadOnlyList<string>? values)
    {
        var list = (values ?? Array.Empty<string>())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (list.Length == 0) return "@()";

        var sb = new StringBuilder();
        sb.Append("@(");
        for (var i = 0; i < list.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append('\'').Append(EscapePsSingleQuoted(list[i])).Append('\'');
        }
        sb.Append(')');
        return sb.ToString();
    }

    private static string EscapePsSingleQuoted(string value)
        => value?.Replace("'", "''") ?? string.Empty;

    private void TryRegenerateBootstrapperFromManifest(ModuleBuildResult buildResult, string moduleName, IReadOnlyList<string>? exportAssemblies)
    {
        try
        {
            var exports = ReadExportsFromManifest(buildResult.ManifestPath);
            ModuleBootstrapperGenerator.Generate(buildResult.StagingPath, moduleName, exports, exportAssemblies);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to regenerate module bootstrapper exports for '{moduleName}'. Error: {ex.Message}");
            if (_logger.IsVerbose) _logger.Verbose(ex.ToString());
        }
    }

    private static ExportSet ReadExportsFromManifest(string psd1Path)
        => new(ReadStringOrArray(psd1Path, "FunctionsToExport"),
            ReadStringOrArray(psd1Path, "CmdletsToExport"),
            ReadStringOrArray(psd1Path, "AliasesToExport"));

    private static string[] ReadStringOrArray(string psd1Path, string key)
    {
        if (ManifestEditor.TryGetTopLevelStringArray(psd1Path, key, out var values) && values is not null)
            return values;
        if (ManifestEditor.TryGetTopLevelString(psd1Path, key, out var value) && !string.IsNullOrWhiteSpace(value))
            return new[] { value! };
        return Array.Empty<string>();
    }

    private static string PrependFunctions(string[] functions, string content)
    {
        var block = (functions ?? Array.Empty<string>())
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .ToArray();
        if (block.Length == 0) return content;

        var prefix = string.Join(Environment.NewLine, block);
        return string.IsNullOrWhiteSpace(content)
            ? prefix
            : prefix + Environment.NewLine + Environment.NewLine + content;
    }

    private static void WriteMergedPsm1(string path, string content)
    {
        var normalized = NormalizeCrLf(content);
        File.WriteAllText(path, normalized, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static bool IsAutoGeneratedPsm1(string content)
        => !string.IsNullOrWhiteSpace(content) &&
           content.IndexOf("Auto-generated by PowerForge", StringComparison.OrdinalIgnoreCase) >= 0;

    private static string NormalizeCrLf(string text)
        => string.IsNullOrEmpty(text)
            ? string.Empty
            : text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");

    private MissingFunctionsReport? AnalyzeMissingFunctions(string? filePath, string? code, ModulePipelinePlan plan)
    {
        if (string.IsNullOrWhiteSpace(filePath) && string.IsNullOrWhiteSpace(code))
            return null;

        var approved = plan.ApprovedModules ?? Array.Empty<string>();
        var analyzer = new MissingFunctionsAnalyzer();
        var options = new MissingFunctionsOptions(
            approvedModules: approved,
            ignoreFunctions: Array.Empty<string>(),
            includeFunctionsRecursively: true);
        try
        {
            return analyzer.Analyze(filePath, code, options);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Missing function analysis failed. {ex.Message}");
            if (_logger.IsVerbose) _logger.Verbose(ex.ToString());
            return null;
        }
    }

    private static string[] GetRequiredModuleNames(ModulePipelinePlan plan)
    {
        if (plan is null) return Array.Empty<string>();
        return (plan.RequiredModules ?? Array.Empty<ManifestEditor.RequiredModule>())
            .Select(m => m.ModuleName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void ValidateMissingFunctions(
        MissingFunctionsReport report,
        ModulePipelinePlan plan,
        IReadOnlyCollection<string>? dependentModules)
    {
        if (report is null) return;

        var requiredModules = GetRequiredModuleNames(plan);

        var required = new HashSet<string>(requiredModules, StringComparer.OrdinalIgnoreCase);
        var approved = new HashSet<string>(plan.ApprovedModules ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var dependentRequiredModules = dependentModules ?? ResolveDependentRequiredModules(requiredModules, approved);
        var dependent = new HashSet<string>(dependentRequiredModules, StringComparer.OrdinalIgnoreCase);
        var ignoreModules = new HashSet<string>(plan.ModuleSkip?.IgnoreModuleName ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var ignoreFunctions = new HashSet<string>(plan.ModuleSkip?.IgnoreFunctionName ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var force = plan.ModuleSkip?.Force == true;
        var strictMissing = plan.ModuleSkip?.FailOnMissingCommands == true;

        var apps = report.Summary
            .Where(c => string.Equals(c.CommandType, "Application", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Source)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (apps.Length > 0)
            _logger.Warn($"Applications used by module: {string.Join(", ", apps)}");

        var failures = new List<string>();

        var moduleCommands = report.Summary
            .Where(c => !string.IsNullOrWhiteSpace(c.CommandType))
            .Where(c => !string.Equals(c.CommandType, "Application", StringComparison.OrdinalIgnoreCase))
            .Where(c => !string.IsNullOrWhiteSpace(c.Source))
            .GroupBy(c => c.Source, StringComparer.OrdinalIgnoreCase);

        foreach (var group in moduleCommands)
        {
            var moduleName = group.Key ?? string.Empty;
            if (string.IsNullOrWhiteSpace(moduleName))
                continue;
            if (IsBuiltInModule(moduleName))
                continue;
            if (required.Contains(moduleName) || approved.Contains(moduleName) || dependent.Contains(moduleName))
                continue;

            var allIgnored = group.All(c => ignoreFunctions.Contains(c.Name));
            if (force || ignoreModules.Contains(moduleName) || allIgnored)
            {
                _logger.Warn($"Missing module '{moduleName}' ignored by configuration.");
                continue;
            }

            failures.Add(moduleName);
            foreach (var cmd in group)
                _logger.Error($"Missing module '{moduleName}' provides '{cmd.Name}' (CommandType: {cmd.CommandType}).");
        }

        var unresolved = report.Summary
            .Where(c => string.IsNullOrWhiteSpace(c.CommandType))
            .ToArray();

        foreach (var cmd in unresolved)
        {
            var name = cmd.Name;
            if (string.IsNullOrWhiteSpace(name))
                continue;
            if (name.StartsWith("$", StringComparison.Ordinal))
                continue;
            if (IsBuiltInCommand(name))
                continue;
            if (ignoreFunctions.Contains(name))
            {
                _logger.Warn($"Unresolved command '{name}' ignored by configuration.");
                continue;
            }

            if (force)
            {
                _logger.Warn($"Unresolved command '{name}' (ignored by Force).");
                continue;
            }

            failures.Add(name);
            _logger.Error($"Unresolved command '{name}' (no module source).");
        }

        if (failures.Count > 0 && !force)
        {
            var unique = failures.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (strictMissing)
            {
                throw new InvalidOperationException(
                    $"Missing commands detected during merge. Resolve dependencies or configure ModuleSkip. Missing: {string.Join(", ", unique)}.");
            }

            _logger.Warn(
                $"Missing commands detected during merge. Continuing because FailOnMissingCommands is disabled. Missing: {string.Join(", ", unique)}.");
        }
    }

    private void LogMergeSummary(
        ModulePipelinePlan plan,
        MergeSourceInfo mergeInfo,
        MissingFunctionsReport? missingReport,
        IReadOnlyCollection<string>? dependentModules)
    {
        if (plan is null) return;

        var requiredModules = plan.RequiredModules ?? Array.Empty<ManifestEditor.RequiredModule>();
        var approvedModules = plan.ApprovedModules ?? Array.Empty<string>();
        var dependent = dependentModules ?? Array.Empty<string>();

        _logger.Info($"Merge/dependency summary (required {requiredModules.Length}, approved {approvedModules.Length}, dependent {dependent.Count}).");
        if (requiredModules.Length > 0)
        {
            var formatted = requiredModules
                .Where(m => m is not null && !string.IsNullOrWhiteSpace(m.ModuleName))
                .OrderBy(m => m.ModuleName, StringComparer.OrdinalIgnoreCase)
                .Select(FormatRequiredModule)
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .ToArray();

            if (formatted.Length > 0)
            {
                _logger.Info($"  Required modules ({formatted.Length}):");
                foreach (var module in formatted)
                    _logger.Info($"    - {module}");
            }
        }

        if (approvedModules.Length > 0)
        {
            var ordered = approvedModules
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Select(m => m.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (ordered.Length > 0)
            {
                _logger.Info($"  Approved modules ({ordered.Length}):");
                foreach (var module in ordered)
                    _logger.Info($"    - {module}");
            }
        }

        if (dependent.Count > 0)
        {
            var ordered = dependent
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Select(m => m.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (ordered.Length > 0)
            {
                _logger.Info($"  Dependent modules ({ordered.Length}):");
                foreach (var module in ordered)
                    _logger.Info($"    - {module}");
            }
        }

        if (plan.MergeModule)
        {
            if (mergeInfo.HasScripts)
                _logger.Info($"MergeModule: {mergeInfo.ScriptFiles.Length} script file(s) found for merge.");
            else if (File.Exists(mergeInfo.Psm1Path))
                _logger.Info("MergeModule: using existing PSM1 (no script sources).");
        }

        if (plan.MergeMissing)
        {
            if (missingReport is null)
            {
                _logger.Warn("MergeMissing: missing function analysis failed; no functions inlined.");
            }
            else
            {
                var topLevel = missingReport.FunctionsTopLevelOnly?.Length ?? 0;
                var total = missingReport.Functions?.Length ?? 0;
                _logger.Info($"MergeMissing: {topLevel} top-level function(s) inlined (total {total} including dependencies).");
            }
        }
    }

    private static string FormatRequiredModule(ManifestEditor.RequiredModule module)
    {
        if (module is null || string.IsNullOrWhiteSpace(module.ModuleName))
            return string.Empty;

        var parts = new List<string>(4);
        if (!string.IsNullOrWhiteSpace(module.RequiredVersion))
            parts.Add($"required {module.RequiredVersion}");
        if (!string.IsNullOrWhiteSpace(module.ModuleVersion))
            parts.Add($"minimum {module.ModuleVersion}");
        if (!string.IsNullOrWhiteSpace(module.MaximumVersion))
            parts.Add($"maximum {module.MaximumVersion}");
        if (!string.IsNullOrWhiteSpace(module.Guid))
            parts.Add($"guid {module.Guid}");

        return parts.Count == 0
            ? module.ModuleName
            : $"{module.ModuleName} ({string.Join(", ", parts)})";
    }

    private string[] ResolveDependentRequiredModules(IEnumerable<string> requiredModules, IReadOnlyCollection<string> approvedModules)
    {
        var deps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var module in requiredModules ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(module))
                continue;
            if (approvedModules is not null && approvedModules.Contains(module))
                continue;

            CollectModuleDependencies(module, visited, deps);
        }

        return deps.ToArray();
    }

    private void CollectModuleDependencies(string moduleName, HashSet<string> visited, HashSet<string> output)
    {
        if (string.IsNullOrWhiteSpace(moduleName))
            return;
        if (!visited.Add(moduleName))
            return;

        var required = GetRequiredModulesFromInstalledModule(moduleName);
        foreach (var dep in required)
        {
            if (string.IsNullOrWhiteSpace(dep))
                continue;
            if (output.Add(dep))
                CollectModuleDependencies(dep, visited, output);
        }
    }

    private string[] GetRequiredModulesFromInstalledModule(string moduleName)
    {
        try
        {
            using var ps = CreatePowerShell();
            var script = @"
param($name)
$mod = Get-Module -ListAvailable -Name $name |
  Sort-Object Version -Descending |
  Select-Object -First 1
if ($null -eq $mod) { return @() }
$req = $mod.RequiredModules
if ($null -eq $req) { return @() }
$req | ForEach-Object { $_.Name }
";
            ps.AddScript(script).AddArgument(moduleName);
            var results = ps.Invoke();
            if (ps.HadErrors || results is null)
                return Array.Empty<string>();

            return results
                .Select(r => r?.BaseObject?.ToString())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to resolve required modules for '{moduleName}': {ex.Message}");
            if (_logger.IsVerbose) _logger.Verbose(ex.ToString());
            return Array.Empty<string>();
        }
    }

    private static PowerShell CreatePowerShell()
    {
        if (Runspace.DefaultRunspace is null)
            return PowerShell.Create();
        return PowerShell.Create(RunspaceMode.CurrentRunspace);
    }

    private static bool IsBuiltInModule(string moduleName)
        => moduleName.StartsWith("Microsoft.PowerShell.", StringComparison.OrdinalIgnoreCase);

    private static readonly HashSet<string> BuiltInCommandNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Add-Content",
            "Add-Type",
            "Clear-Variable",
            "ConvertFrom-Json",
            "ConvertTo-Json",
            "Copy-Item",
            "Export-ModuleMember",
            "ForEach-Object",
            "Format-List",
            "Format-Table",
            "Get-ChildItem",
            "Get-Command",
            "Get-Content",
            "Get-Date",
            "Get-Item",
            "Get-ItemProperty",
            "Get-Location",
            "Get-Member",
            "Get-Variable",
            "Import-Module",
            "Join-Path",
            "Measure-Object",
            "Move-Item",
            "New-Item",
            "New-Object",
            "Out-File",
            "Pop-Location",
            "Push-Location",
            "Remove-Item",
            "Remove-Variable",
            "Resolve-Path",
            "Select-Object",
            "Set-Content",
            "Set-Item",
            "Set-ItemProperty",
            "Set-Location",
            "Set-Variable",
            "Sort-Object",
            "Split-Path",
            "Start-Process",
            "Start-Sleep",
            "Test-Path",
            "Where-Object",
            "Write-Debug",
            "Write-Error",
            "Write-Host",
            "Write-Information",
            "Write-Output",
            "Write-Progress",
            "Write-Verbose",
            "Write-Warning"
        };

    private static bool IsBuiltInCommand(string name)
        => BuiltInCommandNames.Contains(name);

    private sealed class MergeSourceInfo
    {
        public MergeSourceInfo(string psm1Path, string[] scriptFiles, string mergedScriptContent, bool hasLib)
        {
            Psm1Path = psm1Path;
            ScriptFiles = scriptFiles ?? Array.Empty<string>();
            MergedScriptContent = mergedScriptContent ?? string.Empty;
            HasLib = hasLib;
        }

        public string Psm1Path { get; }
        public string[] ScriptFiles { get; }
        public string MergedScriptContent { get; }
        public bool HasLib { get; }
        public bool HasScripts => ScriptFiles.Length > 0;
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
        var standardPsd1 = cfg.Standard.FormatCodePSD1 ?? (string.IsNullOrWhiteSpace(cfg.Standard.Style?.PSD1)
            ? null
            : new FormatCodeOptions { Enabled = true });
        var mergePsd1 = cfg.Merge.FormatCodePSD1 ?? (string.IsNullOrWhiteSpace(cfg.Merge.Style?.PSD1)
            ? null
            : new FormatCodeOptions { Enabled = true });

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

        string[] ps1Files = all.Where(p => HasExtension(p, ".ps1")).ToArray();
        string[] psm1Files = all.Where(p => HasExtension(p, ".psm1")).ToArray();
        string[] psd1Files = all.Where(p => HasExtension(p, ".psd1")).ToArray();

        // Avoid formatting the same output file twice when merge settings are enabled.
        if (includeMergeFormatting)
        {
            if (cfg.Merge.FormatCodePSM1?.Enabled == true)
                psm1Files = psm1Files.Where(p => !string.Equals(p, rootPsm1, StringComparison.OrdinalIgnoreCase)).ToArray();

            if (mergePsd1?.Enabled == true)
                psd1Files = psd1Files.Where(p => !string.Equals(p, manifestPath, StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        var results = new List<FormatterResult>(all.Length + 4);

        // Legacy PSPublishModule defaults did not format standalone PS1 files unless explicitly configured.
        var standardPs1 = cfg.Standard.FormatCodePS1;
        if (standardPs1?.Enabled == true && ps1Files.Length > 0)
            results.AddRange(pipeline.Run(ps1Files, BuildFormatOptions(standardPs1)));

        if (cfg.Standard.FormatCodePSM1?.Enabled == true && psm1Files.Length > 0)
            results.AddRange(pipeline.Run(psm1Files, BuildFormatOptions(cfg.Standard.FormatCodePSM1)));

        if (standardPsd1?.Enabled == true && psd1Files.Length > 0)
            results.AddRange(pipeline.Run(psd1Files, BuildFormatOptions(standardPsd1)));

        if (includeMergeFormatting)
        {
            if (cfg.Merge.FormatCodePSM1?.Enabled == true && File.Exists(rootPsm1))
                results.AddRange(pipeline.Run(new[] { rootPsm1 }, BuildFormatOptions(cfg.Merge.FormatCodePSM1)));

            if (mergePsd1?.Enabled == true && File.Exists(manifestPath))
                results.AddRange(pipeline.Run(new[] { manifestPath }, BuildFormatOptions(mergePsd1)));
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

    private ModuleSigningResult SignBuiltModuleOutput(string moduleName, string rootPath, SigningOptionsConfiguration? signing)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Root path is required.", nameof(rootPath));

        if (signing is null)
        {
            throw new InvalidOperationException(
                "Signing is enabled but no signing options were provided. " +
                "Configure a certificate (CertificateThumbprint / CertificatePFXPath / CertificatePFXBase64) or disable signing.");
        }

        var include = BuildSigningIncludePatterns(signing);
        var exclude = BuildSigningExcludeSubstrings(signing);

        var args = new List<string>(8)
        {
            rootPath,
            EncodeLines(include),
            EncodeLines(exclude),
            signing.CertificateThumbprint ?? string.Empty,
            signing.CertificatePFXPath ?? string.Empty,
            signing.CertificatePFXBase64 ?? string.Empty,
            signing.CertificatePFXPassword ?? string.Empty,
            signing.OverwriteSigned == true ? "1" : "0"
        };

        var runner = new PowerShellRunner();
        var script = BuildSignModuleScript();
        var result = RunScript(runner, script, args, TimeSpan.FromMinutes(10));

        var summary = TryExtractSigningSummary(result.StdOut);
        if (result.ExitCode != 0 || (summary?.Failed ?? 0) > 0)
        {
            var msg = TryExtractSigningError(result.StdOut) ?? result.StdErr;
            var extra = summary is null ? string.Empty : $" {FormatSigningSummary(summary)}";
            var full = $"Signing failed (exit {result.ExitCode}). {msg}{extra}".Trim();

            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdOut)) _logger.Verbose(result.StdOut.Trim());
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdErr)) _logger.Verbose(result.StdErr.Trim());

            throw new ModuleSigningException(full, summary);
        }

        summary ??= new ModuleSigningResult
        {
            Attempted = ParseSignedCount(result.StdOut),
            SignedNew = ParseSignedCount(result.StdOut)
        };

        if (summary.SignedTotal > 0)
        {
            _logger.Success(
                $"Signed {summary.SignedNew} new file(s), re-signed {summary.Resigned} file(s) for '{moduleName}'. " +
                $"(Already signed: {summary.AlreadySignedOther} third-party, {summary.AlreadySignedByThisCert} by this cert)");
        }
        else
        {
            _logger.Info(
                $"No files required signing for '{moduleName}'. " +
                $"(Already signed: {summary.AlreadySignedOther} third-party, {summary.AlreadySignedByThisCert} by this cert)");
        }

        return summary;
    }

    internal static string[] BuildSigningIncludePatterns(SigningOptionsConfiguration signing)
    {
        if (signing.Include is { Length: > 0 })
            return signing.Include.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToArray();

        var include = new List<string> { "*.ps1", "*.psm1", "*.psd1" };
        // New default: include binaries unless explicitly disabled.
        if (signing.IncludeBinaries != false) include.AddRange(new[] { "*.dll", "*.cat" });
        if (signing.IncludeExe == true) include.Add("*.exe");
        return include.ToArray();
    }

    private static string[] BuildSigningExcludeSubstrings(SigningOptionsConfiguration signing)
    {
        var list = new List<string>();

        // Legacy behavior: Internals are excluded unless explicitly included.
        if (signing.IncludeInternals != true)
            list.Add("Internals");

        // Safety: never sign third-party downloaded dependencies by default.
        list.Add("Modules");

        if (signing.ExcludePaths is { Length: > 0 })
            list.AddRange(signing.ExcludePaths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()));

        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static int ParseSignedCount(string stdout)
    {
        foreach (var line in SplitLines(stdout))
        {
            if (!line.StartsWith("PFSIGN::COUNT::", StringComparison.Ordinal)) continue;
            var val = line.Substring("PFSIGN::COUNT::".Length);
            if (int.TryParse(val, out var n)) return n;
        }
        return 0;
    }

    private static string? TryExtractSigningError(string stdout)
    {
        foreach (var line in SplitLines(stdout))
        {
            if (!line.StartsWith("PFSIGN::ERROR::", StringComparison.Ordinal)) continue;
            var b64 = line.Substring("PFSIGN::ERROR::".Length);
            var decoded = Decode(b64);
            return string.IsNullOrWhiteSpace(decoded) ? null : decoded;
        }
        return null;
    }

    private static ModuleSigningResult? TryExtractSigningSummary(string stdout)
    {
        foreach (var line in SplitLines(stdout))
        {
            if (!line.StartsWith("PFSIGN::SUMMARY::", StringComparison.Ordinal)) continue;
            var b64 = line.Substring("PFSIGN::SUMMARY::".Length);
            var decoded = Decode(b64);
            if (string.IsNullOrWhiteSpace(decoded)) return null;

            try
            {
                return JsonSerializer.Deserialize<ModuleSigningResult>(decoded,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    private static string FormatSigningSummary(ModuleSigningResult s)
        => $"matched {s.TotalAfterExclude}, signed {s.SignedNew} new, re-signed {s.Resigned}, " +
           $"already signed {s.AlreadySignedOther} third-party/{s.AlreadySignedByThisCert} by this cert, failed {s.Failed}.";

    private static string BuildSignModuleScript()
        => EmbeddedScripts.Load("Scripts/Signing/Sign-Module.ps1");

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

    private static ValidationSeverity ResolveFileConsistencySeverity(FileConsistencySettings settings)
    {
        if (settings is null) return ValidationSeverity.Warning;
        if (settings.Severity.HasValue) return settings.Severity.Value;
        return settings.FailOnInconsistency ? ValidationSeverity.Error : ValidationSeverity.Warning;
    }

    private static ValidationSeverity ResolveCompatibilitySeverity(CompatibilitySettings settings)
    {
        if (settings is null) return ValidationSeverity.Warning;
        if (settings.Severity.HasValue) return settings.Severity.Value;
        return settings.FailOnIncompatibility ? ValidationSeverity.Error : ValidationSeverity.Warning;
    }

    private static CheckStatus EvaluateFileConsistency(
        ProjectConsistencyReport report,
        FileConsistencySettings settings,
        ValidationSeverity severity)
    {
        var total = report.Summary.TotalFiles;
        if (total <= 0) return CheckStatus.Pass;

        var filesWithIssues = CountFileConsistencyIssues(report, settings);

        if (filesWithIssues == 0) return CheckStatus.Pass;

        var max = Clamp(settings.MaxInconsistencyPercentage, 0, 100);
        var percent = (filesWithIssues / (double)total) * 100.0;
        var status = percent <= max ? CheckStatus.Warning : CheckStatus.Fail;

        if (severity == ValidationSeverity.Off) return CheckStatus.Pass;
        if (severity == ValidationSeverity.Warning && status == CheckStatus.Fail)
            return CheckStatus.Warning;
        return status;
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

    private static PowerShellCompatibilityReport ApplyCompatibilitySettings(
        PowerShellCompatibilityReport report,
        CompatibilitySettings settings,
        ValidationSeverity severity)
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

        if (severity == ValidationSeverity.Off)
        {
            status = CheckStatus.Pass;
        }
        else if (severity == ValidationSeverity.Warning && status == CheckStatus.Fail)
        {
            status = CheckStatus.Warning;
        }

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

    private static void ApplyDeliveryMetadata(string manifestPath, DeliveryOptionsConfiguration delivery)
    {
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath)) return;
        if (delivery is null || !delivery.Enable) return;

        try
        {
            var internals = string.IsNullOrWhiteSpace(delivery.InternalsPath)
                ? "Internals"
                : delivery.InternalsPath.Trim();

            void ApplyTo(string parentKey)
            {
                ManifestEditor.TrySetPsDataSubString(manifestPath, parentKey, "Schema", delivery.Schema ?? "1.3");
                ManifestEditor.TrySetPsDataSubBool(manifestPath, parentKey, "Enable", delivery.Enable);
                ManifestEditor.TrySetPsDataSubString(manifestPath, parentKey, "InternalsPath", internals);
                ManifestEditor.TrySetPsDataSubBool(manifestPath, parentKey, "IncludeRootReadme", delivery.IncludeRootReadme);
                ManifestEditor.TrySetPsDataSubBool(manifestPath, parentKey, "IncludeRootChangelog", delivery.IncludeRootChangelog);
                ManifestEditor.TrySetPsDataSubBool(manifestPath, parentKey, "IncludeRootLicense", delivery.IncludeRootLicense);
                ManifestEditor.TrySetPsDataSubString(manifestPath, parentKey, "ReadmeDestination", delivery.ReadmeDestination.ToString());
                ManifestEditor.TrySetPsDataSubString(manifestPath, parentKey, "ChangelogDestination", delivery.ChangelogDestination.ToString());
                ManifestEditor.TrySetPsDataSubString(manifestPath, parentKey, "LicenseDestination", delivery.LicenseDestination.ToString());

                if (!string.IsNullOrWhiteSpace(delivery.IntroFile))
                    ManifestEditor.TrySetPsDataSubString(manifestPath, parentKey, "IntroFile", delivery.IntroFile!.Trim());
                if (!string.IsNullOrWhiteSpace(delivery.UpgradeFile))
                    ManifestEditor.TrySetPsDataSubString(manifestPath, parentKey, "UpgradeFile", delivery.UpgradeFile!.Trim());

                if (delivery.DocumentationOrder is { Length: > 0 })
                {
                    var ordered = delivery.DocumentationOrder
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(s => s.Trim())
                        .ToArray();
                    if (ordered.Length > 0)
                        ManifestEditor.TrySetPsDataSubStringArray(manifestPath, parentKey, "DocumentationOrder", ordered);
                }

                if (delivery.RepositoryPaths is { Length: > 0 })
                {
                    var repoPaths = delivery.RepositoryPaths
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(s => s.Trim())
                        .ToArray();
                    if (repoPaths.Length > 0)
                        ManifestEditor.TrySetPsDataSubStringArray(manifestPath, parentKey, "RepositoryPaths", repoPaths);
                }

                if (!string.IsNullOrWhiteSpace(delivery.RepositoryBranch))
                    ManifestEditor.TrySetPsDataSubString(manifestPath, parentKey, "RepositoryBranch", delivery.RepositoryBranch!.Trim());

                if (delivery.IntroText is { Length: > 0 })
                {
                    var intro = delivery.IntroText
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(s => s.Trim())
                        .ToArray();
                    if (intro.Length > 0)
                        ManifestEditor.TrySetPsDataSubStringArray(manifestPath, parentKey, "IntroText", intro);
                }

                if (delivery.UpgradeText is { Length: > 0 })
                {
                    var upgrade = delivery.UpgradeText
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(s => s.Trim())
                        .ToArray();
                    if (upgrade.Length > 0)
                        ManifestEditor.TrySetPsDataSubStringArray(manifestPath, parentKey, "UpgradeText", upgrade);
                }

                if (delivery.ImportantLinks is { Length: > 0 })
                {
                    var items = delivery.ImportantLinks
                        .Where(l => l is not null && !string.IsNullOrWhiteSpace(l.Title) && !string.IsNullOrWhiteSpace(l.Url))
                        .Select(l => new Dictionary<string, string>
                        {
                            ["Title"] = l.Title.Trim(),
                            ["Url"] = l.Url.Trim()
                        })
                        .ToArray();

                    if (items.Length > 0)
                        ManifestEditor.TrySetPsDataSubHashtableArray(manifestPath, parentKey, "ImportantLinks", items);
                }
            }

            ApplyTo("Delivery");
            ApplyTo("PSPublishModuleDelivery");

            if (delivery.RepositoryPaths is { Length: > 0 } || !string.IsNullOrWhiteSpace(delivery.RepositoryBranch))
            {
                BuildServices.SetRepository(
                    manifestPath,
                    branch: string.IsNullOrWhiteSpace(delivery.RepositoryBranch) ? null : delivery.RepositoryBranch!.Trim(),
                    paths: delivery.RepositoryPaths);
            }
        }
        catch
        {
            // Best-effort: do not throw in the build pipeline for delivery metadata.
        }
    }
}



