using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
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

}
