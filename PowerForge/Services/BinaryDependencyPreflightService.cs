using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Management.Automation;

namespace PowerForge;

/// <summary>
/// Performs a lightweight preflight analysis of binary module payloads before attempting import.
/// </summary>
public sealed class BinaryDependencyPreflightService
{
    private static readonly string[] WellKnownFrameworkAssemblyNames =
    {
        "mscorlib",
        "netstandard",
        "System",
        "System.Configuration",
        "System.Core",
        "System.Data",
        "System.Drawing",
        "System.Management",
        "System.Management.Automation",
        "System.Runtime",
        "System.Security",
        "System.ValueTuple",
        "System.Web",
        "System.Windows.Forms",
        "System.Xml",
        "System.Xml.Linq",
        "WindowsBase",
        "Microsoft.CSharp",
        "Microsoft.VisualBasic",
        "PresentationCore",
        "PresentationFramework",
        "UIAutomationClient",
        "UIAutomationProvider",
        "UIAutomationTypes"
    };

    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new instance that logs via <paramref name="logger"/>.
    /// </summary>
    public BinaryDependencyPreflightService(ILogger logger) => _logger = logger ?? new NullLogger();

    /// <summary>
    /// Analyzes the module payload that would be used for the specified PowerShell edition.
    /// </summary>
    public BinaryDependencyPreflightResult Analyze(string moduleRoot, string powerShellEdition)
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
            return new BinaryDependencyPreflightResult(
                edition,
                root,
                string.Empty,
                string.Empty,
                Array.Empty<BinaryDependencyPreflightIssue>(),
                summary: "no binary payload");
        }

        var assemblyPaths = EnumerateCandidateAssemblies(assemblyRoot).ToArray();
        if (assemblyPaths.Length == 0)
        {
            return new BinaryDependencyPreflightResult(
                edition,
                root,
                assemblyRoot,
                relativeAssemblyRoot,
                Array.Empty<BinaryDependencyPreflightIssue>(),
                summary: "no managed assemblies");
        }

        var availableAssemblyNames = new HashSet<string>(
            assemblyPaths.Select(GetSimpleAssemblyName)
                .Where(static n => !string.IsNullOrWhiteSpace(n))!,
            StringComparer.OrdinalIgnoreCase);

        var providedByHost = new HashSet<string>(GetHostProvidedAssemblyNames(edition), StringComparer.OrdinalIgnoreCase);
        foreach (var name in availableAssemblyNames)
            providedByHost.Add(name);

        var issues = new List<BinaryDependencyPreflightIssue>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var assemblyPath in assemblyPaths)
        {
            var assemblyFileName = Path.GetFileName(assemblyPath);
            foreach (var reference in ReadAssemblyReferences(assemblyPath))
            {
                if (string.IsNullOrWhiteSpace(reference.Name)) continue;
                if (providedByHost.Contains(reference.Name)) continue;

                var key = assemblyFileName + "->" + reference.Name;
                if (!seen.Add(key)) continue;

                issues.Add(new BinaryDependencyPreflightIssue(
                    assemblyFileName,
                    reference.Name,
                    reference.Version?.ToString()));
            }
        }

        var summary = issues.Count == 0
            ? $"ok ({assemblyPaths.Length} assembly{(assemblyPaths.Length == 1 ? string.Empty : "ies")})"
            : $"{issues.Count} missing dependenc{(issues.Count == 1 ? "y" : "ies")} across {assemblyPaths.Length} assembly{(assemblyPaths.Length == 1 ? string.Empty : "ies")}";

        if (_logger.IsVerbose)
            _logger.Verbose($"Binary dependency preflight ({edition}) scanned '{assemblyRoot}' -> {summary}.");

        return new BinaryDependencyPreflightResult(
            edition,
            root,
            assemblyRoot,
            relativeAssemblyRoot,
            issues.ToArray(),
            summary);
    }

    internal static string BuildFailureMessage(BinaryDependencyPreflightResult result, string? modulePath = null, string? validationTarget = null)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));

        var first = result.Issues.FirstOrDefault();
        var sb = new StringBuilder();
        sb.Append("Binary dependency preflight failed");
        if (validationTarget is { Length: > 0 } rawValidationTarget && !string.IsNullOrWhiteSpace(rawValidationTarget))
        {
            var trimmedValidationTarget = rawValidationTarget.Trim();
            if (trimmedValidationTarget.Length > 0)
                sb.Append(" during ").Append(trimmedValidationTarget).Append(" validation");
        }
        sb.Append('.');

        if (first is not null)
            sb.AppendLine().Append("Cause: ").Append(first.AssemblyFileName).Append(" references missing ").Append(first.MissingDependencyFileName).Append('.');

        if (!string.IsNullOrWhiteSpace(result.AssemblyRootRelativePath))
            sb.AppendLine().Append("Payload: ").Append(result.AssemblyRootRelativePath);

        if (!string.IsNullOrWhiteSpace(modulePath))
            sb.AppendLine().Append("Manifest: ").Append(modulePath);

        if (result.Issues.Length > 0)
        {
            var details = result.Issues
                .Take(4)
                .Select(static issue => issue.AssemblyFileName + " -> " + issue.MissingDependencyFileName)
                .ToArray();

            sb.AppendLine().Append("Detail: ").Append(string.Join(" | ", details));
            if (result.Issues.Length > details.Length)
                sb.Append(" | +").Append(result.Issues.Length - details.Length).Append(" more");
        }

        sb.AppendLine().Append("Hint: Add the missing dependency to the payload or use New-ConfigurationImportModule -SkipBinaryDependencyCheck to bypass this preflight.");
        return sb.ToString();
    }

    private static IEnumerable<string> EnumerateCandidateAssemblies(string assemblyRoot)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(assemblyRoot, "*.dll", SearchOption.AllDirectories);
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

    private static string NormalizeEdition(string? powerShellEdition)
        => string.Equals(powerShellEdition, "Desktop", StringComparison.OrdinalIgnoreCase) ? "Desktop" : "Core";

    private static string GetSimpleAssemblyName(string assemblyPath)
        => Path.GetFileNameWithoutExtension(assemblyPath) ?? string.Empty;

    private static IReadOnlyCollection<string> GetHostProvidedAssemblyNames(string edition)
    {
        var set = new HashSet<string>(WellKnownFrameworkAssemblyNames, StringComparer.OrdinalIgnoreCase);

        if (string.Equals(edition, "Desktop", StringComparison.OrdinalIgnoreCase))
        {
            if (Path.DirectorySeparatorChar != '\\')
                return set;

            foreach (var dir in GetDesktopReferenceAssemblyDirectories())
                AddAssembliesFromDirectory(set, dir);

            var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrWhiteSpace(windir))
            {
                AddAssembliesFromDirectory(set, Path.Combine(windir, "System32", "WindowsPowerShell", "v1.0"));
                AddAssembliesFromDirectory(set, Path.Combine(windir, "System32", "WindowsPowerShell", "v1.0", "Modules"), recursive: true);
                AddSpecificAssemblyIfPresent(
                    set,
                    Path.Combine(windir, "Microsoft.NET", "assembly"),
                    "System.Management.Automation.dll");
            }

            return set;
        }

        AddAssembliesFromDirectory(set, Path.GetDirectoryName(typeof(object).Assembly.Location));
        AddAssembliesFromDirectory(set, Path.GetDirectoryName(typeof(PSObject).Assembly.Location));

        return set;
    }

    private static IEnumerable<string> GetDesktopReferenceAssemblyDirectories()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        }
        .Where(static p => !string.IsNullOrWhiteSpace(p))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

        foreach (var root in roots)
        {
            var baseDir = Path.Combine(root, "Reference Assemblies", "Microsoft", "Framework", ".NETFramework");
            foreach (var version in new[] { "v4.7.2", "v4.8", "v4.8.1" })
            {
                var path = Path.Combine(baseDir, version);
                if (Directory.Exists(path)) yield return path;

                var facades = Path.Combine(path, "Facades");
                if (Directory.Exists(facades)) yield return facades;
            }
        }
    }

    private static void AddAssembliesFromDirectory(ISet<string> set, string? directory, bool recursive = false)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;

        try
        {
            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            foreach (var file in Directory.EnumerateFiles(directory, "*.dll", searchOption))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!string.IsNullOrWhiteSpace(name))
                    set.Add(name);
            }
        }
        catch
        {
            // best effort only
        }
    }

    private static void AddSpecificAssemblyIfPresent(ISet<string> set, string? rootDirectory, string assemblyFileName)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory)) return;
        if (string.IsNullOrWhiteSpace(assemblyFileName)) return;

        try
        {
            var path = Directory.EnumerateFiles(rootDirectory, assemblyFileName, SearchOption.AllDirectories).FirstOrDefault();
            if (path is null) return;

            var name = Path.GetFileNameWithoutExtension(path);
            if (name is { Length: > 0 })
                set.Add(name);
        }
        catch
        {
            // best effort only
        }
    }

    private static IEnumerable<AssemblyReferenceInfo> ReadAssemblyReferences(string assemblyPath)
    {
        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var pe = new PEReader(stream, PEStreamOptions.LeaveOpen);
            if (!pe.HasMetadata) return Array.Empty<AssemblyReferenceInfo>();

            var reader = pe.GetMetadataReader();
            var list = new List<AssemblyReferenceInfo>();
            foreach (var handle in reader.AssemblyReferences)
            {
                var reference = reader.GetAssemblyReference(handle);
                var name = reader.GetString(reference.Name);
                if (string.IsNullOrWhiteSpace(name)) continue;
                list.Add(new AssemblyReferenceInfo(name, reference.Version));
            }

            return list;
        }
        catch
        {
            return Array.Empty<AssemblyReferenceInfo>();
        }
    }

    private sealed class AssemblyReferenceInfo
    {
        public string Name { get; }
        public Version? Version { get; }

        public AssemblyReferenceInfo(string name, Version? version)
        {
            Name = name;
            Version = version;
        }
    }
}

/// <summary>
/// Result of binary dependency preflight analysis.
/// </summary>
public sealed class BinaryDependencyPreflightResult
{
    /// <summary>Resolved PowerShell edition used for the check.</summary>
    public string PowerShellEdition { get; }

    /// <summary>Module root that was analyzed.</summary>
    public string ModuleRoot { get; }

    /// <summary>Absolute path to the payload root that was scanned.</summary>
    public string AssemblyRootPath { get; }

    /// <summary>Relative payload path shown in diagnostics.</summary>
    public string AssemblyRootRelativePath { get; }

    /// <summary>Detected missing dependency issues.</summary>
    public BinaryDependencyPreflightIssue[] Issues { get; }

    /// <summary>Short summary of the analysis.</summary>
    public string Summary { get; }

    /// <summary>Whether any issues were detected.</summary>
    public bool HasIssues => Issues.Length > 0;

    /// <summary>
    /// Creates a new result.
    /// </summary>
    public BinaryDependencyPreflightResult(
        string powerShellEdition,
        string moduleRoot,
        string assemblyRootPath,
        string assemblyRootRelativePath,
        BinaryDependencyPreflightIssue[] issues,
        string summary)
    {
        PowerShellEdition = powerShellEdition ?? string.Empty;
        ModuleRoot = moduleRoot ?? string.Empty;
        AssemblyRootPath = assemblyRootPath ?? string.Empty;
        AssemblyRootRelativePath = assemblyRootRelativePath ?? string.Empty;
        Issues = issues ?? Array.Empty<BinaryDependencyPreflightIssue>();
        Summary = summary ?? string.Empty;
    }
}

/// <summary>
/// A single missing dependency reference discovered during preflight analysis.
/// </summary>
public sealed class BinaryDependencyPreflightIssue
{
    /// <summary>Assembly within the payload that references the missing dependency.</summary>
    public string AssemblyFileName { get; }

    /// <summary>Simple assembly name of the missing dependency.</summary>
    public string MissingDependencyName { get; }

    /// <summary>File name of the missing dependency.</summary>
    public string MissingDependencyFileName => MissingDependencyName + ".dll";

    /// <summary>Referenced version, when available.</summary>
    public string? ReferencedVersion { get; }

    /// <summary>
    /// Creates a new issue.
    /// </summary>
    public BinaryDependencyPreflightIssue(string assemblyFileName, string missingDependencyName, string? referencedVersion)
    {
        AssemblyFileName = assemblyFileName ?? string.Empty;
        MissingDependencyName = missingDependencyName ?? string.Empty;
        ReferencedVersion = referencedVersion;
    }
}
