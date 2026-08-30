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
    /// <summary>Collects and overlays configuration segments without resolving version, build, or delivery output.</summary>
    private ModulePipelinePlanInputs CollectPlanInputs(ModulePipelineSpec spec, string projectRoot, string moduleName)
    {
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
        PowerShellModuleCompilationConfiguration? powerShellCompilationFromSegments = null;

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
        ReleaseProtectionConfiguration? releaseProtection = null;
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
                        ProcessorArchitecture = m.ProcessorArchitecture,
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
                    if (b.PowerShellCompilation is not null)
                        powerShellCompilationFromSegments = b.PowerShellCompilation;
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
                case ConfigurationReleaseProtectionSegment releaseProtectionSegment:
                {
                    releaseProtection = releaseProtectionSegment.Configuration ?? new ReleaseProtectionConfiguration();
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

        return new ModulePipelinePlanInputs
        {
            ExpectedVersion = expectedVersion,
            Compatible = compatible,
            PreRelease = preRelease,
            ManifestConfiguration = manifestConfiguration,
            Author = author,
            CompanyName = companyName,
            Description = description,
            Tags = tags,
            IconUri = iconUri,
            ProjectUri = projectUri,
            LocalVersioning = localVersioning,
            InstallStrategyFromSegments = installStrategyFromSegments,
            KeepVersionsFromSegments = keepVersionsFromSegments,
            LegacyFlatHandlingFromSegments = legacyFlatHandlingFromSegments,
            PreserveInstallVersionsFromSegments = preserveInstallVersionsFromSegments,
            InstallMissingModules = installMissingModules,
            InstallMissingModulesForce = installMissingModulesForce,
            InstallMissingModulesPrerelease = installMissingModulesPrerelease,
            ResolveMissingModulesOnline = resolveMissingModulesOnline,
            WarnIfRequiredModulesOutdated = warnIfRequiredModulesOutdated,
            InstallMissingModulesRepository = installMissingModulesRepository,
            InstallMissingModulesCredential = installMissingModulesCredential,
            SignModule = signModule,
            MergeModule = mergeModule,
            MergeModuleSet = mergeModuleSet,
            MergeMissing = mergeMissing,
            MergeMissingSet = mergeMissingSet,
            SyncNETProjectVersion = syncNETProjectVersion,
            DoNotAttemptToFixRelativePaths = doNotAttemptToFixRelativePaths,
            RefreshPsd1Only = refreshPsd1Only,
            Signing = signing,
            PowerShellCompilationFromSegments = powerShellCompilationFromSegments,
            DotnetConfigFromSegments = dotnetConfigFromSegments,
            DotnetFrameworksFromSegments = dotnetFrameworksFromSegments,
            NetProjectName = netProjectName,
            NetProjectPath = netProjectPath,
            ExportAssembliesFromSegments = exportAssembliesFromSegments,
            ExcludeLibraryFilterFromSegments = excludeLibraryFilterFromSegments,
            IgnoreLibraryOnLoadFromSegments = ignoreLibraryOnLoadFromSegments,
            DoNotCopyLibrariesRecursivelyFromSegments = doNotCopyLibrariesRecursivelyFromSegments,
            HandleRuntimesFromSegments = handleRuntimesFromSegments,
            UseAssemblyLoadContextFromSegments = useAssemblyLoadContextFromSegments,
            DevelopmentBinariesEnabledFromSegments = developmentBinariesEnabledFromSegments,
            DevelopmentBinariesModeFromSegments = developmentBinariesModeFromSegments,
            DevelopmentBinariesPathFromSegments = developmentBinariesPathFromSegments,
            DevelopmentBinariesEnvironmentVariableFromSegments = developmentBinariesEnvironmentVariableFromSegments,
            DevelopmentConfigurationEnvironmentVariableFromSegments = developmentConfigurationEnvironmentVariableFromSegments,
            DevelopmentSourceBootstrapperModeFromSegments = developmentSourceBootstrapperModeFromSegments,
            AssemblyTypeAcceleratorModeFromSegments = assemblyTypeAcceleratorModeFromSegments,
            AssemblyTypeAcceleratorsFromSegments = assemblyTypeAcceleratorsFromSegments,
            AssemblyTypeAcceleratorAssembliesFromSegments = assemblyTypeAcceleratorAssembliesFromSegments,
            DisableBinaryCmdletScanFromSegments = disableBinaryCmdletScanFromSegments,
            ResolveBinaryConflictsProjectName = resolveBinaryConflictsProjectName,
            BinaryModuleDocumentationRequested = binaryModuleDocumentationRequested,
            Information = information,
            Documentation = documentation,
            Delivery = delivery,
            DocumentationBuild = documentationBuild,
            CompatibilitySettings = compatibilitySettings,
            FileConsistencySettings = fileConsistencySettings,
            ValidationSettings = validationSettings,
            Formatting = formatting,
            ImportModules = importModules,
            PlaceHolderOption = placeHolderOption,
            PlaceHolders = placeHolders,
            CommandDependencies = commandDependencies,
            TestsAfterMerge = testsAfterMerge,
            Actions = actions,
            ExternalAssets = externalAssets,
            Artefacts = artefacts,
            Publishes = publishes,
            AppleApps = appleApps,
            XcodeProjectVersions = xcodeProjectVersions,
            ProjectBuilds = projectBuilds,
            PackageBuilds = packageBuilds,
            Release = release,
            ReleaseProtection = releaseProtection,
            GateMode = gateMode,
            ApprovedModules = approvedModules,
            ModuleSkipIgnoreModules = moduleSkipIgnoreModules,
            ModuleSkipIgnoreFunctions = moduleSkipIgnoreFunctions,
            ModuleSkipForce = moduleSkipForce,
            ModuleSkipFailOnMissingCommands = moduleSkipFailOnMissingCommands,
            ResolveMissingModulesOnlineSet = resolveMissingModulesOnlineSet,
            RequiredModulesDraft = requiredModulesDraft,
            RequiredModulesDraftForPackaging = requiredModulesDraftForPackaging,
            EmbeddedModulesDraft = embeddedModulesDraft,
            ExternalModules = externalModules,
        };
    }

    private sealed class ModulePipelinePlanInputs
    {
        internal string? ExpectedVersion { get; init; } = default!;
        internal string[] Compatible { get; init; } = default!;
        internal string? PreRelease { get; init; } = default!;
        internal ManifestConfiguration? ManifestConfiguration { get; init; } = default!;
        internal string? Author { get; init; } = default!;
        internal string? CompanyName { get; init; } = default!;
        internal string? Description { get; init; } = default!;
        internal string[]? Tags { get; init; } = default!;
        internal string? IconUri { get; init; } = default!;
        internal string? ProjectUri { get; init; } = default!;
        internal bool LocalVersioning { get; init; } = default!;
        internal InstallationStrategy? InstallStrategyFromSegments { get; init; } = default!;
        internal int? KeepVersionsFromSegments { get; init; } = default!;
        internal LegacyFlatModuleHandling? LegacyFlatHandlingFromSegments { get; init; } = default!;
        internal List<string> PreserveInstallVersionsFromSegments { get; init; } = default!;
        internal bool InstallMissingModules { get; init; } = default!;
        internal bool InstallMissingModulesForce { get; init; } = default!;
        internal bool InstallMissingModulesPrerelease { get; init; } = default!;
        internal bool ResolveMissingModulesOnline { get; init; } = default!;
        internal bool WarnIfRequiredModulesOutdated { get; init; } = default!;
        internal string? InstallMissingModulesRepository { get; init; } = default!;
        internal RepositoryCredential? InstallMissingModulesCredential { get; init; } = default!;
        internal bool SignModule { get; init; } = default!;
        internal bool MergeModule { get; init; } = default!;
        internal bool MergeModuleSet { get; init; } = default!;
        internal bool MergeMissing { get; init; } = default!;
        internal bool MergeMissingSet { get; init; } = default!;
        internal bool SyncNETProjectVersion { get; init; } = default!;
        internal bool DoNotAttemptToFixRelativePaths { get; init; } = default!;
        internal bool RefreshPsd1Only { get; init; } = default!;
        internal SigningOptionsConfiguration? Signing { get; init; } = default!;
        internal PowerShellModuleCompilationConfiguration? PowerShellCompilationFromSegments { get; init; } = default!;
        internal string? DotnetConfigFromSegments { get; init; } = default!;
        internal string[]? DotnetFrameworksFromSegments { get; init; } = default!;
        internal string? NetProjectName { get; init; } = default!;
        internal string? NetProjectPath { get; init; } = default!;
        internal string[]? ExportAssembliesFromSegments { get; init; } = default!;
        internal string[]? ExcludeLibraryFilterFromSegments { get; init; } = default!;
        internal string[]? IgnoreLibraryOnLoadFromSegments { get; init; } = default!;
        internal bool? DoNotCopyLibrariesRecursivelyFromSegments { get; init; } = default!;
        internal bool? HandleRuntimesFromSegments { get; init; } = default!;
        internal bool? UseAssemblyLoadContextFromSegments { get; init; } = default!;
        internal bool? DevelopmentBinariesEnabledFromSegments { get; init; } = default!;
        internal ModuleDevelopmentBinaryMode? DevelopmentBinariesModeFromSegments { get; init; } = default!;
        internal string? DevelopmentBinariesPathFromSegments { get; init; } = default!;
        internal string? DevelopmentBinariesEnvironmentVariableFromSegments { get; init; } = default!;
        internal string? DevelopmentConfigurationEnvironmentVariableFromSegments { get; init; } = default!;
        internal ModuleDevelopmentSourceBootstrapperMode? DevelopmentSourceBootstrapperModeFromSegments { get; init; } = default!;
        internal AssemblyTypeAcceleratorExportMode? AssemblyTypeAcceleratorModeFromSegments { get; init; } = default!;
        internal string[]? AssemblyTypeAcceleratorsFromSegments { get; init; } = default!;
        internal string[]? AssemblyTypeAcceleratorAssembliesFromSegments { get; init; } = default!;
        internal bool? DisableBinaryCmdletScanFromSegments { get; init; } = default!;
        internal string? ResolveBinaryConflictsProjectName { get; init; } = default!;
        internal bool? BinaryModuleDocumentationRequested { get; init; } = default!;
        internal InformationConfiguration? Information { get; init; } = default!;
        internal DocumentationConfiguration? Documentation { get; init; } = default!;
        internal DeliveryOptionsConfiguration? Delivery { get; init; } = default!;
        internal BuildDocumentationConfiguration? DocumentationBuild { get; init; } = default!;
        internal CompatibilitySettings? CompatibilitySettings { get; init; } = default!;
        internal FileConsistencySettings? FileConsistencySettings { get; init; } = default!;
        internal ModuleValidationSettings? ValidationSettings { get; init; } = default!;
        internal ConfigurationFormattingSegment? Formatting { get; init; } = default!;
        internal ImportModulesConfiguration? ImportModules { get; init; } = default!;
        internal PlaceHolderOptionConfiguration? PlaceHolderOption { get; init; } = default!;
        internal List<PlaceHolderReplacement> PlaceHolders { get; init; } = default!;
        internal Dictionary<string, List<string>> CommandDependencies { get; init; } = default!;
        internal List<TestConfiguration> TestsAfterMerge { get; init; } = default!;
        internal List<ConfigurationActionSegment> Actions { get; init; } = default!;
        internal List<ConfigurationExternalAssetSegment> ExternalAssets { get; init; } = default!;
        internal List<ConfigurationArtefactSegment> Artefacts { get; init; } = default!;
        internal List<ConfigurationPublishSegment> Publishes { get; init; } = default!;
        internal List<ConfigurationAppleAppSegment> AppleApps { get; init; } = default!;
        internal List<ConfigurationXcodeProjectVersionSegment> XcodeProjectVersions { get; init; } = default!;
        internal List<ConfigurationProjectBuildSegment> ProjectBuilds { get; init; } = default!;
        internal List<ConfigurationPackageBuildSegment> PackageBuilds { get; init; } = default!;
        internal ConfigurationReleaseSegment? Release { get; init; } = default!;
        internal ReleaseProtectionConfiguration? ReleaseProtection { get; init; } = default!;
        internal ConfigurationGateMode? GateMode { get; init; } = default!;
        internal List<string> ApprovedModules { get; init; } = default!;
        internal List<string> ModuleSkipIgnoreModules { get; init; } = default!;
        internal List<string> ModuleSkipIgnoreFunctions { get; init; } = default!;
        internal bool ModuleSkipForce { get; init; } = default!;
        internal bool ModuleSkipFailOnMissingCommands { get; init; } = default!;
        internal bool ResolveMissingModulesOnlineSet { get; init; } = default!;
        internal List<RequiredModuleDraft> RequiredModulesDraft { get; init; } = default!;
        internal List<RequiredModuleDraft> RequiredModulesDraftForPackaging { get; init; } = default!;
        internal List<RequiredModuleDraft> EmbeddedModulesDraft { get; init; } = default!;
        internal List<string> ExternalModules { get; init; } = default!;
    }
}
