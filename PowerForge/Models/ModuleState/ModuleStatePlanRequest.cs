using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

internal sealed class ModuleStatePlanRequest
{
    internal ModuleStatePlanRequest(
        ModuleStateInventory inventory,
        IEnumerable<ModuleStateDesiredModule>? desiredModules,
        IEnumerable<ModuleStateFamilyPolicy>? familyPolicies = null,
        IEnumerable<ModuleStateMaintenanceReceipt>? maintenanceReceipts = null,
        bool repair = false,
        ModuleStateCleanupMode cleanupMode = ModuleStateCleanupMode.None)
    {
        Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        DesiredModules = (desiredModules ?? Array.Empty<ModuleStateDesiredModule>()).Where(static module => module is not null).ToArray();
        FamilyPolicies = (familyPolicies ?? Array.Empty<ModuleStateFamilyPolicy>()).Where(static policy => policy is not null).ToArray();
        MaintenanceReceipts = (maintenanceReceipts ?? Array.Empty<ModuleStateMaintenanceReceipt>()).Where(static receipt => receipt is not null).ToArray();
        Repair = repair;
        CleanupMode = cleanupMode;
    }

    internal ModuleStateInventory Inventory { get; }

    internal ModuleStateDesiredModule[] DesiredModules { get; }

    internal ModuleStateFamilyPolicy[] FamilyPolicies { get; }

    internal ModuleStateMaintenanceReceipt[] MaintenanceReceipts { get; }

    internal bool Repair { get; }

    internal ModuleStateCleanupMode CleanupMode { get; }
}
