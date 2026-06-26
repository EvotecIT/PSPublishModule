using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

internal static class ModuleStateInventoryCommandSupport
{
    internal static ModuleStateInventoryResult CreateInventoryResultFromFile(
        string inventoryPath,
        IEnumerable<ModuleStateLoadedModuleEvidence>? loadedModules = null)
    {
        var inventory = new ModuleStateJsonService().LoadInventory(inventoryPath);
        inventory = IncludeLoadedModules(inventory, loadedModules);
        return ModuleStateInventoryResultMapper.ToCmdletResult(
            inventory,
            inventoryPath,
            Array.Empty<string>());
    }

    internal static ModuleStateInventoryResult CreateInventoryResultFromModulePaths(
        IEnumerable<string> modulePaths,
        IEnumerable<ModuleStateLoadedModuleEvidence>? loadedModules = null)
    {
        var paths = modulePaths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => Path.GetFullPath(path.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var request = new ModuleStateInventoryRequest(paths.Select(static path => new ModuleStateModulePath(
            path,
            InferPowerShellEdition(path),
            InferScope(path))));
        var inventory = new ModuleStateInventoryService().Collect(request);
        inventory = IncludeLoadedModules(inventory, loadedModules);
        return ModuleStateInventoryResultMapper.ToCmdletResult(inventory, "ModulePath", paths);
    }

    internal static ModuleStateLoadedModuleEvidence[] GetLoadedModules(PSCmdlet cmdlet)
    {
        if (cmdlet is null)
            throw new ArgumentNullException(nameof(cmdlet));

        return cmdlet.InvokeCommand
            .InvokeScript("Get-Module | Select-Object -Property Name, Version, Path")
            .OfType<PSObject>()
            .Select(static item => new ModuleStateLoadedModuleEvidence(
                item.Properties["Name"]?.Value as string,
                item.Properties["Version"]?.Value?.ToString(),
                item.Properties["Path"]?.Value as string))
            .Where(static item => !string.IsNullOrWhiteSpace(item.Name) && !string.IsNullOrWhiteSpace(item.Version))
            .ToArray();
    }

    internal static string[] ResolveEnvironmentModulePaths()
    {
        var value = Environment.GetEnvironmentVariable("PSModulePath");
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<string>();

        return value.Split(Path.PathSeparator)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

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
        if (!string.IsNullOrWhiteSpace(programFiles) && IsPathUnder(path, programFiles))
            return "AllUsers";
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

    private static ModuleStateInventory IncludeLoadedModules(
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
                    isEffectiveImportCandidate: true);
                continue;
            }

            modules.Add(new ModuleStateInstalledModule(
                loadedModule.Name!,
                loadedModule.Version!,
                path: loadedModule.Path,
                isLoaded: true,
                isEffectiveImportCandidate: true));
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
