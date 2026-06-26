using System;
using System.Collections.Generic;
using System.Linq;
using PowerForge;

namespace PSPublishModule;

internal static class ModuleStateMaintenanceEvidenceMapper
{
    internal static ModuleStateInstalledModule[] ToObservedModules(
        IEnumerable<ModuleStateDeliveryExecutionResult>? executionResults,
        ModuleStateInventoryResult? postApplyInventory,
        string? sourceRepository)
    {
        var modules = new List<ModuleStateInstalledModule>();
        foreach (var executionResult in executionResults ?? Array.Empty<ModuleStateDeliveryExecutionResult>())
        {
            var executionRepository = string.IsNullOrWhiteSpace(executionResult.RepositoryName)
                ? sourceRepository
                : executionResult.RepositoryName;
            foreach (var dependency in executionResult.DependencyResults ?? Array.Empty<ModuleStateDependencyResult>())
            {
                if (string.IsNullOrWhiteSpace(dependency.Name) ||
                    string.IsNullOrWhiteSpace(dependency.ResolvedVersion))
                {
                    continue;
                }

                modules.Add(new ModuleStateInstalledModule(
                    dependency.Name,
                    dependency.ResolvedVersion!,
                    sourceRepository: executionRepository));
            }
        }

        foreach (var module in postApplyInventory?.InstalledModules ?? Array.Empty<ModuleStateInstalledModuleResult>())
        {
            if (string.IsNullOrWhiteSpace(module.Name) ||
                string.IsNullOrWhiteSpace(module.Version))
            {
                continue;
            }

            modules.Add(new ModuleStateInstalledModule(
                module.Name,
                module.Version,
                module.PowerShellEdition,
                module.Scope,
                module.Path,
                module.SourceRepository ?? sourceRepository,
                module.IsLoaded));
        }

        return modules.ToArray();
    }
}
