using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace PowerForge;

internal static partial class ModuleBootstrapperGenerator
{
    private static IReadOnlyList<string> OrderManagedLibrariesForDesktopPreload(
        string directory,
        IReadOnlyCollection<string> fileNames,
        ISet<string> excluded,
        ISet<string> exportAssemblies)
    {
        string[] candidates = fileNames
            .Where(name => !excluded.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var assemblyFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var references = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (string fileName in candidates)
        {
            if (!TryReadAssemblyMetadata(Path.Combine(directory, fileName), out string? assemblyName, out string[] assemblyReferences))
            {
                references[fileName] = Array.Empty<string>();
                continue;
            }

            assemblyFiles[assemblyName!] = fileName;
            references[fileName] = assemblyReferences;
        }

        var remaining = new HashSet<string>(candidates, StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>(candidates.Length);
        while (remaining.Count > 0)
        {
            string? next = remaining
                .Where(fileName => references.TryGetValue(fileName, out string[]? required) &&
                    required.All(reference => !assemblyFiles.TryGetValue(reference, out string? dependencyFile) ||
                                              !remaining.Contains(dependencyFile)))
                .OrderBy(fileName => exportAssemblies.Contains(fileName) ? 1 : 0)
                .ThenBy(fileName => fileName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            // Cycles are unusual for managed assemblies. Keep generation deterministic and let the
            // module-scoped resolver service any cycle edge whose requesting assembly is already owned.
            next ??= remaining
                .OrderBy(fileName => exportAssemblies.Contains(fileName) ? 1 : 0)
                .ThenBy(fileName => fileName, StringComparer.OrdinalIgnoreCase)
                .First();
            remaining.Remove(next);
            ordered.Add(next);
        }

        return ordered;
    }

    private static bool TryReadAssemblyMetadata(string path, out string? assemblyName, out string[] references)
    {
        assemblyName = null;
        references = Array.Empty<string>();
        try
        {
            using FileStream stream = File.OpenRead(path);
            using var reader = new PEReader(stream);
            if (!reader.HasMetadata) return false;
            MetadataReader metadata = reader.GetMetadataReader();
            if (!metadata.IsAssembly) return false;
            assemblyName = metadata.GetString(metadata.GetAssemblyDefinition().Name);
            references = metadata.AssemblyReferences
                .Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return !string.IsNullOrWhiteSpace(assemblyName);
        }
        catch (BadImageFormatException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
