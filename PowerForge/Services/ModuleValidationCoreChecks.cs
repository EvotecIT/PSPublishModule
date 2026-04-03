using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace PowerForge;

internal static class ModuleValidationCoreChecks
{
    internal static ModuleValidationCheckResult? ValidateStructure(
        ModuleValidationSpec spec,
        ModuleStructureValidationSettings settings)
    {
        if (settings.Severity == ValidationSeverity.Off) return null;

        var issues = new List<string>();
        var summaryParts = new List<string>(4);

        var manifestPath = spec.ManifestPath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        {
            issues.Add("Manifest not found.");
            return BuildResult("Module structure", settings.Severity, issues, "manifest missing");
        }

        var moduleRoot = string.IsNullOrWhiteSpace(spec.StagingPath)
            ? Path.GetDirectoryName(manifestPath) ?? string.Empty
            : spec.StagingPath;

        var publicFunctions = DiscoverFunctionFileNames(moduleRoot, settings.PublicFunctionPaths);
        if (publicFunctions.Count > 0)
            summaryParts.Add($"public functions {publicFunctions.Count}");

        var internalFunctions = DiscoverFunctionFileNames(moduleRoot, settings.InternalFunctionPaths);
        if (internalFunctions.Count > 0)
            summaryParts.Add($"internal functions {internalFunctions.Count}");

        if (settings.ValidateExports)
        {
            var (exportedFunctions, wildcardExport) = GetManifestStringArray(manifestPath, "FunctionsToExport");
            if (wildcardExport && !settings.AllowWildcardExports)
            {
                issues.Add("FunctionsToExport uses wildcard; cannot validate exports.");
            }
            else if (exportedFunctions is { Length: > 0 } && !wildcardExport)
            {
                var exportedSet = new HashSet<string>(exportedFunctions, StringComparer.OrdinalIgnoreCase);
                summaryParts.Add($"exports {exportedSet.Count}");

                var missing = publicFunctions.Where(f => !exportedSet.Contains(f)).ToArray();
                var extra = exportedSet.Where(f => !publicFunctions.Contains(f)).ToArray();

                if (missing.Length > 0)
                    issues.Add($"Public functions not exported: {FormatList(missing)}");
                if (extra.Length > 0)
                    issues.Add($"Exports not found in public folder: {FormatList(extra)}");
            }
        }

        if (settings.ValidateInternalNotExported && internalFunctions.Count > 0)
        {
            var (exportedFunctions, wildcardExport) = GetManifestStringArray(manifestPath, "FunctionsToExport");
            if (exportedFunctions is { Length: > 0 } && !wildcardExport)
            {
                var exportedSet = new HashSet<string>(exportedFunctions, StringComparer.OrdinalIgnoreCase);
                var leaked = internalFunctions.Where(f => exportedSet.Contains(f)).ToArray();
                if (leaked.Length > 0)
                    issues.Add($"Internal functions exported: {FormatList(leaked)}");
            }
        }

        if (settings.ValidateManifestFiles)
        {
            if (ModuleManifestValueReader.TryGetTopLevelString(manifestPath, "RootModule", out var root) &&
                !string.IsNullOrWhiteSpace(root) &&
                !File.Exists(Path.Combine(moduleRoot, root)))
            {
                issues.Add($"RootModule missing: {root}");
            }

            foreach (var format in ModuleManifestValueReader.ReadTopLevelStringOrArray(manifestPath, "FormatsToProcess"))
            {
                if (string.IsNullOrWhiteSpace(format)) continue;
                if (!File.Exists(Path.Combine(moduleRoot, format)))
                    issues.Add($"Format file missing: {format}");
            }

            foreach (var type in ModuleManifestValueReader.ReadTopLevelStringOrArray(manifestPath, "TypesToProcess"))
            {
                if (string.IsNullOrWhiteSpace(type)) continue;
                if (!File.Exists(Path.Combine(moduleRoot, type)))
                    issues.Add($"Type file missing: {type}");
            }

            foreach (var assembly in ModuleManifestValueReader.ReadTopLevelStringOrArray(manifestPath, "RequiredAssemblies"))
            {
                if (string.IsNullOrWhiteSpace(assembly)) continue;
                if (assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    if (!File.Exists(Path.Combine(moduleRoot, assembly)))
                        issues.Add($"Required assembly missing: {assembly}");
                }
                else if (!TryLoadAssembly(assembly, out var error))
                {
                    issues.Add($"Required assembly failed to load: {assembly} ({error})");
                }
            }
        }

        var summary = summaryParts.Count == 0 ? "structure verified" : string.Join(", ", summaryParts);
        return BuildResult("Module structure", settings.Severity, issues, summary);
    }

    internal static ModuleValidationCheckResult? ValidateBinary(
        ModuleValidationSpec spec,
        BinaryModuleValidationSettings settings)
    {
        if (settings.Severity == ValidationSeverity.Off) return null;

        var issues = new List<string>();
        var summaryParts = new List<string>(3);
        var manifestPath = spec.ManifestPath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        {
            issues.Add("Manifest not found.");
            return BuildResult("Binary exports", settings.Severity, issues, "manifest missing");
        }

        var moduleRoot = string.IsNullOrWhiteSpace(spec.StagingPath)
            ? Path.GetDirectoryName(manifestPath) ?? string.Empty
            : spec.StagingPath;

        var assemblyPaths = ResolveManifestAssemblies(manifestPath)
            .Where(a => !string.IsNullOrWhiteSpace(a) && a.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(a => Path.Combine(moduleRoot, a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (assemblyPaths.Length == 0)
            return BuildResult("Binary exports", settings.Severity, issues, "no binaries");

        if (settings.ValidateAssembliesExist)
        {
            foreach (var asm in assemblyPaths)
            {
                if (!File.Exists(asm))
                    issues.Add($"Assembly missing: {Path.GetFileName(asm)}");
            }
        }

        var existingAssemblies = assemblyPaths.Where(File.Exists).ToArray();
        if (existingAssemblies.Length == 0)
            return BuildResult("Binary exports", settings.Severity, issues, "no binaries");

        if (settings.ValidateManifestExports)
        {
            var detectedCmdlets = BinaryExportDetector.DetectBinaryCmdlets(existingAssemblies);
            var detectedAliases = BinaryExportDetector.DetectBinaryAliases(existingAssemblies);

            var (manifestCmdlets, cmdletWildcard) = GetManifestStringArray(manifestPath, "CmdletsToExport");
            var (manifestAliases, aliasWildcard) = GetManifestStringArray(manifestPath, "AliasesToExport");

            if (cmdletWildcard && !settings.AllowWildcardExports)
                issues.Add("CmdletsToExport uses wildcard; cannot validate binary exports.");
            else if (manifestCmdlets is { Length: > 0 } && !cmdletWildcard)
            {
                var cmdletSet = new HashSet<string>(manifestCmdlets, StringComparer.OrdinalIgnoreCase);
                var missing = detectedCmdlets.Where(c => !cmdletSet.Contains(c)).ToArray();
                var extra = cmdletSet.Where(c => !detectedCmdlets.Contains(c)).ToArray();
                if (missing.Length > 0)
                    issues.Add($"Binary cmdlets not exported: {FormatList(missing)}");
                if (extra.Length > 0)
                    issues.Add($"Manifest cmdlets missing from binaries: {FormatList(extra)}");
            }

            if (aliasWildcard && !settings.AllowWildcardExports)
                issues.Add("AliasesToExport uses wildcard; cannot validate binary exports.");
            else if (manifestAliases is { Length: > 0 } && !aliasWildcard)
            {
                var aliasSet = new HashSet<string>(manifestAliases, StringComparer.OrdinalIgnoreCase);
                var missing = detectedAliases.Where(a => !aliasSet.Contains(a)).ToArray();
                var extra = aliasSet.Where(a => !detectedAliases.Contains(a)).ToArray();
                if (missing.Length > 0)
                    issues.Add($"Binary aliases not exported: {FormatList(missing)}");
                if (extra.Length > 0)
                    issues.Add($"Manifest aliases missing from binaries: {FormatList(extra)}");
            }

            summaryParts.Add($"cmdlets {detectedCmdlets.Count}");
            summaryParts.Add($"aliases {detectedAliases.Count}");
        }

        var summary = summaryParts.Count == 0 ? "ok" : string.Join(", ", summaryParts);
        return BuildResult("Binary exports", settings.Severity, issues, summary);
    }

    internal static ModuleValidationCheckResult? ValidateCsproj(
        ModuleValidationSpec spec,
        CsprojValidationSettings settings)
    {
        if (settings.Severity == ValidationSeverity.Off) return null;

        var issues = new List<string>();
        var summaryParts = new List<string>(2);

        var csproj = spec.BuildSpec?.CsprojPath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(csproj))
        {
            issues.Add("CsprojPath is not configured.");
            return BuildResult("Csproj", settings.Severity, issues, "missing csproj");
        }

        var csprojPath = Path.IsPathRooted(csproj)
            ? csproj
            : Path.GetFullPath(Path.Combine(spec.ProjectRoot ?? string.Empty, csproj));

        if (!File.Exists(csprojPath))
        {
            issues.Add($"Csproj not found: {csproj}");
            return BuildResult("Csproj", settings.Severity, issues, "missing csproj");
        }

        try
        {
            var doc = XDocument.Load(csprojPath);
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

            string? targetFramework = doc.Descendants(ns + "TargetFramework").Select(e => e.Value).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            string? targetFrameworks = doc.Descendants(ns + "TargetFrameworks").Select(e => e.Value).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            string? outputType = doc.Descendants(ns + "OutputType").Select(e => e.Value).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

            if (settings.RequireTargetFramework && string.IsNullOrWhiteSpace(targetFramework) && string.IsNullOrWhiteSpace(targetFrameworks))
                issues.Add("TargetFramework/TargetFrameworks not set.");
            else
                summaryParts.Add($"tfm {(targetFramework ?? targetFrameworks)}");

            if (settings.RequireLibraryOutput && !string.IsNullOrWhiteSpace(outputType) &&
                !string.Equals(outputType, "Library", StringComparison.OrdinalIgnoreCase))
                issues.Add($"OutputType is '{outputType}' (expected Library).");
            else if (!string.IsNullOrWhiteSpace(outputType))
                summaryParts.Add($"output {outputType}");
        }
        catch (Exception ex)
        {
            issues.Add($"Csproj parse failed: {ex.Message}");
        }

        var summary = summaryParts.Count == 0 ? "ok" : string.Join(", ", summaryParts);
        return BuildResult("Csproj", settings.Severity, issues, summary);
    }

    private static ModuleValidationCheckResult BuildResult(
        string name,
        ValidationSeverity severity,
        List<string> issues,
        string summary)
    {
        var issueArray = issues?.Where(i => !string.IsNullOrWhiteSpace(i)).ToArray() ?? Array.Empty<string>();
        var status = issueArray.Length == 0
            ? CheckStatus.Pass
            : severity == ValidationSeverity.Error
                ? CheckStatus.Fail
                : CheckStatus.Warning;
        return new ModuleValidationCheckResult(name, severity, status, summary, issueArray);
    }

    private static bool TryLoadAssembly(string name, out string? error)
    {
        error = null;
        try
        {
            Assembly.Load(new AssemblyName(name));
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static HashSet<string> DiscoverFunctionFileNames(string moduleRoot, string[]? paths)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (paths is null) return names;

        foreach (var rel in paths)
        {
            if (string.IsNullOrWhiteSpace(rel)) continue;
            var full = Path.IsPathRooted(rel) ? rel : Path.Combine(moduleRoot, rel);
            if (!Directory.Exists(full)) continue;

            foreach (var file in Directory.EnumerateFiles(full, "*.ps1", SearchOption.AllDirectories))
                names.Add(Path.GetFileNameWithoutExtension(file));
        }

        return names;
    }

    private static (string[]? Values, bool Wildcard) GetManifestStringArray(string manifestPath, string key)
    {
        var values = ModuleManifestValueReader.ReadTopLevelStringOrArray(manifestPath, key)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToArray();

        if (values.Length == 1 && string.Equals(values[0].Trim(), "*", StringComparison.OrdinalIgnoreCase))
            return (Array.Empty<string>(), true);

        return (values.Length == 0 ? null : values, false);
    }

    private static string[] ResolveManifestAssemblies(string manifestPath)
    {
        var list = new List<string>();

        var rootModule = ModuleManifestValueReader.ReadTopLevelString(manifestPath, "RootModule");
        if (!string.IsNullOrWhiteSpace(rootModule))
            list.Add(rootModule!);

        list.AddRange(ModuleManifestValueReader.ReadTopLevelStringOrArray(manifestPath, "NestedModules"));
        list.AddRange(ModuleManifestValueReader.ReadTopLevelStringOrArray(manifestPath, "RequiredAssemblies"));

        return list
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string FormatList(IEnumerable<string> items, int max = 8)
    {
        var list = items.Where(i => !string.IsNullOrWhiteSpace(i)).ToArray();
        if (list.Length == 0) return string.Empty;
        if (list.Length <= max) return string.Join(", ", list);
        return string.Join(", ", list.Take(max)) + ", ...";
    }
}
