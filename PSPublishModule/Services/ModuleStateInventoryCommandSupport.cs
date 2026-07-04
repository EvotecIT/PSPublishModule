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
            Array.Empty<string>());
    }

    internal static ModuleStateInventoryResult CreateInventoryResultFromModulePaths(
        IEnumerable<string> modulePaths,
        IEnumerable<ModuleStateLoadedModuleEvidence>? loadedModules = null,
        IEnumerable<string>? names = null,
        string? version = null,
        string? scope = null)
    {
        var paths = NormalizeModulePaths(modulePaths);
        var request = new ModuleStateInventoryRequest(paths.Select(static path => new ModuleStateModulePath(
            path,
            InferPowerShellEdition(path),
            InferScope(path))),
            names,
            version,
            scope);
        var inventory = new ModuleStateInventoryService().Collect(request);
        inventory = IncludeLoadedModulesCore(inventory, loadedModules);
        inventory = ModuleStateInventoryFilter.Apply(inventory, request);
        return ModuleStateInventoryResultMapper.ToCmdletResult(inventory, "ModulePath", paths);
    }

    internal static string[] NormalizeModulePaths(IEnumerable<string> modulePaths)
    {
        return (modulePaths ?? Array.Empty<string>())
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => Path.GetFullPath(path.Trim()))
            .Distinct(ResolveModulePathComparer())
            .ToArray();
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

        var normalizedPath = NormalizePath(path);
        var normalizedRoot = NormalizePath(root);
        return string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
        => Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

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
                    existing.ExportedCommands);
                continue;
            }

            modules.Add(new ModuleStateInstalledModule(
                loadedModule.Name!,
                loadedModule.Version!,
                path: loadedModule.Path,
                isLoaded: true,
                isEffectiveImportCandidate: false));
        }

        return new ModuleStateInventory(modules);
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
        var leftPath = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rightPath = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(leftPath, rightPath, StringComparison.OrdinalIgnoreCase) ||
               rightPath.StartsWith(leftPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               rightPath.StartsWith(leftPath + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
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
