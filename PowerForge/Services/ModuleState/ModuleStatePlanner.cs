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
            var targetRepository = ResolveTargetRepository(desiredModule);
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
                    targetRepository: targetRepository));
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
                    targetRepository: targetRepository));
                continue;
            }

            if (NeedsSourceDelivery(installedModule, desiredModule, targetRepository))
            {
                actions.Add(new ModuleStatePlanAction(
                    ModuleStatePlanActionKind.Install,
                    desiredModule.Name,
                    installedModule.Version,
                    desiredModule.VersionPolicy,
                    "Installed module version satisfies desired policy but source repository does not match desired state.",
                    force: true,
                    targetScope: desiredModule.Scope,
                    targetRepository: targetRepository));
                continue;
            }

            actions.Add(new ModuleStatePlanAction(
                ModuleStatePlanActionKind.NoAction,
                desiredModule.Name,
                installedModule.Version,
                desiredModule.VersionPolicy,
                "Installed module version satisfies desired policy.",
                targetScope: desiredModule.Scope,
                targetRepository: targetRepository));
        }

        var plannedActions = request.Repair
            ? _repairPlanner.CreateRepairActions(request.Inventory, request.MaintenanceReceipts, actions, request.FamilyPolicies)
            : actions.ToArray();
        var cleanupPlan = _cleanupPlanner.CreateCleanupPlan(request);
        var finalActions = plannedActions.Concat(cleanupPlan.Actions).ToArray();

        var findings = _familyAnalyzer
            .Analyze(request.Inventory, request.FamilyPolicies)
            .Concat(_conflictAnalyzer.Analyze(request.Inventory, request.DesiredModules))
            .Concat(_receiptAnalyzer.Analyze(request.Inventory, request.MaintenanceReceipts))
            .Concat(cleanupPlan.Findings)
            .ToArray();
        return new ModuleStatePlan(finalActions, DowngradeActionCoveredFindings(findings, finalActions));
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

    private static bool NeedsSourceDelivery(
        ModuleStateInstalledModule installedModule,
        ModuleStateDesiredModule desiredModule,
        string? targetRepository)
        => !string.IsNullOrWhiteSpace(targetRepository) &&
           !string.IsNullOrWhiteSpace(installedModule.SourceRepository) &&
           !desiredModule.AllowedSources.Contains(installedModule.SourceRepository, StringComparer.OrdinalIgnoreCase);

    private static ModuleStateConflictFinding[] DowngradeActionCoveredFindings(
        ModuleStateConflictFinding[] findings,
        ModuleStatePlanAction[] actions)
    {
        if (findings.Length == 0 || actions.Length == 0)
            return findings;

        return findings
            .Select(finding => IsCoveredByAction(finding, actions)
                ? new ModuleStateConflictFinding(
                    ModuleStateConflictSeverity.Warning,
                    finding.Code,
                    finding.Message,
                    finding.FamilyName,
                    finding.ModuleNames,
                    finding.Versions,
                    finding.Scope,
                    finding.SourceRepository)
                : finding)
            .ToArray();
    }

    private static bool IsCoveredByAction(ModuleStateConflictFinding finding, ModuleStatePlanAction[] actions)
    {
        if (string.Equals(finding.Code, "ModuleState.SourcePreferenceMismatch", StringComparison.OrdinalIgnoreCase))
            return IsSourcePreferenceCoveredByAction(finding, actions);

        if (string.Equals(finding.Code, "ModuleState.FamilyVersionMismatch", StringComparison.OrdinalIgnoreCase))
            return HasDeliveryActionForAnyModule(actions, finding.ModuleNames, requireRepair: true);

        if (finding.Code.StartsWith("ModuleState.Receipt", StringComparison.OrdinalIgnoreCase))
            return HasDeliveryActionForAnyModule(actions, finding.ModuleNames, requireRepair: true);

        return false;
    }

    private static bool HasDeliveryActionForAnyModule(
        ModuleStatePlanAction[] actions,
        string[] moduleNames,
        bool requireRepair)
        => actions.Any(action =>
            action.Kind is ModuleStatePlanActionKind.Install or ModuleStatePlanActionKind.Update &&
            (!requireRepair || action.IsRepair) &&
            moduleNames.Contains(action.ModuleName, StringComparer.OrdinalIgnoreCase));

    private static bool IsSourcePreferenceCoveredByAction(ModuleStateConflictFinding finding, ModuleStatePlanAction[] actions)
        => actions.Any(action =>
            action.Kind is ModuleStatePlanActionKind.Install or ModuleStatePlanActionKind.Update &&
            finding.ModuleNames.Contains(action.ModuleName, StringComparer.OrdinalIgnoreCase) &&
            string.Equals(action.TargetScope, finding.Scope, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(action.TargetRepository) &&
            !string.Equals(action.TargetRepository, finding.SourceRepository, StringComparison.OrdinalIgnoreCase));
}
