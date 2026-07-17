using System;
using System.Collections.Generic;
using System.Linq;
using PowerForge;

namespace PSPublishModule;

internal static class ModuleStateDependencyRootGroupResolver
{
    internal static IReadOnlyList<IReadOnlyList<string>> Resolve(
        IEnumerable<ModuleStateInventoryPathResult>? inventoryPaths,
        string? targetPowerShellEdition,
        string? targetScope,
        string? targetProfileName,
        string targetModuleRoot)
    {
        var eligiblePaths = (inventoryPaths ?? Array.Empty<ModuleStateInventoryPathResult>())
            .Where(path => string.IsNullOrWhiteSpace(targetPowerShellEdition) ||
                           string.IsNullOrWhiteSpace(path.PowerShellEdition) ||
                           string.Equals(path.PowerShellEdition, targetPowerShellEdition, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var targetIsAnonymousScopedRoot = string.IsNullOrWhiteSpace(targetProfileName) &&
                                          (string.Equals(targetScope, "CurrentUser", StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(targetScope, "Custom", StringComparison.OrdinalIgnoreCase));
        var profileNames = targetIsAnonymousScopedRoot
            ? Array.Empty<string>()
            : eligiblePaths
                .Select(static path => path.ProfileName)
                .Where(static profileName => !string.IsNullOrWhiteSpace(profileName))
                .Select(static profileName => profileName!)
                .Where(profileName => string.IsNullOrWhiteSpace(targetProfileName) ||
                                      string.Equals(profileName, targetProfileName, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        var sharedPaths = eligiblePaths
            .Where(static path => string.Equals(path.Scope, "AllUsers", StringComparison.OrdinalIgnoreCase))
            .Select(static path => path.Path)
            .ToArray();
        var groups = profileNames
            .Select(profileName => NormalizeRoots(
                sharedPaths.Concat(eligiblePaths
                    .Where(path => string.Equals(path.ProfileName, profileName, StringComparison.OrdinalIgnoreCase))
                    .Select(static path => path.Path)),
                targetModuleRoot))
            .Cast<IReadOnlyList<string>>()
            .ToList();
        var anonymousProfilePaths = eligiblePaths
            .Where(static path => string.IsNullOrWhiteSpace(path.ProfileName) &&
                                  !string.Equals(path.Scope, "AllUsers", StringComparison.OrdinalIgnoreCase))
            .Select(static path => path.Path)
            .ToArray();
        if (string.IsNullOrWhiteSpace(targetProfileName))
        {
            groups.AddRange(anonymousProfilePaths.Select(path =>
                (IReadOnlyList<string>)NormalizeRoots(sharedPaths.Append(path), targetModuleRoot)));
        }
        if (groups.Count == 0)
            groups.Add(NormalizeRoots(sharedPaths, targetModuleRoot));
        return groups;
    }

    private static string[] NormalizeRoots(IEnumerable<string> roots, string targetModuleRoot)
        => roots
            .Append(targetModuleRoot)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(ModuleStatePathIdentity.Comparer)
            .ToArray();
}
