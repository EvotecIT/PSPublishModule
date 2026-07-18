using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Runtime.InteropServices;
using PowerForge;

namespace PSPublishModule;

internal static class ModuleStateInventoryCommandSupport
{
    internal const string CurrentProcessModulePathVisibilityGroup = "CurrentProcessPSModulePath";

    internal static ModuleStateInventoryResult CreateInventoryResultFromFile(
        string inventoryPath,
        IEnumerable<ModuleStateLoadedModuleEvidence>? loadedModules = null,
        IEnumerable<string>? names = null,
        string? version = null,
        string? scope = null)
    {
        var inventory = new ModuleStateJsonService().LoadInventory(inventoryPath);
        inventory = IncludeLoadedModulesCore(inventory, loadedModules);
        inventory = ModuleStateInventoryFilter.Apply(inventory, new ModuleStateInventoryRequest(
            inventory.InstalledModules
                .Select(static module => module.Path)
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Select(static path => new ModuleStateModulePath(path!)),
            names,
            version,
            scope));
        return ModuleStateInventoryResultMapper.ToCmdletResult(
            inventory,
            inventoryPath,
            inventory.ModulePaths.Select(static path => path.Path).ToArray());
    }

    internal static ModuleStateInventoryResult CreateInventoryResultFromModulePaths(
        IEnumerable<string> modulePaths,
        IEnumerable<ModuleStateLoadedModuleEvidence>? loadedModules = null,
        IEnumerable<string>? names = null,
        string? version = null,
        string? scope = null,
        bool pathsRequired = true)
    {
        var pathEntries = CreateModulePathEntries(modulePaths, pathsRequired);
        return CreateInventoryResultFromModulePathEntries(
            pathEntries,
            loadedModules,
            names,
            version,
            scope,
            "ModulePath");
    }

    internal static ModuleStateModulePath[] CreateModulePathEntries(
        IEnumerable<string> modulePaths,
        bool pathsRequired,
        string? dependencyVisibilityGroup = null)
        => NormalizeModulePaths(modulePaths)
            .Select(path => new ModuleStateModulePath(
                path,
                InferPowerShellEdition(path),
                InferScope(path),
                isRequired: pathsRequired,
                dependencyVisibilityGroup: dependencyVisibilityGroup))
            .ToArray();

    internal static ModuleStateModulePath[] CreateEnvironmentModulePathEntries()
        => CreateModulePathEntries(
            ResolveEnvironmentModulePaths(),
            pathsRequired: false,
            dependencyVisibilityGroup: CurrentProcessModulePathVisibilityGroup);

    internal static ModuleStateInventoryResult CreateInventoryResultFromModulePathEntries(
        IEnumerable<ModuleStateModulePath> modulePaths,
        IEnumerable<ModuleStateLoadedModuleEvidence>? loadedModules = null,
        IEnumerable<string>? names = null,
        string? version = null,
        string? scope = null,
        string source = "ModulePath",
        IEnumerable<ModuleStateInventoryDiagnostic>? additionalDiagnostics = null)
    {
        var paths = NormalizeModulePathEntries(modulePaths);
        var request = new ModuleStateInventoryRequest(paths,
            names,
            version,
            scope);
        var inventory = new ModuleStateInventoryService().Collect(request);
        if (additionalDiagnostics is not null)
        {
            inventory = new ModuleStateInventory(
                inventory.InstalledModules,
                inventory.ModulePaths,
                inventory.Diagnostics.Concat(additionalDiagnostics));
        }
        inventory = IncludeLoadedModulesCore(inventory, loadedModules);
        inventory = ModuleStateInventoryFilter.Apply(inventory, request);
        return ModuleStateInventoryResultMapper.ToCmdletResult(
            inventory,
            source,
            paths.Select(static path => path.Path).ToArray());
    }

    internal static string[] NormalizeModulePaths(IEnumerable<string> modulePaths)
    {
        return (modulePaths ?? Array.Empty<string>())
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => Path.GetFullPath(path.Trim()))
            .Distinct(ResolveModulePathComparer())
            .ToArray();
    }

    internal static ModuleStateModulePath[] NormalizeModulePathEntries(IEnumerable<ModuleStateModulePath> modulePaths)
    {
        var normalized = new List<ModuleStateModulePath>();
        foreach (var path in modulePaths ?? Array.Empty<ModuleStateModulePath>())
        {
            if (path is null || string.IsNullOrWhiteSpace(path.Path))
                continue;

            var fullPath = Path.GetFullPath(path.Path.Trim());
            var existingIndex = normalized.FindIndex(existing => ModuleStatePathIdentity.Equals(existing.Path, fullPath));
            if (existingIndex >= 0)
            {
                var existing = normalized[existingIndex];
                normalized[existingIndex] = new ModuleStateModulePath(
                    fullPath,
                    path.PowerShellEdition ?? existing.PowerShellEdition,
                    path.Scope ?? existing.Scope,
                    path.ProfileName ?? existing.ProfileName,
                    path.IsRequired || existing.IsRequired,
                    path.WasAvailable || existing.WasAvailable,
                    path.DependencyVisibilityGroup ?? existing.DependencyVisibilityGroup);
                continue;
            }

            normalized.Add(new ModuleStateModulePath(
                fullPath,
                path.PowerShellEdition,
                path.Scope,
                path.ProfileName,
                path.IsRequired,
                path.WasAvailable,
                path.DependencyVisibilityGroup));
        }

        return normalized.ToArray();
    }

    internal static ModuleStateInventoryResult IncludeLoadedModules(
        ModuleStateInventoryResult inventory,
        IEnumerable<ModuleStateLoadedModuleEvidence>? loadedModules)
    {
        if (inventory is null)
            throw new ArgumentNullException(nameof(inventory));

        var loaded = (loadedModules ?? Array.Empty<ModuleStateLoadedModuleEvidence>())
            .Where(static module => !string.IsNullOrWhiteSpace(module.Name) && !string.IsNullOrWhiteSpace(module.Version))
            .ToArray();
        if (loaded.Length == 0)
            return inventory;

        var coreInventory = ModuleStateInventoryResultMapper.ToCoreInventory(inventory);
        var mergedInventory = IncludeLoadedModulesCore(coreInventory, loaded);
        return ModuleStateInventoryResultMapper.ToCmdletResult(
            mergedInventory,
            inventory.Source,
            inventory.ModulePaths ?? Array.Empty<string>());
    }

    internal static ModuleStateInventoryResult MergeWithModulePathEntries(
        ModuleStateInventoryResult inventory,
        IEnumerable<ModuleStateModulePath> modulePaths,
        IEnumerable<ModuleStateLoadedModuleEvidence>? loadedModules = null,
        string source = "SupplementalModulePath",
        IEnumerable<ModuleStateInventoryDiagnostic>? additionalDiagnostics = null)
    {
        if (inventory is null)
            throw new ArgumentNullException(nameof(inventory));

        var baseInventory = ModuleStateInventoryResultMapper.ToCoreInventory(
            IncludeLoadedModules(inventory, loadedModules));
        var supplementalResult = CreateInventoryResultFromModulePathEntries(
            modulePaths,
            loadedModules,
            source: source,
            additionalDiagnostics: additionalDiagnostics);
        var supplementalInventory = ModuleStateInventoryResultMapper.ToCoreInventory(supplementalResult);
        var authoritativeSupplementalRoots = supplementalInventory.ModulePaths
            .Where(static path => path.WasAvailable)
            .Select(static path => path.Path)
            .ToArray();
        var installedModules = baseInventory.InstalledModules
            .Where(module => !BelongsToAnyRoot(module, authoritativeSupplementalRoots))
            .Concat(supplementalInventory.InstalledModules)
            .GroupBy(CreateInstalledModuleIdentity, ModuleStatePathIdentity.Comparer)
            .Select(static group => group.Last())
            .ToArray();
        var mergedPaths = MergeInventoryModulePaths(
            baseInventory.ModulePaths,
            supplementalInventory.ModulePaths);
        installedModules = ModuleStateInventoryService.RecomputeEffectiveImportCandidates(installedModules, mergedPaths);
        var diagnostics = baseInventory.Diagnostics
            .Where(diagnostic => !BelongsToAnyRoot(diagnostic.Path, authoritativeSupplementalRoots))
            .Concat(supplementalInventory.Diagnostics)
            .GroupBy(CreateDiagnosticIdentity, ModuleStatePathIdentity.Comparer)
            .Select(static group => group.Last())
            .ToArray();
        var merged = new ModuleStateInventory(installedModules, mergedPaths, diagnostics);
        return ModuleStateInventoryResultMapper.ToCmdletResult(
            merged,
            string.IsNullOrWhiteSpace(inventory.Source) ? source : inventory.Source + "+" + source,
            mergedPaths.Select(static path => path.Path).ToArray());
    }

    private static ModuleStateModulePath[] MergeInventoryModulePaths(
        IEnumerable<ModuleStateModulePath> basePaths,
        IEnumerable<ModuleStateModulePath> supplementalPaths)
    {
        var supplemental = NormalizeModulePathEntries(supplementalPaths);
        return NormalizeModulePathEntries(basePaths.Concat(supplemental))
            .Select(path =>
            {
                var current = supplemental.LastOrDefault(candidate => ModuleStatePathIdentity.Equals(candidate.Path, path.Path));
                return current is null
                    ? path
                    : new ModuleStateModulePath(
                        path.Path,
                        current.PowerShellEdition ?? path.PowerShellEdition,
                        current.Scope ?? path.Scope,
                        current.ProfileName ?? path.ProfileName,
                        path.IsRequired || current.IsRequired,
                        current.WasAvailable,
                        current.DependencyVisibilityGroup ?? path.DependencyVisibilityGroup);
            })
            .ToArray();
    }

    private static bool BelongsToAnyRoot(
        ModuleStateInstalledModule module,
        IReadOnlyCollection<string> roots)
        => roots.Any(root =>
            (!string.IsNullOrWhiteSpace(module.ModuleRoot) && ModuleStatePathIdentity.Equals(module.ModuleRoot, root)) ||
            (!string.IsNullOrWhiteSpace(module.Path) && ModuleStatePathIdentity.IsSameOrChild(module.Path!, root)));

    private static bool BelongsToAnyRoot(string path, IReadOnlyCollection<string> roots)
        => roots.Any(root => ModuleStatePathIdentity.IsSameOrChild(path, root));

    internal static ModuleStateLoadedModuleEvidence[] GetLoadedModules(PSCmdlet cmdlet)
    {
        if (cmdlet is null)
            throw new ArgumentNullException(nameof(cmdlet));

        return cmdlet.InvokeCommand
            .InvokeScript("Get-Module | Select-Object -Property Name, Version, Path, @{ Name = 'Prerelease'; Expression = { if ($_.PrivateData -and $_.PrivateData.PSData) { $_.PrivateData.PSData.Prerelease } } }")
            .OfType<PSObject>()
            .Select(static item => new ModuleStateLoadedModuleEvidence(
                item.Properties["Name"]?.Value as string,
                ResolveLoadedModuleVersion(item),
                item.Properties["Path"]?.Value as string))
            .Where(static item => !string.IsNullOrWhiteSpace(item.Name) && !string.IsNullOrWhiteSpace(item.Version))
            .ToArray();
    }

    internal static string? ResolveLoadedModuleVersion(PSObject item)
    {
        if (item is null)
            return null;

        var version = item.Properties["Version"]?.Value?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(version))
            return version;

        if (ModuleStateVersion.TryParse(version!, out var parsed) && parsed.IsPrerelease)
            return version;

        var prerelease = item.Properties["Prerelease"]?.Value?.ToString()?.Trim();
        return string.IsNullOrWhiteSpace(prerelease)
            ? version
            : version + "-" + prerelease!.TrimStart('-');
    }

    internal static string[] ResolveEnvironmentModulePaths()
    {
        var value = Environment.GetEnvironmentVariable("PSModulePath");
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<string>();

        return value.Split(Path.PathSeparator)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path.Trim())
            .Distinct(ResolveModulePathComparer())
            .ToArray();
    }

    private static StringComparer ResolveModulePathComparer()
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static string CreateInstalledModuleIdentity(ModuleStateInstalledModule module)
        => string.Join(
            "|",
            module.Name.ToUpperInvariant(),
            module.Version.ToUpperInvariant(),
            string.IsNullOrWhiteSpace(module.Path)
                ? ModuleStatePathIdentity.CreateEstateKey(
                    module.PowerShellEdition,
                    module.Scope,
                    module.ModuleRoot,
                    module.ProfileName)
                : ModuleStatePathIdentity.Normalize(module.Path!));

    private static string CreateDiagnosticIdentity(ModuleStateInventoryDiagnostic diagnostic)
        => string.Join(
            "|",
            diagnostic.Code.ToUpperInvariant(),
            ModuleStatePathIdentity.Normalize(diagnostic.Path),
            diagnostic.Message);

    private static string? InferPowerShellEdition(string path)
    {
        if (path.IndexOf("WindowsPowerShell", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Desktop";
        if (path.IndexOf("PowerShell", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Core";

        return null;
    }

    private static string? InferScope(string path)
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles) &&
            (IsPathUnder(path, Path.Combine(programFiles, "PowerShell", "Modules")) ||
             IsPathUnder(path, Path.Combine(programFiles, "WindowsPowerShell", "Modules"))))
        {
            return "AllUsers";
        }

        if (IsPathUnder(path, "/usr/local/share/powershell/Modules") ||
            IsPathUnder(path, "/opt/microsoft/powershell/7/Modules") ||
            IsPathUnder(path, "/usr/share/powershell/Modules"))
        {
            return "AllUsers";
        }

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrWhiteSpace(documents) &&
            (IsPathUnder(path, Path.Combine(documents, "PowerShell", "Modules")) ||
             IsPathUnder(path, Path.Combine(documents, "WindowsPowerShell", "Modules"))))
        {
            return "CurrentUser";
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile) &&
            IsPathUnder(path, Path.Combine(userProfile, ".local", "share", "powershell", "Modules")))
        {
            return "CurrentUser";
        }

        return null;
    }

    private static bool IsPathUnder(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root))
            return false;

        return ModuleStatePathIdentity.IsSameOrChild(path, root);
    }

    private static ModuleStateInventory IncludeLoadedModulesCore(
        ModuleStateInventory inventory,
        IEnumerable<ModuleStateLoadedModuleEvidence>? loadedModules)
    {
        var loaded = (loadedModules ?? Array.Empty<ModuleStateLoadedModuleEvidence>())
            .Where(static module => !string.IsNullOrWhiteSpace(module.Name) && !string.IsNullOrWhiteSpace(module.Version))
            .ToArray();
        if (loaded.Length == 0)
            return inventory;

        var modules = inventory.InstalledModules.ToList();
        foreach (var loadedModule in loaded)
        {
            var index = modules.FindIndex(module =>
                string.Equals(module.Name, loadedModule.Name, StringComparison.OrdinalIgnoreCase) &&
                VersionsEqual(module.Version, loadedModule.Version!) &&
                (string.IsNullOrWhiteSpace(loadedModule.Path) ||
                 string.IsNullOrWhiteSpace(module.Path) ||
                 PathsMatch(module.Path!, loadedModule.Path!)));
            if (index >= 0)
            {
                var existing = modules[index];
                modules[index] = new ModuleStateInstalledModule(
                    existing.Name,
                    existing.Version,
                    existing.PowerShellEdition,
                    existing.Scope,
                    existing.Path,
                    existing.SourceRepository,
                    isLoaded: true,
                    isEffectiveImportCandidate: true,
                    existing.ExportedCommands,
                    existing.ModuleRoot,
                    existing.ProfileName);
                continue;
            }

            modules.Add(new ModuleStateInstalledModule(
                loadedModule.Name!,
                loadedModule.Version!,
                path: loadedModule.Path,
                isLoaded: true,
                isEffectiveImportCandidate: false));
        }

        return new ModuleStateInventory(modules, inventory.ModulePaths, inventory.Diagnostics);
    }

    private static bool VersionsEqual(string installedVersion, string loadedVersion)
    {
        if (ModuleStateVersion.TryParse(installedVersion, out var installed) &&
            ModuleStateVersion.TryParse(loadedVersion, out var loaded))
        {
            return installed.CompareTo(loaded) == 0;
        }

        return string.Equals(installedVersion, loadedVersion, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsMatch(string left, string right)
    {
        return ModuleStatePathIdentity.IsSameOrChild(right, left);
    }
}

internal sealed class ModuleStateLoadedModuleEvidence
{
    internal ModuleStateLoadedModuleEvidence(string? name, string? version, string? path = null)
    {
        Name = string.IsNullOrWhiteSpace(name) ? null : name!.Trim();
        Version = string.IsNullOrWhiteSpace(version) ? null : version!.Trim();
        Path = string.IsNullOrWhiteSpace(path) ? null : path!.Trim();
    }

    internal string? Name { get; }

    internal string? Version { get; }

    internal string? Path { get; }
}
