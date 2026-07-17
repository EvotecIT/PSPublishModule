using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Threading.Tasks;
using PowerForge;

namespace PSPublishModule;

public sealed partial class RepairManagedModuleCommand : AsyncPSCmdlet
{
    private ModuleStateModulePath? _explicitProfilePlacement;

    private ModuleStateInventoryResult ResolveInventory()
    {
        var loadedModules = IncludeLoaded.IsPresent
            ? ModuleStateInventoryCommandSupport.GetLoadedModules(this)
            : null;

        var basePaths = ModuleStateInventoryCommandSupport.CreateModulePathEntries(
            ModulePath is { Length: > 0 }
                ? ModulePath
                : ModuleStateInventoryCommandSupport.ResolveEnvironmentModulePaths(),
            pathsRequired: ModulePath is { Length: > 0 });
        var explicitTargetRoot = ResolveManagedDeliveryModuleRoot();
        var targetPaths = string.IsNullOrWhiteSpace(explicitTargetRoot)
            ? Array.Empty<ModuleStateModulePath>()
            : ModuleStateInventoryCommandSupport.CreateModulePathEntries(
                    new[] { explicitTargetRoot! },
                    pathsRequired: false)
                .Select(path => new ModuleStateModulePath(
                    path.Path,
                    path.PowerShellEdition,
                    Scope ?? path.Scope,
                    path.ProfileName,
                    isRequired: false))
                .ToArray();
        var profilePaths = (UserProfilePath ?? Array.Empty<string>())
            .Select(path => SessionState.Path.GetUnresolvedProviderPathFromPSPath(path))
            .ToArray();
        var profileDiscovery = new ModuleStateProfilePathDiscoveryService().Discover(
            profilePaths,
            IncludeAllUserProfiles.IsPresent);
        _explicitProfilePlacement = ResolveExplicitProfilePlacement(profilePaths, profileDiscovery.ModulePaths);
        var supplementalPaths = targetPaths.Concat(profileDiscovery.ModulePaths).ToArray();
        if (Inventory is not null)
        {
            return ModuleStateInventoryCommandSupport.MergeWithModulePathEntries(
                Inventory,
                supplementalPaths,
                loadedModules,
                source: "ExplicitTarget",
                additionalDiagnostics: profileDiscovery.Diagnostics);
        }
        if (!string.IsNullOrWhiteSpace(InventoryPath))
        {
            var artifactInventory = ModuleStateInventoryCommandSupport.CreateInventoryResultFromFile(
                ResolveFilePath(InventoryPath!, nameof(InventoryPath)),
                loadedModules);
            return ModuleStateInventoryCommandSupport.MergeWithModulePathEntries(
                artifactInventory,
                supplementalPaths,
                loadedModules,
                source: "ExplicitTarget",
                additionalDiagnostics: profileDiscovery.Diagnostics);
        }

        return ModuleStateInventoryCommandSupport.CreateInventoryResultFromModulePathEntries(
            basePaths.Concat(targetPaths).Concat(profileDiscovery.ModulePaths),
            loadedModules,
            source: profileDiscovery.ModulePaths.Length > 0 ? "ModulePath+UserProfile" : "ModulePath",
            additionalDiagnostics: profileDiscovery.Diagnostics);
    }

    private ModuleStateModulePath? ResolveExplicitProfilePlacement(
        IReadOnlyList<string> profilePaths,
        IReadOnlyList<ModuleStateModulePath> discoveredModulePaths)
    {
        if (!string.IsNullOrWhiteSpace(ModuleRoot) || profilePaths.Count != 1)
            return null;

        var edition = SessionState.PSVariable.GetValue("PSEdition")?.ToString();
        if (string.IsNullOrWhiteSpace(edition))
            edition = "Desktop";
        var profileName = new DirectoryInfo(profilePaths[0]).Name;
        return discoveredModulePaths.SingleOrDefault(path =>
            string.Equals(path.ProfileName, profileName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(path.PowerShellEdition, edition, StringComparison.OrdinalIgnoreCase));
    }

    private object CreateDesiredState(
        ModuleStateInstalledModuleResult[] selectedModules,
        ManagedModuleRequiredResourceTarget[] requiredResourceTargets,
        bool requiredResourceInputSupplied)
    {
        var desiredRepository = ResolveRepositoryName();
        var desiredRepositorySource = ResolveRepositorySource();
        var modules = new ArrayList();

        if (requiredResourceInputSupplied)
        {
            foreach (var target in FilterRequiredResourceTargets(requiredResourceTargets))
            {
                var module = new Hashtable(StringComparer.OrdinalIgnoreCase)
                {
                    ["Name"] = target.Name,
                    ["VersionPolicy"] = ResolveVersionPolicy(target)
                };
                var targetRepository = string.IsNullOrWhiteSpace(target.Repository)
                    ? desiredRepository
                    : ResolveRepositoryName(target.Repository);
                var targetRepositorySource = string.IsNullOrWhiteSpace(target.Repository)
                    ? desiredRepositorySource
                    : ResolveRepositorySource(target.Repository);
                if (!string.IsNullOrWhiteSpace(targetRepository))
                    module["Repository"] = targetRepository!;
                if (!string.IsNullOrWhiteSpace(targetRepositorySource))
                    module["RepositorySource"] = targetRepositorySource!;
                if (target.ScopeSpecified || !string.IsNullOrWhiteSpace(Scope))
                    module["Scope"] = target.Scope.ToString();
                if (target.IncludePrerelease)
                    module["Prerelease"] = true;
                if (target.Reinstall)
                    module["Reinstall"] = true;
                if (target.AllowClobber)
                    module["AllowClobber"] = true;
                if (target.AcceptLicense)
                    module["AcceptLicense"] = true;
                if (target.SkipDependencyCheck)
                    module["SkipDependencyCheck"] = true;

                var placements = FindSelectedPlacements(
                    selectedModules,
                    target.Name,
                    target.ScopeSpecified ? target.Scope.ToString() : Scope);
                if (placements.Length == 0)
                {
                    ApplyDesiredPlacement(module, placements);
                    modules.Add(module);
                }
                else
                {
                    foreach (var placement in placements)
                    {
                        var placedModule = CloneHashtable(module);
                        ApplyDesiredPlacement(placedModule, new[] { placement });
                        modules.Add(placedModule);
                    }
                }
            }

            return new Hashtable(StringComparer.OrdinalIgnoreCase)
            {
                ["Modules"] = modules
            };
        }

        foreach (var selected in selectedModules)
        {
            var module = new Hashtable(StringComparer.OrdinalIgnoreCase)
            {
                ["Name"] = selected.Name,
                ["VersionPolicy"] = ResolveVersionPolicy(selected)
            };
            if (!string.IsNullOrWhiteSpace(desiredRepository))
                module["Repository"] = desiredRepository!;
            if (!string.IsNullOrWhiteSpace(desiredRepositorySource))
                module["RepositorySource"] = desiredRepositorySource!;
            if (!string.IsNullOrWhiteSpace(Scope))
                module["Scope"] = Scope!;
            else if (!string.IsNullOrWhiteSpace(selected.Scope))
                module["Scope"] = selected.Scope!;
            ApplyDesiredPlacement(module, new[] { selected });

            modules.Add(module);
        }

        foreach (var missingName in ResolveMissingRequestedNames(selectedModules))
        {
            var module = new Hashtable(StringComparer.OrdinalIgnoreCase)
            {
                ["Name"] = missingName,
                ["VersionPolicy"] = ResolveMissingVersionPolicy()
            };
            if (!string.IsNullOrWhiteSpace(desiredRepository))
                module["Repository"] = desiredRepository!;
            if (!string.IsNullOrWhiteSpace(desiredRepositorySource))
                module["RepositorySource"] = desiredRepositorySource!;
            if (!string.IsNullOrWhiteSpace(Scope))
                module["Scope"] = Scope!;
            if (Prerelease.IsPresent)
                module["Prerelease"] = true;
            if (Force.IsPresent)
                module["Reinstall"] = true;
            if (AllowClobber.IsPresent)
                module["AllowClobber"] = true;
            if (AcceptLicense.IsPresent)
                module["AcceptLicense"] = true;
            if (SkipDependencyCheck.IsPresent)
                module["SkipDependencyCheck"] = true;
            ApplyDesiredPlacement(module, Array.Empty<ModuleStateInstalledModuleResult>());

            modules.Add(module);
        }

        return new Hashtable(StringComparer.OrdinalIgnoreCase)
        {
            ["Modules"] = modules
        };
    }

    private ModuleStateInstalledModuleResult[] FindSelectedPlacements(
        IEnumerable<ModuleStateInstalledModuleResult> selectedModules,
        string moduleName,
        string? scope)
        => selectedModules
            .Where(module => string.Equals(module.Name, moduleName, StringComparison.OrdinalIgnoreCase))
            .Where(module => string.IsNullOrWhiteSpace(scope) ||
                             string.Equals(module.Scope, scope, StringComparison.OrdinalIgnoreCase))
            .ToArray();

    private void ApplyDesiredPlacement(
        Hashtable module,
        IReadOnlyList<ModuleStateInstalledModuleResult> placements)
    {
        var explicitProfilePlacement = CanUseExplicitProfilePlacement(module)
            ? _explicitProfilePlacement
            : null;
        var moduleRoot = !string.IsNullOrWhiteSpace(ModuleRoot)
            ? ManagedModuleCommandSupport.ResolveProviderPath(this, ModuleRoot)
            : placements.Count == 1
                ? ResolveSelectedModuleRoot(placements[0])
                : explicitProfilePlacement?.Path;
        if (!string.IsNullOrWhiteSpace(moduleRoot))
            module["ModuleRoot"] = moduleRoot!;

        if (placements.Count != 1)
        {
            if (explicitProfilePlacement is not null)
            {
                if (!string.IsNullOrWhiteSpace(explicitProfilePlacement.PowerShellEdition))
                    module["PowerShellEdition"] = explicitProfilePlacement.PowerShellEdition!;
                if (!string.IsNullOrWhiteSpace(explicitProfilePlacement.ProfileName))
                    module["ProfileName"] = explicitProfilePlacement.ProfileName!;
                if (!module.ContainsKey("Scope"))
                    module["Scope"] = "CurrentUser";
            }
            return;
        }

        var placement = placements[0];
        if (!string.IsNullOrWhiteSpace(placement.PowerShellEdition))
            module["PowerShellEdition"] = placement.PowerShellEdition!;
        if (!string.IsNullOrWhiteSpace(placement.ProfileName))
            module["ProfileName"] = placement.ProfileName!;
    }

    private bool CanUseExplicitProfilePlacement(Hashtable module)
    {
        if (_explicitProfilePlacement is null)
            return false;

        var requestedScope = module["Scope"]?.ToString();
        return string.IsNullOrWhiteSpace(requestedScope) ||
               string.Equals(requestedScope, "CurrentUser", StringComparison.OrdinalIgnoreCase);
    }

    private static Hashtable CloneHashtable(Hashtable source)
    {
        var clone = new Hashtable(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in source)
            clone[entry.Key] = entry.Value;
        return clone;
    }

    private IEnumerable<ManagedModuleRequiredResourceTarget> ResolveRequiredResourceTargets()
    {
        var resource = RequiredResource;
        if (resource is null && !string.IsNullOrWhiteSpace(RequiredResourceFile))
            resource = ManagedModuleRequiredResourceSupport.ImportRequiredResourceFile(this, RequiredResourceFile);
        if (resource is null)
            return Array.Empty<ManagedModuleRequiredResourceTarget>();

        var defaults = new ManagedModuleRequiredResourceDefaults(
            Prerelease.IsPresent,
            ParseInstallScope(Scope),
            Force.IsPresent,
            AllowClobber.IsPresent,
            AcceptLicense.IsPresent,
            SkipDependencyCheck.IsPresent);
        return ManagedModuleRequiredResourceSupport.Parse(resource, defaults);
    }

    private string[] ResolveMissingRequestedNames(IReadOnlyList<ModuleStateInstalledModuleResult> selectedModules)
    {
        if (!InstallMissing.IsPresent || Name.Length == 0)
            return Array.Empty<string>();

        var installed = selectedModules
            .Select(static module => module.Name)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Name
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name.Trim())
            .Where(static name => !ManagedModuleCommandSupport.HasWildcard(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => !installed.Contains(name))
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private ManagedModuleRequiredResourceTarget[] FilterRequiredResourceTargets(
        ManagedModuleRequiredResourceTarget[] requiredResourceTargets)
    {
        var filters = CreateNameFilters();
        if (filters.Length == 0)
            return requiredResourceTargets;

        return requiredResourceTargets
            .Where(target => filters.Any(filter => filter.IsMatch(target.Name)))
            .ToArray();
    }

    private WildcardPattern[] CreateNameFilters()
        => Name
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => new WildcardPattern(name.Trim(), WildcardOptions.IgnoreCase))
            .ToArray();

    private static string? ResolveSelectedModuleRoot(ModuleStateInstalledModuleResult selected)
    {
        if (!string.IsNullOrWhiteSpace(selected.ModuleRoot))
            return selected.ModuleRoot;
        if (string.IsNullOrWhiteSpace(selected.Path))
            return null;

        var selectedDirectory = new DirectoryInfo(selected.Path!);
        if (string.Equals(selectedDirectory.Name, selected.Name, StringComparison.OrdinalIgnoreCase))
            return selectedDirectory.Parent?.FullName;

        var moduleDirectory = selectedDirectory.Parent;
        if (moduleDirectory is null)
            return null;

        return string.Equals(moduleDirectory.Name, selected.Name, StringComparison.OrdinalIgnoreCase)
            ? moduleDirectory.Parent?.FullName
            : null;
    }

    private ModuleStateInstalledModuleResult[] SelectBaselineModules(ModuleStateInventoryResult inventory)
    {
        var modules = inventory.InstalledModules ?? Array.Empty<ModuleStateInstalledModuleResult>();
        var filters = CreateNameFilters();

        if (filters.Length > 0)
            modules = modules.Where(module => filters.Any(filter => filter.IsMatch(module.Name))).ToArray();
        if (!string.IsNullOrWhiteSpace(Scope))
            modules = modules.Where(module => string.Equals(module.Scope, Scope, StringComparison.OrdinalIgnoreCase)).ToArray();
        var targetModuleRoot = ResolveManagedDeliveryModuleRoot();
        if (!string.IsNullOrWhiteSpace(targetModuleRoot))
        {
            modules = modules
                .Where(module => ModuleStatePathIdentity.Equals(ResolveSelectedModuleRoot(module), targetModuleRoot))
                .ToArray();
        }

        return modules
            .GroupBy(CreateSelectedPlacementKey, StringComparer.Ordinal)
            .Select(group => SelectInventoryModule(group, Scope))
            .Where(static module => module is not null)
            .Cast<ModuleStateInstalledModuleResult>()
            .OrderBy(static module => module.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static module => module.Scope, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static module => module.PowerShellEdition, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static module => module.ModuleRoot, ModuleStatePathIdentity.Comparer)
            .ToArray();
    }

    private static string CreateSelectedPlacementKey(ModuleStateInstalledModuleResult module)
        => ModuleStatePathIdentity.CreatePlacementKey(
            module.Name,
            module.PowerShellEdition,
            module.Scope,
            module.ModuleRoot ?? ResolveSelectedModuleRoot(module));

    private static ModuleStateInstalledModuleResult? SelectInventoryModule(
        IEnumerable<ModuleStateInstalledModuleResult> modules,
        string? scope)
    {
        var candidates = modules
            .Where(module => string.IsNullOrWhiteSpace(scope) ||
                             string.Equals(module.Scope, scope, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return candidates
            .Where(static module => module.IsEffectiveImportCandidate)
            .OrderByDescending(static module => ModuleStateVersion.TryParse(module.Version, out var version) ? version : default)
            .FirstOrDefault()
            ?? candidates
                .OrderByDescending(static module => ModuleStateVersion.TryParse(module.Version, out var version) ? version : default)
                .FirstOrDefault();
    }

    private string ResolveVersionPolicy(ModuleStateInstalledModuleResult selected)
    {
        if (Latest.IsPresent)
            return "*";
        if (!string.IsNullOrWhiteSpace(Version))
            return "=" + Version!.Trim();
        if (!string.IsNullOrWhiteSpace(MinimumVersion))
            return ">=" + MinimumVersion!.Trim();
        if (!string.IsNullOrWhiteSpace(VersionPolicy))
            return VersionPolicy!.Trim();

        return string.IsNullOrWhiteSpace(selected.Version)
            ? "*"
            : "=" + selected.Version.Trim();
    }

    private string ResolveMissingVersionPolicy()
    {
        if (Latest.IsPresent)
            return "*";
        if (!string.IsNullOrWhiteSpace(Version))
            return "=" + Version!.Trim();
        if (!string.IsNullOrWhiteSpace(MinimumVersion))
            return ">=" + MinimumVersion!.Trim();
        if (!string.IsNullOrWhiteSpace(VersionPolicy))
            return VersionPolicy!.Trim();

        return "*";
    }

    private string ResolveVersionPolicy(ManagedModuleRequiredResourceTarget target)
    {
        if (!string.IsNullOrWhiteSpace(target.Version))
            return "=" + target.Version!.Trim();
        if (!string.IsNullOrWhiteSpace(target.VersionPolicy))
            return target.VersionPolicy!.Trim();
        if (!string.IsNullOrWhiteSpace(Version))
            return "=" + Version!.Trim();
        if (!string.IsNullOrWhiteSpace(MinimumVersion))
            return ">=" + MinimumVersion!.Trim();
        if (!string.IsNullOrWhiteSpace(VersionPolicy))
            return VersionPolicy!.Trim();

        return "*";
    }
}
