using System;
using System.Linq;
using PowerForge;

namespace PSPublishModule;

internal static class ModuleStateInventoryResultMapper
{
    internal static ModuleStateInventoryResult ToCmdletResult(
        ModuleStateInventory inventory,
        string source,
        string[] modulePaths)
    {
        if (inventory is null)
            throw new ArgumentNullException(nameof(inventory));

        return new ModuleStateInventoryResult
        {
            Source = source,
            ModulePaths = modulePaths ?? Array.Empty<string>(),
            InstalledModules = inventory.InstalledModules.Select(static module => new ModuleStateInstalledModuleResult
            {
                Name = module.Name,
                Version = module.Version,
                PowerShellEdition = module.PowerShellEdition,
                Scope = module.Scope,
                Path = module.Path,
                SourceRepository = module.SourceRepository,
                IsLoaded = module.IsLoaded,
                IsEffectiveImportCandidate = module.IsEffectiveImportCandidate,
                ExportedCommands = module.ExportedCommands
            }).ToArray()
        };
    }

    internal static ModuleStateInventory ToCoreInventory(ModuleStateInventoryResult result)
    {
        if (result is null)
            throw new ArgumentNullException(nameof(result));

        return new ModuleStateInventory((result.InstalledModules ?? Array.Empty<ModuleStateInstalledModuleResult>())
            .Select(static module => new ModuleStateInstalledModule(
                module.Name,
                module.Version,
                module.PowerShellEdition,
                module.Scope,
                module.Path,
                module.SourceRepository,
                module.IsLoaded,
                module.IsEffectiveImportCandidate,
                module.ExportedCommands)));
    }
}
