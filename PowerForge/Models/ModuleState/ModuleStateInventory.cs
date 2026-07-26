using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

internal sealed class ModuleStateInventory
{
    internal ModuleStateInventory(IEnumerable<ModuleStateInstalledModule>? installedModules)
        : this(installedModules, null, null)
    {
    }

    internal ModuleStateInventory(
        IEnumerable<ModuleStateInstalledModule>? installedModules,
        IEnumerable<ModuleStateModulePath>? modulePaths,
        IEnumerable<ModuleStateInventoryDiagnostic>? diagnostics)
    {
        InstalledModules = (installedModules ?? Array.Empty<ModuleStateInstalledModule>()).Where(static m => m is not null).ToArray();
        ModulePaths = (modulePaths ?? Array.Empty<ModuleStateModulePath>()).Where(static path => path is not null).ToArray();
        Diagnostics = (diagnostics ?? Array.Empty<ModuleStateInventoryDiagnostic>()).Where(static diagnostic => diagnostic is not null).ToArray();
    }

    internal ModuleStateInstalledModule[] InstalledModules { get; }

    internal ModuleStateModulePath[] ModulePaths { get; }

    internal ModuleStateInventoryDiagnostic[] Diagnostics { get; }
}
