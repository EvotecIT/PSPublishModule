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
            ModulePaths = modulePaths is { Length: > 0 }
                ? modulePaths
                : inventory.ModulePaths.Select(static path => path.Path).ToArray(),
            ScannedPaths = inventory.ModulePaths.Select(static path => new ModuleStateInventoryPathResult
            {
                Path = path.Path,
                PowerShellEdition = path.PowerShellEdition,
                Scope = path.Scope,
                ProfileName = path.ProfileName,
                IsRequired = path.IsRequired
            }).ToArray(),
            Diagnostics = inventory.Diagnostics.Select(static diagnostic => new ModuleStateInventoryDiagnosticResult
            {
                Severity = diagnostic.Severity.ToString(),
                Code = diagnostic.Code,
                Message = diagnostic.Message,
                Path = diagnostic.Path,
                PowerShellEdition = diagnostic.PowerShellEdition,
                Scope = diagnostic.Scope,
                ProfileName = diagnostic.ProfileName
            }).ToArray(),
            InstalledModules = inventory.InstalledModules.Select(static module => new ModuleStateInstalledModuleResult
            {
                Name = module.Name,
                Version = module.Version,
                PowerShellEdition = module.PowerShellEdition,
                Scope = module.Scope,
                Path = module.Path,
                ModuleRoot = module.ModuleRoot,
                ProfileName = module.ProfileName,
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

        var modulePaths = result.ScannedPaths is { Length: > 0 }
            ? result.ScannedPaths.Select(static path => new ModuleStateModulePath(
                path.Path,
                path.PowerShellEdition,
                path.Scope,
                path.ProfileName,
                path.IsRequired))
            : (result.ModulePaths ?? Array.Empty<string>()).Select(static path => new ModuleStateModulePath(path));
        var diagnostics = (result.Diagnostics ?? Array.Empty<ModuleStateInventoryDiagnosticResult>())
            .Select(static diagnostic => new ModuleStateInventoryDiagnostic(
                ParseSeverity(diagnostic.Severity),
                diagnostic.Code,
                diagnostic.Message,
                diagnostic.Path,
                diagnostic.PowerShellEdition,
                diagnostic.Scope,
                diagnostic.ProfileName));
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
                module.ExportedCommands,
                module.ModuleRoot,
                module.ProfileName)),
            modulePaths,
            diagnostics);
    }

    private static ModuleStateConflictSeverity ParseSeverity(string? severity)
        => Enum.TryParse<ModuleStateConflictSeverity>(severity, ignoreCase: true, out var parsed)
            ? parsed
            : ModuleStateConflictSeverity.Warning;
}
