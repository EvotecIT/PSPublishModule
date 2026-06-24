using System.Linq;
using PowerForge;

namespace PSPublishModule;

internal static class ModuleStatePlanCommandSupport
{
    internal static ModuleStatePlanResult CreatePlanResult(
        string inventoryPath,
        string desiredStatePath,
        string[]? maintenanceReceiptPaths = null,
        bool repair = false,
        ModuleStateCleanupMode cleanupMode = ModuleStateCleanupMode.None,
        string[]? families = null)
        => ModuleStatePlanResultMapper.ToCmdletResult(
            CreatePlan(inventoryPath, desiredStatePath, maintenanceReceiptPaths, repair, cleanupMode, families),
            inventoryPath,
            desiredStatePath,
            maintenanceReceiptPaths);

    internal static ModuleStatePlanResult CreatePlanResult(
        ModuleStateInventoryResult inventory,
        object desiredState,
        string[]? maintenanceReceiptPaths = null,
        bool repair = false,
        ModuleStateCleanupMode cleanupMode = ModuleStateCleanupMode.None,
        string[]? families = null)
        => ModuleStatePlanResultMapper.ToCmdletResult(
            CreatePlan(inventory, desiredState, maintenanceReceiptPaths, repair, cleanupMode, families),
            inventory.Source,
            "Object",
            maintenanceReceiptPaths);

    internal static ModuleStatePlan CreatePlan(
        string inventoryPath,
        string desiredStatePath,
        string[]? maintenanceReceiptPaths = null,
        bool repair = false,
        ModuleStateCleanupMode cleanupMode = ModuleStateCleanupMode.None,
        string[]? families = null)
    {
        var json = new ModuleStateJsonService();
        var inventory = json.LoadInventory(inventoryPath);
        var desiredState = json.LoadDesiredState(desiredStatePath);
        var receipts = (maintenanceReceiptPaths ?? [])
            .Select(json.LoadMaintenanceReceipt)
            .ToArray();
        var familyPolicies = desiredState.FamilyPolicies
            .Concat(new ModuleStateFamilyCatalog().Resolve(families))
            .GroupBy(static policy => policy.Name, System.StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
        return new ModuleStatePlanner().CreatePlan(new ModuleStatePlanRequest(
            inventory,
            desiredState.Modules,
            familyPolicies,
            receipts,
            repair,
            cleanupMode));
    }

    internal static ModuleStatePlan CreatePlan(
        ModuleStateInventoryResult inventory,
        object desiredState,
        string[]? maintenanceReceiptPaths = null,
        bool repair = false,
        ModuleStateCleanupMode cleanupMode = ModuleStateCleanupMode.None,
        string[]? families = null)
    {
        var json = new ModuleStateJsonService();
        var desired = ModuleStateObjectAdapter.ToDesiredState(desiredState);
        var receipts = (maintenanceReceiptPaths ?? [])
            .Select(json.LoadMaintenanceReceipt)
            .ToArray();
        var familyPolicies = desired.FamilyPolicies
            .Concat(new ModuleStateFamilyCatalog().Resolve(families))
            .GroupBy(static policy => policy.Name, System.StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();

        return new ModuleStatePlanner().CreatePlan(new ModuleStatePlanRequest(
            ModuleStateInventoryResultMapper.ToCoreInventory(inventory),
            desired.Modules,
            familyPolicies,
            receipts,
            repair,
            cleanupMode));
    }
}
