namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static bool TryBuildDotNetProjectApiInput(
        string root,
        IReadOnlyList<string> placeholderMarkers,
        out ProjectApiInputCandidate? candidate)
    {
        candidate = null;
        var dotNetRoot = ResolveExistingSubdirectory(root, "dotnet", "DotNet", "csharp", "CSharp");
        if (string.IsNullOrWhiteSpace(dotNetRoot))
            dotNetRoot = root;
        if (!Directory.Exists(dotNetRoot))
            return false;

        var xmlFiles = Directory.GetFiles(dotNetRoot, "*.xml", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (xmlFiles.Length == 0)
            return false;

        var hasPlaceholder = false;
        var placeholderPath = string.Empty;
        foreach (var xmlPath in xmlFiles)
        {
            if (!TryDetectPlaceholderContent(xmlPath, placeholderMarkers, out var detectedPath))
                continue;
            hasPlaceholder = true;
            placeholderPath = detectedPath;
            break;
        }

        var dllFiles = Directory.GetFiles(dotNetRoot, "*.dll", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .ToArray();
        var assemblyPaths = xmlFiles.Select(xmlPath =>
        {
            var baseName = Path.GetFileNameWithoutExtension(xmlPath);
            var exactPath = Path.ChangeExtension(xmlPath, ".dll");
            if (File.Exists(exactPath))
                return exactPath;
            var matches = dllFiles.Where(path => Path.GetFileNameWithoutExtension(path).Equals(baseName, StringComparison.OrdinalIgnoreCase)).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var primaryAssembly = assemblyPaths.FirstOrDefault(path => !Path.GetFileNameWithoutExtension(path).Contains('.', StringComparison.Ordinal))
                              ?? assemblyPaths.FirstOrDefault();

        candidate = new ProjectApiInputCandidate
        {
            Type = "CSharp",
            RootPath = dotNetRoot,
            XmlPath = xmlFiles[0],
            XmlPaths = xmlFiles,
            AssemblyPath = primaryAssembly,
            AssemblyPaths = assemblyPaths,
            HasPlaceholderContent = hasPlaceholder,
            PlaceholderPath = placeholderPath
        };
        return true;
    }
}
