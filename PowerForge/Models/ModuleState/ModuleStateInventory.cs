using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

internal sealed class ModuleStateInventory
{
    internal ModuleStateInventory(IEnumerable<ModuleStateInstalledModule>? installedModules)
        => InstalledModules = (installedModules ?? Array.Empty<ModuleStateInstalledModule>()).Where(static m => m is not null).ToArray();

    internal ModuleStateInstalledModule[] InstalledModules { get; }
}
