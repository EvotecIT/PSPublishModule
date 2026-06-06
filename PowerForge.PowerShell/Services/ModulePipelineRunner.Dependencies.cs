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

    private ModuleDependencyInstallResult[] EnsureFeatureToolDependenciesInstalled(ModulePipelinePlan plan)
    {
        if (plan is null) return Array.Empty<ModuleDependencyInstallResult>();

        var deps = ResolveFeatureToolDependencies(plan);
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

    private ModuleDependency[] ResolveFeatureToolDependencies(ModulePipelinePlan plan)
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
            if (!ArtefactRequiresRequiredModuleDownloadTool(artefact, plan.RequiredModulesForPackaging))
                continue;

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

        if (publishVersionSource is not null || warnIfRequiredModulesOutdated)
            return true;

        return resolveMissingModulesOnline && HasOnlineResolvableAutoRequiredModules(drafts);
    }

    private static bool ArtefactRequiresRequiredModuleDownloadTool(
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

        return (requiredModulesForPackaging ?? Array.Empty<RequiredModuleReference>())
            .Any(module => module is not null &&
                           !string.IsNullOrWhiteSpace(module.ModuleName) &&
                           !excluded.Contains(module.ModuleName!));
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
               IsInstalledModuleAvailable(installed, "PowerShellGet");
    }

    private static bool IsInstalledModuleAvailable(
        IReadOnlyDictionary<string, InstalledModuleMetadata> installed,
        string moduleName)
    {
        return installed.TryGetValue(moduleName, out var metadata) &&
               (!string.IsNullOrWhiteSpace(metadata.Version) ||
                !string.IsNullOrWhiteSpace(metadata.ModuleBasePath));
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
