using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Management.Automation;
using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Performs a lightweight preflight analysis of binary module payloads before attempting import.
/// </summary>
public sealed class BinaryDependencyPreflightService
{
    private const string BundledModulesFolderName = "Modules";

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

    private static readonly string[] WellKnownCoreRuntimeAssemblyNames =
    {
        "Microsoft.CSharp",
        "System.Buffers",
        "System.Collections.Immutable",
        "System.Memory",
        "System.Net.ServicePoint",
        "System.Numerics.Vectors",
        "System.Runtime.CompilerServices.Unsafe",
        "System.Text.Encoding.CodePages",
        "System.Threading.Tasks.Extensions"
    };

    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new instance that logs via <paramref name="logger"/>.
    /// </summary>
    public BinaryDependencyPreflightService(ILogger logger) => _logger = logger ?? new NullLogger();

    /// <summary>
    /// Analyzes the module payload that would be used for the specified PowerShell edition.
    /// Callers that have the module manifest path should prefer the manifest-aware overload so
    /// root-level script-package payloads can scope analysis to import-relevant assemblies.
    /// </summary>
    public BinaryDependencyPreflightResult Analyze(string moduleRoot, string powerShellEdition)
        => Analyze(moduleRoot, powerShellEdition, manifestPath: null);

    /// <summary>
    /// Analyzes the module payload that would be used for the specified PowerShell edition.
    /// When <paramref name="manifestPath"/> is provided and the module has no <c>Lib</c> payload,
    /// the analysis scopes root-level DLL checks to import-relevant assemblies and ignores delivery internals.
    /// </summary>
    public BinaryDependencyPreflightResult Analyze(string moduleRoot, string powerShellEdition, string? manifestPath)
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

        var scopedRootAnalysis = TryCreateScopedRootAnalysis(root, assemblyRoot, manifestPath);
        if (scopedRootAnalysis is not null)
            return AnalyzeScopedRootGraph(edition, root, assemblyRoot, relativeAssemblyRoot, scopedRootAnalysis);

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

    private BinaryDependencyPreflightResult AnalyzeScopedRootGraph(
        string edition,
        string moduleRoot,
        string assemblyRoot,
        string relativeAssemblyRoot,
        ScopedRootAnalysis scoped)
    {
        if (scoped.EntryAssemblyPaths.Length == 0)
        {
            return new BinaryDependencyPreflightResult(
                edition,
                moduleRoot,
                assemblyRoot,
                relativeAssemblyRoot,
                Array.Empty<BinaryDependencyPreflightIssue>(),
                summary: "no declared binary assemblies");
        }

        var availableAssemblyNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var assemblyPath in scoped.CandidateAssemblyPaths.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            var name = GetSimpleAssemblyName(assemblyPath);
            if (string.IsNullOrWhiteSpace(name) || availableAssemblyNames.ContainsKey(name))
                continue;

            availableAssemblyNames[name] = assemblyPath;
        }

        var providedByHost = new HashSet<string>(GetHostProvidedAssemblyNames(edition), StringComparer.OrdinalIgnoreCase);

        var issues = new List<BinaryDependencyPreflightIssue>();
        var seenIssues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visitedAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(scoped.EntryAssemblyPaths);

        while (queue.Count > 0)
        {
            var assemblyPath = queue.Dequeue();
            if (!visitedAssemblies.Add(assemblyPath))
                continue;

            var assemblyFileName = Path.GetFileName(assemblyPath);
            foreach (var reference in ReadAssemblyReferences(assemblyPath))
            {
                if (string.IsNullOrWhiteSpace(reference.Name)) continue;

                if (availableAssemblyNames.TryGetValue(reference.Name, out var dependencyPath))
                {
                    if (!visitedAssemblies.Contains(dependencyPath))
                        queue.Enqueue(dependencyPath);
                    continue;
                }

                if (providedByHost.Contains(reference.Name)) continue;

                var key = assemblyFileName + "->" + reference.Name;
                if (!seenIssues.Add(key)) continue;

                issues.Add(new BinaryDependencyPreflightIssue(
                    assemblyFileName,
                    reference.Name,
                    reference.Version?.ToString()));
            }
        }

        var scannedCount = visitedAssemblies.Count;
        var summary = issues.Count == 0
            ? $"ok ({scannedCount} import assembly{(scannedCount == 1 ? string.Empty : "ies")})"
            : $"{issues.Count} missing dependenc{(issues.Count == 1 ? "y" : "ies")} across {scannedCount} import assembly{(scannedCount == 1 ? string.Empty : "ies")}";

        if (_logger.IsVerbose)
            _logger.Verbose($"Binary dependency preflight ({edition}) scoped root scan '{assemblyRoot}' -> {summary}.");

        return new BinaryDependencyPreflightResult(
            edition,
            moduleRoot,
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
        => EnumerateCandidateAssemblies(
            assemblyRoot,
            excludedRelativePaths: null,
            explicitlyIncludedPaths: null,
            explicitlyIncludedDirectories: null);

    private static IEnumerable<string> EnumerateCandidateAssemblies(
        string assemblyRoot,
        IReadOnlyCollection<string>? excludedRelativePaths,
        IReadOnlyCollection<string>? explicitlyIncludedPaths,
        IReadOnlyCollection<string>? explicitlyIncludedDirectories)
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
            if (ShouldExcludeAssembly(file, assemblyRoot, excludedRelativePaths, explicitlyIncludedPaths, explicitlyIncludedDirectories)) continue;
            yield return file;
        }
    }

    private static bool ShouldExcludeAssembly(
        string assemblyPath,
        string assemblyRoot,
        IReadOnlyCollection<string>? excludedRelativePaths,
        IReadOnlyCollection<string>? explicitlyIncludedPaths,
        IReadOnlyCollection<string>? explicitlyIncludedDirectories)
    {
        if (excludedRelativePaths is not { Count: > 0 })
            return false;

        var fullAssemblyPath = Path.GetFullPath(assemblyPath);
        if (explicitlyIncludedPaths is { Count: > 0 } &&
            explicitlyIncludedPaths.Contains(fullAssemblyPath, StringComparer.OrdinalIgnoreCase))
            return false;

        if (explicitlyIncludedDirectories is { Count: > 0 })
        {
            var directory = Path.GetDirectoryName(fullAssemblyPath);
            if (!string.IsNullOrWhiteSpace(directory) &&
                explicitlyIncludedDirectories.Contains(Path.GetFullPath(directory), StringComparer.OrdinalIgnoreCase))
                return false;
        }

        var relative = FrameworkCompatibility.GetRelativePath(assemblyRoot, assemblyPath)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);

        if (string.IsNullOrWhiteSpace(relative))
            return false;

        foreach (var excluded in excludedRelativePaths)
        {
            if (string.IsNullOrWhiteSpace(excluded)) continue;

            var normalized = excluded.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .Trim()
                .TrimStart(Path.DirectorySeparatorChar)
                .TrimEnd(Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(normalized)) continue;

            if (relative.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith(normalized + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
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

    private static ScopedRootAnalysis? TryCreateScopedRootAnalysis(string moduleRoot, string assemblyRoot, string? manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
            return null;

        var normalizedModuleRoot = NormalizePathForComparison(moduleRoot);
        var normalizedAssemblyRoot = NormalizePathForComparison(assemblyRoot);
        if (!string.Equals(normalizedModuleRoot, normalizedAssemblyRoot, StringComparison.OrdinalIgnoreCase))
            return null;

        var entryAssemblyPaths = ResolveManifestAssemblyPaths(moduleRoot, manifestPath!)
            .Where(static path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var explicitlyIncluded = new HashSet<string>(entryAssemblyPaths, StringComparer.OrdinalIgnoreCase);
        var explicitlyIncludedDirectories = new HashSet<string>(
            entryAssemblyPaths
                .Select(Path.GetDirectoryName)
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Select(static path => Path.GetFullPath(path!)),
            StringComparer.OrdinalIgnoreCase);
        var excludedRelativePaths = ResolveRootScanExcludedPaths(manifestPath!);
        var candidateAssemblyPaths = EnumerateCandidateAssemblies(
                moduleRoot,
                excludedRelativePaths,
                explicitlyIncluded,
                explicitlyIncludedDirectories)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ScopedRootAnalysis(candidateAssemblyPaths, entryAssemblyPaths);
    }

    private static string[] ResolveManifestAssemblyPaths(string moduleRoot, string manifestPath)
    {
        var list = new List<string>();

        if (ManifestEditor.TryGetTopLevelString(manifestPath, "RootModule", out var root) &&
            !string.IsNullOrWhiteSpace(root))
        {
            list.Add(ResolveModuleRelativePath(moduleRoot, root!));
        }

        if (ManifestEditor.TryGetTopLevelStringArray(manifestPath, "NestedModules", out var nested) &&
            nested is { Length: > 0 })
        {
            foreach (var entry in nested.Where(static value => !string.IsNullOrWhiteSpace(value)))
                list.Add(ResolveModuleRelativePath(moduleRoot, entry!));
        }

        if (ManifestEditor.TryGetTopLevelStringArray(manifestPath, "RequiredAssemblies", out var assemblies) &&
            assemblies is { Length: > 0 })
        {
            foreach (var entry in assemblies.Where(static value => !string.IsNullOrWhiteSpace(value)))
                list.Add(ResolveModuleRelativePath(moduleRoot, entry!));
        }

        return list
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ResolveModuleRelativePath(string moduleRoot, string value)
    {
        var trimmed = (value ?? string.Empty).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(trimmed))
            return string.Empty;

        return Path.GetFullPath(Path.IsPathRooted(trimmed) ? trimmed : Path.Combine(moduleRoot, trimmed));
    }

    private static string[] ResolveRootScanExcludedPaths(string manifestPath)
    {
        // Script-package layouts commonly stage bundled required modules under a top-level Modules folder.
        // Those DLLs are copied for installation, not imported as part of the root module itself.
        // If a package genuinely imports assemblies from that folder, they should be declared through
        // RootModule, NestedModules, or RequiredAssemblies so scoped analysis includes them explicitly.
        var list = new List<string> { BundledModulesFolderName };
        var internalsPath = TryReadDeliveryInternalsPath(manifestPath);
        if (!string.IsNullOrWhiteSpace(internalsPath))
            list.Add(internalsPath!);

        return list
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path.Trim())
            .Select(static path => path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar))
            .Select(static path => path.TrimStart(Path.DirectorySeparatorChar))
            .Select(static path => path.TrimEnd(Path.DirectorySeparatorChar))
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? TryReadDeliveryInternalsPath(string manifestPath)
    {
        try
        {
            Token[] tokens;
            ParseError[] errors;
            var ast = Parser.ParseFile(manifestPath, out tokens, out errors);
            if (errors is { Length: > 0 })
                return null;

            var top = TryGetTopLevelManifestHashtable(ast);
            if (top is null)
                return null;

            var privateData = FindChildHashtable(top, "PrivateData");
            var psData = privateData is null ? null : FindChildHashtable(privateData, "PSData");
            var delivery = psData is null ? null : FindChildHashtable(psData, "Delivery");
            if (delivery is null)
                return null;

            var deliveryEnabled = true;
            foreach (var kv in delivery.KeyValuePairs)
            {
                var key = GetHashtableKeyName(kv.Item1);
                var expr = UnwrapExpression(kv.Item2);

                if (string.Equals(key, "Enable", StringComparison.OrdinalIgnoreCase))
                {
                    switch (expr)
                    {
                        case ConstantExpressionAst c when c.Value is bool value:
                            deliveryEnabled = value;
                            break;
                        case VariableExpressionAst v when string.Equals(v.VariablePath.UserPath, "true", StringComparison.OrdinalIgnoreCase):
                            deliveryEnabled = true;
                            break;
                        case VariableExpressionAst v when string.Equals(v.VariablePath.UserPath, "false", StringComparison.OrdinalIgnoreCase):
                            deliveryEnabled = false;
                            break;
                    }

                    continue;
                }

                if (!string.Equals(key, "InternalsPath", StringComparison.OrdinalIgnoreCase))
                    continue;

                switch (expr)
                {
                    case StringConstantExpressionAst s when !string.IsNullOrWhiteSpace(s.Value):
                        return s.Value.Trim();
                    case ConstantExpressionAst c when c.Value is string str && !string.IsNullOrWhiteSpace(str):
                        return str.Trim();
                }

                return deliveryEnabled ? "Internals" : null;
            }

            return deliveryEnabled ? "Internals" : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static HashtableAst? TryGetTopLevelManifestHashtable(Ast ast)
    {
        if (ast is ScriptBlockAst scriptBlock &&
            scriptBlock.EndBlock is not null)
        {
            foreach (var statement in scriptBlock.EndBlock.Statements)
            {
                var expr = UnwrapExpression(statement);
                if (expr is HashtableAst hashtable)
                    return hashtable;
            }
        }

        return (HashtableAst?)ast.Find(static node => node is HashtableAst, searchNestedScriptBlocks: false);
    }

    private static HashtableAst? FindChildHashtable(HashtableAst parent, string key)
    {
        foreach (var kv in parent.KeyValuePairs)
        {
            var name = GetHashtableKeyName(kv.Item1);
            if (!string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
                continue;

            var expr = UnwrapExpression(kv.Item2);
            if (expr is HashtableAst hashtable)
                return hashtable;
        }

        return null;
    }

    private static ExpressionAst? UnwrapExpression(StatementAst? statement)
    {
        if (statement is PipelineAst pipeline &&
            pipeline.PipelineElements.Count == 1 &&
            pipeline.PipelineElements[0] is CommandExpressionAst commandExpression)
        {
            return commandExpression.Expression;
        }

        return null;
    }

    private static string? GetHashtableKeyName(Ast? keyAst)
    {
        return keyAst switch
        {
            StringConstantExpressionAst s => s.Value,
            ConstantExpressionAst c when c.Value is string str => str,
            _ => null
        };
    }

    private static string NormalizePathForComparison(string path)
        => Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

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

        // Core validation may run from a Desktop host, so seed assemblies PowerShell 7 provides at runtime.
        foreach (var name in WellKnownCoreRuntimeAssemblyNames)
            set.Add(name);

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

    private sealed class ScopedRootAnalysis
    {
        public string[] CandidateAssemblyPaths { get; }
        public string[] EntryAssemblyPaths { get; }

        public ScopedRootAnalysis(string[] candidateAssemblyPaths, string[] entryAssemblyPaths)
        {
            CandidateAssemblyPaths = candidateAssemblyPaths ?? Array.Empty<string>();
            EntryAssemblyPaths = entryAssemblyPaths ?? Array.Empty<string>();
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
