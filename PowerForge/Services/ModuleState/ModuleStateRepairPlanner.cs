using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

internal sealed class ModuleStateRepairPlanner
{
    internal ModuleStatePlanAction[] CreateRepairActions(
        ModuleStateInventory inventory,
        IEnumerable<ModuleStateMaintenanceReceipt> receipts,
        IEnumerable<ModuleStatePlanAction> existingActions,
        IEnumerable<ModuleStateFamilyPolicy>? familyPolicies = null)
    {
        if (inventory is null)
            throw new ArgumentNullException(nameof(inventory));

        var actionsByModule = (existingActions ?? Array.Empty<ModuleStatePlanAction>())
            .Where(static action => action is not null)
            .GroupBy(static action => CreateActionKey(action), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var receipt in receipts ?? Array.Empty<ModuleStateMaintenanceReceipt>())
        {
            foreach (var receiptModule in receipt.Modules)
            {
                var repairAction = CreateRepairAction(inventory, receiptModule);
                if (repairAction is not null)
                    actionsByModule[CreateActionKey(repairAction)] = repairAction;
            }
        }

        foreach (var repairAction in CreateFamilyRepairActions(inventory, familyPolicies))
        {
            actionsByModule[CreateActionKey(repairAction)] = repairAction;
        }

        return actionsByModule.Values
            .OrderBy(static action => action.ModuleName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static action => action.TargetScope, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string CreateActionKey(ModuleStatePlanAction action)
        => string.Join("|", action.ModuleName, action.TargetScope ?? string.Empty);

    private static IEnumerable<ModuleStatePlanAction> CreateFamilyRepairActions(
        ModuleStateInventory inventory,
        IEnumerable<ModuleStateFamilyPolicy>? familyPolicies)
    {
        foreach (var policy in familyPolicies ?? Array.Empty<ModuleStateFamilyPolicy>())
        {
            if (policy.CoherenceRule != ModuleStateFamilyCoherenceRule.SameVersion)
                continue;

            var installedFamilyModules = inventory.InstalledModules
                .Where(module => policy.Modules.Contains(module.Name, StringComparer.OrdinalIgnoreCase))
                .ToArray();

            if (installedFamilyModules.Length <= 1)
                continue;

            var targetModule = SelectInstalledModule(installedFamilyModules);
            if (targetModule is null)
                continue;

            foreach (var moduleName in policy.Modules)
            {
                var installedModules = installedFamilyModules
                    .Where(module => string.Equals(module.Name, moduleName, StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                if (installedModules.Length == 0 ||
                    installedModules.Any(module => VersionsEqual(module.Version, targetModule.Version)))
                {
                    continue;
                }

                var selectedModule = SelectInstalledModule(installedModules);
                yield return new ModuleStatePlanAction(
                    ModuleStatePlanActionKind.Update,
                    moduleName,
                    selectedModule?.Version,
                    "=" + ModuleStateVersion.NormalizeOrOriginal(targetModule.Version),
                    $"Family repair: align '{policy.Name}' modules to the highest installed family version.",
                    isRepair: true);
            }
        }
    }

    private static ModuleStatePlanAction? CreateRepairAction(
        ModuleStateInventory inventory,
        ModuleStateMaintenanceReceiptModule receiptModule)
    {
        var installedModules = inventory.InstalledModules
            .Where(module => string.Equals(module.Name, receiptModule.Name, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var exactPolicy = "=" + receiptModule.Version;

        if (installedModules.Length == 0)
        {
            return new ModuleStatePlanAction(
                ModuleStatePlanActionKind.Install,
                receiptModule.Name,
                installedVersion: null,
                exactPolicy,
                "Maintenance receipt repair: module is missing; install the receipt-managed version.",
                isRepair: true,
                targetScope: receiptModule.Scope,
                targetRepository: receiptModule.SourceRepository);
        }

        var selectedModule = SelectInstalledModule(installedModules);
        if (!installedModules.Any(module => VersionsEqual(module.Version, receiptModule.Version)))
        {
            return new ModuleStatePlanAction(
                ModuleStatePlanActionKind.Update,
                receiptModule.Name,
                selectedModule?.Version,
                exactPolicy,
                "Maintenance receipt repair: installed versions drift from the receipt-managed version.",
                isRepair: true,
                targetScope: receiptModule.Scope,
                targetRepository: receiptModule.SourceRepository);
        }

        if (!string.IsNullOrWhiteSpace(receiptModule.SourceRepository) &&
            !installedModules.Any(module =>
                VersionsEqual(module.Version, receiptModule.Version) &&
                string.Equals(module.SourceRepository, receiptModule.SourceRepository, StringComparison.OrdinalIgnoreCase)))
        {
            return new ModuleStatePlanAction(
                ModuleStatePlanActionKind.Install,
                receiptModule.Name,
                receiptModule.Version,
                exactPolicy,
                "Maintenance receipt repair: reinstall the receipt-managed version from the expected source.",
                isRepair: true,
                force: true,
                targetScope: receiptModule.Scope,
                targetRepository: receiptModule.SourceRepository);
        }

        if (!string.IsNullOrWhiteSpace(receiptModule.Scope) &&
            !installedModules.Any(module =>
                VersionsEqual(module.Version, receiptModule.Version) &&
                string.Equals(module.Scope, receiptModule.Scope, StringComparison.OrdinalIgnoreCase)))
        {
            return new ModuleStatePlanAction(
                ModuleStatePlanActionKind.Install,
                receiptModule.Name,
                receiptModule.Version,
                exactPolicy,
                "Maintenance receipt repair: install the receipt-managed version in the expected scope.",
                isRepair: true,
                targetScope: receiptModule.Scope,
                targetRepository: receiptModule.SourceRepository);
        }

        return null;
    }

    private static ModuleStateInstalledModule? SelectInstalledModule(IEnumerable<ModuleStateInstalledModule> installedModules)
        => installedModules
            .OrderByDescending(static module => ModuleStateVersion.TryParse(module.Version, out var version) ? version : default)
            .FirstOrDefault();

    private static bool VersionsEqual(string installedVersion, string receiptVersion)
    {
        if (ModuleStateVersion.TryParse(installedVersion, out var installed) &&
            ModuleStateVersion.TryParse(receiptVersion, out var expected))
        {
            return installed.CompareTo(expected) == 0;
        }

        return string.Equals(installedVersion, receiptVersion, StringComparison.OrdinalIgnoreCase);
    }
}
