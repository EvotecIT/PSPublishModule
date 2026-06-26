using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

internal sealed class ModuleStateConflictAnalyzer
{
    internal ModuleStateConflictFinding[] Analyze(ModuleStateInventory inventory, IEnumerable<ModuleStateDesiredModule> desiredModules)
    {
        if (inventory is null)
            throw new ArgumentNullException(nameof(inventory));

        var findings = new List<ModuleStateConflictFinding>();
        foreach (var desiredModule in desiredModules ?? Array.Empty<ModuleStateDesiredModule>())
        {
            var installedModules = inventory.InstalledModules
                .Where(module => string.Equals(module.Name, desiredModule.Name, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var policy = ModuleStateVersionPolicy.Parse(desiredModule.VersionPolicy);

            if (installedModules.Length == 0)
                continue;

            AddScopeAmbiguityFinding(findings, desiredModule, installedModules);
            AddDesiredScopeFinding(findings, desiredModule, installedModules);
            AddSourcePreferenceFinding(findings, desiredModule, installedModules, policy);
            AddDowngradeBlockFinding(findings, desiredModule, installedModules, policy);
            AddLoadedModuleFinding(findings, desiredModule, installedModules, policy);
        }

        return findings.ToArray();
    }

    private static void AddScopeAmbiguityFinding(
        List<ModuleStateConflictFinding> findings,
        ModuleStateDesiredModule desiredModule,
        ModuleStateInstalledModule[] installedModules)
    {
        if (!string.IsNullOrWhiteSpace(desiredModule.Scope))
            return;

        var scopes = installedModules
            .Select(static module => module.Scope)
            .Where(static scope => !string.IsNullOrWhiteSpace(scope))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static scope => scope, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var versions = installedModules
            .Select(static module => module.Version)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static version => ModuleStateVersion.TryParse(version, out var parsed) ? parsed : default)
            .ToArray();

        if (scopes.Length <= 1 || versions.Length <= 1)
            return;

        findings.Add(new ModuleStateConflictFinding(
            ModuleStateConflictSeverity.Warning,
            "ModuleState.ScopeAmbiguity",
            $"Module '{desiredModule.Name}' is installed in multiple scopes with different versions. Effective import precedence should be reviewed before relying on this module.",
            string.Empty,
            new[] { desiredModule.Name },
            versions));
    }

    private static void AddDesiredScopeFinding(
        List<ModuleStateConflictFinding> findings,
        ModuleStateDesiredModule desiredModule,
        ModuleStateInstalledModule[] installedModules)
    {
        if (string.IsNullOrWhiteSpace(desiredModule.Scope))
            return;
        if (installedModules.Any(module => string.Equals(module.Scope, desiredModule.Scope, StringComparison.OrdinalIgnoreCase)))
            return;

        var scopes = installedModules
            .Select(static module => string.IsNullOrWhiteSpace(module.Scope) ? "<unknown>" : module.Scope!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static scope => scope, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        findings.Add(new ModuleStateConflictFinding(
            ModuleStateConflictSeverity.Warning,
            "ModuleState.ScopeMismatch",
            $"Module '{desiredModule.Name}' is required in scope '{desiredModule.Scope}', but installed copies are in: {string.Join(", ", scopes)}.",
            string.Empty,
            new[] { desiredModule.Name },
            installedModules.Select(static module => module.Version).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()));
    }

    private static void AddSourcePreferenceFinding(
        List<ModuleStateConflictFinding> findings,
        ModuleStateDesiredModule desiredModule,
        ModuleStateInstalledModule[] installedModules,
        ModuleStateVersionPolicy policy)
    {
        if (desiredModule.AllowedSources.Length == 0)
            return;

        var selectedModule = SelectInstalledModule(
            installedModules.Where(module => policy.IsSatisfiedBy(module.Version)),
            desiredModule.Scope);
        if (selectedModule is null)
            return;

        if (string.IsNullOrWhiteSpace(selectedModule.SourceRepository))
        {
            findings.Add(new ModuleStateConflictFinding(
                ModuleStateConflictSeverity.Warning,
                "ModuleState.SourceUnknown",
                $"Module '{desiredModule.Name}' satisfies the version policy, but its source repository is unknown. Desired state allows: {string.Join(", ", desiredModule.AllowedSources)}.",
                string.Empty,
                new[] { desiredModule.Name },
                new[] { selectedModule.Version }));
            return;
        }

        if (desiredModule.AllowedSources.Contains(selectedModule.SourceRepository, StringComparer.OrdinalIgnoreCase))
            return;

        findings.Add(new ModuleStateConflictFinding(
            ModuleStateConflictSeverity.Error,
            "ModuleState.SourcePreferenceMismatch",
            $"Module '{desiredModule.Name}' was selected from source '{selectedModule.SourceRepository}', but desired state allows: {string.Join(", ", desiredModule.AllowedSources)}.",
            string.Empty,
            new[] { desiredModule.Name },
            new[] { selectedModule.Version }));
    }

    private static void AddLoadedModuleFinding(
        List<ModuleStateConflictFinding> findings,
        ModuleStateDesiredModule desiredModule,
        ModuleStateInstalledModule[] installedModules,
        ModuleStateVersionPolicy policy)
    {
        foreach (var loadedModule in installedModules.Where(static module => module.IsLoaded))
        {
            if (policy.IsSatisfiedBy(loadedModule.Version))
                continue;

            findings.Add(new ModuleStateConflictFinding(
                ModuleStateConflictSeverity.Error,
                "ModuleState.LoadedVersionMismatch",
                $"Module '{desiredModule.Name}' is already loaded at version {loadedModule.Version}, which does not satisfy desired policy '{desiredModule.VersionPolicy}'. Start a fresh process or unload/isolate before applying the plan.",
                string.Empty,
                new[] { desiredModule.Name },
                new[] { loadedModule.Version }));
        }
    }

    private static void AddDowngradeBlockFinding(
        List<ModuleStateConflictFinding> findings,
        ModuleStateDesiredModule desiredModule,
        ModuleStateInstalledModule[] installedModules,
        ModuleStateVersionPolicy policy)
    {
        var selectedModule = SelectInstalledModule(installedModules, desiredModule.Scope);
        if (selectedModule is null ||
            policy.IsSatisfiedBy(selectedModule.Version) ||
            !ModuleStateVersion.TryParse(selectedModule.Version, out var installedVersion))
        {
            return;
        }

        if (policy.ExactVersion.HasValue && installedVersion.CompareTo(policy.ExactVersion.Value) > 0)
        {
            findings.Add(CreateDowngradeFinding(desiredModule, selectedModule));
            return;
        }

        if (policy.MaximumVersion.HasValue)
        {
            var maximumComparison = installedVersion.CompareTo(policy.MaximumVersion.Value);
            if (maximumComparison > 0 || (maximumComparison == 0 && !policy.MaximumInclusive))
                findings.Add(CreateDowngradeFinding(desiredModule, selectedModule));
        }
    }

    private static ModuleStateConflictFinding CreateDowngradeFinding(
        ModuleStateDesiredModule desiredModule,
        ModuleStateInstalledModule installedModule)
        => new(
            ModuleStateConflictSeverity.Error,
            "ModuleState.DowngradeRequiresCleanup",
            $"Module '{desiredModule.Name}' is installed at version {installedModule.Version}, which is above desired policy '{desiredModule.VersionPolicy}'. Remove or isolate the higher version before applying the downgrade policy.",
            string.Empty,
            new[] { desiredModule.Name },
            new[] { installedModule.Version });

    private static ModuleStateInstalledModule? SelectInstalledModule(IEnumerable<ModuleStateInstalledModule> installedModules, string? desiredScope)
    {
        var candidates = installedModules
            .Where(module => string.IsNullOrWhiteSpace(desiredScope)
                || string.Equals(module.Scope, desiredScope, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return candidates
            .OrderByDescending(static module => module.IsLoaded)
            .ThenByDescending(static module => module.IsEffectiveImportCandidate)
            .ThenByDescending(static module => ModuleStateVersion.TryParse(module.Version, out var version) ? version : default)
            .FirstOrDefault();
    }
}
