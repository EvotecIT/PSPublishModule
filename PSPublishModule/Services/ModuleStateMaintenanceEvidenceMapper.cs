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
        foreach (var dependency in (executionResults ?? Array.Empty<ModuleStateDeliveryExecutionResult>())
            .SelectMany(static result => result.DependencyResults ?? Array.Empty<ModuleStateDependencyResult>()))
        {
            if (string.IsNullOrWhiteSpace(dependency.Name) ||
                string.IsNullOrWhiteSpace(dependency.ResolvedVersion))
            {
                continue;
            }

            modules.Add(new ModuleStateInstalledModule(
                dependency.Name,
                dependency.ResolvedVersion!,
                sourceRepository: sourceRepository));
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
