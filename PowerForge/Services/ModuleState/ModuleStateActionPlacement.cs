using System;
using System.Linq;

namespace PowerForge;

/// <summary>Resolves the physical delivery root carried by a module-state action.</summary>
internal static class ModuleStateActionPlacement
{
    internal static string? ResolveDeliveryRoot(ModuleStatePlanAction action, string? fallbackRoot = null)
    {
        if (action is null)
            throw new ArgumentNullException(nameof(action));

        return ResolveDeliveryRoot(action.Kind, action.TargetPath, action.TargetModuleRoot, fallbackRoot);
    }

    internal static string? ResolveDeliveryRoot(
        ModuleStatePlanActionKind actionKind,
        string? targetPath,
        string? targetModuleRoot,
        string? fallbackRoot = null)
    {
        var actionRoot = actionKind == ModuleStatePlanActionKind.Save
            ? FirstNonEmpty(targetPath, targetModuleRoot)
            : FirstNonEmpty(targetModuleRoot, targetPath);
        return FirstNonEmpty(actionRoot, fallbackRoot);
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
}
