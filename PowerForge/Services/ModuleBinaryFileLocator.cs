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

    internal static string[] ResolveAssemblyReferences(
        string moduleName,
        IReadOnlyList<string>? exportAssemblies)
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
            candidate = NormalizePathSeparators(candidate);
            var currentDirectoryPrefix = "." + Path.DirectorySeparatorChar;
            while (candidate.StartsWith(currentDirectoryPrefix, StringComparison.Ordinal))
                candidate = candidate.Substring(currentDirectoryPrefix.Length);
            if (IsPortableRootedPath(candidate))
            {
                // Absolute configuration paths identify a build-time input. The generated module must
                // resolve the copied payload from its packaged Lib layout after installation.
                candidate = Path.GetFileName(candidate);
            }
            else
            {
                var libPrefix = "Lib" + Path.DirectorySeparatorChar;
                if (candidate.StartsWith(libPrefix, StringComparison.OrdinalIgnoreCase))
                    candidate = candidate.Substring(libPrefix.Length);
                else
                {
                    // Relative paths outside Lib identify build-time inputs. ModuleBuilder copies those
                    // assemblies into the packaged payload by file name, so installed resolution must not
                    // retain project-relative directories such as Artifacts/ or bin/.
                    candidate = Path.GetFileName(candidate);
                }
            }
            candidate = candidate.Replace(Path.DirectorySeparatorChar, '/');
            if (seen.Add(candidate))
                normalized.Add(candidate);
        }

        if (normalized.Count == 0)
            return new[] { moduleName + ".dll" };

        var moduleAssembly = moduleName + ".dll";
        return normalized
            .OrderBy(entry => string.Equals(Path.GetFileName(entry), moduleAssembly, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
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
            candidate = Path.GetFileName(NormalizePathSeparators(candidate));
            if (!string.IsNullOrWhiteSpace(candidate) && seen.Add(candidate))
                normalized.Add(candidate);
        }

        return normalized.ToArray();
    }

    private static string NormalizePathSeparators(string path)
        => path
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

    private static bool IsPortableRootedPath(string path)
    {
        if (Path.IsPathRooted(path))
            return true;

        return path.Length >= 3 &&
               char.IsLetter(path[0]) &&
               path[1] == ':' &&
               (path[2] == '\\' || path[2] == '/');
    }
}
