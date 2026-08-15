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

    internal static string[] ReadRelativePaths(string manifestPath)
    {
        var paths = new List<string>();
        foreach (string key in Keys)
            paths.AddRange(ModuleManifestValueReader.ReadTopLevelStringOrArray(manifestPath, key));
        paths.AddRange(ModuleManifestValueReader.ReadTopLevelModuleReferencePaths(manifestPath, "NestedModules"));
        return Normalize(paths);
    }

    internal static string[] ReadRelativePathsFromText(string manifestText)
    {
        var paths = new List<string>();
        foreach (string key in Keys)
            paths.AddRange(ModuleManifestValueReader.ReadTopLevelStringOrArrayFromText(manifestText, key));
        paths.AddRange(ModuleManifestValueReader.ReadTopLevelModuleReferencePathsFromText(manifestText, "NestedModules"));
        return Normalize(paths);
    }

    private static string[] Normalize(IEnumerable<string> paths)
        => paths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
