namespace PowerForge;

internal static partial class ModuleBootstrapperGenerator
{
    /// <summary>
    /// Returns every Lib payload directory that can hold a configured export assembly on PowerShell 7,
    /// including runtime fallbacks. The type accelerator surface report uses it to inspect the same payloads.
    /// </summary>
    internal static string[] ResolveAssemblyLoadContextTargetDirectories(
        string libRoot,
        IReadOnlyList<string>? exportAssemblyFileNames = null)
    {
        var runtimeCandidates = ModuleBinaryPayloadLayout.ResolveAssemblyLoadContextTargetDirectories(libRoot);
        if (exportAssemblyFileNames is { Count: > 0 })
        {
            var allTargetDirectories = new List<string>();
            foreach (var assemblyFileName in exportAssemblyFileNames)
            {
                var qualifiedAssemblyPath = ResolveQualifiedAssemblyPath(libRoot, assemblyFileName);
                if (!string.IsNullOrWhiteSpace(qualifiedAssemblyPath))
                {
                    allTargetDirectories.Add(Path.GetDirectoryName(qualifiedAssemblyPath!)!);
                    continue;
                }

                var configuredFileName = Path.GetFileName(assemblyFileName);
                var configuredTargetDirectories = runtimeCandidates.Where(directory =>
                    Directory.Exists(directory) &&
                    ModuleBinaryFileLocator.Enumerate(directory, SearchOption.TopDirectoryOnly)
                        .Any(path => string.Equals(Path.GetFileName(path), configuredFileName, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                if (Directory.Exists(libRoot) &&
                    ModuleBinaryFileLocator.Enumerate(libRoot, SearchOption.TopDirectoryOnly)
                        .Any(path => string.Equals(Path.GetFileName(path), configuredFileName, StringComparison.OrdinalIgnoreCase)))
                {
                    configuredTargetDirectories.Add(libRoot);
                }

                var defaultDirectory = Path.Combine(libRoot, "Default");
                if (Directory.Exists(defaultDirectory) &&
                    ModuleBinaryFileLocator.Enumerate(defaultDirectory, SearchOption.TopDirectoryOnly)
                        .Any(path => string.Equals(Path.GetFileName(path), configuredFileName, StringComparison.OrdinalIgnoreCase)))
                {
                    configuredTargetDirectories.Add(defaultDirectory);
                }

                // An unqualified runtime reference can fall through preferred folders to a unique
                // arbitrary nested match when the named Core payload is incompatible with the host.
                // Emit the helper beside every physical candidate so all runtime-selectable fallbacks
                // have the same loader available.
                foreach (var candidate in ModuleBinaryFileLocator.Enumerate(libRoot, SearchOption.AllDirectories)
                             .Where(path => string.Equals(Path.GetFileName(path), configuredFileName, StringComparison.OrdinalIgnoreCase)))
                {
                    configuredTargetDirectories.Add(Path.GetDirectoryName(candidate)!);
                }

                allTargetDirectories.AddRange(configuredTargetDirectories);
            }

            return allTargetDirectories.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        if (runtimeCandidates.Length > 0)
            return runtimeCandidates;

        return ModuleBinaryFileLocator.HasAny(libRoot, SearchOption.TopDirectoryOnly)
            ? new[] { libRoot }
            : Array.Empty<string>();
    }

    private static string? ResolveQualifiedAssemblyPath(string libRoot, string assemblyReference)
    {
        if (string.IsNullOrWhiteSpace(assemblyReference) ||
            assemblyReference.IndexOf(Path.DirectorySeparatorChar) < 0 &&
            assemblyReference.IndexOf(Path.AltDirectorySeparatorChar) < 0)
        {
            return null;
        }

        if (Path.IsPathRooted(assemblyReference))
            return File.Exists(assemblyReference) ? Path.GetFullPath(assemblyReference) : null;

        var normalizedReference = assemblyReference
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
        var libPrefix = "Lib" + Path.DirectorySeparatorChar;
        if (normalizedReference.StartsWith(libPrefix, StringComparison.OrdinalIgnoreCase))
            normalizedReference = normalizedReference.Substring(libPrefix.Length);

        var normalizedLibRoot = Path.GetFullPath(libRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var candidate in ModuleBinaryFileLocator.Enumerate(normalizedLibRoot, SearchOption.AllDirectories))
        {
            var relativeCandidate = Path.GetFullPath(candidate).Substring(normalizedLibRoot.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            if (string.Equals(relativeCandidate, normalizedReference, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return null;
    }
}
