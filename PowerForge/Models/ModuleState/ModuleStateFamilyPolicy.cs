using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

internal sealed class ModuleStateFamilyPolicy
{
    internal ModuleStateFamilyPolicy(
        string name,
        IEnumerable<string> modules,
        ModuleStateFamilyCoherenceRule coherenceRule = ModuleStateFamilyCoherenceRule.SameVersion)
    {
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Module family name is required.", nameof(name))
            : name.Trim();
        Modules = (modules ?? Array.Empty<string>())
            .Where(static module => !string.IsNullOrWhiteSpace(module))
            .Select(static module => module.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        CoherenceRule = coherenceRule;

        if (Modules.Length == 0)
            throw new ArgumentException("At least one module is required for a module family policy.", nameof(modules));
    }

    internal string Name { get; }

    internal string[] Modules { get; }

    internal ModuleStateFamilyCoherenceRule CoherenceRule { get; }
}

internal enum ModuleStateFamilyCoherenceRule
{
    ObserveOnly,
    SameVersion
}
