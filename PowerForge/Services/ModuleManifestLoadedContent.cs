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
    {
        var paths = new List<string>();
        foreach (string key in Keys)
            paths.AddRange(ModuleManifestValueReader.ReadTopLevelStringOrArray(manifestPath, key));
        paths.AddRange(ModuleManifestValueReader.ReadTopLevelModuleReferencePaths(manifestPath, "NestedModules"));
        return Normalize(paths, comparer);
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
