using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

internal sealed class ModuleStateFamilyCoherenceAnalyzer
{
    internal ModuleStateConflictFinding[] Analyze(
        ModuleStateInventory inventory,
        IEnumerable<ModuleStateFamilyPolicy> familyPolicies)
    {
        if (inventory is null)
            throw new ArgumentNullException(nameof(inventory));

        var findings = new List<ModuleStateConflictFinding>();
        foreach (var policy in familyPolicies ?? Array.Empty<ModuleStateFamilyPolicy>())
        {
            if (policy.CoherenceRule != ModuleStateFamilyCoherenceRule.SameVersion)
                continue;

            var installedFamilyModules = inventory.InstalledModules
                .Where(module => policy.Modules.Contains(module.Name, StringComparer.OrdinalIgnoreCase))
                .ToArray();

            if (installedFamilyModules.Length <= 1)
                continue;

            var versions = installedFamilyModules
                .Select(static module => ModuleStateVersion.NormalizeOrOriginal(module.Version))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static version => version, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (versions.Length <= 1)
                continue;

            findings.Add(new ModuleStateConflictFinding(
                ModuleStateConflictSeverity.Error,
                "ModuleState.FamilyVersionMismatch",
                $"Module family '{policy.Name}' has installed modules from {versions.Length} versions: {string.Join(", ", versions)}.",
                policy.Name,
                installedFamilyModules.Select(static module => module.Name).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static name => name, StringComparer.OrdinalIgnoreCase).ToArray(),
                versions));
        }

        return findings.ToArray();
    }
}
