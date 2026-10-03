using PowerForge;

namespace PowerForge.Web;

/// <summary>Preflights website output locations before generation or destructive cleanup.</summary>
internal static class WebOutputPathGuard
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    internal static void ValidateSite(SiteSpec spec, WebSitePlan plan, string outputPath, string? pipelineRoot = null)
    {
        var output = ResolveOutput(outputPath);
        Protect(output, plan.RootPath, allowOutputBelowInput: true);
        Protect(output, pipelineRoot, allowOutputBelowInput: true);
        Protect(output, plan.ConfigPath, allowOutputBelowInput: true);
        if (!string.IsNullOrWhiteSpace(plan.ConfigPath) && File.Exists(plan.ConfigPath))
            foreach (var config in WebSiteSpecLoader.DiscoverConfigurationInputs(plan.ConfigPath))
                Protect(output, config, allowOutputBelowInput: true);

        void ProtectInput(string? input) => Protect(output, input,
            allowOutputBelowInput: !string.IsNullOrWhiteSpace(input) &&
                string.Equals(Path.GetFullPath(input), Path.GetFullPath(plan.RootPath), PathComparison));
        foreach (var input in new[] { plan.ContentRoot, plan.ProjectsRoot, plan.SharedRoot, plan.ThemesRoot })
            ProtectInput(input);
        foreach (var input in plan.ContentRoots ?? Array.Empty<string>()) ProtectInput(input);
        foreach (var collection in plan.Collections ?? Array.Empty<WebCollectionPlan>()) ProtectInput(collection.InputPath);
        foreach (var project in plan.Projects ?? Array.Empty<WebProjectPlan>())
        {
            ProtectInput(project.RootPath);
            ProtectInput(project.ContentPath);
        }
        foreach (var input in new[] { spec.DataRoot ?? "data", spec.ThemesRoot ?? "themes", "static" })
            if (!string.IsNullOrWhiteSpace(input)) ProtectInput(Path.Combine(plan.RootPath, input));
        foreach (var asset in spec.StaticAssets ?? Array.Empty<StaticAssetSpec>())
            if (!string.IsNullOrWhiteSpace(asset.Source))
                ProtectInput(Path.IsPathRooted(asset.Source) ? asset.Source : Path.Combine(plan.RootPath, asset.Source));
    }

    internal static void ValidateProject(string projectPath, string outputPath, string? pipelineRoot = null, bool cleanOutput = false)
    {
        var project = Path.GetFullPath(projectPath);
        var output = ResolveOutput(outputPath);
        Protect(output, pipelineRoot, allowOutputBelowInput: true);
        Protect(output, project, allowOutputBelowInput: true);
        Protect(output, Path.GetDirectoryName(project), allowOutputBelowInput: true);
        if (cleanOutput && Directory.Exists(output))
        {
            // A cleanup request must not erase source code even when the selected folder is below the project root.
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = false,
                AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var file in Directory.EnumerateFiles(output, "*", options))
            {
                var extension = Path.GetExtension(file);
                if (extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".fs", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".vb", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".fsproj", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".vbproj", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Refusing to clean output '{output}' containing project source '{file}'. Choose a separate output folder.");
            }
        }
    }

    private static string ResolveOutput(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path is required.", nameof(outputPath));
        return FileSystemPathSafety.ResolveParentDirectoryAliases(Path.GetFullPath(outputPath.Trim().Trim('"')));
    }

    private static void Protect(string output, string? input, bool allowOutputBelowInput = false)
    {
        if (string.IsNullOrWhiteSpace(input)) return;
        var fullInput = Path.GetFullPath(input);
        if (File.Exists(fullInput) || Directory.Exists(fullInput))
        {
            if ((File.GetAttributes(fullInput) & FileAttributes.ReparsePoint) != 0)
            {
                FileSystemInfo info = Directory.Exists(fullInput) ? new DirectoryInfo(fullInput) : new FileInfo(fullInput);
                fullInput = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName
                    ?? throw new InvalidOperationException($"Cannot resolve build input link: {fullInput}");
            }
            fullInput = FileSystemPathSafety.ResolveParentDirectoryAliases(fullInput);
        }
        if (Contains(output, fullInput) || (!allowOutputBelowInput && Contains(fullInput, output)))
            throw new InvalidOperationException($"Output '{output}' overlaps build source '{input}'. Use a separate output directory such as '_site'.");
    }

    private static bool Contains(string root, string path)
    {
        var normalized = Path.TrimEndingDirectorySeparator(root);
        return string.Equals(normalized, Path.TrimEndingDirectorySeparator(path), PathComparison) ||
               path.StartsWith(Path.EndsInDirectorySeparator(normalized) ? normalized : normalized + Path.DirectorySeparatorChar, PathComparison);
    }
}
