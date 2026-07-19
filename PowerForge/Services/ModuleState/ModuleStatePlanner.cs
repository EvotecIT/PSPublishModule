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
        var placementFindings = new List<ModuleStateConflictFinding>();
        foreach (var desiredModule in request.DesiredModules)
        {
            var installedModules = SelectInstalledModules(request.Inventory, desiredModule.Name);
            var targetModuleRoot = ResolveTargetModuleRoot(request, desiredModule);
            var actionTargetPath = desiredModule.TargetPath ?? targetModuleRoot;
            var installedModule = SelectInstalledModule(
                installedModules,
                desiredModule.Scope,
                actionTargetPath,
                desiredModule.PowerShellEdition,
                desiredModule.ProfileName);
            var versionPolicy = ModuleStateVersionPolicy.Parse(desiredModule.VersionPolicy, desiredModule.IncludePrerelease);
            var targetRepository = ResolveTargetRepository(desiredModule);
            var targetRepositorySource = desiredModule.TargetRepositorySource;
            if (installedModule is null)
            {
                var ambiguityFinding = CreateAmbiguousPlacementFinding(request, desiredModule);
                if (ambiguityFinding is not null)
                    placementFindings.Add(ambiguityFinding);
                actions.Add(new ModuleStatePlanAction(
                    ResolveMissingActionKind(desiredModule),
                    desiredModule.Name,
                    installedVersion: null,
                    desiredModule.VersionPolicy,
                    !string.IsNullOrWhiteSpace(desiredModule.TargetPath)
                        ? "Module is not saved in desired target path."
                        : string.IsNullOrWhiteSpace(desiredModule.Scope)
                        ? "Module is not installed."
                        : "Module is not installed in desired scope.",
                    force: desiredModule.Force,
                    targetScope: desiredModule.Scope,
                    targetPath: actionTargetPath,
                    targetRepository: targetRepository,
                    expectedPackageSha256: desiredModule.ExpectedPackageSha256,
                    includePrerelease: desiredModule.IncludePrerelease,
                    acceptLicense: desiredModule.AcceptLicense,
                    allowClobber: desiredModule.AllowClobber,
                    skipDependencyCheck: desiredModule.SkipDependencyCheck,
                    targetRepositorySource: targetRepositorySource,
                    targetModuleRoot: targetModuleRoot,
                    targetPowerShellEdition: desiredModule.PowerShellEdition,
                    targetProfileName: desiredModule.ProfileName));
                continue;
            }

            if (!versionPolicy.IsSatisfiedBy(installedModule.Version))
            {
                actions.Add(new ModuleStatePlanAction(
                    ResolveVersionActionKind(desiredModule),
                    desiredModule.Name,
                    installedModule.Version,
                    desiredModule.VersionPolicy,
                    "Installed module version does not satisfy desired policy.",
                    force: desiredModule.Force,
                    targetScope: desiredModule.Scope,
                    targetPath: actionTargetPath,
                    targetRepository: targetRepository,
                    expectedPackageSha256: desiredModule.ExpectedPackageSha256,
                    includePrerelease: desiredModule.IncludePrerelease,
                    acceptLicense: desiredModule.AcceptLicense,
                    allowClobber: desiredModule.AllowClobber,
                    skipDependencyCheck: desiredModule.SkipDependencyCheck,
                    targetRepositorySource: targetRepositorySource,
                    targetModuleRoot: targetModuleRoot ?? ModuleStatePathIdentity.ResolveModuleRoot(installedModule),
                    targetPowerShellEdition: desiredModule.PowerShellEdition ?? installedModule.PowerShellEdition,
                    targetProfileName: desiredModule.ProfileName ?? installedModule.ProfileName));
                continue;
            }

            if (NeedsSourceDelivery(installedModule, desiredModule, targetRepository))
            {
                actions.Add(new ModuleStatePlanAction(
                    ResolveSourceDeliveryActionKind(desiredModule),
                    desiredModule.Name,
                    installedModule.Version,
                    desiredModule.VersionPolicy,
                    "Installed module version satisfies desired policy but source repository does not match desired state.",
                    force: true,
                    targetScope: string.IsNullOrWhiteSpace(desiredModule.Scope) ? installedModule.Scope : desiredModule.Scope,
                    targetPath: actionTargetPath,
                    targetRepository: targetRepository,
                    expectedPackageSha256: desiredModule.ExpectedPackageSha256,
                    includePrerelease: desiredModule.IncludePrerelease,
                    acceptLicense: desiredModule.AcceptLicense,
                    allowClobber: desiredModule.AllowClobber,
                    skipDependencyCheck: desiredModule.SkipDependencyCheck,
                    targetRepositorySource: targetRepositorySource,
                    targetModuleRoot: targetModuleRoot ?? ModuleStatePathIdentity.ResolveModuleRoot(installedModule),
                    targetPowerShellEdition: desiredModule.PowerShellEdition ?? installedModule.PowerShellEdition,
                    targetProfileName: desiredModule.ProfileName ?? installedModule.ProfileName));
                continue;
            }

            if (!string.IsNullOrWhiteSpace(desiredModule.ExpectedPackageSha256))
            {
                actions.Add(new ModuleStatePlanAction(
                    ResolveSourceDeliveryActionKind(desiredModule),
                    desiredModule.Name,
                    installedModule.Version,
                    desiredModule.VersionPolicy,
                    "Installed module version satisfies desired policy but package hash verification is required.",
                    force: true,
                    targetScope: string.IsNullOrWhiteSpace(desiredModule.Scope) ? installedModule.Scope : desiredModule.Scope,
                    targetPath: actionTargetPath,
                    targetRepository: targetRepository,
                    expectedPackageSha256: desiredModule.ExpectedPackageSha256,
                    includePrerelease: desiredModule.IncludePrerelease,
                    acceptLicense: desiredModule.AcceptLicense,
                    allowClobber: desiredModule.AllowClobber,
                    skipDependencyCheck: desiredModule.SkipDependencyCheck,
                    targetRepositorySource: targetRepositorySource,
                    targetModuleRoot: targetModuleRoot ?? ModuleStatePathIdentity.ResolveModuleRoot(installedModule),
                    targetPowerShellEdition: desiredModule.PowerShellEdition ?? installedModule.PowerShellEdition,
                    targetProfileName: desiredModule.ProfileName ?? installedModule.ProfileName));
                continue;
            }

            if (desiredModule.Force)
            {
                actions.Add(new ModuleStatePlanAction(
                    ResolveSourceDeliveryActionKind(desiredModule),
                    desiredModule.Name,
                    installedModule.Version,
                    desiredModule.VersionPolicy,
                    "Desired state requested reinstall of the selected module version.",
                    force: true,
                    targetScope: string.IsNullOrWhiteSpace(desiredModule.Scope) ? installedModule.Scope : desiredModule.Scope,
                    targetPath: actionTargetPath,
                    targetRepository: targetRepository,
                    expectedPackageSha256: desiredModule.ExpectedPackageSha256,
                    includePrerelease: desiredModule.IncludePrerelease,
                    acceptLicense: desiredModule.AcceptLicense,
                    allowClobber: desiredModule.AllowClobber,
                    skipDependencyCheck: desiredModule.SkipDependencyCheck,
                    targetRepositorySource: targetRepositorySource,
                    targetModuleRoot: targetModuleRoot ?? ModuleStatePathIdentity.ResolveModuleRoot(installedModule),
                    targetPowerShellEdition: desiredModule.PowerShellEdition ?? installedModule.PowerShellEdition,
                    targetProfileName: desiredModule.ProfileName ?? installedModule.ProfileName));
                continue;
            }

            actions.Add(new ModuleStatePlanAction(
                ModuleStatePlanActionKind.NoAction,
                desiredModule.Name,
                installedModule.Version,
                desiredModule.VersionPolicy,
                "Installed module version satisfies desired policy.",
                targetScope: desiredModule.Scope,
                targetPath: actionTargetPath,
                targetRepository: targetRepository,
                expectedPackageSha256: desiredModule.ExpectedPackageSha256,
                includePrerelease: desiredModule.IncludePrerelease,
                acceptLicense: desiredModule.AcceptLicense,
                allowClobber: desiredModule.AllowClobber,
                skipDependencyCheck: desiredModule.SkipDependencyCheck,
                targetRepositorySource: targetRepositorySource,
                targetModuleRoot: targetModuleRoot ?? ModuleStatePathIdentity.ResolveModuleRoot(installedModule),
                targetPowerShellEdition: desiredModule.PowerShellEdition ?? installedModule.PowerShellEdition,
                targetProfileName: desiredModule.ProfileName ?? installedModule.ProfileName));
        }

        var plannedActions = request.Repair
            ? _repairPlanner.CreateRepairActions(request.Inventory, request.MaintenanceReceipts, actions, request.FamilyPolicies)
            : actions.ToArray();
        var cleanupPlan = _cleanupPlanner.CreateCleanupPlan(request);
        var finalActions = plannedActions.Concat(cleanupPlan.Actions).ToArray();
        var repairActionPlacementFindings = finalActions
            .Select(action => CreateRepairActionPlacementFinding(request, action))
            .Where(static finding => finding is not null)
            .Cast<ModuleStateConflictFinding>();

        var inventoryFindings = request.Inventory.Diagnostics.Select(static diagnostic => new ModuleStateConflictFinding(
            diagnostic.Severity,
            diagnostic.Code,
            diagnostic.Message,
            string.Empty,
            Array.Empty<string>(),
            Array.Empty<string>(),
            diagnostic.Scope,
            path: diagnostic.Path,
            powerShellEdition: diagnostic.PowerShellEdition,
            profileName: diagnostic.ProfileName));
        var findings = inventoryFindings
            .Concat(placementFindings)
            .Concat(repairActionPlacementFindings)
            .Concat(_familyAnalyzer
            .Analyze(request.Inventory, request.FamilyPolicies)
            .Concat(_conflictAnalyzer.Analyze(
                request.Inventory,
                request.DesiredModules,
                includeCrossScopeCommandConflicts: request.Repair))
            .Concat(_receiptAnalyzer.Analyze(request.Inventory, request.MaintenanceReceipts))
            .Concat(cleanupPlan.Findings))
            .ToArray();
        return new ModuleStatePlan(finalActions, DowngradeActionCoveredFindings(findings, finalActions));
    }

    private static ModuleStateInstalledModule[] SelectInstalledModules(ModuleStateInventory inventory, string moduleName)
        => inventory.InstalledModules
            .Where(module => string.Equals(module.Name, moduleName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

    private static ModuleStateInstalledModule? SelectInstalledModule(
        IEnumerable<ModuleStateInstalledModule> installedModules,
        string? desiredScope,
        string? targetPath,
        string? powerShellEdition,
        string? profileName)
    {
        var candidates = installedModules
            .Where(module =>
                (string.IsNullOrWhiteSpace(desiredScope)
                 || string.Equals(module.Scope, desiredScope, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(targetPath) || ModuleStatePathIdentity.IsSameOrChild(module.Path, targetPath)) &&
                (string.IsNullOrWhiteSpace(powerShellEdition) ||
                 string.Equals(module.PowerShellEdition, powerShellEdition, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(profileName) ||
                 string.Equals(module.ProfileName, profileName, StringComparison.OrdinalIgnoreCase)))
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

    private static string? ResolveTargetModuleRoot(
        ModuleStatePlanRequest request,
        ModuleStateDesiredModule desiredModule)
    {
        if (!string.IsNullOrWhiteSpace(desiredModule.ModuleRoot))
            return desiredModule.ModuleRoot;
        if (!request.Repair || !string.IsNullOrWhiteSpace(desiredModule.TargetPath))
            return null;

        var candidates = SelectPlacementPaths(request.Inventory, desiredModule).ToArray();
        return candidates.Length == 1 ? candidates[0].Path : null;
    }

    private static ModuleStateConflictFinding? CreateAmbiguousPlacementFinding(
        ModuleStatePlanRequest request,
        ModuleStateDesiredModule desiredModule)
    {
        if (!request.Repair ||
            !string.IsNullOrWhiteSpace(desiredModule.ModuleRoot) ||
            !string.IsNullOrWhiteSpace(desiredModule.TargetPath))
        {
            return null;
        }

        var candidates = SelectPlacementPaths(request.Inventory, desiredModule).ToArray();
        if (candidates.Length == 1)
            return null;

        if (candidates.Length == 0)
        {
            return new ModuleStateConflictFinding(
                ModuleStateConflictSeverity.Error,
                "ModuleState.RepairTargetMissing",
                $"Module '{desiredModule.Name}' is missing from the selected inventory and no eligible module root is available. Specify ModuleRoot or include exactly one target through ModulePath or profile selection before applying repair.",
                string.Empty,
                new[] { desiredModule.Name },
                Array.Empty<string>(),
                desiredModule.Scope,
                powerShellEdition: desiredModule.PowerShellEdition,
                profileName: desiredModule.ProfileName);
        }

        return new ModuleStateConflictFinding(
            ModuleStateConflictSeverity.Error,
            "ModuleState.AmbiguousRepairTarget",
            $"Module '{desiredModule.Name}' is missing from the selected inventory, but {candidates.Length} module roots are eligible. Specify ModuleRoot, narrow ModulePath, Scope, PowerShellEdition, or profile selection before applying repair.",
            string.Empty,
            new[] { desiredModule.Name },
            Array.Empty<string>(),
            desiredModule.Scope,
            path: string.Join("; ", candidates.Select(static candidate => candidate.Path)),
            powerShellEdition: desiredModule.PowerShellEdition,
            profileName: desiredModule.ProfileName);
    }

    private static ModuleStateConflictFinding? CreateRepairActionPlacementFinding(
        ModuleStatePlanRequest request,
        ModuleStatePlanAction action)
    {
        if (!request.Repair ||
            !action.IsRepair ||
            action.Kind != ModuleStatePlanActionKind.Install ||
            !string.IsNullOrWhiteSpace(action.TargetModuleRoot) ||
            !string.IsNullOrWhiteSpace(action.TargetPath))
        {
            return null;
        }

        var candidates = request.Inventory.ModulePaths
            .Where(path =>
                (string.IsNullOrWhiteSpace(action.TargetScope) ||
                 string.Equals(path.Scope, action.TargetScope, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(action.TargetPowerShellEdition) ||
                 string.Equals(path.PowerShellEdition, action.TargetPowerShellEdition, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(action.TargetProfileName) ||
                 string.Equals(path.ProfileName, action.TargetProfileName, StringComparison.OrdinalIgnoreCase)))
            .Take(2)
            .ToArray();
        if (candidates.Length == 1)
            return null;

        var code = candidates.Length == 0
            ? "ModuleState.RepairTargetMissing"
            : "ModuleState.AmbiguousRepairTarget";
        var message = candidates.Length == 0
            ? $"Repair requires installing missing module '{action.ModuleName}', but no eligible module root is available. Specify ModuleRoot or include exactly one target through ModulePath or profile selection."
            : $"Repair requires installing missing module '{action.ModuleName}', but multiple module roots are eligible. Specify ModuleRoot, narrow ModulePath, Scope, PowerShellEdition, or profile selection before applying repair.";
        return new ModuleStateConflictFinding(
            ModuleStateConflictSeverity.Error,
            code,
            message,
            string.Empty,
            new[] { action.ModuleName },
            Array.Empty<string>(),
            action.TargetScope,
            path: string.Join("; ", candidates.Select(static candidate => candidate.Path)),
            powerShellEdition: action.TargetPowerShellEdition,
            profileName: action.TargetProfileName);
    }

    private static IEnumerable<ModuleStateModulePath> SelectPlacementPaths(
        ModuleStateInventory inventory,
        ModuleStateDesiredModule desiredModule)
        => inventory.ModulePaths.Where(path =>
            (string.IsNullOrWhiteSpace(desiredModule.Scope) ||
             string.Equals(path.Scope, desiredModule.Scope, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(desiredModule.PowerShellEdition) ||
             string.Equals(path.PowerShellEdition, desiredModule.PowerShellEdition, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(desiredModule.ProfileName) ||
             string.Equals(path.ProfileName, desiredModule.ProfileName, StringComparison.OrdinalIgnoreCase)));

    private static ModuleStatePlanActionKind ResolveMissingActionKind(ModuleStateDesiredModule desiredModule)
        => string.IsNullOrWhiteSpace(desiredModule.TargetPath)
            ? ModuleStatePlanActionKind.Install
            : ModuleStatePlanActionKind.Save;

    private static ModuleStatePlanActionKind ResolveVersionActionKind(ModuleStateDesiredModule desiredModule)
        => string.IsNullOrWhiteSpace(desiredModule.TargetPath)
            ? ModuleStatePlanActionKind.Update
            : ModuleStatePlanActionKind.Save;

    private static ModuleStatePlanActionKind ResolveSourceDeliveryActionKind(ModuleStateDesiredModule desiredModule)
        => string.IsNullOrWhiteSpace(desiredModule.TargetPath)
            ? ModuleStatePlanActionKind.Install
            : ModuleStatePlanActionKind.Save;

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
                    finding.SourceRepository,
                    finding.Path,
                    finding.PowerShellEdition,
                    finding.ProfileName)
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
            IsDeliveryAction(action.Kind) &&
            (!requireRepair || action.IsRepair) &&
            moduleNames.Contains(action.ModuleName, StringComparer.OrdinalIgnoreCase));

    private static bool IsSourcePreferenceCoveredByAction(ModuleStateConflictFinding finding, ModuleStatePlanAction[] actions)
        => actions.Any(action =>
            IsDeliveryAction(action.Kind) &&
            finding.ModuleNames.Contains(action.ModuleName, StringComparer.OrdinalIgnoreCase) &&
            string.Equals(action.TargetScope, finding.Scope, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(action.TargetRepository) &&
            !string.Equals(action.TargetRepository, finding.SourceRepository, StringComparison.OrdinalIgnoreCase));

    private static bool IsDeliveryAction(ModuleStatePlanActionKind kind)
        => kind is ModuleStatePlanActionKind.Install or ModuleStatePlanActionKind.Update or ModuleStatePlanActionKind.Save;
}
