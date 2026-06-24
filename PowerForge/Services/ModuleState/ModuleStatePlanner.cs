using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

internal sealed class ModuleStatePlanner
{
    private readonly ModuleStateFamilyCoherenceAnalyzer _familyAnalyzer;
    private readonly ModuleStateConflictAnalyzer _conflictAnalyzer;
    private readonly ModuleStateReceiptDriftAnalyzer _receiptAnalyzer;
    private readonly ModuleStateRepairPlanner _repairPlanner;
    private readonly ModuleStateCleanupPlanner _cleanupPlanner;

    internal ModuleStatePlanner(
        ModuleStateFamilyCoherenceAnalyzer? familyAnalyzer = null,
        ModuleStateConflictAnalyzer? conflictAnalyzer = null,
        ModuleStateReceiptDriftAnalyzer? receiptAnalyzer = null,
        ModuleStateRepairPlanner? repairPlanner = null,
        ModuleStateCleanupPlanner? cleanupPlanner = null)
    {
        _familyAnalyzer = familyAnalyzer ?? new ModuleStateFamilyCoherenceAnalyzer();
        _conflictAnalyzer = conflictAnalyzer ?? new ModuleStateConflictAnalyzer();
        _receiptAnalyzer = receiptAnalyzer ?? new ModuleStateReceiptDriftAnalyzer();
        _repairPlanner = repairPlanner ?? new ModuleStateRepairPlanner();
        _cleanupPlanner = cleanupPlanner ?? new ModuleStateCleanupPlanner();
    }

    internal ModuleStatePlan CreatePlan(ModuleStatePlanRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var actions = new List<ModuleStatePlanAction>();
        foreach (var desiredModule in request.DesiredModules)
        {
            var installedModules = SelectInstalledModules(request.Inventory, desiredModule.Name);
            var installedModule = SelectInstalledModule(installedModules, desiredModule.Scope);
            var versionPolicy = ModuleStateVersionPolicy.Parse(desiredModule.VersionPolicy);
            if (installedModule is null)
            {
                actions.Add(new ModuleStatePlanAction(
                    ModuleStatePlanActionKind.Install,
                    desiredModule.Name,
                    installedVersion: null,
                    desiredModule.VersionPolicy,
                    string.IsNullOrWhiteSpace(desiredModule.Scope)
                        ? "Module is not installed."
                        : "Module is not installed in desired scope.",
                    targetScope: desiredModule.Scope,
                    targetRepository: ResolveTargetRepository(desiredModule)));
                continue;
            }

            if (!versionPolicy.IsSatisfiedBy(installedModule.Version))
            {
                actions.Add(new ModuleStatePlanAction(
                    ModuleStatePlanActionKind.Update,
                    desiredModule.Name,
                    installedModule.Version,
                    desiredModule.VersionPolicy,
                    "Installed module version does not satisfy desired policy.",
                    targetScope: desiredModule.Scope,
                    targetRepository: ResolveTargetRepository(desiredModule)));
                continue;
            }

            actions.Add(new ModuleStatePlanAction(
                ModuleStatePlanActionKind.NoAction,
                desiredModule.Name,
                installedModule.Version,
                desiredModule.VersionPolicy,
                "Installed module version satisfies desired policy.",
                targetScope: desiredModule.Scope,
                targetRepository: ResolveTargetRepository(desiredModule)));
        }

        var plannedActions = request.Repair
            ? _repairPlanner.CreateRepairActions(request.Inventory, request.MaintenanceReceipts, actions, request.FamilyPolicies)
            : actions.ToArray();
        var cleanupPlan = _cleanupPlanner.CreateCleanupPlan(request);

        var findings = _familyAnalyzer
            .Analyze(request.Inventory, request.FamilyPolicies)
            .Concat(_conflictAnalyzer.Analyze(request.Inventory, request.DesiredModules))
            .Concat(_receiptAnalyzer.Analyze(request.Inventory, request.MaintenanceReceipts))
            .Concat(cleanupPlan.Findings)
            .ToArray();
        return new ModuleStatePlan(plannedActions.Concat(cleanupPlan.Actions).ToArray(), findings);
    }

    private static ModuleStateInstalledModule[] SelectInstalledModules(ModuleStateInventory inventory, string moduleName)
        => inventory.InstalledModules
            .Where(module => string.Equals(module.Name, moduleName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

    private static ModuleStateInstalledModule? SelectInstalledModule(IEnumerable<ModuleStateInstalledModule> installedModules, string? desiredScope)
    {
        var candidates = installedModules
            .Where(module => string.IsNullOrWhiteSpace(desiredScope)
                || string.Equals(module.Scope, desiredScope, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return candidates
            .Where(static module => module.IsEffectiveImportCandidate)
            .OrderByDescending(static module => ModuleStateVersion.TryParse(module.Version, out var version) ? version : default)
            .FirstOrDefault()
            ?? candidates
                .OrderByDescending(static module => ModuleStateVersion.TryParse(module.Version, out var version) ? version : default)
                .FirstOrDefault();
    }

    private static string? ResolveTargetRepository(ModuleStateDesiredModule desiredModule)
        => desiredModule.AllowedSources.Length == 1
            ? desiredModule.AllowedSources[0]
            : null;
}
