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

        var inputs = CollectPlanInputs(spec, projectRoot, moduleName);
        var expectedVersion = inputs.ExpectedVersion;
        var compatible = inputs.Compatible;
        var preRelease = inputs.PreRelease;
        var manifestConfiguration = inputs.ManifestConfiguration;
        var author = inputs.Author;
        var companyName = inputs.CompanyName;
        var description = inputs.Description;
        var tags = inputs.Tags;
        var iconUri = inputs.IconUri;
        var projectUri = inputs.ProjectUri;
        var localVersioning = inputs.LocalVersioning;
        var installStrategyFromSegments = inputs.InstallStrategyFromSegments;
        var keepVersionsFromSegments = inputs.KeepVersionsFromSegments;
        var legacyFlatHandlingFromSegments = inputs.LegacyFlatHandlingFromSegments;
        var preserveInstallVersionsFromSegments = inputs.PreserveInstallVersionsFromSegments;
        var installMissingModules = inputs.InstallMissingModules;
        var installMissingModulesForce = inputs.InstallMissingModulesForce;
        var installMissingModulesPrerelease = inputs.InstallMissingModulesPrerelease;
        var resolveMissingModulesOnline = inputs.ResolveMissingModulesOnline;
        var warnIfRequiredModulesOutdated = inputs.WarnIfRequiredModulesOutdated;
        var installMissingModulesRepository = inputs.InstallMissingModulesRepository;
        var installMissingModulesCredential = inputs.InstallMissingModulesCredential;
        var signModule = inputs.SignModule;
        var mergeModule = inputs.MergeModule;
        var mergeModuleSet = inputs.MergeModuleSet;
        var mergeMissing = inputs.MergeMissing;
        var mergeMissingSet = inputs.MergeMissingSet;
        var syncNETProjectVersion = inputs.SyncNETProjectVersion;
        var doNotAttemptToFixRelativePaths = inputs.DoNotAttemptToFixRelativePaths;
        var refreshPsd1Only = inputs.RefreshPsd1Only;
        var signing = inputs.Signing;
        var powerShellCompilationFromSegments = inputs.PowerShellCompilationFromSegments;
        var dotnetConfigFromSegments = inputs.DotnetConfigFromSegments;
        var dotnetFrameworksFromSegments = inputs.DotnetFrameworksFromSegments;
        var netProjectName = inputs.NetProjectName;
        var netProjectPath = inputs.NetProjectPath;
        var exportAssembliesFromSegments = inputs.ExportAssembliesFromSegments;
        var excludeLibraryFilterFromSegments = inputs.ExcludeLibraryFilterFromSegments;
        var ignoreLibraryOnLoadFromSegments = inputs.IgnoreLibraryOnLoadFromSegments;
        var doNotCopyLibrariesRecursivelyFromSegments = inputs.DoNotCopyLibrariesRecursivelyFromSegments;
        var handleRuntimesFromSegments = inputs.HandleRuntimesFromSegments;
        var useAssemblyLoadContextFromSegments = inputs.UseAssemblyLoadContextFromSegments;
        var developmentBinariesEnabledFromSegments = inputs.DevelopmentBinariesEnabledFromSegments;
        var developmentBinariesModeFromSegments = inputs.DevelopmentBinariesModeFromSegments;
        var developmentBinariesPathFromSegments = inputs.DevelopmentBinariesPathFromSegments;
        var developmentBinariesEnvironmentVariableFromSegments = inputs.DevelopmentBinariesEnvironmentVariableFromSegments;
        var developmentConfigurationEnvironmentVariableFromSegments = inputs.DevelopmentConfigurationEnvironmentVariableFromSegments;
        var developmentSourceBootstrapperModeFromSegments = inputs.DevelopmentSourceBootstrapperModeFromSegments;
        var assemblyTypeAcceleratorModeFromSegments = inputs.AssemblyTypeAcceleratorModeFromSegments;
        var assemblyTypeAcceleratorsFromSegments = inputs.AssemblyTypeAcceleratorsFromSegments;
        var assemblyTypeAcceleratorAssembliesFromSegments = inputs.AssemblyTypeAcceleratorAssembliesFromSegments;
        var disableBinaryCmdletScanFromSegments = inputs.DisableBinaryCmdletScanFromSegments;
        var resolveBinaryConflictsProjectName = inputs.ResolveBinaryConflictsProjectName;
        var binaryModuleDocumentationRequested = inputs.BinaryModuleDocumentationRequested;
        var information = inputs.Information;
        var documentation = inputs.Documentation;
        var delivery = inputs.Delivery;
        var documentationBuild = inputs.DocumentationBuild;
        var compatibilitySettings = inputs.CompatibilitySettings;
        var fileConsistencySettings = inputs.FileConsistencySettings;
        var validationSettings = inputs.ValidationSettings;
        var formatting = inputs.Formatting;
        var importModules = inputs.ImportModules;
        var placeHolderOption = inputs.PlaceHolderOption;
        var placeHolders = inputs.PlaceHolders;
        var commandDependencies = inputs.CommandDependencies;
        var testsAfterMerge = inputs.TestsAfterMerge;
        var actions = inputs.Actions;
        var externalAssets = inputs.ExternalAssets;
        var artefacts = inputs.Artefacts;
        var publishes = inputs.Publishes;
        var appleApps = inputs.AppleApps;
        var xcodeProjectVersions = inputs.XcodeProjectVersions;
        var projectBuilds = inputs.ProjectBuilds;
        var packageBuilds = inputs.PackageBuilds;
        var release = inputs.Release;
        var releaseProtection = inputs.ReleaseProtection;
        var gateMode = inputs.GateMode;
        var approvedModules = inputs.ApprovedModules;
        var moduleSkipIgnoreModules = inputs.ModuleSkipIgnoreModules;
        var moduleSkipIgnoreFunctions = inputs.ModuleSkipIgnoreFunctions;
        var moduleSkipForce = inputs.ModuleSkipForce;
        var moduleSkipFailOnMissingCommands = inputs.ModuleSkipFailOnMissingCommands;
        var resolveMissingModulesOnlineSet = inputs.ResolveMissingModulesOnlineSet;
        var requiredModulesDraft = inputs.RequiredModulesDraft;
        var requiredModulesDraftForPackaging = inputs.RequiredModulesDraftForPackaging;
        var embeddedModulesDraft = inputs.EmbeddedModulesDraft;
        var externalModules = inputs.ExternalModules;

        if (spec.Build.PreReleaseTag is not null)
        {
            preRelease = string.IsNullOrWhiteSpace(spec.Build.PreReleaseTag)
                ? null
                : spec.Build.PreReleaseTag.Trim();
            if (manifestConfiguration is not null)
                manifestConfiguration.Prerelease = preRelease;
        }

        ApplyGateModeToPlanInputs(
            gateMode,
            ref refreshPsd1Only);
        var enabledPublishes = ResolveGateFilteredPublishes(gateMode, publishes);

        var synchronizeModuleVersionForRun =
            !refreshPsd1Only &&
            ShouldSynchronizeModuleVersionForRun(release, gateMode);
        if (synchronizeModuleVersionForRun)
        {
            ValidateSynchronizedModuleVersionConfiguration(
                release,
                projectBuilds,
                packageBuilds,
                gateMode);
        }

        expectedVersion ??= spec.Build.Version;
        var psd1 = Path.Combine(projectRoot, $"{moduleName}.psd1");
        if (gateMode == ConfigurationGateMode.Documentation &&
            File.Exists(psd1) &&
            ModuleManifestValueReader.TryGetTopLevelString(psd1, "ModuleVersion", out var documentationManifestVersion) &&
            !string.IsNullOrWhiteSpace(documentationManifestVersion))
        {
            if (!string.Equals(expectedVersion, documentationManifestVersion, StringComparison.OrdinalIgnoreCase))
                _logger.Info($"Gate mode Documentation enabled: using current manifest version {documentationManifestVersion} instead of configured version {expectedVersion}.");

            expectedVersion = documentationManifestVersion;
        }
        else if (IsAutoVersion(expectedVersion))
        {
            try
            {
                if (File.Exists(psd1) &&
                    ModuleManifestValueReader.TryGetTopLevelString(psd1, "ModuleVersion", out var v) &&
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

        string resolved;
        if (synchronizeModuleVersionForRun)
        {
            resolved = ResolveProvisionalSynchronizedModuleVersion(expectedVersionResolved);
            _logger.Info("Synchronized release version selected: deferring the module repository lookup to the coordinated release-source build.");
        }
        else
        {
            var localPsd1 = localVersioning ? Path.Combine(projectRoot, $"{moduleName}.psd1") : null;
            resolved = _moduleVersionStepResolver(
                expectedVersionResolved,
                moduleName,
                localPsd1,
                prerelease: !string.IsNullOrWhiteSpace(preRelease),
                verifyRepositoryAvailability: gateMode == ConfigurationGateMode.Publish).Version;
            if (gateMode == ConfigurationGateMode.Publish &&
                IsVersionPattern(expectedVersionResolved))
            {
                resolved = ResolveGitHubReleaseVersion(
                    expectedVersionResolved,
                    resolved,
                    enabledPublishes,
                    projectRoot,
                    moduleName,
                    preRelease);
            }
        }

        // Resolve .csproj path: explicit build setting wins, otherwise derive from BuildLibraries NETProjectPath/ProjectName.
        var configuredCsproj = !string.IsNullOrWhiteSpace(spec.Build.CsprojPath)
            ? spec.Build.CsprojPath
            : ModulePipelinePlanningHelpers.TryResolveCsprojPath(projectRoot, moduleName, netProjectPath, netProjectName);
        var csproj = spec.Build.SkipDotNetBuild ? null : configuredCsproj;

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

        var assemblyTypeAccelerators = NormalizeStringArray(assemblyTypeAcceleratorsFromSegments ?? spec.Build.AssemblyTypeAccelerators);
        var assemblyTypeAcceleratorAssemblies = NormalizeStringArray(assemblyTypeAcceleratorAssembliesFromSegments ?? spec.Build.AssemblyTypeAcceleratorAssemblies);
        var assemblyTypeAcceleratorModeSpecified = assemblyTypeAcceleratorModeFromSegments.HasValue
            || spec.Build.AssemblyTypeAcceleratorMode.HasValue;
        var assemblyTypeAcceleratorMode = AssemblyTypeAcceleratorOptions.ResolveMode(
            assemblyTypeAcceleratorModeSpecified
                ? assemblyTypeAcceleratorModeFromSegments ?? spec.Build.AssemblyTypeAcceleratorMode
                : null,
            assemblyTypeAccelerators,
            assemblyTypeAcceleratorAssemblies);

        var requestedUseAssemblyLoadContext = useAssemblyLoadContextFromSegments ?? spec.Build.UseAssemblyLoadContext;
        var typeAcceleratorsRequireAlc = assemblyTypeAcceleratorMode != AssemblyTypeAcceleratorExportMode.None;
        var effectiveUseAssemblyLoadContext = requestedUseAssemblyLoadContext || typeAcceleratorsRequireAlc;
        if (typeAcceleratorsRequireAlc && !requestedUseAssemblyLoadContext)
            _logger.Info("Assembly type accelerators requested; UseAssemblyLoadContext automatically enabled.");

        var developmentBinariesMode = ResolveDevelopmentBinariesMode(
            developmentBinariesEnabledFromSegments,
            developmentBinariesModeFromSegments,
            spec.Build.DevelopmentBinariesMode);
        var developmentBinariesPath = developmentBinariesPathFromSegments ?? spec.Build.DevelopmentBinariesPath;

        if (gateMode == ConfigurationGateMode.Documentation && syncNETProjectVersion)
        {
            _logger.Info("Gate mode Documentation enabled: disabling project version sync for this run.");
            syncNETProjectVersion = false;
        }

        var configuredCsprojRequiredReasons = refreshPsd1Only
            ? Array.Empty<string>()
            : BuildMissingCsprojReasonList(
                spec,
                syncNETProjectVersion,
                dotnetFrameworksFromSegments,
                exportAssembliesFromSegments,
                excludeLibraryFilterFromSegments,
                doNotCopyLibrariesRecursivelyFromSegments,
                handleRuntimesFromSegments,
                requestedUseAssemblyLoadContext,
                typeAcceleratorsRequireAlc,
                resolveBinaryConflictsProjectName,
                binaryModuleDocumentationRequested == true,
                developmentBinariesMode,
                developmentBinariesPath);
        var csprojRequiredReasons = spec.Build.SkipDotNetBuild &&
                                    configuredCsprojRequiredReasons.Length == 0 &&
                                    !string.IsNullOrWhiteSpace(configuredCsproj)
            ? new[] { "CsprojPath" }
            : configuredCsprojRequiredReasons;

        var buildSpec = new ModuleBuildSpec
        {
            Name = moduleName,
            SourcePath = projectRoot,
            StagingPath = spec.Build.StagingPath,
            ReuseStaging = spec.Build.ReuseStaging,
            CsprojPath = refreshPsd1Only || spec.Build.SkipDotNetBuild ? string.Empty : csproj,
            SkipDotNetBuild = spec.Build.SkipDotNetBuild,
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
            ExcludeLibraryFilter = excludeLibraryFilterFromSegments ?? spec.Build.ExcludeLibraryFilter ?? Array.Empty<string>(),
            DoNotCopyLibrariesRecursively = doNotCopyLibrariesRecursivelyFromSegments ?? spec.Build.DoNotCopyLibrariesRecursively,
            HandleRuntimes = handleRuntimesFromSegments ?? spec.Build.HandleRuntimes,
            UseAssemblyLoadContext = effectiveUseAssemblyLoadContext,
            DevelopmentBinariesMode = developmentBinariesMode,
            DevelopmentBinariesPath = developmentBinariesPath,
            DevelopmentBinariesEnvironmentVariable = developmentBinariesEnvironmentVariableFromSegments ?? spec.Build.DevelopmentBinariesEnvironmentVariable,
            DevelopmentConfigurationEnvironmentVariable = developmentConfigurationEnvironmentVariableFromSegments ?? spec.Build.DevelopmentConfigurationEnvironmentVariable,
            DevelopmentSourceBootstrapperMode = developmentSourceBootstrapperModeFromSegments ?? spec.Build.DevelopmentSourceBootstrapperMode,
            AssemblyTypeAcceleratorMode = assemblyTypeAcceleratorMode,
            AssemblyTypeAccelerators = assemblyTypeAccelerators,
            AssemblyTypeAcceleratorAssemblies = assemblyTypeAcceleratorAssemblies,
            DisableBinaryCmdletScan = disableBinaryCmdletScanFromSegments ?? spec.Build.DisableBinaryCmdletScan,
            CsprojRequiredReasons = string.IsNullOrWhiteSpace(csproj) ? csprojRequiredReasons : Array.Empty<string>(),
            BinaryConflictPriorityModuleNames = requiredModulesDraft
                .Select(static module => module.ModuleName)
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            BinaryConflictReportRoot = projectRoot,
            AnalyzeInstalledBinaryConflictsDuringBuild = spec.Build.AnalyzeInstalledBinaryConflictsDuringBuild,
            IgnoreLibraryOnLoad = NormalizeStringArray(ignoreLibraryOnLoadFromSegments ?? spec.Build.IgnoreLibraryOnLoad),
            KeepStaging = spec.Build.KeepStaging,
            RefreshManifestOnly = refreshPsd1Only,
            PowerShellCompilation = ClonePowerShellCompilationConfiguration(
                powerShellCompilationFromSegments ?? spec.Build.PowerShellCompilation)
        };

        ValidatePowerShellModuleCompilation(buildSpec);

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
        var legacyFlatHandling = spec.Install?.LegacyFlatHandling
                                 ?? legacyFlatHandlingFromSegments
                                 ?? LegacyFlatModuleHandling.Warn;
        var preserveInstallVersions = (spec.Install?.PreserveVersions ?? preserveInstallVersionsFromSegments.ToArray())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var roots = (spec.Install?.Roots ?? Array.Empty<string>())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .ToArray();

        if (roots.Length == 0 && compatible is { Length: > 0 })
            roots = ModulePipelinePlanningHelpers.ResolveInstallRootsFromCompatiblePSEditions(compatible);

        if (!resolveMissingModulesOnlineSet && HasOnlineResolvableAutoRequiredModules(requiredModulesDraft.Concat(embeddedModulesDraft)))
        {
            resolveMissingModulesOnline = true;
            _logger.Info("ResolveMissingModulesOnline not explicitly set; enabling because module dependencies use Auto/Latest/Guid Auto.");
        }

        var dependencyVersionSourceRepository = ResolvePublishDependencyVersionSource(
            ResolveDependencyVersionSourcePublishes(gateMode, publishes));

        var approved = NormalizeApprovedModules(approvedModules);
        var ignoredModules = NormalizeStringArray(moduleSkipIgnoreModules);
        ApplyMergeDefaultsForPlan(
            refreshPsd1Only,
            csproj,
            approved,
            mergeModuleSet,
            mergeMissingSet,
            ref mergeModule,
            ref mergeMissing);

        var requiredModuleSets = ResolveRequiredModuleSets(
            requiredModulesDraft,
            requiredModulesDraftForPackaging,
            approved,
            ignoredModules,
            mergeMissing,
            importModules,
            compatible,
            resolveMissingModulesOnline,
            warnIfRequiredModulesOutdated,
            installMissingModulesPrerelease,
            installMissingModulesRepository,
            installMissingModulesCredential,
            dependencyVersionSourceRepository);
        var requiredModules = requiredModuleSets.RequiredModules;
        var requiredModulesForPackaging = requiredModuleSets.RequiredModulesForPackaging;
        var embeddedModules = ResolveRequiredModules(
            embeddedModulesDraft,
            resolveMissingModulesOnline,
            warnIfRequiredModulesOutdated,
            installMissingModulesPrerelease,
            installMissingModulesRepository,
            installMissingModulesCredential,
            dependencyVersionSourceRepository);
        var embeddedSourceDrafts = BuildRequiredModuleDraftMap(embeddedModulesDraft);
        var embeddedRoots = embeddedModulesDraft
            .Select(static draft => draft.ModuleName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        embeddedModules = IncludeTransitiveRequiredModules(
            embeddedModules,
            embeddedRoots,
            embeddedSourceDrafts,
            ignoredModules,
            resolveMissingModulesOnline,
            warnIfRequiredModulesOutdated,
            installMissingModulesPrerelease,
            installMissingModulesRepository,
            installMissingModulesCredential,
            dependencyVersionSourceRepository);
        embeddedModules = OrderRequiredModulesByDependenciesFirst(embeddedModules);

        var executionSurface = FinalizePlanExecutionSurface(new ModulePlanExecutionSurface
        {
            Delivery = delivery, Signing = signing, SignModule = signModule,
            ModuleSkipForce = moduleSkipForce, ModuleSkipFailOnMissingCommands = moduleSkipFailOnMissingCommands,
            IgnoredModules = ignoredModules, IgnoredFunctions = moduleSkipIgnoreFunctions,
            Formatting = formatting, RefreshManifestOnly = refreshPsd1Only, GateMode = gateMode,
            InstallEnabled = installEnabled, InstallMissingModules = installMissingModules,
            InstallMissingModulesForce = installMissingModulesForce,
            InstallMissingModulesPrerelease = installMissingModulesPrerelease,
            Documentation = documentation, DocumentationBuild = documentationBuild,
            CompatibilitySettings = compatibilitySettings, FileConsistencySettings = fileConsistencySettings,
            ValidationSettings = validationSettings, ImportModules = importModules,
            EnabledExternalAssets = externalAssets.Where(static asset => asset?.Configuration?.Enabled != false).ToArray(),
            EnabledArtefacts = artefacts.Where(static artefact => artefact?.Configuration?.Enabled == true).ToArray(),
            EnabledPublishes = enabledPublishes, Release = release,
            TestsAfterMerge = testsAfterMerge, ProjectBuilds = projectBuilds, PackageBuilds = packageBuilds,
            AppleApps = appleApps, XcodeProjectVersions = xcodeProjectVersions, Actions = actions
        });
        delivery = executionSurface.Delivery; signing = executionSurface.Signing; signModule = executionSurface.SignModule;
        var moduleSkip = executionSurface.ModuleSkip; formatting = executionSurface.Formatting;
        installEnabled = executionSurface.InstallEnabled; installMissingModules = executionSurface.InstallMissingModules;
        installMissingModulesForce = executionSurface.InstallMissingModulesForce;
        installMissingModulesPrerelease = executionSurface.InstallMissingModulesPrerelease;
        documentation = executionSurface.Documentation; documentationBuild = executionSurface.DocumentationBuild;
        compatibilitySettings = executionSurface.CompatibilitySettings;
        fileConsistencySettings = executionSurface.FileConsistencySettings;
        validationSettings = executionSurface.ValidationSettings; importModules = executionSurface.ImportModules;
        var enabledExternalAssets = executionSurface.EnabledExternalAssets;
        var enabledArtefacts = executionSurface.EnabledArtefacts; enabledPublishes = executionSurface.EnabledPublishes;
        release = executionSurface.Release;
        if (gateMode == ConfigurationGateMode.Documentation) syncNETProjectVersion = false;

        // Run delivery validation after refresh-only pruning so artefact overlap checks reflect
        // the operations that will actually execute for this plan.
        ValidateDeliveryPathConflicts(
            projectRoot,
            moduleName,
            resolved,
            preRelease,
            buildSpec.ExcludeDirectories,
            delivery,
            enabledArtefacts);

        var commandDeps = NormalizeCommandDependencies(commandDependencies);
        var placeHolderEntries = NormalizePlaceHolders(placeHolders);

        var plan = new ModulePipelinePlan(
            moduleName: moduleName,
            projectRoot: projectRoot,
            expectedVersion: expectedVersionResolved,
            resolvedVersion: resolved,
            preRelease: preRelease,
            manifest: manifestConfiguration,
            buildSpec: buildSpec,
            resolvedCsprojPath: csproj,
            syncNETProjectVersion: !spec.Build.SkipDotNetBuild && syncNETProjectVersion,
            compatiblePSEditions: compatible,
            requiredModules: requiredModules,
            externalModuleDependencies: externalModules
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Select(m => m.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
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
            actions: refreshPsd1Only
                ? Array.Empty<ConfigurationActionSegment>()
                : actions.ToArray(),
            externalAssets: enabledExternalAssets,
            appleApps: refreshPsd1Only
                ? Array.Empty<ConfigurationAppleAppSegment>()
                : appleApps
                .Where(static app => app?.Configuration?.Enabled != false)
                .ToArray(),
            xcodeProjectVersions: refreshPsd1Only
                ? Array.Empty<ConfigurationXcodeProjectVersionSegment>()
                : xcodeProjectVersions
                .Where(static project => project?.Configuration?.Enabled != false)
                .ToArray(),
            projectBuilds: projectBuilds
                .Where(projectBuild => IsGateEnabledProjectBuild(gateMode, projectBuild))
                .ToArray(),
            packageBuilds: packageBuilds
                .Where(packageBuild => IsGateEnabledPackageBuild(gateMode, packageBuild))
                .ToArray(),
            release: release,
            mergeModule: mergeModule,
            mergeMissing: mergeMissing,
            doNotAttemptToFixRelativePaths: doNotAttemptToFixRelativePaths,
            approvedModules: approved,
            moduleSkip: moduleSkip,
            signModule: signModule,
            signing: signing,
            publishes: enabledPublishes,
            gateMode: gateMode,
            artefacts: enabledArtefacts,
            installEnabled: installEnabled,
            installStrategy: strategy,
            installKeepVersions: keep,
            installRoots: roots,
            installLegacyFlatHandling: legacyFlatHandling,
            installPreserveVersions: preserveInstallVersions,
            installMissingModules: installMissingModules,
            installMissingModulesForce: installMissingModulesForce,
            installMissingModulesPrerelease: installMissingModulesPrerelease,
            installMissingModulesRepository: installMissingModulesRepository,
            installMissingModulesCredential: installMissingModulesCredential,
            stagingWasGenerated: stagingWasGenerated,
            deleteGeneratedStagingAfterRun: deleteAfter,
            embeddedModules: embeddedModules);
        ApplyReleaseSourceProtection(spec, plan, localVersioning, releaseProtection, gateMode);
        return plan;
    }
}
