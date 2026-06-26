using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PowerForge;

internal sealed class ModuleStateInventoryService
{
    private static readonly Regex ModuleVersionPattern = new(
        @"(?im)^\s*ModuleVersion\s*=\s*['""]?(?<version>[0-9]+(?:\.[0-9]+){0,3}(?:-[A-Za-z0-9.-]+)?)['""]?",
        RegexOptions.Compiled);
    private static readonly Regex SourceRepositoryPattern = new(
        @"(?im)^\s*(?:SourceRepository|Repository)\s*=\s*['""](?<repository>[^'""]+)['""]",
        RegexOptions.Compiled);

    internal ModuleStateInventory Collect(ModuleStateInventoryRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var modules = new List<DiscoveredModule>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var modulePaths = request.ModulePaths.ToArray();
        for (var pathIndex = 0; pathIndex < modulePaths.Length; pathIndex++)
        {
            var modulePath = modulePaths[pathIndex];
            if (!Directory.Exists(modulePath.Path))
                continue;

            foreach (var moduleDirectory in EnumerateDirectoriesSafe(modulePath.Path))
            {
                foreach (var module in DiscoverModule(moduleDirectory, modulePath))
                {
                    var key = string.Join("|", module.Name, module.Version, module.PowerShellEdition, module.Scope, module.Path);
                    if (seen.Add(key))
                        modules.Add(new DiscoveredModule(module, pathIndex));
                }
            }
        }

        var effectiveModules = new HashSet<ModuleStateInstalledModule>(
            modules
                .GroupBy(static module => module.Module.Name, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group
                    .OrderBy(static module => module.ModulePathIndex)
                    .ThenByDescending(static module => ModuleStateVersion.TryParse(module.Module.Version, out var version) ? version : default)
                    .ThenBy(static module => module.Module.Path, StringComparer.OrdinalIgnoreCase)
                    .First()
                    .Module));

        return new ModuleStateInventory(modules
            .Select(module => MarkEffective(module.Module, effectiveModules.Contains(module.Module)))
            .OrderBy(static module => module.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static module => module.Version, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static module => module.Path, StringComparer.OrdinalIgnoreCase));
    }

    private static ModuleStateInstalledModule MarkEffective(ModuleStateInstalledModule module, bool isEffective)
        => new(
            module.Name,
            module.Version,
            module.PowerShellEdition,
            module.Scope,
            module.Path,
            module.SourceRepository,
            module.IsLoaded,
            isEffective);

    private static IEnumerable<ModuleStateInstalledModule> DiscoverModule(DirectoryInfo moduleDirectory, ModuleStateModulePath modulePath)
    {
        var directManifest = FindManifest(moduleDirectory, moduleDirectory.Name);
        if (directManifest is not null)
        {
            yield return CreateModule(moduleDirectory.Name, directManifest, modulePath, moduleDirectory.Name);
        }

        foreach (var versionDirectory in EnumerateDirectoriesSafe(moduleDirectory.FullName)
                     .Where(static directory => ModuleStateVersion.TryParse(directory.Name, out _)))
        {
            var versionManifest = FindManifest(versionDirectory, moduleDirectory.Name);
            if (versionManifest is null)
                continue;

            yield return CreateModule(moduleDirectory.Name, versionManifest, modulePath, versionDirectory.Name);
        }
    }

    private static ModuleStateInstalledModule CreateModule(
        string moduleName,
        FileInfo manifest,
        ModuleStateModulePath modulePath,
        string fallbackVersion)
    {
        var manifestText = TryReadManifestText(manifest.FullName);
        var version = ResolveDiscoveredVersion(manifestText, fallbackVersion);
        return new ModuleStateInstalledModule(
            moduleName,
            version,
            modulePath.PowerShellEdition,
            modulePath.Scope,
            manifest.DirectoryName ?? manifest.FullName,
            TryReadSourceRepository(manifestText, manifest.Directory));
    }

    private static FileInfo? FindManifest(DirectoryInfo directory, string moduleName)
    {
        var preferred = Path.Combine(directory.FullName, moduleName + ".psd1");
        if (File.Exists(preferred))
            return new FileInfo(preferred);

        try
        {
            return directory.EnumerateFiles("*.psd1", SearchOption.TopDirectoryOnly)
                .OrderBy(static file => file.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadManifestText(string manifestPath)
    {
        try
        {
            return File.ReadAllText(manifestPath);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadManifestVersion(string? manifestText)
    {
        if (string.IsNullOrWhiteSpace(manifestText))
            return null;

        var match = ModuleVersionPattern.Match(manifestText!);
        return match.Success ? match.Groups["version"].Value : null;
    }

    private static string ResolveDiscoveredVersion(string? manifestText, string fallbackVersion)
    {
        if (ModuleStateVersion.TryParse(fallbackVersion, out var fallback) && fallback.IsPrerelease)
            return fallbackVersion;

        return TryReadManifestVersion(manifestText) ?? fallbackVersion;
    }

    private static string? TryReadSourceRepository(string? manifestText, DirectoryInfo? moduleDirectory)
        => TryReadManifestSourceRepository(manifestText)
           ?? TryReadPSGetModuleInfoRepository(moduleDirectory)
           ?? TryReadNuspecRepository(moduleDirectory);

    private static string? TryReadManifestSourceRepository(string? manifestText)
    {
        if (string.IsNullOrWhiteSpace(manifestText))
            return null;

        var match = SourceRepositoryPattern.Match(manifestText!);
        return match.Success ? match.Groups["repository"].Value.Trim() : null;
    }

    private static string? TryReadPSGetModuleInfoRepository(DirectoryInfo? moduleDirectory)
    {
        var file = moduleDirectory is null ? null : Path.Combine(moduleDirectory.FullName, "PSGetModuleInfo.xml");
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
            return null;

        try
        {
            var document = XDocument.Load(file);
            return document
                .Descendants()
                .FirstOrDefault(static element =>
                    string.Equals(element.Name.LocalName, "Repository", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(element.Name.LocalName, "RepositoryName", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(element.Attribute("N")?.Value, "Repository", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(element.Attribute("N")?.Value, "RepositoryName", StringComparison.OrdinalIgnoreCase))
                ?.Value
                ?.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadNuspecRepository(DirectoryInfo? moduleDirectory)
    {
        if (moduleDirectory is null)
            return null;

        try
        {
            var nuspec = moduleDirectory.EnumerateFiles("*.nuspec", SearchOption.TopDirectoryOnly)
                .OrderBy(static file => file.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (nuspec is null)
                return null;

            var document = XDocument.Load(nuspec.FullName);
            return document
                .Descendants()
                .FirstOrDefault(static element => string.Equals(element.Name.LocalName, "repository", StringComparison.OrdinalIgnoreCase)
                                                  || string.Equals(element.Name.LocalName, "repositoryName", StringComparison.OrdinalIgnoreCase))
                ?.Attribute("name")
                ?.Value
                ?.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<DirectoryInfo> EnumerateDirectoriesSafe(string path)
    {
        IEnumerable<DirectoryInfo> directories;
        try
        {
            directories = new DirectoryInfo(path).EnumerateDirectories("*", SearchOption.TopDirectoryOnly);
        }
        catch
        {
            yield break;
        }

        foreach (var directory in directories)
            yield return directory;
    }

    private readonly struct DiscoveredModule
    {
        internal DiscoveredModule(ModuleStateInstalledModule module, int modulePathIndex)
        {
            Module = module;
            ModulePathIndex = modulePathIndex;
        }

        internal ModuleStateInstalledModule Module { get; }

        internal int ModulePathIndex { get; }
    }
}
