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

            var installedFamilyEstates = inventory.InstalledModules
                .Where(module => policy.Matches(module.Name))
                .GroupBy(
                    static module => ModuleStatePathIdentity.CreateEstateKey(
                        module.PowerShellEdition,
                        module.Scope,
                        ModuleStatePathIdentity.ResolveModuleRoot(module),
                        module.ProfileName),
                    StringComparer.Ordinal);

            foreach (var estate in installedFamilyEstates)
            {
                var installedFamilyModules = estate
                    .GroupBy(static module => module.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(static group => SelectInstalledModule(group))
                    .Where(static module => module is not null)
                    .Cast<ModuleStateInstalledModule>()
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

                var placement = installedFamilyModules[0];
                findings.Add(new ModuleStateConflictFinding(
                    ModuleStateConflictSeverity.Error,
                    "ModuleState.FamilyVersionMismatch",
                    $"Module family '{policy.Name}' has installed modules from {versions.Length} versions in module root '{ModuleStatePathIdentity.ResolveModuleRoot(placement) ?? "<unknown>"}': {string.Join(", ", versions)}.",
                    policy.Name,
                    installedFamilyModules.Select(static module => module.Name).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static name => name, StringComparer.OrdinalIgnoreCase).ToArray(),
                    versions,
                    placement.Scope,
                    path: ModuleStatePathIdentity.ResolveModuleRoot(placement),
                    powerShellEdition: placement.PowerShellEdition,
                    profileName: placement.ProfileName));
            }
        }

        return findings.ToArray();
    }

    private static ModuleStateInstalledModule? SelectInstalledModule(IEnumerable<ModuleStateInstalledModule> installedModules)
        => installedModules
            .OrderByDescending(static module => module.IsLoaded)
            .ThenByDescending(static module => module.IsEffectiveImportCandidate)
            .ThenByDescending(static module => ModuleStateVersion.TryParse(module.Version, out var version) ? version : default)
            .FirstOrDefault();
}
