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

        // Aggregated values from segments (last-wins for scalars, last-wins per module for required modules).
        string? expectedVersion = null;
        string[] compatible = Array.Empty<string>();
        string? preRelease = null;
        ManifestConfiguration? manifestConfiguration = null;

        string? author = null;
        string? companyName = null;
        string? description = null;
        string[]? tags = null;
        string? iconUri = null;
        string? projectUri = null;

        bool localVersioning = false;
        InstallationStrategy? installStrategyFromSegments = null;
        int? keepVersionsFromSegments = null;
        LegacyFlatModuleHandling? legacyFlatHandlingFromSegments = null;
        var preserveInstallVersionsFromSegments = new List<string>();
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
        bool syncNETProjectVersion = false;
        bool doNotAttemptToFixRelativePaths = false;
        bool refreshPsd1Only = false;
        SigningOptionsConfiguration? signing = null;

        string? dotnetConfigFromSegments = null;
        string[]? dotnetFrameworksFromSegments = null;
        string? netProjectName = null;
        string? netProjectPath = null;
        string[]? exportAssembliesFromSegments = null;
        string[]? excludeLibraryFilterFromSegments = null;
        string[]? ignoreLibraryOnLoadFromSegments = null;
        bool? doNotCopyLibrariesRecursivelyFromSegments = null;
        bool? handleRuntimesFromSegments = null;
        bool? useAssemblyLoadContextFromSegments = null;
        bool? developmentBinariesEnabledFromSegments = null;
        ModuleDevelopmentBinaryMode? developmentBinariesModeFromSegments = null;
        string? developmentBinariesPathFromSegments = null;
        string? developmentBinariesEnvironmentVariableFromSegments = null;
        string? developmentConfigurationEnvironmentVariableFromSegments = null;
        ModuleDevelopmentSourceBootstrapperMode? developmentSourceBootstrapperModeFromSegments = null;
        AssemblyTypeAcceleratorExportMode? assemblyTypeAcceleratorModeFromSegments = null;
        string[]? assemblyTypeAcceleratorsFromSegments = null;
        string[]? assemblyTypeAcceleratorAssembliesFromSegments = null;
        bool? disableBinaryCmdletScanFromSegments = null;
        string? resolveBinaryConflictsProjectName = null;
        bool? binaryModuleDocumentationRequested = null;

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
        var actions = new List<ConfigurationActionSegment>();
        var externalAssets = new List<ConfigurationExternalAssetSegment>();
        var artefacts = new List<ConfigurationArtefactSegment>();
        var publishes = new List<ConfigurationPublishSegment>();
        var appleApps = new List<ConfigurationAppleAppSegment>();
        var xcodeProjectVersions = new List<ConfigurationXcodeProjectVersionSegment>();
        var projectBuilds = new List<ConfigurationProjectBuildSegment>();
        var packageBuilds = new List<ConfigurationPackageBuildSegment>();
        ConfigurationReleaseSegment? release = null;
        ConfigurationGateMode? gateMode = null;
        var approvedModules = new List<string>();
        var moduleSkipIgnoreModules = new List<string>();
        var moduleSkipIgnoreFunctions = new List<string>();
        bool moduleSkipForce = false;
        bool moduleSkipFailOnMissingCommands = false;
        bool resolveMissingModulesOnlineSet = false;

        var requiredModulesDraft = new List<RequiredModuleDraft>();
        var requiredIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var requiredModulesDraftForPackaging = new List<RequiredModuleDraft>();
        var requiredPackagingIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var embeddedModulesDraft = new List<RequiredModuleDraft>();
        var embeddedIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var externalModules = new List<string>();
        var externalIndex = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var segments = (spec.Segments ?? Array.Empty<IConfigurationSegment>())
            .Where(static segment => segment is not null)
            .ToArray();

        var manifestBaseline = TryReadProjectManifestBaseline(projectRoot, moduleName);
        if (manifestBaseline is not null)
        {
            manifestConfiguration = manifestBaseline.Manifest;
            // Source manifests seed descriptive metadata only. Dependency/export fields are rebuilt from
            // configuration so stale PSD1 entries do not survive after build settings remove them.

            if (manifestBaseline.Manifest.CompatiblePSEditions is { Length: > 0 })
                compatible = manifestBaseline.Manifest.CompatiblePSEditions;
            if (!string.IsNullOrWhiteSpace(manifestBaseline.Manifest.Prerelease))
                preRelease = manifestBaseline.Manifest.Prerelease;

            if (!string.IsNullOrWhiteSpace(manifestBaseline.Manifest.Author))
                author = manifestBaseline.Manifest.Author;
            if (!string.IsNullOrWhiteSpace(manifestBaseline.Manifest.CompanyName))
                companyName = manifestBaseline.Manifest.CompanyName;
            if (!string.IsNullOrWhiteSpace(manifestBaseline.Manifest.Description))
                description = manifestBaseline.Manifest.Description;
            if (manifestBaseline.Manifest.Tags is { Length: > 0 })
                tags = manifestBaseline.Manifest.Tags;
            if (!string.IsNullOrWhiteSpace(manifestBaseline.Manifest.IconUri))
                iconUri = manifestBaseline.Manifest.IconUri;
            if (!string.IsNullOrWhiteSpace(manifestBaseline.Manifest.ProjectUri))
                projectUri = manifestBaseline.Manifest.ProjectUri;
        }

        foreach (var segment in segments)
        {
            switch (segment)
            {
                case ConfigurationGateSegment gate:
                {
                    gateMode = gate.Configuration.Mode;
                    break;
                }
                case ConfigurationManifestSegment manifest:
                {
                    var m = manifest.Configuration;
                    manifestConfiguration = new ManifestConfiguration
                    {
                        ModuleVersion = m.ModuleVersion,
                        CompatiblePSEditions = m.CompatiblePSEditions ?? Array.Empty<string>(),
                        Guid = m.Guid,
                        Author = m.Author,
                        CompanyName = m.CompanyName,
                        Copyright = m.Copyright,
                        Description = m.Description,
                        PowerShellVersion = m.PowerShellVersion,
                        Tags = m.Tags,
                        IconUri = m.IconUri,
                        ProjectUri = m.ProjectUri,
                        DotNetFrameworkVersion = m.DotNetFrameworkVersion,
                        LicenseUri = m.LicenseUri,
                        RequireLicenseAcceptance = m.RequireLicenseAcceptance,
                        Prerelease = m.Prerelease,
                        FunctionsToExport = m.FunctionsToExport,
                        CmdletsToExport = m.CmdletsToExport,
                        AliasesToExport = m.AliasesToExport,
                        FormatsToProcess = m.FormatsToProcess
                    };

                    if (!string.IsNullOrWhiteSpace(m.ModuleVersion)) expectedVersion = m.ModuleVersion;
                    if (m.CompatiblePSEditions is { Length: > 0 }) compatible = m.CompatiblePSEditions;
                    // Unconditional: an absent Prerelease explicitly clears any baseline prerelease value.
                    preRelease = string.IsNullOrWhiteSpace(m.Prerelease) ? null : m.Prerelease!.Trim();

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
                    if (b.LegacyFlatHandling.HasValue) legacyFlatHandlingFromSegments = b.LegacyFlatHandling.Value;
                    if (b.PreserveInstallVersions is { Length: > 0 })
                        preserveInstallVersionsFromSegments.AddRange(b.PreserveInstallVersions);
                    if (b.InstallMissingModules.HasValue) installMissingModules = b.InstallMissingModules.Value;
                    if (b.InstallMissingModulesForce.HasValue) installMissingModulesForce = b.InstallMissingModulesForce.Value;
                    if (b.InstallMissingModulesPrerelease.HasValue) installMissingModulesPrerelease = b.InstallMissingModulesPrerelease.Value;
                    if (b.ResolveMissingModulesOnline.HasValue)
                    {
                        resolveMissingModulesOnline = b.ResolveMissingModulesOnline.Value;
                        resolveMissingModulesOnlineSet = true;
                    }
                    if (b.WarnIfRequiredModulesOutdated.HasValue) warnIfRequiredModulesOutdated = b.WarnIfRequiredModulesOutdated.Value;
                    if (!string.IsNullOrWhiteSpace(b.InstallMissingModulesRepository)) installMissingModulesRepository = b.InstallMissingModulesRepository;
                    if (b.InstallMissingModulesCredential is not null) installMissingModulesCredential = b.InstallMissingModulesCredential;
                    if (b.SignMerged.HasValue) signModule = b.SignMerged.Value;
                    if (b.RefreshPSD1Only.HasValue) refreshPsd1Only = b.RefreshPSD1Only.Value;
                    if (b.SyncNETProjectVersion.HasValue) syncNETProjectVersion = b.SyncNETProjectVersion.Value;
                    if (b.DoNotAttemptToFixRelativePaths.HasValue) doNotAttemptToFixRelativePaths = b.DoNotAttemptToFixRelativePaths.Value;
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
                    if (bl.ExcludeLibraryFilter is { Length: > 0 }) excludeLibraryFilterFromSegments = bl.ExcludeLibraryFilter;
                    if (bl.IgnoreLibraryOnLoad is { Length: > 0 }) ignoreLibraryOnLoadFromSegments = bl.IgnoreLibraryOnLoad;
                    if (bl.NETDoNotCopyLibrariesRecursively.HasValue) doNotCopyLibrariesRecursivelyFromSegments = bl.NETDoNotCopyLibrariesRecursively.Value;
                    if (bl.HandleRuntimes.HasValue) handleRuntimesFromSegments = bl.HandleRuntimes.Value;
                    if (bl.UseAssemblyLoadContext.HasValue)
                        useAssemblyLoadContextFromSegments = bl.UseAssemblyLoadContext.Value;
                    else if (bl.NETAssemblyLoadContext.HasValue)
                        useAssemblyLoadContextFromSegments = bl.NETAssemblyLoadContext.Value;
                    if (bl.DevelopmentBinaries.HasValue)
                        developmentBinariesEnabledFromSegments = bl.DevelopmentBinaries.Value;
                    else if (bl.NETDevelopmentBinaries.HasValue)
                        developmentBinariesEnabledFromSegments = bl.NETDevelopmentBinaries.Value;
                    if (bl.DevelopmentBinariesMode.HasValue)
                        developmentBinariesModeFromSegments = bl.DevelopmentBinariesMode.Value;
                    else if (bl.NETDevelopmentBinariesMode.HasValue)
                        developmentBinariesModeFromSegments = bl.NETDevelopmentBinariesMode.Value;
                    if (!string.IsNullOrWhiteSpace(bl.DevelopmentBinariesPath))
                        developmentBinariesPathFromSegments = bl.DevelopmentBinariesPath;
                    else if (!string.IsNullOrWhiteSpace(bl.NETDevelopmentBinariesPath))
                        developmentBinariesPathFromSegments = bl.NETDevelopmentBinariesPath;
                    if (!string.IsNullOrWhiteSpace(bl.DevelopmentBinariesEnvironmentVariable))
                        developmentBinariesEnvironmentVariableFromSegments = bl.DevelopmentBinariesEnvironmentVariable;
                    else if (!string.IsNullOrWhiteSpace(bl.NETDevelopmentBinariesEnvironmentVariable))
                        developmentBinariesEnvironmentVariableFromSegments = bl.NETDevelopmentBinariesEnvironmentVariable;
                    if (!string.IsNullOrWhiteSpace(bl.DevelopmentConfigurationEnvironmentVariable))
                        developmentConfigurationEnvironmentVariableFromSegments = bl.DevelopmentConfigurationEnvironmentVariable;
                    else if (!string.IsNullOrWhiteSpace(bl.NETDevelopmentConfigurationEnvironmentVariable))
                        developmentConfigurationEnvironmentVariableFromSegments = bl.NETDevelopmentConfigurationEnvironmentVariable;
                    if (bl.DevelopmentSourceBootstrapperMode.HasValue)
                        developmentSourceBootstrapperModeFromSegments = bl.DevelopmentSourceBootstrapperMode.Value;
                    else if (bl.NETDevelopmentSourceBootstrapperMode.HasValue)
                        developmentSourceBootstrapperModeFromSegments = bl.NETDevelopmentSourceBootstrapperMode.Value;
                    if (bl.AssemblyTypeAcceleratorMode.HasValue)
                        assemblyTypeAcceleratorModeFromSegments = bl.AssemblyTypeAcceleratorMode.Value;
                    else if (bl.NETAssemblyTypeAcceleratorMode.HasValue)
                        assemblyTypeAcceleratorModeFromSegments = bl.NETAssemblyTypeAcceleratorMode.Value;
                    if (bl.AssemblyTypeAccelerators is not null)
                        assemblyTypeAcceleratorsFromSegments = bl.AssemblyTypeAccelerators;
                    else if (bl.NETAssemblyTypeAccelerators is not null)
                        assemblyTypeAcceleratorsFromSegments = bl.NETAssemblyTypeAccelerators;
                    if (bl.AssemblyTypeAcceleratorAssemblies is not null)
                        assemblyTypeAcceleratorAssembliesFromSegments = bl.AssemblyTypeAcceleratorAssemblies;
                    else if (bl.NETAssemblyTypeAcceleratorAssemblies is not null)
                        assemblyTypeAcceleratorAssembliesFromSegments = bl.NETAssemblyTypeAcceleratorAssemblies;
                    if (bl.BinaryModuleCmdletScanDisabled.HasValue) disableBinaryCmdletScanFromSegments = bl.BinaryModuleCmdletScanDisabled.Value;
                    if (bl.NETBinaryModuleDocumentation.HasValue) binaryModuleDocumentationRequested = bl.NETBinaryModuleDocumentation.Value;
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

                    if (moduleSeg.Kind == ModuleDependencyKind.ExternalModule)
                    {
                        if (!TryAddExternalModuleDependency(name, externalIndex, externalModules))
                            break;
                        break;
                    }

                    if (moduleSeg.Kind == ModuleDependencyKind.EmbeddedModule)
                    {
                        if (ModulePipelinePlanningHelpers.ShouldSkipManifestDependencyModule(name))
                            break;

                        var embeddedDraft = new RequiredModuleDraft(
                            moduleName: name,
                            moduleVersion: md.ModuleVersion,
                            minimumVersion: md.MinimumVersion,
                            requiredVersion: md.RequiredVersion,
                            guid: md.Guid,
                            versionSource: md.VersionSource);

                        if (embeddedIndex.TryGetValue(name, out var embeddedIdx))
                            embeddedModulesDraft[embeddedIdx] = embeddedDraft;
                        else
                        {
                            embeddedIndex[name] = embeddedModulesDraft.Count;
                            embeddedModulesDraft.Add(embeddedDraft);
                        }

                        break;
                    }

                    if (moduleSeg.Kind is not ModuleDependencyKind.RequiredModule)
                        break;

                    if (ModulePipelinePlanningHelpers.ShouldSkipManifestDependencyModule(name))
                        break;

                    var draft = new RequiredModuleDraft(
                        moduleName: name,
                        moduleVersion: md.ModuleVersion,
                        minimumVersion: md.MinimumVersion,
                        requiredVersion: md.RequiredVersion,
                        guid: md.Guid,
                        versionSource: md.VersionSource);

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
                    if (string.IsNullOrWhiteSpace(commandModuleName))
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
                    if (cfg.AnalyzeBinaryConflicts.HasValue) importModules.AnalyzeBinaryConflicts = cfg.AnalyzeBinaryConflicts;
                    if (cfg.PreferBinaryConflictOrder.HasValue) importModules.PreferBinaryConflictOrder = cfg.PreferBinaryConflictOrder;
                    if (cfg.SkipBinaryDependencyCheck.HasValue) importModules.SkipBinaryDependencyCheck = cfg.SkipBinaryDependencyCheck;
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
                    formatting = ModulePipelinePlanningHelpers.MergeFormattingSegments(formatting, formattingSegment);
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
                case ConfigurationActionSegment action:
                {
                    var cfg = action.Configuration ?? new ModulePipelineActionConfiguration();
                    if (cfg.Enabled)
                    {
                        action.Configuration = cfg;
                        actions.Add(action);
                    }
                    break;
                }
                case ConfigurationExternalAssetSegment externalAsset:
                {
                    externalAsset.Configuration ??= new ExternalAssetConfiguration();
                    externalAssets.Add(externalAsset);
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
                case ConfigurationAppleAppSegment appleApp:
                {
                    appleApps.Add(appleApp);
                    var cfg = appleApp.Configuration ?? new AppleAppConfiguration();
                    if (cfg.UseResolvedVersion && string.IsNullOrWhiteSpace(expectedVersion))
                        expectedVersion = spec.Build.Version;
                    break;
                }
                case ConfigurationXcodeProjectVersionSegment xcode:
                {
                    xcodeProjectVersions.Add(xcode);
                    var cfg = xcode.Configuration ?? new XcodeProjectVersionConfiguration();
                    if (cfg.UseResolvedVersion && string.IsNullOrWhiteSpace(expectedVersion))
                        expectedVersion = spec.Build.Version;
                    break;
                }
                case ConfigurationProjectBuildSegment projectBuild:
                {
                    projectBuilds.Add(projectBuild);
                    break;
                }
                case ConfigurationPackageBuildSegment packageBuild:
                {
                    packageBuilds.Add(packageBuild);
                    break;
                }
                case ConfigurationReleaseSegment releaseSegment:
                {
                    if (releaseSegment.Configuration is not null)
                        release = releaseSegment;
                    break;
                }
            }
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

        var localPsd1 = localVersioning ? Path.Combine(projectRoot, $"{moduleName}.psd1") : null;
        var stepper = new ModuleVersionStepper(_logger);
        var resolved = stepper.Step(expectedVersionResolved, moduleName, localPsd1Path: localPsd1).Version;

        // Resolve .csproj path: explicit build setting wins, otherwise derive from BuildLibraries NETProjectPath/ProjectName.
        var csproj = !string.IsNullOrWhiteSpace(spec.Build.CsprojPath)
            ? spec.Build.CsprojPath
            : ModulePipelinePlanningHelpers.TryResolveCsprojPath(projectRoot, moduleName, netProjectPath, netProjectName);

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

        ApplyGateModeToPlanInputs(
            gateMode,
            ref refreshPsd1Only);

        if (gateMode == ConfigurationGateMode.Documentation && syncNETProjectVersion)
        {
            _logger.Info("Gate mode Documentation enabled: disabling project version sync for this run.");
            syncNETProjectVersion = false;
        }

        var csprojRequiredReasons = refreshPsd1Only
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

        var buildSpec = new ModuleBuildSpec
        {
            Name = moduleName,
            SourcePath = projectRoot,
            StagingPath = spec.Build.StagingPath,
            CsprojPath = refreshPsd1Only ? string.Empty : csproj,
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
            IgnoreLibraryOnLoad = NormalizeStringArray(ignoreLibraryOnLoadFromSegments ?? spec.Build.IgnoreLibraryOnLoad),
            KeepStaging = spec.Build.KeepStaging,
            RefreshManifestOnly = refreshPsd1Only
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

        var enabledPublishes = ResolveGateFilteredPublishes(gateMode, publishes);
        var dependencyVersionSourceRepository = ResolvePublishDependencyVersionSource(
            ResolveDependencyVersionSourcePublishes(gateMode, publishes));

        var approved = NormalizeApprovedModules(approvedModules);
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
            resolveMissingModulesOnline,
            warnIfRequiredModulesOutdated,
            installMissingModulesPrerelease,
            installMissingModulesRepository,
            installMissingModulesCredential,
            dependencyVersionSourceRepository);
        embeddedModules = OrderRequiredModulesByDependenciesFirst(embeddedModules);

        if (delivery?.Sign == true)
        {
            signing = ApplyDeliverySigningPreference(signing, delivery);

            if (!signModule)
            {
                signModule = true;
                _logger.Info("Delivery signing requested; enabling signing so bundled internals are also signed.");
            }
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
        var enabledExternalAssets = externalAssets
            .Where(static asset => asset is not null && asset.Configuration?.Enabled != false)
            .ToArray();

        if (formatting is not null &&
            formatting.Options is not null &&
            !formatting.Options.UpdateProjectRoot &&
            ModulePipelinePlanningHelpers.HasStandardFormattingConfiguration(formatting))
        {
            formatting.Options.UpdateProjectRoot = true;
            _logger.Info("UpdateProjectRoot not explicitly set; enabling because Default* formatting targets are configured (legacy compatibility).");
        }

        if (refreshPsd1Only)
        {
            if (signModule)
                _logger.Info("RefreshPSD1Only enabled: disabling signing for this run.");

            signModule = false;
            installEnabled = false;
            installMissingModules = false;
            installMissingModulesForce = false;
            installMissingModulesPrerelease = false;
            documentation = null;
            documentationBuild = null;
            compatibilitySettings = null;
            fileConsistencySettings = null;
            validationSettings = null;
            importModules = null;
            testsAfterMerge.Clear();
            enabledExternalAssets = Array.Empty<ConfigurationExternalAssetSegment>();
            enabledArtefacts = Array.Empty<ConfigurationArtefactSegment>();
            enabledPublishes = Array.Empty<ConfigurationPublishSegment>();
            projectBuilds.Clear();
            packageBuilds.Clear();
            release = null;
        }

        if (gateMode == ConfigurationGateMode.Documentation)
        {
            if (signModule)
                _logger.Info("Gate mode Documentation enabled: disabling signing for this run.");

            syncNETProjectVersion = false;
            signModule = false;
            installEnabled = false;
            formatting = null;
            compatibilitySettings = null;
            fileConsistencySettings = null;
            validationSettings = null;
            importModules = null;
            testsAfterMerge.Clear();
            enabledArtefacts = Array.Empty<ConfigurationArtefactSegment>();
            enabledPublishes = Array.Empty<ConfigurationPublishSegment>();
            delivery = null;
            projectBuilds = projectBuilds
                .Where(static build => build?.Configuration?.BuildBeforeModule == true)
                .ToList();
            packageBuilds = packageBuilds
                .Where(static build => build?.Configuration?.BuildBeforeModule == true)
                .ToList();
            appleApps.Clear();
            xcodeProjectVersions.Clear();
            release = null;
            actions = actions
                .Where(static action => action?.Configuration is not null &&
                                        action.Configuration.Enabled &&
                                        IsDocumentationGateActionStage(action.Configuration.At))
                .ToList();
        }

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

        var commandDeps = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in commandDependencies)
        {
            var cmds = kvp.Value
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
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
            manifest: manifestConfiguration,
            buildSpec: buildSpec,
            resolvedCsprojPath: csproj,
            syncNETProjectVersion: syncNETProjectVersion,
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
    }

    private void ApplyGateModeToPlanInputs(
        ConfigurationGateMode? gateMode,
        ref bool refreshPsd1Only)
    {
        if (gateMode is null)
            return;

        switch (gateMode.Value)
        {
            case ConfigurationGateMode.Manifest:
                if (!refreshPsd1Only)
                    _logger.Info("Gate mode Manifest enabled: forcing RefreshPSD1Only for this run.");
                refreshPsd1Only = true;
                break;
            case ConfigurationGateMode.Documentation:
            case ConfigurationGateMode.Build:
            case ConfigurationGateMode.Publish:
                if (refreshPsd1Only)
                    _logger.Info($"Gate mode {gateMode.Value} enabled: disabling RefreshPSD1Only for this run.");
                refreshPsd1Only = false;
                break;
        }
    }

    private static bool IsVersionPattern(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           value!.IndexOf("X", StringComparison.OrdinalIgnoreCase) >= 0;

    private static ModuleDevelopmentBinaryMode ResolveDevelopmentBinariesMode(
        bool? enabledFromSegments,
        ModuleDevelopmentBinaryMode? modeFromSegments,
        ModuleDevelopmentBinaryMode modeFromSpec)
    {
        if (enabledFromSegments.HasValue)
        {
            if (!enabledFromSegments.Value)
                return ModuleDevelopmentBinaryMode.Off;

            return modeFromSegments ?? ModuleDevelopmentBinaryMode.Environment;
        }

        return modeFromSegments ?? modeFromSpec;
    }

    private static ConfigurationPublishSegment[] ResolveGateFilteredPublishes(
        ConfigurationGateMode? gateMode,
        IEnumerable<ConfigurationPublishSegment> publishes)
        => gateMode switch
        {
            ConfigurationGateMode.Manifest or ConfigurationGateMode.Documentation or ConfigurationGateMode.Build => Array.Empty<ConfigurationPublishSegment>(),
            ConfigurationGateMode.Publish => publishes
                .Where(static publish => publish?.Configuration is not null)
                .Select(static publish => NormalizePublishGateSegment(publish))
                .ToArray(),
            _ => publishes
                .Where(static publish => publish?.Configuration?.Enabled == true)
                .ToArray()
        };

    private static ConfigurationPublishSegment[] ResolveDependencyVersionSourcePublishes(
        ConfigurationGateMode? gateMode,
        IEnumerable<ConfigurationPublishSegment> publishes)
        => gateMode switch
        {
            ConfigurationGateMode.Manifest => publishes
                .Where(static publish => publish?.Configuration?.Enabled == true)
                .ToArray(),
            ConfigurationGateMode.Documentation or ConfigurationGateMode.Build or ConfigurationGateMode.Publish => publishes
                .Where(static publish => publish?.Configuration is not null)
                .ToArray(),
            _ => publishes
                .Where(static publish => publish?.Configuration?.Enabled == true)
                .ToArray()
        };

    private static ConfigurationPublishSegment NormalizePublishGateSegment(ConfigurationPublishSegment publish)
    {
        publish.Configuration.Enabled = true;
        return publish;
    }

    private static bool IsGateEnabledProjectBuild(
        ConfigurationGateMode? gateMode,
        ConfigurationProjectBuildSegment? segment)
        => segment?.Configuration is not null &&
           gateMode is not ConfigurationGateMode.Manifest &&
           (gateMode != ConfigurationGateMode.Documentation ||
            (segment.Configuration.Enabled && segment.Configuration.BuildBeforeModule)) &&
           (gateMode.HasValue || segment.Configuration.Enabled);

    private static bool IsGateEnabledPackageBuild(
        ConfigurationGateMode? gateMode,
        ConfigurationPackageBuildSegment? segment)
        => segment?.Configuration is not null &&
           gateMode is not ConfigurationGateMode.Manifest &&
           (gateMode != ConfigurationGateMode.Documentation ||
            (segment.Configuration.Enabled && segment.Configuration.BuildBeforeModule)) &&
           (gateMode.HasValue || segment.Configuration.Enabled);

    private static bool IsDocumentationGateActionStage(ModulePipelineActionStage stage)
        => stage is ModulePipelineActionStage.BeforeDependencies
            or ModulePipelineActionStage.AfterDependencies
            or ModulePipelineActionStage.BeforeVersioning
            or ModulePipelineActionStage.AfterVersioning
            or ModulePipelineActionStage.BeforeStaging
            or ModulePipelineActionStage.AfterStaging
            or ModulePipelineActionStage.BeforeBuild
            or ModulePipelineActionStage.AfterBuild
            or ModulePipelineActionStage.BeforeManifest
            or ModulePipelineActionStage.AfterManifest
            or ModulePipelineActionStage.BeforeDocumentation
            or ModulePipelineActionStage.AfterDocumentation;

    private DependencyVersionSourceRepository? ResolvePublishDependencyVersionSource(ConfigurationPublishSegment[] enabledPublishes)
    {
        var candidates = (enabledPublishes ?? Array.Empty<ConfigurationPublishSegment>())
            .Select(static publish => publish.Configuration)
            .Where(static publish => publish.UseAsDependencyVersionSource)
            .ToArray();

        if (candidates.Length == 0)
            return null;

        if (candidates.Length > 1)
            throw new InvalidOperationException("Only one effective New-ConfigurationPublish segment can use -UseAsDependencyVersionSource.");

        var publish = candidates[0];
        if (publish.Destination != PublishDestination.PowerShellGallery)
            throw new InvalidOperationException("-UseAsDependencyVersionSource can only be used with PowerShell repository publish destinations.");

        var repository = publish.Repository?.Name ?? publish.RepositoryName;
        if (string.IsNullOrWhiteSpace(repository))
            repository = "PSGallery";

        _logger.Info($"Dependency version source: resolving Auto/Latest module dependencies from repository '{repository}'.");
        return new DependencyVersionSourceRepository(
            repository,
            publish.Repository?.Credential,
            preferOnlineMetadata: true,
            allowOnlineLookup: true);
    }

    private bool TryAddExternalModuleDependency(
        string moduleName,
        HashSet<string> externalIndex,
        List<string> externalModules)
    {
        if (ModulePipelinePlanningHelpers.ShouldSkipManifestDependencyModule(moduleName))
        {
            _logger.Info($"Skipping built-in PowerShell module '{moduleName}' from manifest dependency output.");
            return false;
        }

        if (externalIndex.Add(moduleName))
            externalModules.Add(moduleName);

        return true;
    }
}
