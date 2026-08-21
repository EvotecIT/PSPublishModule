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
}
