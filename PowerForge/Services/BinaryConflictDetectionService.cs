using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace PowerForge;

/// <summary>
/// Detects likely "first one wins" assembly version conflicts between a staged module payload and installed modules.
/// </summary>
internal sealed class BinaryConflictDetectionService
{
    private readonly ILogger _logger;

    internal BinaryConflictDetectionService(ILogger logger) => _logger = logger ?? new NullLogger();

    internal BinaryConflictDetectionResult Analyze(
        string moduleRoot,
        string powerShellEdition,
        string? currentModuleName = null,
        IReadOnlyList<string>? searchRoots = null)
    {
        if (string.IsNullOrWhiteSpace(moduleRoot))
            throw new ArgumentException("Module root is required.", nameof(moduleRoot));

        var root = Path.GetFullPath(moduleRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Module root not found: {root}");

        var edition = NormalizeEdition(powerShellEdition);
        var assemblyRoot = ResolveAssemblyRoot(root, edition, out var relativeAssemblyRoot);
        if (string.IsNullOrWhiteSpace(assemblyRoot) || !Directory.Exists(assemblyRoot))
        {
            return new BinaryConflictDetectionResult(
                edition,
                root,
                string.Empty,
                string.Empty,
                Array.Empty<BinaryConflictDetectionIssue>(),
                summary: "no binary payload");
        }

        var payloadAssemblies = EnumerateManagedAssemblies(assemblyRoot)
            .GroupBy(static asm => asm.SimpleName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);

        if (payloadAssemblies.Count == 0)
        {
            return new BinaryConflictDetectionResult(
                edition,
                root,
                assemblyRoot,
                relativeAssemblyRoot,
                Array.Empty<BinaryConflictDetectionIssue>(),
                summary: "no managed assemblies");
        }

        var roots = ResolveSearchRoots(searchRoots, edition);
        if (roots.Length == 0)
        {
            return new BinaryConflictDetectionResult(
                edition,
                root,
                assemblyRoot,
                relativeAssemblyRoot,
                Array.Empty<BinaryConflictDetectionIssue>(),
                summary: "no module roots to scan");
        }

        var issues = new List<BinaryConflictDetectionIssue>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedCurrentModuleName = string.IsNullOrWhiteSpace(currentModuleName) ? null : currentModuleName.Trim();

        foreach (var searchRootPath in roots)
        {
            foreach (var installedAssemblyPath in EnumerateCandidateAssemblies(searchRootPath))
            {
                if (!TryReadAssemblyIdentity(installedAssemblyPath, out var installedAssembly))
                    continue;

                if (string.IsNullOrWhiteSpace(installedAssembly.SimpleName))
                    continue;

                if (!payloadAssemblies.TryGetValue(installedAssembly.SimpleName, out var payloadMatches))
                    continue;

                var moduleInfo = ResolveModuleInfo(searchRootPath, installedAssemblyPath);
                if (!string.IsNullOrWhiteSpace(normalizedCurrentModuleName) &&
                    string.Equals(moduleInfo.ModuleName, normalizedCurrentModuleName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var payloadAssembly in payloadMatches)
                {
                    if (payloadAssembly.Version is null || installedAssembly.Version is null)
                        continue;

                    if (payloadAssembly.Version.Equals(installedAssembly.Version))
                        continue;

                    var key = string.Join(
                        "|",
                        edition,
                        payloadAssembly.SimpleName,
                        payloadAssembly.Version,
                        moduleInfo.ModuleName,
                        moduleInfo.ModuleVersion,
                        installedAssembly.Version,
                        installedAssemblyPath);

                    if (!seen.Add(key))
                        continue;

                    issues.Add(new BinaryConflictDetectionIssue(
                        powerShellEdition: edition,
                        assemblyName: payloadAssembly.SimpleName,
                        payloadAssemblyFileName: payloadAssembly.FileName,
                        payloadAssemblyVersion: payloadAssembly.Version.ToString(),
                        installedModuleName: moduleInfo.ModuleName,
                        installedModuleVersion: moduleInfo.ModuleVersion,
                        installedAssemblyVersion: installedAssembly.Version.ToString(),
                        installedAssemblyPath: installedAssemblyPath,
                        versionComparison: payloadAssembly.Version.CompareTo(installedAssembly.Version)));
                }
            }
        }

        var summary = issues.Count == 0
            ? $"no conflicts across {roots.Length} module root{(roots.Length == 1 ? string.Empty : "s")}"
            : $"{issues.Count} conflict{(issues.Count == 1 ? string.Empty : "s")} across {roots.Length} module root{(roots.Length == 1 ? string.Empty : "s")}";

        if (_logger.IsVerbose)
            _logger.Verbose($"Binary conflict detection ({edition}) scanned '{assemblyRoot}' -> {summary}.");

        return new BinaryConflictDetectionResult(
            edition,
            root,
            assemblyRoot,
            relativeAssemblyRoot,
            issues.ToArray(),
            summary);
    }

    private static string[] ResolveSearchRoots(IReadOnlyList<string>? searchRoots, string edition)
    {
        if (searchRoots is { Count: > 0 })
        {
            return searchRoots
                .Where(static p => !string.IsNullOrWhiteSpace(p))
                .Select(static p => Path.GetFullPath(p.Trim()))
                .Where(Directory.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        if (OperatingSystem.IsWindows())
        {
            var roots = new List<string>(2);
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(docs))
                docs = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (!string.IsNullOrWhiteSpace(docs))
            {
                roots.Add(Path.Combine(
                    docs,
                    string.Equals(edition, "Core", StringComparison.OrdinalIgnoreCase) ? "PowerShell" : "WindowsPowerShell",
                    "Modules"));
            }

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                roots.Add(Path.Combine(
                    programFiles,
                    string.Equals(edition, "Core", StringComparison.OrdinalIgnoreCase) ? "PowerShell" : "WindowsPowerShell",
                    "Modules"));
            }

            return roots
                .Where(Directory.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var home = Environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrWhiteSpace(home))
            home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var linuxRoots = new[]
        {
            !string.IsNullOrWhiteSpace(home) ? Path.Combine(home, ".local", "share", "powershell", "Modules") : string.Empty,
            "/usr/local/share/powershell/Modules",
            "/usr/share/powershell/Modules"
        };

        return linuxRoots
            .Where(static p => !string.IsNullOrWhiteSpace(p))
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeEdition(string? edition)
        => string.Equals(edition?.Trim(), "Desktop", StringComparison.OrdinalIgnoreCase) ? "Desktop" : "Core";

    private static string ResolveAssemblyRoot(string moduleRoot, string edition, out string relativeAssemblyRoot)
    {
        relativeAssemblyRoot = string.Empty;

        var libRoot = Path.Combine(moduleRoot, "Lib");
        if (!Directory.Exists(libRoot))
            return moduleRoot;

        var folders = new HashSet<string>(
            Directory.EnumerateDirectories(libRoot).Select(Path.GetFileName).Where(static n => !string.IsNullOrWhiteSpace(n))!,
            StringComparer.OrdinalIgnoreCase);

        var framework = string.Empty;
        var frameworkNet = string.Empty;
        var hasStandard = folders.Contains("Standard");
        var hasCore = folders.Contains("Core");
        var hasDefault = folders.Contains("Default");

        if (hasStandard && hasCore && hasDefault)
        {
            framework = "Standard";
            frameworkNet = "Default";
        }
        else if (hasStandard && hasCore)
        {
            framework = "Standard";
            frameworkNet = "Standard";
        }
        else if (hasCore && hasDefault)
        {
            framework = "Core";
            frameworkNet = "Default";
        }
        else if (hasStandard && hasDefault)
        {
            framework = "Standard";
            frameworkNet = "Default";
        }
        else if (hasStandard)
        {
            framework = "Standard";
            frameworkNet = "Standard";
        }
        else if (hasCore)
        {
            framework = "Core";
            frameworkNet = string.Empty;
        }
        else if (hasDefault)
        {
            framework = string.Empty;
            frameworkNet = "Default";
        }

        var selected = string.Equals(edition, "Core", StringComparison.OrdinalIgnoreCase)
            ? framework
            : frameworkNet;

        if (string.IsNullOrWhiteSpace(selected))
            return libRoot;

        relativeAssemblyRoot = Path.Combine("Lib", selected);
        return Path.Combine(libRoot, selected);
    }

    private static IEnumerable<DiscoveredAssembly> EnumerateManagedAssemblies(string assemblyRoot)
    {
        foreach (var path in EnumerateCandidateAssemblies(assemblyRoot))
        {
            if (!TryReadAssemblyIdentity(path, out var identity))
                continue;

            yield return identity;
        }
    }

    private static IEnumerable<string> EnumerateCandidateAssemblies(string searchRoot)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(searchRoot, "*.dll", SearchOption.AllDirectories);
        }
        catch
        {
            yield break;
        }

        foreach (var file in files.OrderBy(static p => p, StringComparer.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileName(file);
            if (string.IsNullOrWhiteSpace(fileName)) continue;
            if (fileName.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase)) continue;
            yield return file;
        }
    }

    private static bool TryReadAssemblyIdentity(string path, out DiscoveredAssembly assembly)
    {
        try
        {
            var assemblyName = AssemblyName.GetAssemblyName(path);
            var simpleName = assemblyName.Name ?? Path.GetFileNameWithoutExtension(path);
            assembly = new DiscoveredAssembly(simpleName, assemblyName.Version, Path.GetFileName(path), path);
            return !string.IsNullOrWhiteSpace(simpleName);
        }
        catch
        {
            assembly = default;
            return false;
        }
    }

    private static InstalledModuleInfo ResolveModuleInfo(string searchRoot, string assemblyPath)
    {
        try
        {
            var relative = Path.GetRelativePath(searchRoot, assemblyPath);
            if (string.IsNullOrWhiteSpace(relative))
                return new InstalledModuleInfo("(unknown)", null);

            var segments = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                return new InstalledModuleInfo("(unknown)", null);

            var moduleName = segments[0];
            string? moduleVersion = null;
            if (segments.Length > 1 && Version.TryParse(segments[1], out _))
                moduleVersion = segments[1];

            return new InstalledModuleInfo(moduleName, moduleVersion);
        }
        catch
        {
            return new InstalledModuleInfo("(unknown)", null);
        }
    }

    private struct DiscoveredAssembly
    {
        internal string SimpleName { get; }
        internal Version? Version { get; }
        internal string FileName { get; }
        internal string FullPath { get; }

        internal DiscoveredAssembly(string simpleName, Version? version, string fileName, string fullPath)
        {
            SimpleName = simpleName;
            Version = version;
            FileName = fileName;
            FullPath = fullPath;
        }
    }

    private struct InstalledModuleInfo
    {
        internal string ModuleName { get; }
        internal string? ModuleVersion { get; }

        internal InstalledModuleInfo(string moduleName, string? moduleVersion)
        {
            ModuleName = moduleName;
            ModuleVersion = moduleVersion;
        }
    }
}

internal sealed class BinaryConflictDetectionResult
{
    internal string PowerShellEdition { get; }
    internal string ModuleRoot { get; }
    internal string AssemblyRootPath { get; }
    internal string AssemblyRootRelativePath { get; }
    internal BinaryConflictDetectionIssue[] Issues { get; }
    internal string Summary { get; }
    internal bool HasConflicts => Issues.Length > 0;

    internal BinaryConflictDetectionResult(
        string powerShellEdition,
        string moduleRoot,
        string assemblyRootPath,
        string assemblyRootRelativePath,
        BinaryConflictDetectionIssue[] issues,
        string summary)
    {
        PowerShellEdition = powerShellEdition;
        ModuleRoot = moduleRoot;
        AssemblyRootPath = assemblyRootPath;
        AssemblyRootRelativePath = assemblyRootRelativePath;
        Issues = issues ?? Array.Empty<BinaryConflictDetectionIssue>();
        Summary = summary ?? string.Empty;
    }
}

internal sealed class BinaryConflictDetectionIssue
{
    internal string PowerShellEdition { get; }
    internal string AssemblyName { get; }
    internal string PayloadAssemblyFileName { get; }
    internal string PayloadAssemblyVersion { get; }
    internal string InstalledModuleName { get; }
    internal string? InstalledModuleVersion { get; }
    internal string InstalledAssemblyVersion { get; }
    internal string InstalledAssemblyPath { get; }
    internal int VersionComparison { get; }

    internal BinaryConflictDetectionIssue(
        string powerShellEdition,
        string assemblyName,
        string payloadAssemblyFileName,
        string payloadAssemblyVersion,
        string installedModuleName,
        string? installedModuleVersion,
        string installedAssemblyVersion,
        string installedAssemblyPath,
        int versionComparison)
    {
        PowerShellEdition = powerShellEdition;
        AssemblyName = assemblyName;
        PayloadAssemblyFileName = payloadAssemblyFileName;
        PayloadAssemblyVersion = payloadAssemblyVersion;
        InstalledModuleName = installedModuleName;
        InstalledModuleVersion = installedModuleVersion;
        InstalledAssemblyVersion = installedAssemblyVersion;
        InstalledAssemblyPath = installedAssemblyPath;
        VersionComparison = versionComparison;
    }
}
