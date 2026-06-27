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
                .Where(module => string.IsNullOrWhiteSpace(desiredModule.TargetPath) || IsUnderTargetPath(module.Path, desiredModule.TargetPath!))
                .ToArray();
            var policy = ModuleStateVersionPolicy.Parse(desiredModule.VersionPolicy);

            if (installedModules.Length == 0)
                continue;

            AddScopeAmbiguityFinding(findings, desiredModule, installedModules);
            AddSideBySideVersionFindings(findings, desiredModule, installedModules);
            AddDesiredScopeFinding(findings, desiredModule, installedModules);
            AddScopeShadowingFinding(findings, desiredModule, installedModules, policy);
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

    private static void AddSideBySideVersionFindings(
        List<ModuleStateConflictFinding> findings,
        ModuleStateDesiredModule desiredModule,
        ModuleStateInstalledModule[] installedModules)
    {
        foreach (var group in installedModules
                     .GroupBy(static module => NormalizeScope(module.Scope), StringComparer.OrdinalIgnoreCase)
                     .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var versions = SortVersions(group
                .Select(static module => module.Version)
                .Distinct(StringComparer.OrdinalIgnoreCase));
            if (versions.Length <= 1)
                continue;

            findings.Add(new ModuleStateConflictFinding(
                ModuleStateConflictSeverity.Warning,
                "ModuleState.SideBySideVersions",
                $"Module '{desiredModule.Name}' has multiple installed versions in scope '{group.Key}': {string.Join(", ", versions)}. Review loaded modules and cleanup policy before assuming a single effective version.",
                string.Empty,
                new[] { desiredModule.Name },
                versions,
                group.Key));
        }
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

    private static void AddScopeShadowingFinding(
        List<ModuleStateConflictFinding> findings,
        ModuleStateDesiredModule desiredModule,
        ModuleStateInstalledModule[] installedModules,
        ModuleStateVersionPolicy policy)
    {
        if (string.IsNullOrWhiteSpace(desiredModule.Scope))
            return;

        var desiredScopeModule = SelectInstalledModule(installedModules, desiredModule.Scope);
        if (desiredScopeModule is null)
            return;

        foreach (var effectiveModule in installedModules
                     .Where(module => module.IsEffectiveImportCandidate)
                     .Where(module => !string.Equals(module.Scope, desiredModule.Scope, StringComparison.OrdinalIgnoreCase)))
        {
            var effectiveSatisfiesPolicy = policy.IsSatisfiedBy(effectiveModule.Version);
            if (string.Equals(effectiveModule.Version, desiredScopeModule.Version, StringComparison.OrdinalIgnoreCase) &&
                effectiveSatisfiesPolicy)
            {
                continue;
            }

            var shadowScope = NormalizeScope(effectiveModule.Scope);
            findings.Add(new ModuleStateConflictFinding(
                effectiveSatisfiesPolicy ? ModuleStateConflictSeverity.Warning : ModuleStateConflictSeverity.Error,
                "ModuleState.ScopeShadowing",
                $"Module '{desiredModule.Name}' is desired in scope '{desiredModule.Scope}' at version {desiredScopeModule.Version}, but effective import precedence points to scope '{shadowScope}' version {effectiveModule.Version}. Remove, update, or isolate the shadowing copy before relying on the scoped module.",
                string.Empty,
                new[] { desiredModule.Name },
                SortVersions(new[] { desiredScopeModule.Version, effectiveModule.Version }),
                shadowScope));
        }
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
                new[] { selectedModule.Version },
                selectedModule.Scope,
                selectedModule.SourceRepository));
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
            new[] { selectedModule.Version },
            selectedModule.Scope,
            selectedModule.SourceRepository));
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
        var selectedModule = SelectInstalledModuleForDowngrade(installedModules, desiredModule.Scope);
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

    private static ModuleStateInstalledModule? SelectInstalledModuleForDowngrade(IEnumerable<ModuleStateInstalledModule> installedModules, string? desiredScope)
    {
        var candidates = installedModules
            .Where(module => string.IsNullOrWhiteSpace(desiredScope)
                || string.Equals(module.Scope, desiredScope, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return candidates
            .OrderByDescending(static module => module.IsEffectiveImportCandidate)
            .ThenByDescending(static module => ModuleStateVersion.TryParse(module.Version, out var version) ? version : default)
            .ThenByDescending(static module => module.IsLoaded)
            .FirstOrDefault();
    }

    private static bool IsUnderTargetPath(string? modulePath, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(modulePath))
            return false;

        var normalizedModulePath = NormalizePath(modulePath!);
        var normalizedTargetPath = NormalizePath(targetPath);
        return string.Equals(normalizedModulePath, normalizedTargetPath, StringComparison.OrdinalIgnoreCase) ||
               normalizedModulePath.StartsWith(normalizedTargetPath + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
        => path.Trim()
            .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
            .Replace('\\', '/');

    private static string NormalizeScope(string? scope)
        => string.IsNullOrWhiteSpace(scope) ? "<unknown>" : scope!.Trim();

    private static string[] SortVersions(IEnumerable<string> versions)
        => versions
            .OrderBy(static version => ModuleStateVersion.TryParse(version, out var parsed) ? parsed : default)
            .ToArray();
}
