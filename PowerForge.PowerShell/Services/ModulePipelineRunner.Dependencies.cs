using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private const string PesterMinimumVersion = "5.7.1";
    private const string PowerShellGetMinimumVersion = "2.2.5";

    private ModuleDependencyInstallResult[] EnsureBuildDependenciesInstalled(ModulePipelinePlan plan)
    {
        if (plan is null) return Array.Empty<ModuleDependencyInstallResult>();

        var required = plan.RequiredModules ?? Array.Empty<RequiredModuleReference>();
        if (required.Length == 0)
        {
            var manifestPath = Path.Combine(plan.ProjectRoot, $"{plan.ModuleName}.psd1");
            if (File.Exists(manifestPath))
            {
                var fromManifest = ModuleManifestValueReader.ReadRequiredModules(manifestPath);
                if (fromManifest.Length > 0)
                    required = fromManifest;
            }
        }

        if (required.Length == 0)
        {
            _logger.Info("InstallMissingModules enabled, but no RequiredModules were found.");
            return Array.Empty<ModuleDependencyInstallResult>();
        }

        var depList = required
            .Where(r => !string.IsNullOrWhiteSpace(r.ModuleName))
            .Select(r => new ModuleDependency(
                name: r.ModuleName.Trim(),
                requiredVersion: r.RequiredVersion,
                minimumVersion: r.ModuleVersion,
                maximumVersion: r.MaximumVersion))
            .ToList();

        if (plan.ExternalModuleDependencies is { Length: > 0 })
        {
            var known = new HashSet<string>(depList.Select(d => d.Name), StringComparer.OrdinalIgnoreCase);
            foreach (var name in plan.ExternalModuleDependencies)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                var trimmed = name.Trim();
                if (known.Contains(trimmed)) continue;
                known.Add(trimmed);
                depList.Add(new ModuleDependency(trimmed, requiredVersion: null, minimumVersion: null, maximumVersion: null));
            }
        }

        var deps = depList.ToArray();

        if (deps.Length == 0)
        {
            _logger.Info("InstallMissingModules enabled, but no valid module dependencies were resolved.");
            return Array.Empty<ModuleDependencyInstallResult>();
        }

        _logger.Info($"Installing missing modules ({deps.Length}): {string.Join(", ", deps.Select(d => d.Name))}");

        var results = _hostedOperations.EnsureDependenciesInstalled(
            dependencies: deps,
            force: plan.InstallMissingModulesForce,
            repository: plan.InstallMissingModulesRepository,
            credential: plan.InstallMissingModulesCredential,
            prerelease: plan.InstallMissingModulesPrerelease,
            skipModules: plan.ModuleSkip);

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

    private ModuleDependencyInstallResult[] EnsureFeatureToolDependenciesInstalled(
        ModulePipelinePlan plan,
        IReadOnlyList<RequiredModuleReference>? packagingRequiredModules = null)
    {
        if (plan is null) return Array.Empty<ModuleDependencyInstallResult>();

        var deps = ResolveFeatureToolDependencies(plan, packagingRequiredModules);
        if (deps.Length == 0)
            return Array.Empty<ModuleDependencyInstallResult>();

        return EnsureFeatureToolDependenciesInstalled(
            deps,
            plan.InstallMissingModulesForce,
            plan.InstallMissingModulesRepository,
            plan.InstallMissingModulesCredential,
            plan.InstallMissingModulesPrerelease);
    }

    private ModuleDependencyInstallResult[] EnsureFeatureToolDependenciesInstalled(
        ModuleDependency[] deps,
        bool force,
        string? repository,
        RepositoryCredential? credential,
        bool prerelease)
    {
        if (deps.Length == 0)
            return Array.Empty<ModuleDependencyInstallResult>();

        _logger.Info($"Ensuring build feature tool modules ({deps.Length}): {string.Join(", ", deps.Select(d => d.Name))}");

        var results = _hostedOperations.EnsureDependenciesInstalled(
            dependencies: deps,
            force: force,
            repository: repository,
            credential: credential,
            prerelease: prerelease,
            skipModules: null);

        var failures = results.Where(r => r.Status == ModuleDependencyInstallStatus.Failed).ToArray();
        if (failures.Length > 0)
            throw new InvalidOperationException($"Build feature tool dependency installation failed for {failures.Length} module{(failures.Length == 1 ? string.Empty : "s")}.");

        if (results.Count > 0)
        {
            var installed = results.Count(r => r.Status == ModuleDependencyInstallStatus.Installed);
            var updated = results.Count(r => r.Status == ModuleDependencyInstallStatus.Updated);
            var satisfied = results.Count(r => r.Status == ModuleDependencyInstallStatus.Satisfied);
            var skipped = results.Count(r => r.Status == ModuleDependencyInstallStatus.Skipped);
            _logger.Info($"Build feature tool dependency summary: {installed} installed, {updated} updated, {satisfied} satisfied, {skipped} skipped.");
        }

        return results.ToArray();
    }

    private ModuleDependency[] ResolveFeatureToolDependencies(
        ModulePipelinePlan plan,
        IReadOnlyList<RequiredModuleReference>? packagingRequiredModules)
    {
        var dependencies = new Dictionary<string, ModuleDependency>(StringComparer.OrdinalIgnoreCase);

        if (plan.TestsAfterMerge is { Length: > 0 })
            AddFeatureDependency(dependencies, new ModuleDependency("Pester", minimumVersion: PesterMinimumVersion));

        foreach (var publish in plan.Publishes ?? Array.Empty<ConfigurationPublishSegment>())
        {
            var cfg = publish.Configuration;
            if (cfg is null || !cfg.Enabled || cfg.Destination != PublishDestination.PowerShellGallery)
                continue;

            switch (cfg.Tool)
            {
                case PublishTool.PowerShellGet:
                    AddFeatureDependency(dependencies, new ModuleDependency("PowerShellGet", minimumVersion: PowerShellGetMinimumVersion));
                    break;
                case PublishTool.PSResourceGet:
                    AddFeatureDependency(dependencies, new ModuleDependency("Microsoft.PowerShell.PSResourceGet"));
                    break;
                case PublishTool.Auto:
                    AddRepositoryToolDependencyForAuto(dependencies);
                    break;
            }
        }

        foreach (var artefact in plan.Artefacts ?? Array.Empty<ConfigurationArtefactSegment>())
        {
            if (!ArtefactRequiresRequiredModuleDownloadTool(
                    artefact,
                    packagingRequiredModules ?? plan.RequiredModulesForPackaging))
            {
                continue;
            }

            switch (artefact.Configuration.RequiredModules.Tool ?? ModuleSaveTool.Auto)
            {
                case ModuleSaveTool.PowerShellGet:
                    AddFeatureDependency(dependencies, new ModuleDependency("PowerShellGet", minimumVersion: PowerShellGetMinimumVersion));
                    break;
                case ModuleSaveTool.PSResourceGet:
                    AddFeatureDependency(dependencies, new ModuleDependency("Microsoft.PowerShell.PSResourceGet"));
                    break;
                case ModuleSaveTool.Auto:
                    AddRepositoryToolDependencyForAuto(dependencies);
                    break;
            }
        }

        return dependencies.Values
            .OrderBy(static dependency => dependency.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void EnsureRequiredModuleOnlineResolutionToolInstalledIfNeeded(
        IReadOnlyList<RequiredModuleDraft> requiredModules,
        IReadOnlyList<RequiredModuleDraft> requiredModulesForPackaging,
        bool resolveMissingModulesOnline,
        bool warnIfRequiredModulesOutdated,
        DependencyVersionSourceRepository? publishVersionSource,
        bool force,
        string? repository,
        RepositoryCredential? credential,
        bool prerelease)
    {
        if (!RequiresRequiredModuleOnlineResolutionTool(
                requiredModules,
                requiredModulesForPackaging,
                resolveMissingModulesOnline,
                warnIfRequiredModulesOutdated,
                publishVersionSource))
        {
            return;
        }

        if (IsRepositoryToolAvailable())
            return;

        EnsureFeatureToolDependenciesInstalled(
            new[] { new ModuleDependency("Microsoft.PowerShell.PSResourceGet") },
            force,
            repository,
            credential,
            prerelease);
    }

    private static bool RequiresRequiredModuleOnlineResolutionTool(
        IReadOnlyList<RequiredModuleDraft> requiredModules,
        IReadOnlyList<RequiredModuleDraft> requiredModulesForPackaging,
        bool resolveMissingModulesOnline,
        bool warnIfRequiredModulesOutdated,
        DependencyVersionSourceRepository? publishVersionSource)
    {
        var drafts = (requiredModules ?? Array.Empty<RequiredModuleDraft>())
            .Concat(requiredModulesForPackaging ?? Array.Empty<RequiredModuleDraft>())
            .Where(static draft => draft is not null && !string.IsNullOrWhiteSpace(draft.ModuleName))
            .ToArray();
        if (drafts.Length == 0)
            return false;

        if (warnIfRequiredModulesOutdated ||
            HasRepositoryPreferredOnlineRequiredModules(drafts, publishVersionSource))
        {
            return true;
        }

        return resolveMissingModulesOnline && HasOnlineResolvableAutoRequiredModules(drafts);
    }

    private static bool HasRepositoryPreferredOnlineRequiredModules(
        IEnumerable<RequiredModuleDraft> drafts,
        DependencyVersionSourceRepository? publishVersionSource)
        => HasAutoRequiredModules((drafts ?? Array.Empty<RequiredModuleDraft>())
            .Where(draft => draft is not null &&
                            ResolveDependencyVersionSource(draft.VersionSource, publishVersionSource).PreferOnlineMetadata));

    private static bool ShouldRefreshPrecomputedPlanAfterOnlineRequiredModulePreflight(ModulePipelinePlan plan)
    {
        if (plan is null || plan.BuildSpec.RefreshManifestOnly)
            return false;

        return HasOnlineResolvableRequiredModuleReferences(plan.RequiredModules) ||
               HasOnlineResolvableRequiredModuleReferences(plan.RequiredModulesForPackaging);
    }

    private static bool HasOnlineResolvableRequiredModuleReferences(IEnumerable<RequiredModuleReference> modules)
        => (modules ?? Array.Empty<RequiredModuleReference>())
            .Any(static module => module is not null &&
                                  !string.IsNullOrWhiteSpace(module.ModuleName) &&
                                  (IsAutoVersion(module.ModuleVersion) ||
                                   IsAutoGuid(module.Guid)));

    private static bool IsAutoGuid(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           value!.Trim().Equals("Auto", StringComparison.OrdinalIgnoreCase);

    private bool ArtefactRequiresRequiredModuleDownloadTool(
        ConfigurationArtefactSegment artefact,
        IReadOnlyList<RequiredModuleReference> requiredModulesForPackaging)
    {
        var cfg = artefact.Configuration;
        if (cfg?.Enabled != true || cfg.RequiredModules.Enabled != true)
            return false;

        var source = cfg.RequiredModules.Source ?? RequiredModulesSource.Installed;
        if (source == RequiredModulesSource.Installed)
            return false;

        var excluded = new HashSet<string>(
            (cfg.RequiredModules.ExcludeModuleName ?? Array.Empty<string>())
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Select(static name => name.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var modules = (requiredModulesForPackaging ?? Array.Empty<RequiredModuleReference>())
            .Where(module => module is not null &&
                             !string.IsNullOrWhiteSpace(module.ModuleName) &&
                             !excluded.Contains(module.ModuleName!))
            .ToArray();
        if (modules.Length == 0)
            return false;

        return source == RequiredModulesSource.Download ||
               modules.Any(RequiredModuleNeedsDownload);
    }

    private bool RequiredModuleNeedsDownload(RequiredModuleReference requiredModule)
    {
        if (requiredModule is null || string.IsNullOrWhiteSpace(requiredModule.ModuleName))
            return false;

        var name = requiredModule.ModuleName.Trim();
        var requiredVersion = NormalizeLocatorVersionArgument(requiredModule.RequiredVersion);
        var minimumVersion = NormalizeLocatorVersionArgument(requiredModule.ModuleVersion);
        var maximumVersion = NormalizeLocatorVersionArgument(requiredModule.MaximumVersion);
        var installed = _moduleDependencyMetadataProvider.GetLatestInstalledModules(new[] { name });
        if (!installed.TryGetValue(name, out var metadata) || !HasInstalledModuleMetadata(metadata))
            return true;

        if (IsInstalledModuleAvailable(metadata, requiredVersion, minimumVersion, maximumVersion))
        {
            return false;
        }

        return !HasInstalledRequiredModule(name, requiredVersion, minimumVersion, maximumVersion);
    }

    private static bool HasInstalledModuleMetadata(InstalledModuleMetadata metadata)
        => metadata is not null &&
           (!string.IsNullOrWhiteSpace(metadata.Version) ||
            !string.IsNullOrWhiteSpace(metadata.ModuleBasePath));

    private bool HasInstalledRequiredModule(
        string moduleName,
        string? requiredVersion,
        string? minimumVersion,
        string? maximumVersion)
    {
        if (string.IsNullOrWhiteSpace(moduleName) ||
            (string.IsNullOrWhiteSpace(requiredVersion) &&
             string.IsNullOrWhiteSpace(minimumVersion) &&
             string.IsNullOrWhiteSpace(maximumVersion)))
        {
            return false;
        }

        var script = EmbeddedScripts.Load("Scripts/ModuleLocator/Find-InstalledModule.ps1");
        var args = new[]
        {
            moduleName.Trim(),
            requiredVersion ?? string.Empty,
            minimumVersion ?? string.Empty,
            maximumVersion ?? string.Empty
        };
        var result = RunScript(_powerShellRunner, script, args, TimeSpan.FromMinutes(2));
        if (result.ExitCode != 0)
        {
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdOut)) _logger.Verbose(result.StdOut.Trim());
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdErr)) _logger.Verbose(result.StdErr.Trim());
            return false;
        }

        return SplitLines(result.StdOut)
            .Any(static line => line.StartsWith("PFMODLOC::FOUND::", StringComparison.Ordinal));
    }

    private static string? NormalizeLocatorVersionArgument(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value!.Trim();
        return IsAutoVersion(trimmed) ? null : trimmed;
    }

    private static bool IsInstalledModuleAvailable(
        InstalledModuleMetadata metadata,
        string? requiredVersion,
        string? minimumVersion,
        string? maximumVersion)
    {
        if (metadata is null || string.IsNullOrWhiteSpace(metadata.ModuleBasePath))
            return false;

        if (string.IsNullOrWhiteSpace(requiredVersion) &&
            string.IsNullOrWhiteSpace(minimumVersion) &&
            string.IsNullOrWhiteSpace(maximumVersion))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(metadata.Version) ||
            !TryParseVersion(metadata.Version, out var installedVersion))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(requiredVersion))
            return TryParseVersion(requiredVersion, out var required) &&
                   installedVersion.CompareTo(required) == 0;

        if (!string.IsNullOrWhiteSpace(minimumVersion) &&
            (!TryParseVersion(minimumVersion, out var minimum) ||
             installedVersion.CompareTo(minimum) < 0))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(maximumVersion) &&
            (!TryParseVersion(maximumVersion, out var maximum) ||
             installedVersion.CompareTo(maximum) > 0))
        {
            return false;
        }

        return true;
    }

    private static RequiredModuleDraft[] ReadRequiredModuleDraftsFromManifest(ModulePipelineSpec spec)
    {
        var moduleName = spec?.Build?.Name;
        var sourcePath = spec?.Build?.SourcePath;
        if (string.IsNullOrWhiteSpace(moduleName) || string.IsNullOrWhiteSpace(sourcePath))
            return Array.Empty<RequiredModuleDraft>();

        var manifestPath = Path.Combine(sourcePath, $"{moduleName}.psd1");
        if (!File.Exists(manifestPath))
            return Array.Empty<RequiredModuleDraft>();

        return ModuleManifestValueReader.ReadRequiredModules(manifestPath)
            .Where(static module => module is not null && !string.IsNullOrWhiteSpace(module.ModuleName))
            .Select(static module => new RequiredModuleDraft(
                moduleName: module.ModuleName.Trim(),
                moduleVersion: module.ModuleVersion,
                minimumVersion: null,
                requiredVersion: module.RequiredVersion,
                guid: module.Guid,
                versionSource: ModuleDependencyVersionSource.Auto))
            .ToArray();
    }

    private void EnsureRequiredModuleOnlineResolutionToolInstalledIfNeededForRun(ModulePipelineSpec spec)
    {
        if (spec is null) return;

        var requiredModulesDraft = new List<RequiredModuleDraft>();
        var requiredModulesDraftForPackaging = new List<RequiredModuleDraft>();
        var publishes = new List<ConfigurationPublishSegment>();
        var resolveMissingModulesOnline = false;
        var resolveMissingModulesOnlineSet = false;
        var warnIfRequiredModulesOutdated = false;
        var refreshPsd1Only = false;
        var installMissingModulesForce = false;
        var installMissingModulesPrerelease = false;
        string? installMissingModulesRepository = null;
        RepositoryCredential? installMissingModulesCredential = null;

        foreach (var segment in spec.Segments ?? Array.Empty<IConfigurationSegment>())
        {
            switch (segment)
            {
                case ConfigurationBuildSegment build:
                {
                    var cfg = build.BuildModule;
                    if (cfg.ResolveMissingModulesOnline.HasValue)
                    {
                        resolveMissingModulesOnline = cfg.ResolveMissingModulesOnline.Value;
                        resolveMissingModulesOnlineSet = true;
                    }

                    if (cfg.WarnIfRequiredModulesOutdated.HasValue)
                        warnIfRequiredModulesOutdated = cfg.WarnIfRequiredModulesOutdated.Value;
                    if (cfg.RefreshPSD1Only.HasValue)
                        refreshPsd1Only = cfg.RefreshPSD1Only.Value;
                    if (cfg.InstallMissingModulesForce.HasValue)
                        installMissingModulesForce = cfg.InstallMissingModulesForce.Value;
                    if (cfg.InstallMissingModulesPrerelease.HasValue)
                        installMissingModulesPrerelease = cfg.InstallMissingModulesPrerelease.Value;
                    if (!string.IsNullOrWhiteSpace(cfg.InstallMissingModulesRepository))
                        installMissingModulesRepository = cfg.InstallMissingModulesRepository;
                    if (cfg.InstallMissingModulesCredential is not null)
                        installMissingModulesCredential = cfg.InstallMissingModulesCredential;
                    break;
                }
                case ConfigurationModuleSegment moduleSeg:
                {
                    var cfg = moduleSeg.Configuration;
                    if (moduleSeg.Kind != ModuleDependencyKind.RequiredModule ||
                        cfg is null ||
                        string.IsNullOrWhiteSpace(cfg.ModuleName) ||
                        ModulePipelinePlanningHelpers.ShouldSkipManifestDependencyModule(cfg.ModuleName))
                    {
                        break;
                    }

                    var draft = new RequiredModuleDraft(
                        moduleName: cfg.ModuleName.Trim(),
                        moduleVersion: cfg.ModuleVersion,
                        minimumVersion: cfg.MinimumVersion,
                        requiredVersion: cfg.RequiredVersion,
                        guid: cfg.Guid,
                        versionSource: cfg.VersionSource);
                    requiredModulesDraft.Add(draft);
                    requiredModulesDraftForPackaging.Add(draft);
                    break;
                }
                case ConfigurationPublishSegment publish:
                    publishes.Add(publish);
                    break;
            }
        }

        if (refreshPsd1Only)
            return;

        var manifestRequiredModuleDrafts = ReadRequiredModuleDraftsFromManifest(spec);
        if (manifestRequiredModuleDrafts.Length > 0)
        {
            requiredModulesDraft.AddRange(manifestRequiredModuleDrafts);
            requiredModulesDraftForPackaging.AddRange(manifestRequiredModuleDrafts);
        }

        if (!resolveMissingModulesOnlineSet && HasOnlineResolvableAutoRequiredModules(requiredModulesDraft))
            resolveMissingModulesOnline = true;

        var dependencyVersionSourceRepository = ResolvePublishDependencyVersionSource(
            publishes
                .Where(static publish => publish is not null && publish.Configuration?.Enabled == true)
                .ToArray());

        EnsureRequiredModuleOnlineResolutionToolInstalledIfNeeded(
            requiredModulesDraft,
            requiredModulesDraftForPackaging,
            resolveMissingModulesOnline,
            warnIfRequiredModulesOutdated,
            dependencyVersionSourceRepository,
            installMissingModulesForce,
            installMissingModulesRepository,
            installMissingModulesCredential,
            installMissingModulesPrerelease);
    }

    private void AddRepositoryToolDependencyForAuto(IDictionary<string, ModuleDependency> dependencies)
    {
        if (!IsRepositoryToolAvailable() &&
            !dependencies.ContainsKey("Microsoft.PowerShell.PSResourceGet") &&
            !dependencies.ContainsKey("PowerShellGet"))
        {
            AddFeatureDependency(dependencies, new ModuleDependency("Microsoft.PowerShell.PSResourceGet"));
        }
    }

    private bool IsRepositoryToolAvailable()
    {
        var installed = _moduleDependencyMetadataProvider.GetLatestInstalledModules(new[]
        {
            "Microsoft.PowerShell.PSResourceGet",
            "PowerShellGet"
        });

        return IsInstalledModuleAvailable(installed, "Microsoft.PowerShell.PSResourceGet") ||
               IsInstalledModuleAvailable(installed, "PowerShellGet", PowerShellGetMinimumVersion);
    }

    private static bool IsInstalledModuleAvailable(
        IReadOnlyDictionary<string, InstalledModuleMetadata> installed,
        string moduleName,
        string? minimumVersion = null)
    {
        if (!installed.TryGetValue(moduleName, out var metadata))
            return false;

        if (string.IsNullOrWhiteSpace(metadata.Version) &&
            string.IsNullOrWhiteSpace(metadata.ModuleBasePath))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(minimumVersion))
            return true;

        return !string.IsNullOrWhiteSpace(metadata.Version) &&
               TryParseVersion(metadata.Version, out var installedVersion) &&
               TryParseVersion(minimumVersion, out var requiredVersion) &&
               installedVersion.CompareTo(requiredVersion) >= 0;
    }

    private static bool TryParseVersion(string? value, out Version version)
    {
        var parsed = Version.TryParse(value, out var result);
        version = result ?? new Version(0, 0);
        return parsed;
    }

    private static void AddFeatureDependency(IDictionary<string, ModuleDependency> dependencies, ModuleDependency dependency)
    {
        if (!dependencies.TryGetValue(dependency.Name, out var existing))
        {
            dependencies[dependency.Name] = dependency;
            return;
        }

        if (HasStrongerVersionConstraint(dependency, existing))
            dependencies[dependency.Name] = dependency;
    }

    private static bool HasStrongerVersionConstraint(ModuleDependency candidate, ModuleDependency existing)
    {
        if (!string.IsNullOrWhiteSpace(candidate.RequiredVersion) &&
            !string.Equals(candidate.RequiredVersion, existing.RequiredVersion, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(existing.RequiredVersion) &&
            string.IsNullOrWhiteSpace(existing.MinimumVersion) &&
            !string.IsNullOrWhiteSpace(candidate.MinimumVersion))
        {
            return true;
        }

        return false;
    }

}
