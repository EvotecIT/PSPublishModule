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

        string? assemblyPath = null;
        if (xmlFiles.Length == 1)
        {
            var baseName = Path.GetFileNameWithoutExtension(xmlFiles[0]);
            var xmlDirectory = Path.GetDirectoryName(xmlFiles[0]) ?? dotNetRoot;
            assemblyPath = Directory.GetFiles(xmlDirectory, "*.dll", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(path => Path.GetFileNameWithoutExtension(path).Equals(baseName, StringComparison.OrdinalIgnoreCase));
            assemblyPath ??= Directory.GetFiles(dotNetRoot, "*.dll", SearchOption.AllDirectories)
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        candidate = new ProjectApiInputCandidate
        {
            Type = "CSharp",
            RootPath = dotNetRoot,
            XmlPath = xmlFiles[0],
            XmlPaths = xmlFiles,
            AssemblyPath = string.IsNullOrWhiteSpace(assemblyPath) ? null : Path.GetFullPath(assemblyPath),
            HasPlaceholderContent = hasPlaceholder,
            PlaceholderPath = placeholderPath
        };
        return true;
    }
}
