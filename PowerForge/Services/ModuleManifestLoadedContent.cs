namespace PowerForge;

internal static class ModuleManifestLoadedContent
{
    private static readonly string[] Keys =
    {
        "RootModule",
        "ModuleToProcess",
        "RequiredAssemblies",
        "ScriptsToProcess",
        "TypesToProcess",
        "FormatsToProcess"
    };

    internal static IEnumerable<string> PropertyNames => Keys.Concat(new[] { "NestedModules" });

    internal static string[] ReadRelativePaths(string manifestPath, StringComparer? comparer = null)
        => Normalize(ReadReferences(manifestPath).Select(static reference => reference.Path), comparer);

    /// <summary>Each manifest-loaded entry with the key that lists it.</summary>
    internal static IEnumerable<(string Key, string Path)> ReadReferences(string manifestPath)
    {
        foreach (string key in Keys)
        foreach (string path in ModuleManifestValueReader.ReadTopLevelStringOrArray(manifestPath, key))
            yield return (key, path);
        foreach (string path in ModuleManifestValueReader.ReadTopLevelModuleReferencePaths(manifestPath, "NestedModules"))
            yield return ("NestedModules", path);
    }

    internal static string[] ReadRelativePathsFromText(string manifestText)
    {
        var paths = new List<string>();
        foreach (string key in Keys)
            paths.AddRange(ModuleManifestValueReader.ReadTopLevelStringOrArrayFromText(manifestText, key));
        paths.AddRange(ModuleManifestValueReader.ReadTopLevelModuleReferencePathsFromText(manifestText, "NestedModules"));
        return Normalize(paths);
    }

    private static string[] Normalize(IEnumerable<string> paths, StringComparer? comparer = null)
        => paths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path.Trim())
            .Distinct(comparer ?? StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
