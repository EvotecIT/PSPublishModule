namespace PowerForge;

internal static class ModuleManifestLoadedContent
{
    private static readonly string[] Keys =
    {
        "RootModule",
        "ModuleToProcess",
        "NestedModules",
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
        return Normalize(paths);
    }

    internal static string[] ReadRelativePathsFromText(string manifestText)
    {
        var paths = new List<string>();
        foreach (string key in Keys)
            paths.AddRange(ModuleManifestValueReader.ReadTopLevelStringOrArrayFromText(manifestText, key));
        return Normalize(paths);
    }

    private static string[] Normalize(IEnumerable<string> paths)
        => paths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
