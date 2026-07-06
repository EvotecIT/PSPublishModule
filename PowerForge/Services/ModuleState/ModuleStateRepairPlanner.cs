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
            action.ModuleName,
            action.TargetScope ?? string.Empty,
            action.IsRepair ? action.VersionPolicy ?? string.Empty : string.Empty,
            action.IsRepair ? action.TargetRepository ?? string.Empty : string.Empty,
            action.IsRepair ? action.TargetRepositorySource ?? string.Empty : string.Empty);

    private static string CreateBaseActionKey(ModuleStatePlanAction action)
        => string.Join("|", action.ModuleName, action.TargetScope ?? string.Empty, string.Empty, string.Empty, string.Empty);

    private static void RemoveActionKeysForModuleScope(
        IDictionary<string, ModuleStatePlanAction> actionsByModule,
        ModuleStatePlanAction repairAction)
    {
        var keys = actionsByModule
            .Where(pair =>
                string.Equals(pair.Value.ModuleName, repairAction.ModuleName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(pair.Value.TargetScope ?? string.Empty, repairAction.TargetScope ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            .Select(static pair => pair.Key)
            .ToArray();

        foreach (var key in keys)
        {
            actionsByModule.Remove(key);
        }
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

            var installedFamilyModules = inventory.InstalledModules
                .Where(module => policy.Matches(module.Name))
                .ToArray();

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
                existing.Any(action => string.Equals(action.ModuleName, name, StringComparison.OrdinalIgnoreCase)));

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
                    HasExplicitDesiredAction(existing, moduleName, selectedModule.Scope))
                {
                    continue;
                }

                var coveredAction = FindCoveredAction(existing, moduleName, selectedModule.Scope);
                yield return new ModuleStatePlanAction(
                    ModuleStatePlanActionKind.Update,
                    moduleName,
                    selectedModule.Version,
                    "=" + ModuleStateVersion.NormalizeOrOriginal(targetModule.Version),
                    $"Family repair: align '{policy.Name}' modules to the highest installed family version.",
                    isRepair: true,
                    targetScope: selectedModule.Scope,
                    targetRepository: coveredAction?.TargetRepository,
                    targetRepositorySource: coveredAction?.TargetRepositorySource,
                    includePrerelease: coveredAction?.IncludePrerelease ?? false,
                    acceptLicense: coveredAction?.AcceptLicense ?? false,
                    allowClobber: coveredAction?.AllowClobber ?? false,
                    skipDependencyCheck: coveredAction?.SkipDependencyCheck ?? false);
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
                skipDependencyCheck: action.SkipDependencyCheck);
        }
    }

    private static ModuleStatePlanAction? FindCoveredAction(
        IEnumerable<ModuleStatePlanAction> existingActions,
        string moduleName,
        string? targetScope)
        => existingActions.FirstOrDefault(action =>
            string.Equals(action.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(action.TargetScope ?? string.Empty, targetScope ?? string.Empty, StringComparison.OrdinalIgnoreCase));

    private static bool HasExplicitDesiredAction(
        IEnumerable<ModuleStatePlanAction> existingActions,
        string moduleName,
        string? targetScope)
        => existingActions.Any(action =>
            !action.IsRepair &&
            string.Equals(action.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(action.TargetScope) ||
             string.Equals(action.TargetScope, targetScope, StringComparison.OrdinalIgnoreCase)));

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
            .Where(module => string.IsNullOrWhiteSpace(action.TargetPath) ||
                             IsUnderTargetPath(module.Path, action.TargetPath!))
            .ToArray();

        return SelectInstalledModule(candidates);
    }

    private static string? ResolveModuleRoot(ModuleStateInstalledModule module)
    {
        if (string.IsNullOrWhiteSpace(module.Path))
            return null;

        var moduleDirectory = new System.IO.DirectoryInfo(module.Path!);
        if (string.Equals(moduleDirectory.Name, module.Name, StringComparison.OrdinalIgnoreCase))
            return moduleDirectory.Parent?.FullName;

        var parent = moduleDirectory.Parent;
        return parent is not null &&
               string.Equals(parent.Name, module.Name, StringComparison.OrdinalIgnoreCase)
            ? parent.Parent?.FullName
            : null;
    }

    private static bool IsUnderTargetPath(string? modulePath, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(modulePath))
            return false;

        var normalizedModulePath = NormalizePath(modulePath!);
        var normalizedTargetPath = NormalizePath(targetPath);
        var comparison = FrameworkCompatibility.GetPathStringComparison(targetPath);
        return string.Equals(normalizedModulePath, normalizedTargetPath, comparison) ||
               normalizedModulePath.StartsWith(normalizedTargetPath + "/", comparison);
    }

    private static string NormalizePath(string path)
        => path.Trim()
            .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
            .Replace('\\', '/');

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
