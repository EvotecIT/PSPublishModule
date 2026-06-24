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
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var moduleGroup in GetManagedModuleGroups(request))
        {
            var installedModules = moduleGroup.ToArray();
            var keepVersions = ResolveKeepVersions(request, moduleGroup.Key, installedModules);
            if (keepVersions.Count == 0)
                continue;

            foreach (var installedModule in installedModules)
            {
                if (keepVersions.Contains(installedModule.Version))
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
                    targetPath: installedModule.Path));
            }
        }

        return new ModuleStateCleanupPlan(actions.ToArray(), findings.ToArray());
    }

    private static IEnumerable<IGrouping<string, ModuleStateInstalledModule>> GetManagedModuleGroups(ModuleStatePlanRequest request)
    {
        var managedNames = request.DesiredModules
            .Select(static module => module.Name)
            .Concat(request.MaintenanceReceipts.SelectMany(static receipt => receipt.Modules.Select(static module => module.Name)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return request.Inventory.InstalledModules
            .Where(module => managedNames.Contains(module.Name, StringComparer.OrdinalIgnoreCase))
            .GroupBy(static module => module.Name, StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> ResolveKeepVersions(
        ModuleStatePlanRequest request,
        string moduleName,
        ModuleStateInstalledModule[] installedModules)
    {
        var keepVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var receiptModule in request.MaintenanceReceipts.SelectMany(static receipt => receipt.Modules))
        {
            if (!string.Equals(receiptModule.Name, moduleName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (installedModules.Any(module => string.Equals(module.Version, receiptModule.Version, StringComparison.OrdinalIgnoreCase)))
                keepVersions.Add(receiptModule.Version);
        }

        foreach (var desiredModule in request.DesiredModules)
        {
            if (!string.Equals(desiredModule.Name, moduleName, StringComparison.OrdinalIgnoreCase))
                continue;

            var policy = ModuleStateVersionPolicy.Parse(desiredModule.VersionPolicy);
            var selectedModule = installedModules
                .Where(module => policy.IsSatisfiedBy(module.Version))
                .OrderByDescending(static module => ModuleStateVersion.TryParse(module.Version, out var version) ? version : default)
                .FirstOrDefault();
            selectedModule ??= installedModules
                .OrderByDescending(static module => ModuleStateVersion.TryParse(module.Version, out var version) ? version : default)
                .FirstOrDefault();
            if (selectedModule is not null)
                keepVersions.Add(selectedModule.Version);
        }

        return keepVersions;
    }

    private static ModuleStateConflictFinding CreateLoadedCleanupFinding(ModuleStateInstalledModule installedModule)
        => new(
            ModuleStateConflictSeverity.Error,
            "ModuleState.CleanupLoadedVersion",
            $"Module '{installedModule.Name}' version {installedModule.Version} is loaded and was not selected for cleanup. Start a fresh process before removing loaded module versions.",
            string.Empty,
            new[] { installedModule.Name },
            new[] { installedModule.Version });
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
