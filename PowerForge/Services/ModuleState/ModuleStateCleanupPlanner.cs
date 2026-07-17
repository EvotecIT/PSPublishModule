using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

internal sealed class ModuleStateCleanupPlanner
{
    internal ModuleStateCleanupPlan CreateCleanupPlan(ModuleStatePlanRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        if (request.CleanupMode == ModuleStateCleanupMode.None)
            return ModuleStateCleanupPlan.Empty;

        if (request.CleanupMode != ModuleStateCleanupMode.OldVersions)
            throw new NotSupportedException($"ModuleState cleanup mode '{request.CleanupMode}' is not supported.");

        var actions = new List<ModuleStatePlanAction>();
        var findings = new List<ModuleStateConflictFinding>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var moduleGroup in GetManagedModuleGroups(request))
        {
            var installedModules = moduleGroup.ToArray();
            var keepModules = ResolveKeepModules(request, installedModules);
            if (keepModules.Count == 0)
                continue;

            foreach (var installedModule in installedModules)
            {
                if (keepModules.Contains(CreateKeepKey(installedModule)))
                    continue;

                if (installedModule.IsLoaded)
                {
                    findings.Add(CreateLoadedCleanupFinding(installedModule));
                    continue;
                }

                var key = string.Join("|", installedModule.Name, installedModule.Version, installedModule.Path ?? string.Empty);
                if (!seen.Add(key))
                    continue;

                actions.Add(new ModuleStatePlanAction(
                    ModuleStatePlanActionKind.Remove,
                    installedModule.Name,
                    installedModule.Version,
                    "cleanup:old-versions",
                    "Installed module version is older than the managed version selected by ModuleState cleanup.",
                    targetScope: installedModule.Scope,
                    targetPath: installedModule.Path,
                    targetModuleRoot: ModuleStatePathIdentity.ResolveModuleRoot(installedModule),
                    targetPowerShellEdition: installedModule.PowerShellEdition,
                    targetProfileName: installedModule.ProfileName));
            }
        }

        return new ModuleStateCleanupPlan(actions.ToArray(), findings.ToArray());
    }

    private static IEnumerable<IGrouping<string, ModuleStateInstalledModule>> GetManagedModuleGroups(ModuleStatePlanRequest request)
    {
        var selectedModules = new List<ModuleStateInstalledModule>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var desiredModule in request.DesiredModules)
        {
            var targetRoot = desiredModule.TargetPath ?? desiredModule.ModuleRoot;
            AddCleanupCandidates(
                request.Inventory.InstalledModules.Where(module =>
                    string.Equals(module.Name, desiredModule.Name, StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrWhiteSpace(desiredModule.Scope) ||
                     string.Equals(module.Scope, desiredModule.Scope, StringComparison.OrdinalIgnoreCase)) &&
                    (string.IsNullOrWhiteSpace(targetRoot) || ModuleStatePathIdentity.IsSameOrChild(module.Path, targetRoot)) &&
                    (string.IsNullOrWhiteSpace(desiredModule.PowerShellEdition) ||
                     string.Equals(module.PowerShellEdition, desiredModule.PowerShellEdition, StringComparison.OrdinalIgnoreCase)) &&
                    (string.IsNullOrWhiteSpace(desiredModule.ProfileName) ||
                     string.Equals(module.ProfileName, desiredModule.ProfileName, StringComparison.OrdinalIgnoreCase))),
                selectedModules,
                seen);
        }

        foreach (var receiptModule in request.MaintenanceReceipts.SelectMany(static receipt => receipt.Modules))
        {
            AddCleanupCandidates(
                request.Inventory.InstalledModules.Where(module =>
                    string.Equals(module.Name, receiptModule.Name, StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrWhiteSpace(receiptModule.Scope) ||
                     string.Equals(module.Scope, receiptModule.Scope, StringComparison.OrdinalIgnoreCase)) &&
                    (string.IsNullOrWhiteSpace(receiptModule.ModuleRoot) ||
                     ModuleStatePathIdentity.Equals(module.ModuleRoot, receiptModule.ModuleRoot)) &&
                    (string.IsNullOrWhiteSpace(receiptModule.PowerShellEdition) ||
                     string.Equals(module.PowerShellEdition, receiptModule.PowerShellEdition, StringComparison.OrdinalIgnoreCase)) &&
                    (string.IsNullOrWhiteSpace(receiptModule.ProfileName) ||
                     string.Equals(module.ProfileName, receiptModule.ProfileName, StringComparison.OrdinalIgnoreCase))),
                selectedModules,
                seen);
        }

        return selectedModules.GroupBy(
            static module => ModuleStatePathIdentity.CreatePlacementKey(
                module.Name,
                module.PowerShellEdition,
                module.Scope,
                ModuleStatePathIdentity.ResolveModuleRoot(module)),
            StringComparer.Ordinal);
    }

    private static HashSet<string> ResolveKeepModules(
        ModuleStatePlanRequest request,
        ModuleStateInstalledModule[] installedModules)
    {
        var keepModules = new HashSet<string>(StringComparer.Ordinal);
        var moduleName = installedModules.FirstOrDefault()?.Name;
        if (string.IsNullOrWhiteSpace(moduleName))
            return keepModules;

        foreach (var receiptModule in request.MaintenanceReceipts.SelectMany(static receipt => receipt.Modules))
        {
            if (!string.Equals(receiptModule.Name, moduleName, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var installedModule in installedModules.Where(module =>
                         VersionsEquivalent(module.Version, receiptModule.Version) &&
                         (string.IsNullOrWhiteSpace(receiptModule.Scope) ||
                          string.Equals(module.Scope, receiptModule.Scope, StringComparison.OrdinalIgnoreCase)) &&
                         (string.IsNullOrWhiteSpace(receiptModule.ModuleRoot) ||
                          ModuleStatePathIdentity.Equals(module.ModuleRoot, receiptModule.ModuleRoot)) &&
                         (string.IsNullOrWhiteSpace(receiptModule.PowerShellEdition) ||
                          string.Equals(module.PowerShellEdition, receiptModule.PowerShellEdition, StringComparison.OrdinalIgnoreCase)) &&
                         (string.IsNullOrWhiteSpace(receiptModule.ProfileName) ||
                          string.Equals(module.ProfileName, receiptModule.ProfileName, StringComparison.OrdinalIgnoreCase))))
            {
                keepModules.Add(CreateKeepKey(installedModule));
            }
        }

        foreach (var desiredModule in request.DesiredModules)
        {
            if (!string.Equals(desiredModule.Name, moduleName, StringComparison.OrdinalIgnoreCase))
                continue;

            var policy = ModuleStateVersionPolicy.Parse(desiredModule.VersionPolicy, desiredModule.IncludePrerelease);
            var candidates = installedModules
                .Where(module =>
                    (string.IsNullOrWhiteSpace(desiredModule.Scope) ||
                     string.Equals(module.Scope, desiredModule.Scope, StringComparison.OrdinalIgnoreCase)) &&
                    (string.IsNullOrWhiteSpace(desiredModule.TargetPath ?? desiredModule.ModuleRoot) ||
                     ModuleStatePathIdentity.IsSameOrChild(module.Path, desiredModule.TargetPath ?? desiredModule.ModuleRoot)))
                .ToArray();
            if (candidates.Length == 0)
                continue;

            var selectedModule = candidates
                .Where(module => policy.IsSatisfiedBy(module.Version))
                .OrderByDescending(static module => ModuleStateVersion.TryParse(module.Version, out var version) ? version : default)
                .FirstOrDefault();
            selectedModule ??= candidates
                .OrderByDescending(static module => ModuleStateVersion.TryParse(module.Version, out var version) ? version : default)
                .FirstOrDefault();
            if (selectedModule is not null)
                keepModules.Add(CreateKeepKey(selectedModule));
        }

        return keepModules;
    }

    private static string CreateKeepKey(ModuleStateInstalledModule module)
        => string.Join("|", module.Version, module.Scope ?? string.Empty, NormalizeOptionalPath(module.Path));

    private static bool VersionsEquivalent(string installedVersion, string receiptVersion)
    {
        if (ModuleStateVersion.TryParse(installedVersion, out var installed) &&
            ModuleStateVersion.TryParse(receiptVersion, out var receipt))
        {
            return installed.Equals(receipt);
        }

        return string.Equals(installedVersion, receiptVersion, StringComparison.OrdinalIgnoreCase);
    }

    private static ModuleStateConflictFinding CreateLoadedCleanupFinding(ModuleStateInstalledModule installedModule)
        => new(
            ModuleStateConflictSeverity.Error,
            "ModuleState.CleanupLoadedVersion",
            $"Module '{installedModule.Name}' version {installedModule.Version} is loaded and was not selected for cleanup. Start a fresh process before removing loaded module versions.",
            string.Empty,
            new[] { installedModule.Name },
            new[] { installedModule.Version });

    private static void AddCleanupCandidates(
        IEnumerable<ModuleStateInstalledModule> candidates,
        List<ModuleStateInstalledModule> selectedModules,
        HashSet<string> seen)
    {
        foreach (var module in candidates)
        {
            var key = string.Join(
                "|",
                ModuleStatePathIdentity.CreatePlacementKey(
                    module.Name,
                    module.PowerShellEdition,
                    module.Scope,
                    ModuleStatePathIdentity.ResolveModuleRoot(module)),
                module.Version,
                NormalizeOptionalPath(module.Path));
            if (seen.Add(key))
                selectedModules.Add(module);
        }
    }

    private static string NormalizeOptionalPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var normalized = ModuleStatePathIdentity.Normalize(path!);
        return FrameworkCompatibility.IsWindows() ? normalized.ToUpperInvariant() : normalized;
    }
}

internal sealed class ModuleStateCleanupPlan
{
    internal static ModuleStateCleanupPlan Empty { get; } = new(
        Array.Empty<ModuleStatePlanAction>(),
        Array.Empty<ModuleStateConflictFinding>());

    internal ModuleStateCleanupPlan(ModuleStatePlanAction[] actions, ModuleStateConflictFinding[] findings)
    {
        Actions = actions ?? Array.Empty<ModuleStatePlanAction>();
        Findings = findings ?? Array.Empty<ModuleStateConflictFinding>();
    }

    internal ModuleStatePlanAction[] Actions { get; }

    internal ModuleStateConflictFinding[] Findings { get; }
}
