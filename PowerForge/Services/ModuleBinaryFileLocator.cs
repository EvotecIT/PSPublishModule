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

    internal static bool ContainsFileName(string directory, string fileName, SearchOption searchOption)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        var expected = Path.GetFileName(fileName);
        return Enumerate(directory, searchOption)
            .Any(path => string.Equals(Path.GetFileName(path), expected, StringComparison.OrdinalIgnoreCase));
    }

    internal static string ResolvePrimaryAssemblyFileName(
        string moduleName,
        IReadOnlyList<string>? exportAssemblies)
    {
        var configured = (exportAssemblies ?? Array.Empty<string>())
            .FirstOrDefault(static entry => !string.IsNullOrWhiteSpace(entry));
        var candidate = string.IsNullOrWhiteSpace(configured) ? moduleName + ".dll" : configured!.Trim().Trim('"');
        if (!candidate.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            candidate += ".dll";

        return Path.GetFileName(candidate);
    }
}
