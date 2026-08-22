using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

internal static class ModuleBinaryFileLocator
{
    internal static bool HasAny(string directory, SearchOption searchOption)
        => Enumerate(directory, searchOption).Any();

    internal static IEnumerable<string> Enumerate(string directory, SearchOption searchOption)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return Enumerable.Empty<string>();

        return Directory.EnumerateFiles(directory, "*", searchOption)
            .Where(static path => string.Equals(Path.GetExtension(path), ".dll", StringComparison.OrdinalIgnoreCase));
    }

    internal static bool ContainsAnyFileName(
        string directory,
        IReadOnlyList<string>? fileNames,
        SearchOption searchOption)
    {
        var expected = new HashSet<string>(
            (fileNames ?? Array.Empty<string>())
                .Where(static fileName => !string.IsNullOrWhiteSpace(fileName))
                .Select(Path.GetFileName)
                .Where(static fileName => !string.IsNullOrWhiteSpace(fileName))!,
            StringComparer.OrdinalIgnoreCase);
        if (expected.Count == 0)
            return false;

        return Enumerate(directory, searchOption)
            .Any(path => expected.Contains(Path.GetFileName(path)));
    }

    internal static string[] ResolveAssemblyFileNames(
        string moduleName,
        IReadOnlyList<string>? exportAssemblies)
    {
        var configured = NormalizeAssemblyFileNames(exportAssemblies);
        if (configured.Length == 0)
            return new[] { moduleName + ".dll" };

        var moduleAssembly = moduleName + ".dll";
        return configured
            .OrderBy(entry => string.Equals(entry, moduleAssembly, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ToArray();
    }

    private static string[] NormalizeAssemblyFileNames(IReadOnlyList<string>? exportAssemblies)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<string>();
        foreach (var entry in exportAssemblies ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(entry))
                continue;

            var candidate = entry.Trim().Trim('"');
            if (!candidate.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                candidate += ".dll";
            candidate = Path.GetFileName(candidate);
            if (!string.IsNullOrWhiteSpace(candidate) && seen.Add(candidate))
                normalized.Add(candidate);
        }

        return normalized.ToArray();
    }
}
