using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

internal sealed class ModuleStateCleanupPlanner
{
    internal ModuleStateCleanupPlan CreateCleanupPlan(ModuleStatePlanRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        if (request.CleanupMode == ModuleStateCleanupMode.None)
            return ModuleStateCleanupPlan.Empty;

        if (request.CleanupMode != ModuleStateCleanupMode.OldVersions)
            throw new NotSupportedException($"ModuleState cleanup mode '{request.CleanupMode}' is not supported.");

        var actions = new List<ModuleStatePlanAction>();
        var findings = new List<ModuleStateConflictFinding>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var moduleGroup in GetManagedModuleGroups(request))
        {
            var installedModules = moduleGroup.ToArray();
            var keepVersions = ResolveKeepVersions(request, moduleGroup.Key, installedModules);
            if (keepVersions.Count == 0)
                continue;

            foreach (var installedModule in installedModules)
            {
                if (keepVersions.Contains(installedModule.Version))
                    continue;

                if (installedModule.IsLoaded)
                {
                    findings.Add(CreateLoadedCleanupFinding(installedModule));
                    continue;
                }

                var key = string.Join("|", installedModule.Name, installedModule.Version, installedModule.Path ?? string.Empty);
                if (!seen.Add(key))
                    continue;

                actions.Add(new ModuleStatePlanAction(
                    ModuleStatePlanActionKind.Remove,
                    installedModule.Name,
                    installedModule.Version,
                    "cleanup:old-versions",
                    "Installed module version is older than the managed version selected by ModuleState cleanup.",
                    targetScope: installedModule.Scope,
                    targetPath: installedModule.Path));
            }
        }

        return new ModuleStateCleanupPlan(actions.ToArray(), findings.ToArray());
    }

    private static IEnumerable<IGrouping<string, ModuleStateInstalledModule>> GetManagedModuleGroups(ModuleStatePlanRequest request)
    {
        var selectedModules = new List<ModuleStateInstalledModule>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var desiredModule in request.DesiredModules)
        {
            AddCleanupCandidates(
                request.Inventory.InstalledModules.Where(module =>
                    string.Equals(module.Name, desiredModule.Name, StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrWhiteSpace(desiredModule.Scope) ||
                     string.Equals(module.Scope, desiredModule.Scope, StringComparison.OrdinalIgnoreCase)) &&
                    (string.IsNullOrWhiteSpace(desiredModule.TargetPath) || IsUnderTargetPath(module.Path, desiredModule.TargetPath!))),
                selectedModules,
                seen);
        }

        foreach (var receiptModule in request.MaintenanceReceipts.SelectMany(static receipt => receipt.Modules))
        {
            AddCleanupCandidates(
                request.Inventory.InstalledModules.Where(module =>
                    string.Equals(module.Name, receiptModule.Name, StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrWhiteSpace(receiptModule.Scope) ||
                     string.Equals(module.Scope, receiptModule.Scope, StringComparison.OrdinalIgnoreCase))),
                selectedModules,
                seen);
        }

        return selectedModules.GroupBy(static module => module.Name, StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> ResolveKeepVersions(
        ModuleStatePlanRequest request,
        string moduleName,
        ModuleStateInstalledModule[] installedModules)
    {
        var keepVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var receiptModule in request.MaintenanceReceipts.SelectMany(static receipt => receipt.Modules))
        {
            if (!string.Equals(receiptModule.Name, moduleName, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var installedModule in installedModules.Where(module =>
                         VersionsEquivalent(module.Version, receiptModule.Version) &&
                         (string.IsNullOrWhiteSpace(receiptModule.Scope) ||
                          string.Equals(module.Scope, receiptModule.Scope, StringComparison.OrdinalIgnoreCase))))
            {
                keepVersions.Add(installedModule.Version);
            }
        }

        foreach (var desiredModule in request.DesiredModules)
        {
            if (!string.Equals(desiredModule.Name, moduleName, StringComparison.OrdinalIgnoreCase))
                continue;

            var policy = ModuleStateVersionPolicy.Parse(desiredModule.VersionPolicy);
            var candidates = installedModules
                .Where(module =>
                    (string.IsNullOrWhiteSpace(desiredModule.Scope) ||
                     string.Equals(module.Scope, desiredModule.Scope, StringComparison.OrdinalIgnoreCase)) &&
                    (string.IsNullOrWhiteSpace(desiredModule.TargetPath) || IsUnderTargetPath(module.Path, desiredModule.TargetPath!)))
                .ToArray();
            if (candidates.Length == 0)
                continue;

            var selectedModule = candidates
                .Where(module => policy.IsSatisfiedBy(module.Version))
                .OrderByDescending(static module => ModuleStateVersion.TryParse(module.Version, out var version) ? version : default)
                .FirstOrDefault();
            selectedModule ??= candidates
                .OrderByDescending(static module => ModuleStateVersion.TryParse(module.Version, out var version) ? version : default)
                .FirstOrDefault();
            if (selectedModule is not null)
                keepVersions.Add(selectedModule.Version);
        }

        return keepVersions;
    }

    private static bool VersionsEquivalent(string installedVersion, string receiptVersion)
    {
        if (ModuleStateVersion.TryParse(installedVersion, out var installed) &&
            ModuleStateVersion.TryParse(receiptVersion, out var receipt))
        {
            return installed.Equals(receipt);
        }

        return string.Equals(installedVersion, receiptVersion, StringComparison.OrdinalIgnoreCase);
    }

    private static ModuleStateConflictFinding CreateLoadedCleanupFinding(ModuleStateInstalledModule installedModule)
        => new(
            ModuleStateConflictSeverity.Error,
            "ModuleState.CleanupLoadedVersion",
            $"Module '{installedModule.Name}' version {installedModule.Version} is loaded and was not selected for cleanup. Start a fresh process before removing loaded module versions.",
            string.Empty,
            new[] { installedModule.Name },
            new[] { installedModule.Version });

    private static void AddCleanupCandidates(
        IEnumerable<ModuleStateInstalledModule> candidates,
        List<ModuleStateInstalledModule> selectedModules,
        HashSet<string> seen)
    {
        foreach (var module in candidates)
        {
            var key = string.Join("|", module.Name, module.Version, module.Path ?? string.Empty);
            if (seen.Add(key))
                selectedModules.Add(module);
        }
    }

    private static bool IsUnderTargetPath(string? modulePath, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(modulePath))
            return false;

        var normalizedModulePath = NormalizePath(modulePath!);
        var normalizedTargetPath = NormalizePath(targetPath);
        return string.Equals(normalizedModulePath, normalizedTargetPath, StringComparison.OrdinalIgnoreCase) ||
               normalizedModulePath.StartsWith(normalizedTargetPath + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
        => path.Trim()
            .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
            .Replace('\\', '/');
}

internal sealed class ModuleStateCleanupPlan
{
    internal static ModuleStateCleanupPlan Empty { get; } = new(
        Array.Empty<ModuleStatePlanAction>(),
        Array.Empty<ModuleStateConflictFinding>());

    internal ModuleStateCleanupPlan(ModuleStatePlanAction[] actions, ModuleStateConflictFinding[] findings)
    {
        Actions = actions ?? Array.Empty<ModuleStatePlanAction>();
        Findings = findings ?? Array.Empty<ModuleStateConflictFinding>();
    }

    internal ModuleStatePlanAction[] Actions { get; }

    internal ModuleStateConflictFinding[] Findings { get; }
}
