using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

internal sealed class ModuleBinaryExportSurface
{
    internal ModuleBinaryExportSurface(bool hasAssemblies, string[] cmdlets, string[] aliases)
    {
        HasAssemblies = hasAssemblies;
        Cmdlets = cmdlets ?? Array.Empty<string>();
        Aliases = aliases ?? Array.Empty<string>();
    }

    internal bool HasAssemblies { get; }
    internal string[] Cmdlets { get; }
    internal string[] Aliases { get; }
}

internal static class ModuleBinaryExportSurfaceValidator
{
    internal static ModuleBinaryExportSurface Detect(
        string projectRoot,
        string moduleName,
        IReadOnlyList<string>? exportAssemblies)
    {
        var assembliesByPayload = ResolveAssembliesByPayload(projectRoot, moduleName, exportAssemblies);
        var hasSelectablePayloads = !assembliesByPayload.ContainsKey("module");
        var hasAssemblies = assembliesByPayload.Values.Any(static assemblies => assemblies.Length > 0);
        if (hasSelectablePayloads)
            ValidateConfiguredAssemblies(assembliesByPayload, moduleName, exportAssemblies);
        else if (!hasAssemblies)
            return new ModuleBinaryExportSurface(false, Array.Empty<string>(), Array.Empty<string>());
        else
            ValidateConfiguredAssemblies(assembliesByPayload, moduleName, exportAssemblies);
        var surfaces = assembliesByPayload
            .Select(pair => new
            {
                Payload = pair.Key,
                HasAssemblies = pair.Value.Length > 0,
                AssemblyFileNames = pair.Value
                    .Select(Path.GetFileName)
                    .Where(static name => !string.IsNullOrWhiteSpace(name))
                    .Select(static name => name!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                Cmdlets = BinaryExportDetector.DetectBinaryCmdlets(pair.Value).ToArray(),
                Aliases = BinaryExportDetector.DetectBinaryAliases(pair.Value).ToArray(),
            })
            .ToArray();

        if (surfaces.Length == 0)
            return new ModuleBinaryExportSurface(false, Array.Empty<string>(), Array.Empty<string>());

        var baseline = surfaces[0];
        foreach (var candidate in surfaces.Skip(1))
        {
            var assembliesMatch = baseline.AssemblyFileNames.ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(candidate.AssemblyFileNames);
            var cmdletsMatch = baseline.Cmdlets.ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(candidate.Cmdlets);
            var aliasesMatch = baseline.Aliases.ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(candidate.Aliases);
            if (assembliesMatch && cmdletsMatch && aliasesMatch)
                continue;

            throw new InvalidOperationException(BuildMismatchMessage(
                baseline.Payload,
                baseline.AssemblyFileNames,
                baseline.Cmdlets,
                baseline.Aliases,
                candidate.Payload,
                candidate.AssemblyFileNames,
                candidate.Cmdlets,
                candidate.Aliases));
        }

        return new ModuleBinaryExportSurface(
            surfaces.Any(static surface => surface.HasAssemblies),
            baseline.Cmdlets,
            baseline.Aliases);
    }

    internal static void ValidateConfiguredAssemblies(
        string projectRoot,
        string moduleName,
        IReadOnlyList<string>? exportAssemblies)
    {
        var assembliesByPayload = ResolveAssembliesByPayload(projectRoot, moduleName, exportAssemblies);
        var hasSelectablePayloads = !assembliesByPayload.ContainsKey("module");
        var hasAssemblies = assembliesByPayload.Values.Any(static assemblies => assemblies.Length > 0);
        if (!hasSelectablePayloads && !hasAssemblies)
            return;

        ValidateConfiguredAssemblies(
            assembliesByPayload,
            moduleName,
            exportAssemblies);
    }

    private static void ValidateConfiguredAssemblies(
        IReadOnlyDictionary<string, string[]> assembliesByPayload,
        string moduleName,
        IReadOnlyList<string>? exportAssemblies)
    {
        var configuredAssemblyFileNames = ResolveExportAssemblyFileNames(moduleName, exportAssemblies);
        foreach (var payload in assembliesByPayload)
        {
            var availableAssemblyFileNames = payload.Value
                .Select(Path.GetFileName)
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Select(static name => name!)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            var missingConfiguredAssemblies = configuredAssemblyFileNames
                .Except(availableAssemblyFileNames, StringComparer.OrdinalIgnoreCase)
                .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (missingConfiguredAssemblies.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Payload '{payload.Key}' is missing configured export assemblies: {string.Join(", ", missingConfiguredAssemblies)}.");
            }
        }
    }

    private static IReadOnlyDictionary<string, string[]> ResolveAssembliesByPayload(
        string projectRoot,
        string moduleName,
        IReadOnlyList<string>? exportAssemblies)
    {
        var fileNames = ResolveExportAssemblyFileNames(moduleName, exportAssemblies);
        var hasExplicitExportAssemblies = exportAssemblies?.Any(static entry => !string.IsNullOrWhiteSpace(entry)) == true;
        var libRoot = Path.Combine(projectRoot, "Lib");
        if (Directory.Exists(libRoot))
        {
            var payloadDirectories = Directory.EnumerateDirectories(libRoot)
                .Select(path => new { Path = path, Name = Path.GetFileName(path) })
                .Where(static item => ModuleBinaryPayloadLayout.IsSelectablePayloadFolderName(item.Name))
                .OrderBy(static item => GetPayloadFolderSortOrder(item.Name!))
                .ThenBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (payloadDirectories.Length > 0)
            {
                var pathQualifiedEntries = ResolveConfiguredEntries(moduleName, exportAssemblies)
                    .Where(IsPathQualifiedEntry)
                    .ToArray();
                if (pathQualifiedEntries.Length > 0)
                {
                    throw new InvalidOperationException(
                        "Path-qualified export assemblies are ambiguous with side-by-side module payloads: " +
                        string.Join(", ", pathQualifiedEntries) +
                        ". Configure export assembly file names so each payload can be validated independently.");
                }

                return payloadDirectories.ToDictionary(
                    static item => item.Name!,
                    item => ResolveMatchingAssemblies(
                        item.Path,
                        fileNames,
                        hasExplicitExportAssemblies
                            ? StringComparison.OrdinalIgnoreCase
                            : StringComparison.Ordinal),
                    StringComparer.OrdinalIgnoreCase);
            }
        }

        return new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["module"] = ResolveLegacyMatchingAssemblies(projectRoot, moduleName, exportAssemblies),
        };
    }

    private static string[] ResolveLegacyMatchingAssemblies(
        string projectRoot,
        string moduleName,
        IReadOnlyList<string>? exportAssemblies)
    {
        var paths = new List<string>();
        var entries = ResolveConfiguredEntries(moduleName, exportAssemblies);

        foreach (var entry in entries)
        {
            var name = entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? entry
                : entry + ".dll";
            try
            {
                if (Path.IsPathRooted(name))
                {
                    if (File.Exists(name))
                        paths.Add(Path.GetFullPath(name));
                    continue;
                }

                if (name.Contains(Path.DirectorySeparatorChar) || name.Contains(Path.AltDirectorySeparatorChar))
                {
                    var relativePath = Path.GetFullPath(Path.Combine(projectRoot, name));
                    if (File.Exists(relativePath))
                        paths.Add(relativePath);
                    continue;
                }

                paths.AddRange(Directory.EnumerateFiles(projectRoot, name, SearchOption.AllDirectories));
            }
            catch
            {
                // Preserve best-effort legacy discovery for single-payload and prebuilt layouts.
            }
        }

        return paths
            .Where(path => !IsUnderUnselectablePayloadLikeDirectory(projectRoot, path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsUnderUnselectablePayloadLikeDirectory(string projectRoot, string assemblyPath)
    {
        var libRoot = Path.GetFullPath(Path.Combine(projectRoot, "Lib"))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(assemblyPath);
        if (!fullPath.StartsWith(libRoot, StringComparison.OrdinalIgnoreCase))
            return false;

        var relativePath = fullPath.Substring(libRoot.Length);
        var separatorIndex = relativePath.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
        if (separatorIndex <= 0)
            return false;

        var firstDirectory = relativePath.Substring(0, separatorIndex);
        return ModuleBinaryPayloadLayout.IsPayloadLikeFolderName(firstDirectory) &&
               !ModuleBinaryPayloadLayout.IsSelectablePayloadFolderName(firstDirectory);
    }

    private static string[] ResolveMatchingAssemblies(
        string root,
        ISet<string> fileNames,
        StringComparison fileNameComparison)
    {
        if (!Directory.Exists(root))
            return Array.Empty<string>();

        try
        {
            return Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
                .Where(static path => string.Equals(Path.GetExtension(path), ".dll", StringComparison.OrdinalIgnoreCase))
                .Where(path => fileNames.Any(expected =>
                    string.Equals(expected, Path.GetFileName(path), fileNameComparison)))
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static ISet<string> ResolveExportAssemblyFileNames(
        string moduleName,
        IReadOnlyList<string>? exportAssemblies)
    {
        var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = ResolveConfiguredEntries(moduleName, exportAssemblies);

        foreach (var entry in entries)
        {
            var name = entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? entry
                : entry + ".dll";
            name = Path.GetFileName(name);
            if (!string.IsNullOrWhiteSpace(name))
                fileNames.Add(name);
        }

        return fileNames;
    }

    private static string[] ResolveConfiguredEntries(
        string moduleName,
        IReadOnlyList<string>? exportAssemblies)
    {
        var entries = (exportAssemblies ?? Array.Empty<string>())
            .Where(static entry => !string.IsNullOrWhiteSpace(entry))
            .Select(static entry => entry.Trim().Trim('"'))
            .ToArray();
        return entries.Length > 0 ? entries : new[] { moduleName + ".dll" };
    }

    private static bool IsPathQualifiedEntry(string entry)
        => Path.IsPathRooted(entry) ||
           entry.IndexOf('/') >= 0 ||
           entry.IndexOf('\\') >= 0 ||
           entry.Length >= 2 && char.IsLetter(entry[0]) && entry[1] == ':';

    private static int GetPayloadFolderSortOrder(string folder)
    {
        if (folder.Equals("Core", StringComparison.OrdinalIgnoreCase)) return 0;
        if (folder.StartsWith("Core-", StringComparison.OrdinalIgnoreCase)) return 1;
        if (folder.Equals("Default", StringComparison.OrdinalIgnoreCase)) return 10;
        if (folder.StartsWith("Default-", StringComparison.OrdinalIgnoreCase)) return 11;
        if (folder.Equals("Standard", StringComparison.OrdinalIgnoreCase)) return 20;
        if (folder.StartsWith("Standard-", StringComparison.OrdinalIgnoreCase)) return 21;
        return 30;
    }

    private static string BuildMismatchMessage(
        string baselinePayload,
        IReadOnlyList<string> baselineAssemblyFileNames,
        IReadOnlyList<string> baselineCmdlets,
        IReadOnlyList<string> baselineAliases,
        string candidatePayload,
        IReadOnlyList<string> candidateAssemblyFileNames,
        IReadOnlyList<string> candidateCmdlets,
        IReadOnlyList<string> candidateAliases)
    {
        var differences = new List<string>();
        AddDifferences(differences, "export assemblies", baselinePayload, baselineAssemblyFileNames, candidatePayload, candidateAssemblyFileNames);
        AddDifferences(differences, "cmdlets", baselinePayload, baselineCmdlets, candidatePayload, candidateCmdlets);
        AddDifferences(differences, "aliases", baselinePayload, baselineAliases, candidatePayload, candidateAliases);
        return "Binary export surfaces must match across side-by-side module payloads. " + string.Join(" ", differences);
    }

    private static void AddDifferences(
        ICollection<string> differences,
        string surfaceName,
        string baselinePayload,
        IReadOnlyList<string> baselineValues,
        string candidatePayload,
        IReadOnlyList<string> candidateValues)
    {
        var baselineSet = baselineValues.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidateSet = candidateValues.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = baselineSet.Except(candidateSet, StringComparer.OrdinalIgnoreCase).OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        var additional = candidateSet.Except(baselineSet, StringComparer.OrdinalIgnoreCase).OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        if (missing.Length > 0)
            differences.Add($"Payload '{candidatePayload}' is missing {surfaceName} from '{baselinePayload}': {string.Join(", ", missing)}.");
        if (additional.Length > 0)
            differences.Add($"Payload '{candidatePayload}' adds {surfaceName} not present in '{baselinePayload}': {string.Join(", ", additional)}.");
    }
}
