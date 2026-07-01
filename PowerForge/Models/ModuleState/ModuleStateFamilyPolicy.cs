using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

internal sealed class ModuleStateFamilyPolicy
{
    internal ModuleStateFamilyPolicy(
        string name,
        IEnumerable<string> modules,
        ModuleStateFamilyCoherenceRule coherenceRule = ModuleStateFamilyCoherenceRule.SameVersion,
        IEnumerable<string>? modulePrefixes = null)
    {
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Module family name is required.", nameof(name))
            : name.Trim();
        Modules = (modules ?? Array.Empty<string>())
            .Where(static module => !string.IsNullOrWhiteSpace(module))
            .Select(static module => module.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        ModulePrefixes = (modulePrefixes ?? Array.Empty<string>())
            .Where(static prefix => !string.IsNullOrWhiteSpace(prefix))
            .Select(static prefix => prefix.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        CoherenceRule = coherenceRule;

        if (Modules.Length == 0 && ModulePrefixes.Length == 0)
            throw new ArgumentException("At least one module or module prefix is required for a module family policy.", nameof(modules));
    }

    internal string Name { get; }

    internal string[] Modules { get; }

    internal string[] ModulePrefixes { get; }

    internal ModuleStateFamilyCoherenceRule CoherenceRule { get; }

    internal bool Matches(string moduleName)
        => !string.IsNullOrWhiteSpace(moduleName) &&
           (Modules.Contains(moduleName, StringComparer.OrdinalIgnoreCase) ||
            ModulePrefixes.Any(prefix => moduleName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
}

internal enum ModuleStateFamilyCoherenceRule
{
    ObserveOnly,
    SameVersion
}
