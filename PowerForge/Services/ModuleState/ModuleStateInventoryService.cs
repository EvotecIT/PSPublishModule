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
    private static readonly Regex PrereleasePattern = new(
        @"(?im)^\s*Prerelease\s*=\s*['""](?<prerelease>[^'""]+)['""]",
        RegexOptions.Compiled);
    private static readonly Regex SourceRepositoryPattern = new(
        @"(?im)^\s*(?:SourceRepository|Repository)\s*=\s*['""](?<repository>[^'""]+)['""]",
        RegexOptions.Compiled);

    internal ModuleStateInventory Collect(ModuleStateInventoryRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var modules = new List<DiscoveredModule>();
        var diagnostics = new List<ModuleStateInventoryDiagnostic>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var modulePaths = request.ModulePaths.ToArray();
        var scannedPaths = new ModuleStateModulePath[modulePaths.Length];
        for (var pathIndex = 0; pathIndex < modulePaths.Length; pathIndex++)
        {
            var modulePath = modulePaths[pathIndex];
            var directoryProbe = ModuleStateDirectoryProbe.Probe(modulePath.Path);
            if (directoryProbe.Status != ModuleStateDirectoryProbeStatus.Available)
            {
                scannedPaths[pathIndex] = WithAvailability(modulePath, wasAvailable: false);
                if (directoryProbe.Status == ModuleStateDirectoryProbeStatus.Inaccessible)
                {
                    diagnostics.Add(CreatePathDiagnostic(
                        modulePath,
                        "ModuleState.InventoryPathInaccessible",
                        $"Module inventory path '{modulePath.Path}' could not be inspected: {directoryProbe.Reason}"));
                }
                else if (modulePath.IsRequired)
                {
                    diagnostics.Add(CreatePathDiagnostic(
                        modulePath,
                        "ModuleState.InventoryPathMissing",
                        $"Required module inventory path '{modulePath.Path}' does not exist."));
                }

                continue;
            }

            if (!TryEnumerateDirectories(modulePath.Path, out var moduleDirectories, out var enumerationError))
            {
                scannedPaths[pathIndex] = WithAvailability(modulePath, wasAvailable: false);
                diagnostics.Add(CreatePathDiagnostic(
                    modulePath,
                    "ModuleState.InventoryPathInaccessible",
                    $"Module inventory path '{modulePath.Path}' could not be enumerated: {enumerationError!.Message}"));
                continue;
            }

            scannedPaths[pathIndex] = WithAvailability(modulePath, wasAvailable: true);

            foreach (var moduleDirectory in moduleDirectories)
            {
                foreach (var module in DiscoverModule(moduleDirectory, modulePath))
                {
                    var key = string.Join(
                        "|",
                        ModuleStatePathIdentity.CreatePlacementKey(module.Name, module.PowerShellEdition, module.Scope, module.ModuleRoot),
                        module.Version,
                        NormalizePathKey(module.Path));
                    if (seen.Add(key))
                        modules.Add(new DiscoveredModule(module, pathIndex));
                }
            }
        }

        var effectiveModules = new HashSet<ModuleStateInstalledModule>(
            modules
                .GroupBy(
                    static module => string.Join(
                        "|",
                        module.Module.Name,
                        module.Module.PowerShellEdition ?? string.Empty,
                        module.Module.ProfileName ?? string.Empty),
                    StringComparer.OrdinalIgnoreCase)
                .Select(static group => group
                    .OrderBy(static module => module.ModulePathIndex)
                    .ThenByDescending(static module => ModuleStateVersion.TryParse(module.Module.Version, out var version) ? version : default)
                    .ThenBy(static module => module.Module.Path, ModuleStatePathIdentity.Comparer)
                    .First()
                    .Module));

        var inventory = new ModuleStateInventory(modules
            .Select(module => MarkEffective(module.Module, effectiveModules.Contains(module.Module)))
            .OrderBy(static module => module.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static module => module.Version, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static module => module.Path, ModuleStatePathIdentity.Comparer),
            scannedPaths,
            diagnostics);
        return ModuleStateInventoryFilter.Apply(inventory, request);
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
            isEffective,
            module.ExportedCommands,
            module.ModuleRoot,
            module.ProfileName);

    private static ModuleStateModulePath WithAvailability(ModuleStateModulePath path, bool wasAvailable)
        => new(
            path.Path,
            path.PowerShellEdition,
            path.Scope,
            path.ProfileName,
            path.IsRequired,
            wasAvailable,
            path.DependencyVisibilityGroup);

    internal static ModuleStateInstalledModule[] RecomputeEffectiveImportCandidates(
        IEnumerable<ModuleStateInstalledModule> installedModules,
        IReadOnlyList<ModuleStateModulePath> modulePaths)
    {
        var modules = (installedModules ?? Array.Empty<ModuleStateInstalledModule>()).ToArray();
        var paths = modulePaths ?? Array.Empty<ModuleStateModulePath>();
        var effectiveModules = new HashSet<ModuleStateInstalledModule>();
        foreach (var group in modules.GroupBy(
                     static module => string.Join(
                         "|",
                         module.Name,
                         module.PowerShellEdition ?? string.Empty,
                         module.ProfileName ?? string.Empty),
                     StringComparer.OrdinalIgnoreCase))
        {
            var placed = group
                .Select(module => new
                {
                    Module = module,
                    ModulePathIndex = ResolveModulePathIndex(module, paths)
                })
                .Where(static candidate => candidate.ModulePathIndex != int.MaxValue)
                .OrderBy(static candidate => candidate.ModulePathIndex)
                .ThenByDescending(static candidate => ModuleStateVersion.TryParse(candidate.Module.Version, out var version) ? version : default)
                .ThenBy(static candidate => candidate.Module.Path, ModuleStatePathIdentity.Comparer)
                .FirstOrDefault();
            var winner = placed?.Module
                         ?? group.FirstOrDefault(static module => module.IsEffectiveImportCandidate)
                         ?? group
                             .OrderByDescending(static module => ModuleStateVersion.TryParse(module.Version, out var version) ? version : default)
                             .ThenBy(static module => module.Path, ModuleStatePathIdentity.Comparer)
                             .First();
            effectiveModules.Add(winner);
        }

        return modules
            .Select(module => MarkEffective(module, effectiveModules.Contains(module)))
            .ToArray();
    }

    private static int ResolveModulePathIndex(
        ModuleStateInstalledModule module,
        IReadOnlyList<ModuleStateModulePath> modulePaths)
    {
        for (var index = 0; index < modulePaths.Count; index++)
        {
            var root = modulePaths[index].Path;
            if ((!string.IsNullOrWhiteSpace(module.ModuleRoot) && ModuleStatePathIdentity.Equals(module.ModuleRoot, root)) ||
                (!string.IsNullOrWhiteSpace(module.Path) && ModuleStatePathIdentity.IsSameOrChild(module.Path, root)))
            {
                return index;
            }
        }

        return int.MaxValue;
    }

    private static IEnumerable<ModuleStateInstalledModule> DiscoverModule(DirectoryInfo moduleDirectory, ModuleStateModulePath modulePath)
    {
        var directManifest = FindManifest(moduleDirectory, moduleDirectory.Name);
        if (directManifest is not null)
        {
            yield return CreateModule(moduleDirectory.Name, directManifest, modulePath, moduleDirectory.Name);
        }
        else if (FindScriptModule(moduleDirectory, moduleDirectory.Name) is { } directScriptModule)
        {
            yield return CreateScriptModule(moduleDirectory.Name, directScriptModule, modulePath, "0.0");
        }
        else if (FindBinaryModule(moduleDirectory, moduleDirectory.Name) is { } directBinaryModule)
        {
            yield return CreateBinaryModule(moduleDirectory.Name, directBinaryModule, modulePath, "0.0");
        }

        foreach (var versionDirectory in EnumerateDirectoriesSafe(moduleDirectory.FullName)
                     .Where(static directory => ModuleStateVersion.TryParse(directory.Name, out _)))
        {
            var versionManifest = FindManifest(versionDirectory, moduleDirectory.Name);
            if (versionManifest is not null)
            {
                yield return CreateModule(moduleDirectory.Name, versionManifest, modulePath, versionDirectory.Name);
                continue;
            }

            if (FindScriptModule(versionDirectory, moduleDirectory.Name) is { } versionScriptModule)
            {
                yield return CreateScriptModule(moduleDirectory.Name, versionScriptModule, modulePath, versionDirectory.Name);
                continue;
            }

            if (FindBinaryModule(versionDirectory, moduleDirectory.Name) is { } versionBinaryModule)
                yield return CreateBinaryModule(moduleDirectory.Name, versionBinaryModule, modulePath, versionDirectory.Name);
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
            TryReadSourceRepository(manifestText, manifest.Directory),
            exportedCommands: ReadCommandExports(manifest.FullName),
            moduleRoot: modulePath.Path,
            profileName: modulePath.ProfileName);
    }

    private static ModuleStateInstalledModule CreateScriptModule(
        string moduleName,
        FileInfo scriptModule,
        ModuleStateModulePath modulePath,
        string fallbackVersion)
        => new(
            moduleName,
            fallbackVersion,
            modulePath.PowerShellEdition,
            modulePath.Scope,
            scriptModule.DirectoryName ?? scriptModule.FullName,
            TryReadSourceRepository(manifestText: null, scriptModule.Directory),
            moduleRoot: modulePath.Path,
            profileName: modulePath.ProfileName);

    private static ModuleStateInstalledModule CreateBinaryModule(
        string moduleName,
        FileInfo binaryModule,
        ModuleStateModulePath modulePath,
        string fallbackVersion)
        => new(
            moduleName,
            fallbackVersion,
            modulePath.PowerShellEdition,
            modulePath.Scope,
            binaryModule.DirectoryName ?? binaryModule.FullName,
            TryReadSourceRepository(manifestText: null, binaryModule.Directory),
            moduleRoot: modulePath.Path,
            profileName: modulePath.ProfileName);

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

    private static FileInfo? FindBinaryModule(DirectoryInfo directory, string moduleName)
    {
        var preferred = Path.Combine(directory.FullName, moduleName + ".dll");
        if (File.Exists(preferred))
            return new FileInfo(preferred);

        try
        {
            return directory.EnumerateFiles("*.dll", SearchOption.TopDirectoryOnly)
                .OrderBy(static file => file.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static FileInfo? FindScriptModule(DirectoryInfo directory, string moduleName)
    {
        var preferred = Path.Combine(directory.FullName, moduleName + ".psm1");
        if (File.Exists(preferred))
            return new FileInfo(preferred);

        try
        {
            return directory.EnumerateFiles("*.psm1", SearchOption.TopDirectoryOnly)
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

    private static string[] ReadCommandExports(string manifestPath)
    {
        try
        {
            var exports = ModuleManifestExportReader.ReadExports(manifestPath);
            return exports.Functions
                .Concat(exports.Cmdlets)
                .Concat(exports.Aliases)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
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

        var manifestVersion = TryReadManifestVersion(manifestText);
        if (string.IsNullOrWhiteSpace(manifestVersion))
            return fallbackVersion;

        var resolvedManifestVersion = manifestVersion!;
        if (ModuleStateVersion.TryParse(resolvedManifestVersion, out var parsedManifest) && parsedManifest.IsPrerelease)
            return resolvedManifestVersion;

        var prerelease = TryReadManifestPrerelease(manifestText);
        return string.IsNullOrWhiteSpace(prerelease)
            ? resolvedManifestVersion
            : resolvedManifestVersion + "-" + prerelease!.Trim().TrimStart('-');
    }

    private static string? TryReadManifestPrerelease(string? manifestText)
    {
        if (string.IsNullOrWhiteSpace(manifestText))
            return null;

        var match = PrereleasePattern.Match(manifestText!);
        return match.Success ? match.Groups["prerelease"].Value.Trim() : null;
    }

    private static string? TryReadSourceRepository(string? manifestText, DirectoryInfo? moduleDirectory)
        => TryReadManifestSourceRepository(manifestText)
           ?? TryReadManagedReceiptRepository(moduleDirectory)
           ?? TryReadPSGetModuleInfoRepository(moduleDirectory)
           ?? TryReadNuspecRepository(moduleDirectory);

    private static string? TryReadManagedReceiptRepository(DirectoryInfo? moduleDirectory)
    {
        if (moduleDirectory is null)
            return null;

        var receiptPath = ManagedModuleReceiptStore.GetReceiptPath(moduleDirectory.FullName);
        if (!File.Exists(receiptPath))
            return null;

        try
        {
            var receipt = System.Text.Json.JsonSerializer.Deserialize<ManagedModuleReceipt>(File.ReadAllText(receiptPath));
            return FirstNonEmpty(receipt?.RepositoryName, receipt?.RepositorySource);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadManifestSourceRepository(string? manifestText)
    {
        if (string.IsNullOrWhiteSpace(manifestText))
            return null;

        var match = SourceRepositoryPattern.Match(manifestText!);
        return match.Success ? match.Groups["repository"].Value.Trim() : null;
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();

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
        try
        {
            return new DirectoryInfo(path)
                .EnumerateDirectories("*", SearchOption.TopDirectoryOnly)
                .ToArray();
        }
        catch
        {
            return Array.Empty<DirectoryInfo>();
        }
    }

    private static bool TryEnumerateDirectories(
        string path,
        out DirectoryInfo[] directories,
        out Exception? error)
    {
        try
        {
            directories = new DirectoryInfo(path)
                .EnumerateDirectories("*", SearchOption.TopDirectoryOnly)
                .ToArray();
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            directories = Array.Empty<DirectoryInfo>();
            error = ex;
            return false;
        }
    }

    internal static ModuleStateInventoryDiagnostic CreatePathDiagnostic(
        ModuleStateModulePath modulePath,
        string code,
        string message)
        => new(
            modulePath.IsRequired ? ModuleStateConflictSeverity.Error : ModuleStateConflictSeverity.Warning,
            code,
            message,
            modulePath.Path,
            modulePath.PowerShellEdition,
            modulePath.Scope,
            modulePath.ProfileName);

    private static string NormalizePathKey(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var normalized = ModuleStatePathIdentity.Normalize(path!);
        return FrameworkCompatibility.IsWindows() ? normalized.ToUpperInvariant() : normalized;
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
