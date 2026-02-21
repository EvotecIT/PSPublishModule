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
        bool refreshPsd1Only = false;
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
        bool resolveMissingModulesOnlineSet = false;

        var requiredModulesDraft = new List<RequiredModuleDraft>();
        var requiredIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var requiredModulesDraftForPackaging = new List<RequiredModuleDraft>();
        var requiredPackagingIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var externalModules = new List<string>();
        var externalIndex = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var segment in (spec.Segments ?? Array.Empty<IConfigurationSegment>()).Where(s => s is not null))
        {
            switch (segment)
            {
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

                    if (moduleSeg.Kind == ModuleDependencyKind.ExternalModule)
                    {
                        if (!externalIndex.Contains(name))
                        {
                            externalIndex.Add(name);
                            externalModules.Add(name);
                        }
                        var externalDraft = new RequiredModuleDraft(
                            moduleName: name,
                            moduleVersion: md.ModuleVersion,
                            minimumVersion: md.MinimumVersion,
                            requiredVersion: md.RequiredVersion,
                            guid: md.Guid);

                        // Legacy behavior compatibility: external dependencies are mirrored into RequiredModules
                        // so Import-Module honors runtime prerequisites, but they are not included in
                        // RequiredModulesForPackaging (so artefacts do not bundle inbox/platform modules).
                        if (requiredIndex.TryGetValue(name, out var externalIdx))
                            requiredModulesDraft[externalIdx] = externalDraft;
                        else
                        {
                            requiredIndex[name] = requiredModulesDraft.Count;
                            requiredModulesDraft.Add(externalDraft);
                        }
                        break;
                    }

                    if (moduleSeg.Kind is not ModuleDependencyKind.RequiredModule)
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
            DisableBinaryCmdletScan = disableBinaryCmdletScanFromSegments ?? spec.Build.DisableBinaryCmdletScan,
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
            roots = ResolveInstallRootsFromCompatiblePSEditions(compatible);

        if (!resolveMissingModulesOnlineSet && HasAutoRequiredModules(requiredModulesDraft))
        {
            resolveMissingModulesOnline = true;
            _logger.Info("ResolveMissingModulesOnline not explicitly set; enabling because RequiredModules use Auto/Latest/Guid Auto.");
        }

        var requiredModules = ResolveRequiredModules(
            requiredModulesDraft,
            resolveMissingModulesOnline,
            warnIfRequiredModulesOutdated,
            installMissingModulesPrerelease,
            installMissingModulesRepository,
            installMissingModulesCredential);
        var requiredModulesForPackaging = AreRequiredModuleDraftListsEquivalent(requiredModulesDraft, requiredModulesDraftForPackaging)
            ? requiredModules.ToArray()
            : ResolveRequiredModules(
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

        if (refreshPsd1Only)
        {
            if (!string.IsNullOrWhiteSpace(csproj))
                _logger.Info("RefreshPSD1Only enabled: skipping .NET publish/binary rebuild for this run.");

            if (mergeModule)
                _logger.Info("RefreshPSD1Only enabled: disabling merge for this run.");
            mergeModule = false;
            mergeMissing = false;
        }
        else if (!mergeModuleSet)
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
            enabledArtefacts = Array.Empty<ConfigurationArtefactSegment>();
            enabledPublishes = Array.Empty<ConfigurationPublishSegment>();
        }

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
            manifest: manifestConfiguration,
            buildSpec: buildSpec,
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
            installLegacyFlatHandling: legacyFlatHandling,
            installPreserveVersions: preserveInstallVersions,
            installMissingModules: installMissingModules,
            installMissingModulesForce: installMissingModulesForce,
            installMissingModulesPrerelease: installMissingModulesPrerelease,
            installMissingModulesRepository: installMissingModulesRepository,
            installMissingModulesCredential: installMissingModulesCredential,
            stagingWasGenerated: stagingWasGenerated,
            deleteGeneratedStagingAfterRun: deleteAfter);
    }

}
