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
                {
                    actionsByModule.Remove(CreateBaseActionKey(repairAction));
                    actionsByModule[CreateActionKey(repairAction)] = repairAction;
                }
            }
        }

        foreach (var repairAction in CreateFamilyRepairActions(inventory, familyPolicies, actionsByModule.Values))
        {
            actionsByModule.Remove(CreateBaseActionKey(repairAction));
            actionsByModule[CreateActionKey(repairAction)] = repairAction;
        }

        return actionsByModule.Values
            .OrderBy(static action => action.ModuleName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static action => action.TargetScope, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string CreateActionKey(ModuleStatePlanAction action)
        => string.Join(
            "|",
            action.ModuleName,
            action.TargetScope ?? string.Empty,
            action.IsRepair ? action.VersionPolicy ?? string.Empty : string.Empty,
            action.IsRepair ? action.TargetRepository ?? string.Empty : string.Empty);

    private static string CreateBaseActionKey(ModuleStatePlanAction action)
        => string.Join("|", action.ModuleName, action.TargetScope ?? string.Empty, string.Empty, string.Empty);

    private static IEnumerable<ModuleStatePlanAction> CreateFamilyRepairActions(
        ModuleStateInventory inventory,
        IEnumerable<ModuleStateFamilyPolicy>? familyPolicies,
        IEnumerable<ModuleStatePlanAction> existingActions)
    {
        var existing = (existingActions ?? Array.Empty<ModuleStatePlanAction>())
            .Where(static action => action is not null)
            .ToArray();

        foreach (var policy in familyPolicies ?? Array.Empty<ModuleStateFamilyPolicy>())
        {
            if (policy.CoherenceRule != ModuleStateFamilyCoherenceRule.SameVersion)
                continue;

            var installedFamilyModules = inventory.InstalledModules
                .Where(module => policy.Matches(module.Name))
                .ToArray();

            if (installedFamilyModules.Length <= 1)
                continue;

            var targetModule = SelectInstalledModule(installedFamilyModules);
            if (targetModule is null)
                continue;

            var installedFamilyModuleNames = installedFamilyModules
                .Select(static module => module.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var moduleName in installedFamilyModuleNames)
            {
                var installedModules = installedFamilyModules
                    .Where(module => string.Equals(module.Name, moduleName, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                var selectedModule = SelectInstalledModule(installedModules);

                if (installedModules.Length == 0 ||
                    selectedModule is null ||
                    VersionsEqual(selectedModule.Version, targetModule.Version))
                {
                    continue;
                }

                var targetRepository = FindCoveredAction(existing, moduleName, selectedModule.Scope)?.TargetRepository;
                yield return new ModuleStatePlanAction(
                    ModuleStatePlanActionKind.Update,
                    moduleName,
                    selectedModule.Version,
                    "=" + ModuleStateVersion.NormalizeOrOriginal(targetModule.Version),
                    $"Family repair: align '{policy.Name}' modules to the highest installed family version.",
                    isRepair: true,
                    targetScope: selectedModule.Scope,
                    targetRepository: targetRepository);
            }
        }
    }

    private static ModuleStatePlanAction? FindCoveredAction(
        IEnumerable<ModuleStatePlanAction> existingActions,
        string moduleName,
        string? targetScope)
        => existingActions.FirstOrDefault(action =>
            string.Equals(action.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(action.TargetScope ?? string.Empty, targetScope ?? string.Empty, StringComparison.OrdinalIgnoreCase));

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
            !HasInstalledReceiptCopy(installedModules, receiptModule))
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
            !HasInstalledReceiptCopy(installedModules, receiptModule))
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

    private static bool HasInstalledReceiptCopy(
        IEnumerable<ModuleStateInstalledModule> installedModules,
        ModuleStateMaintenanceReceiptModule receiptModule)
        => installedModules.Any(module =>
            VersionsEqual(module.Version, receiptModule.Version) &&
            (string.IsNullOrWhiteSpace(receiptModule.SourceRepository) ||
             string.Equals(module.SourceRepository, receiptModule.SourceRepository, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(receiptModule.Scope) ||
             string.Equals(module.Scope, receiptModule.Scope, StringComparison.OrdinalIgnoreCase)));

    private static ModuleStateInstalledModule? SelectInstalledModule(IEnumerable<ModuleStateInstalledModule> installedModules)
        => installedModules
            .OrderByDescending(static module => module.IsLoaded)
            .ThenByDescending(static module => module.IsEffectiveImportCandidate)
            .ThenByDescending(static module => ModuleStateVersion.TryParse(module.Version, out var version) ? version : default)
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
