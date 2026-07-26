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
            .GroupBy(static action => CreateActionKey(action), StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);

        foreach (var receipt in receipts ?? Array.Empty<ModuleStateMaintenanceReceipt>())
        {
            foreach (var receiptModule in receipt.Modules)
            {
                foreach (var repairAction in CreateReceiptRepairActions(inventory, receiptModule))
                {
                    RemoveNonRepairActionForPlacement(actionsByModule, repairAction);
                    actionsByModule[CreateActionKey(repairAction)] = repairAction;
                }
            }
        }

        foreach (var repairAction in CreateFamilyRepairActions(inventory, familyPolicies, actionsByModule.Values))
        {
            RemoveActionKeysForModuleScope(actionsByModule, repairAction);
            actionsByModule[CreateActionKey(repairAction)] = repairAction;
        }

        foreach (var repairAction in CreateManifestDependencyRepairActions(inventory, actionsByModule.Values.ToArray()))
        {
            RemoveActionKeysForModuleScope(actionsByModule, repairAction);
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
            action.ModuleName.ToUpperInvariant(),
            (action.TargetScope ?? string.Empty).ToUpperInvariant(),
            NormalizePlacementPath(action.TargetModuleRoot ?? action.TargetPath),
            (action.TargetPowerShellEdition ?? string.Empty).ToUpperInvariant(),
            (action.TargetProfileName ?? string.Empty).ToUpperInvariant(),
            action.IsRepair ? action.VersionPolicy ?? string.Empty : string.Empty,
            action.IsRepair ? (action.TargetRepository ?? string.Empty).ToUpperInvariant() : string.Empty,
            action.IsRepair ? NormalizeRepositorySource(action.TargetRepositorySource) : string.Empty);

    private static void RemoveActionKeysForModuleScope(
        IDictionary<string, ModuleStatePlanAction> actionsByModule,
        ModuleStatePlanAction repairAction)
    {
        var keys = actionsByModule
            .Where(pair =>
                string.Equals(pair.Value.ModuleName, repairAction.ModuleName, StringComparison.OrdinalIgnoreCase) &&
                IsSameActionPlacement(pair.Value, repairAction))
            .Select(static pair => pair.Key)
            .ToArray();

        foreach (var key in keys)
        {
            actionsByModule.Remove(key);
        }
    }

    private static void RemoveNonRepairActionForPlacement(
        IDictionary<string, ModuleStatePlanAction> actionsByModule,
        ModuleStatePlanAction repairAction)
    {
        var keys = actionsByModule
            .Where(pair =>
                !pair.Value.IsRepair &&
                string.Equals(pair.Value.ModuleName, repairAction.ModuleName, StringComparison.OrdinalIgnoreCase) &&
                IsSameReceiptActionPlacement(pair.Value, repairAction))
            .Select(static pair => pair.Key)
            .ToArray();

        foreach (var key in keys)
            actionsByModule.Remove(key);
    }

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

            var installedFamilyEstates = inventory.InstalledModules
                .Where(module => policy.Matches(module.Name))
                .GroupBy(
                    static module => ModuleStatePathIdentity.CreateEstateKey(
                        module.PowerShellEdition,
                        module.Scope,
                        ModuleStatePathIdentity.ResolveModuleRoot(module),
                        module.ProfileName),
                    StringComparer.Ordinal);

            foreach (var installedFamilyEstate in installedFamilyEstates)
            {
                var installedFamilyModules = installedFamilyEstate.ToArray();
                if (installedFamilyModules.Length <= 1)
                    continue;

                var targetModule = SelectHighestInstalledModule(installedFamilyModules);
                if (targetModule is null)
                    continue;

                var installedFamilyModuleNames = installedFamilyModules
                    .Select(static module => module.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var wholeFamilyHasPlannedActions = installedFamilyModuleNames.All(name =>
                    existing.Any(action =>
                        string.Equals(action.ModuleName, name, StringComparison.OrdinalIgnoreCase) &&
                        IsActionForModulePlacement(action, targetModule)));

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

                    if (!wholeFamilyHasPlannedActions &&
                        HasExplicitDesiredAction(existing, moduleName, selectedModule))
                    {
                        continue;
                    }

                    var coveredAction = FindCoveredAction(existing, moduleName, selectedModule);
                    var moduleRoot = ModuleStatePathIdentity.ResolveModuleRoot(selectedModule);
                    yield return new ModuleStatePlanAction(
                        ModuleStatePlanActionKind.Update,
                        moduleName,
                        selectedModule.Version,
                        "=" + ModuleStateVersion.NormalizeOrOriginal(targetModule.Version),
                        $"Family repair: align '{policy.Name}' modules to the highest installed family version.",
                        isRepair: true,
                        targetScope: selectedModule.Scope,
                        targetPath: moduleRoot,
                        targetRepository: coveredAction?.TargetRepository,
                        targetRepositorySource: coveredAction?.TargetRepositorySource,
                        includePrerelease: coveredAction?.IncludePrerelease ?? false,
                        acceptLicense: coveredAction?.AcceptLicense ?? false,
                        allowClobber: coveredAction?.AllowClobber ?? false,
                        skipDependencyCheck: coveredAction?.SkipDependencyCheck ?? false,
                        targetModuleRoot: moduleRoot,
                        targetPowerShellEdition: selectedModule.PowerShellEdition,
                        targetProfileName: selectedModule.ProfileName);
                }
            }
        }
    }

    private static IEnumerable<ModuleStatePlanAction> CreateManifestDependencyRepairActions(
        ModuleStateInventory inventory,
        IEnumerable<ModuleStatePlanAction> existingActions)
    {
        foreach (var action in existingActions ?? Array.Empty<ModuleStatePlanAction>())
        {
            if (action.Kind != ModuleStatePlanActionKind.NoAction)
                continue;

            var installedModule = FindInstalledModuleForAction(inventory, action);
            if (installedModule is null ||
                string.IsNullOrWhiteSpace(installedModule.Path))
            {
                continue;
            }

            var moduleRoot = ResolveModuleRoot(installedModule);
            if (string.IsNullOrWhiteSpace(moduleRoot) ||
                !ManagedModuleInstallService.WouldRepairInstalledManifestDependencies(
                    installedModule.Name,
                    moduleRoot!,
                    installedModule.Path!,
                    action.IncludePrerelease,
                    action.SkipDependencyCheck))
            {
                continue;
            }

            yield return new ModuleStatePlanAction(
                ModuleStatePlanActionKind.Install,
                installedModule.Name,
                installedModule.Version,
                "=" + ModuleStateVersion.NormalizeOrOriginal(installedModule.Version),
                "Manifest dependency repair: installed module has missing or unsatisfied manifest RequiredModules.",
                isRepair: true,
                targetScope: installedModule.Scope,
                targetPath: moduleRoot,
                targetRepository: action.TargetRepository,
                targetRepositorySource: action.TargetRepositorySource,
                includePrerelease: action.IncludePrerelease,
                acceptLicense: action.AcceptLicense,
                allowClobber: action.AllowClobber,
                skipDependencyCheck: action.SkipDependencyCheck,
                targetModuleRoot: moduleRoot,
                targetPowerShellEdition: installedModule.PowerShellEdition,
                targetProfileName: installedModule.ProfileName);
        }
    }

    private static ModuleStatePlanAction? FindCoveredAction(
        IEnumerable<ModuleStatePlanAction> existingActions,
        string moduleName,
        ModuleStateInstalledModule placement)
        => existingActions.FirstOrDefault(action =>
            string.Equals(action.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase) &&
            IsActionForModulePlacement(action, placement));

    private static bool HasExplicitDesiredAction(
        IEnumerable<ModuleStatePlanAction> existingActions,
        string moduleName,
        ModuleStateInstalledModule placement)
        => existingActions.Any(action =>
            !action.IsRepair &&
            string.Equals(action.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase) &&
            IsActionForModulePlacement(action, placement));

    private static ModuleStateInstalledModule? FindInstalledModuleForAction(
        ModuleStateInventory inventory,
        ModuleStatePlanAction action)
    {
        var candidates = inventory.InstalledModules
            .Where(module => string.Equals(module.Name, action.ModuleName, StringComparison.OrdinalIgnoreCase))
            .Where(module => string.IsNullOrWhiteSpace(action.InstalledVersion) ||
                             VersionsEqual(module.Version, action.InstalledVersion!))
            .Where(module => string.IsNullOrWhiteSpace(action.TargetScope) ||
                             string.Equals(module.Scope, action.TargetScope, StringComparison.OrdinalIgnoreCase))
            .Where(module => string.IsNullOrWhiteSpace(action.TargetModuleRoot ?? action.TargetPath) ||
                             ModuleStatePathIdentity.IsSameOrChild(module.Path, action.TargetModuleRoot ?? action.TargetPath))
            .Where(module => string.IsNullOrWhiteSpace(action.TargetPowerShellEdition) ||
                             string.Equals(module.PowerShellEdition, action.TargetPowerShellEdition, StringComparison.OrdinalIgnoreCase))
            .Where(module => string.IsNullOrWhiteSpace(action.TargetProfileName) ||
                             string.Equals(module.ProfileName, action.TargetProfileName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return SelectInstalledModule(candidates);
    }

    private static string? ResolveModuleRoot(ModuleStateInstalledModule module)
    {
        return ModuleStatePathIdentity.ResolveModuleRoot(module);
    }

    private static bool IsSameActionPlacement(
        ModuleStatePlanAction left,
        ModuleStatePlanAction right)
    {
        var leftRoot = left.TargetModuleRoot ?? left.TargetPath;
        var rightRoot = right.TargetModuleRoot ?? right.TargetPath;
        return string.Equals(left.TargetScope ?? string.Empty, right.TargetScope ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
               (string.IsNullOrWhiteSpace(leftRoot) || string.IsNullOrWhiteSpace(rightRoot) || ModuleStatePathIdentity.Equals(leftRoot, rightRoot)) &&
               (string.IsNullOrWhiteSpace(left.TargetPowerShellEdition) || string.IsNullOrWhiteSpace(right.TargetPowerShellEdition) ||
                string.Equals(left.TargetPowerShellEdition, right.TargetPowerShellEdition, StringComparison.OrdinalIgnoreCase)) &&
               (string.IsNullOrWhiteSpace(left.TargetProfileName) || string.IsNullOrWhiteSpace(right.TargetProfileName) ||
                string.Equals(left.TargetProfileName, right.TargetProfileName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSameReceiptActionPlacement(
        ModuleStatePlanAction existingAction,
        ModuleStatePlanAction receiptRepairAction)
    {
        var existingRoot = existingAction.TargetModuleRoot ?? existingAction.TargetPath;
        var repairRoot = receiptRepairAction.TargetModuleRoot ?? receiptRepairAction.TargetPath;
        if (string.IsNullOrWhiteSpace(existingRoot) != string.IsNullOrWhiteSpace(repairRoot))
            return false;

        if (string.IsNullOrWhiteSpace(existingRoot))
        {
            return string.Equals(existingAction.TargetScope ?? string.Empty, receiptRepairAction.TargetScope ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(existingAction.TargetPowerShellEdition ?? string.Empty, receiptRepairAction.TargetPowerShellEdition ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(existingAction.TargetProfileName ?? string.Empty, receiptRepairAction.TargetProfileName ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        return IsSameActionPlacement(existingAction, receiptRepairAction);
    }

    private static bool IsActionForModulePlacement(
        ModuleStatePlanAction action,
        ModuleStateInstalledModule module)
    {
        if (!string.IsNullOrWhiteSpace(action.TargetScope) &&
            !string.Equals(action.TargetScope, module.Scope, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(action.TargetPowerShellEdition) &&
            !string.Equals(action.TargetPowerShellEdition, module.PowerShellEdition, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(action.TargetProfileName) &&
            !string.Equals(action.TargetProfileName, module.ProfileName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var actionRoot = action.TargetModuleRoot ?? action.TargetPath;
        return string.IsNullOrWhiteSpace(actionRoot) ||
               ModuleStatePathIdentity.Equals(actionRoot, ModuleStatePathIdentity.ResolveModuleRoot(module));
    }

    private static string NormalizePlacementPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var normalized = ModuleStatePathIdentity.Normalize(path!);
        return FrameworkCompatibility.IsWindows() ? normalized.ToUpperInvariant() : normalized;
    }

    private static string NormalizeRepositorySource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return string.Empty;

        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.IsFile)
            return NormalizePlacementPath(uri.LocalPath);

        return source!.Trim().ToUpperInvariant();
    }

    private static IEnumerable<ModuleStatePlanAction> CreateReceiptRepairActions(
        ModuleStateInventory inventory,
        ModuleStateMaintenanceReceiptModule receiptModule)
    {
        var allInstalledModules = inventory.InstalledModules
            .Where(module => string.Equals(module.Name, receiptModule.Name, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var installedModules = allInstalledModules
            .Where(module => MatchesReceiptPlacement(module, receiptModule))
            .ToArray();
        var exactPolicy = "=" + receiptModule.Version;

        if (installedModules.Length == 0)
        {
            var installedElsewhere = SelectInstalledModule(allInstalledModules);
            var targetPlacement = inventory.ModulePaths
                .Where(path => MatchesReceiptPlacement(path, receiptModule))
                .Take(2)
                .ToArray();
            var targetModuleRoot = receiptModule.ModuleRoot ??
                (targetPlacement.Length == 1 ? targetPlacement[0].Path : null);
            yield return new ModuleStatePlanAction(
                ModuleStatePlanActionKind.Install,
                receiptModule.Name,
                installedVersion: installedElsewhere?.Version,
                exactPolicy,
                "Maintenance receipt repair: module is missing; install the receipt-managed version.",
                isRepair: true,
                targetScope: receiptModule.Scope ?? (targetPlacement.Length == 1 ? targetPlacement[0].Scope : null),
                targetPath: targetModuleRoot,
                targetRepository: receiptModule.SourceRepository,
                targetModuleRoot: targetModuleRoot,
                targetPowerShellEdition: receiptModule.PowerShellEdition ?? (targetPlacement.Length == 1 ? targetPlacement[0].PowerShellEdition : null),
                targetProfileName: receiptModule.ProfileName ?? (targetPlacement.Length == 1 ? targetPlacement[0].ProfileName : null));
            yield break;
        }

        var placementGroups = installedModules.GroupBy(
            static module => ModuleStatePathIdentity.CreateEstateKey(
                module.PowerShellEdition,
                module.Scope,
                ModuleStatePathIdentity.ResolveModuleRoot(module),
                module.ProfileName),
            StringComparer.Ordinal);
        foreach (var placementGroup in placementGroups)
        {
            var placementModules = placementGroup.ToArray();
            var selectedModule = SelectInstalledModule(placementModules);
            if (selectedModule is null)
                continue;
            var moduleRoot = ModuleStatePathIdentity.ResolveModuleRoot(selectedModule);

            if (!placementModules.Any(module => VersionsEqual(module.Version, receiptModule.Version)))
            {
                yield return new ModuleStatePlanAction(
                    ModuleStatePlanActionKind.Update,
                    receiptModule.Name,
                    selectedModule.Version,
                    exactPolicy,
                    "Maintenance receipt repair: installed versions drift from the receipt-managed version.",
                    isRepair: true,
                    targetScope: selectedModule.Scope,
                    targetPath: moduleRoot,
                    targetRepository: receiptModule.SourceRepository,
                    targetModuleRoot: moduleRoot,
                    targetPowerShellEdition: selectedModule.PowerShellEdition,
                    targetProfileName: selectedModule.ProfileName);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(receiptModule.SourceRepository) &&
                !HasInstalledReceiptCopy(placementModules, receiptModule))
            {
                yield return new ModuleStatePlanAction(
                    ModuleStatePlanActionKind.Install,
                    receiptModule.Name,
                    receiptModule.Version,
                    exactPolicy,
                    "Maintenance receipt repair: reinstall the receipt-managed version from the expected source.",
                    isRepair: true,
                    force: true,
                    targetScope: selectedModule.Scope,
                    targetPath: moduleRoot,
                    targetRepository: receiptModule.SourceRepository,
                    targetModuleRoot: moduleRoot,
                    targetPowerShellEdition: selectedModule.PowerShellEdition,
                    targetProfileName: selectedModule.ProfileName);
            }
        }
    }

    private static bool HasInstalledReceiptCopy(
        IEnumerable<ModuleStateInstalledModule> installedModules,
        ModuleStateMaintenanceReceiptModule receiptModule)
        => installedModules.Any(module =>
            VersionsEqual(module.Version, receiptModule.Version) &&
            (string.IsNullOrWhiteSpace(receiptModule.SourceRepository) ||
             string.Equals(module.SourceRepository, receiptModule.SourceRepository, StringComparison.OrdinalIgnoreCase)) &&
            MatchesReceiptPlacement(module, receiptModule));

    private static bool MatchesReceiptPlacement(
        ModuleStateInstalledModule installedModule,
        ModuleStateMaintenanceReceiptModule receiptModule)
        => (string.IsNullOrWhiteSpace(receiptModule.Scope) ||
            string.Equals(installedModule.Scope, receiptModule.Scope, StringComparison.OrdinalIgnoreCase)) &&
           (string.IsNullOrWhiteSpace(receiptModule.ModuleRoot) ||
            ModuleStatePathIdentity.Equals(ModuleStatePathIdentity.ResolveModuleRoot(installedModule), receiptModule.ModuleRoot)) &&
           (string.IsNullOrWhiteSpace(receiptModule.PowerShellEdition) ||
            string.Equals(installedModule.PowerShellEdition, receiptModule.PowerShellEdition, StringComparison.OrdinalIgnoreCase)) &&
           (string.IsNullOrWhiteSpace(receiptModule.ProfileName) ||
            string.Equals(installedModule.ProfileName, receiptModule.ProfileName, StringComparison.OrdinalIgnoreCase));

    private static bool MatchesReceiptPlacement(
        ModuleStateModulePath modulePath,
        ModuleStateMaintenanceReceiptModule receiptModule)
        => (string.IsNullOrWhiteSpace(receiptModule.Scope) ||
            string.Equals(modulePath.Scope, receiptModule.Scope, StringComparison.OrdinalIgnoreCase)) &&
           (string.IsNullOrWhiteSpace(receiptModule.ModuleRoot) ||
            ModuleStatePathIdentity.Equals(modulePath.Path, receiptModule.ModuleRoot)) &&
           (string.IsNullOrWhiteSpace(receiptModule.PowerShellEdition) ||
            string.Equals(modulePath.PowerShellEdition, receiptModule.PowerShellEdition, StringComparison.OrdinalIgnoreCase)) &&
           (string.IsNullOrWhiteSpace(receiptModule.ProfileName) ||
            string.Equals(modulePath.ProfileName, receiptModule.ProfileName, StringComparison.OrdinalIgnoreCase));

    private static ModuleStateInstalledModule? SelectInstalledModule(IEnumerable<ModuleStateInstalledModule> installedModules)
        => installedModules
            .OrderByDescending(static module => module.IsLoaded)
            .ThenByDescending(static module => module.IsEffectiveImportCandidate)
            .ThenByDescending(static module => ModuleStateVersion.TryParse(module.Version, out var version) ? version : default)
            .FirstOrDefault();

    private static ModuleStateInstalledModule? SelectHighestInstalledModule(IEnumerable<ModuleStateInstalledModule> installedModules)
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
