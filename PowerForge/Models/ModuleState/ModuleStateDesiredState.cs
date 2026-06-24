using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

internal sealed class ModuleStateDesiredState
{
    internal ModuleStateDesiredState(
        IEnumerable<ModuleStateDesiredModule>? modules,
        IEnumerable<ModuleStateFamilyPolicy>? familyPolicies)
    {
        Modules = (modules ?? Array.Empty<ModuleStateDesiredModule>()).Where(static module => module is not null).ToArray();
        FamilyPolicies = (familyPolicies ?? Array.Empty<ModuleStateFamilyPolicy>()).Where(static policy => policy is not null).ToArray();
    }

    internal ModuleStateDesiredModule[] Modules { get; }

    internal ModuleStateFamilyPolicy[] FamilyPolicies { get; }
}
