using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

internal sealed class ModuleStateInventoryRequest
{
    internal ModuleStateInventoryRequest(IEnumerable<ModuleStateModulePath>? modulePaths)
        => ModulePaths = (modulePaths ?? Array.Empty<ModuleStateModulePath>()).Where(static path => path is not null).ToArray();

    internal ModuleStateModulePath[] ModulePaths { get; }
}
