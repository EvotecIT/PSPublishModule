namespace PowerForge.Web;

public static partial class WebSiteBuilder
{
    private static void WriteTemplateExports(
        string outputRoot,
        SiteSpec spec,
        string rootPath,
        IReadOnlyList<ContentItem> items,
        IReadOnlyDictionary<string, object?> data,
        IReadOnlyDictionary<string, ProjectSpec> projectMap,
        MenuSpec[] menuSpecs)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var export in spec.TemplateExports ?? Array.Empty<TemplateExportSpec>())
        {
            if (export is null || string.IsNullOrWhiteSpace(export.Name) ||
                !export.Name.All(static c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-') ||
                export.Name[0] == '-' || !names.Add(export.Name))
                throw new InvalidOperationException("Template exports require unique lowercase names containing only letters, digits, and hyphens, starting with a letter or digit.");
            if (string.IsNullOrWhiteSpace(export.Layout) || string.IsNullOrWhiteSpace(export.SourceRoute))
                throw new InvalidOperationException($"Template export '{export.Name}' requires a layout and source route.");

            var source = items.FirstOrDefault(item => !item.Draft &&
                string.Equals(NormalizeRouteForMatch(item.OutputPath), NormalizeRouteForMatch(export.SourceRoute), StringComparison.Ordinal));
            if (source is null)
                throw new InvalidOperationException($"Template export '{export.Name}' source route '{export.SourceRoute}' is not included in this build.");

            var effectiveData = ResolveDataForProject(data, source.ProjectSlug);
            var formats = ResolveOutputFormats(spec, source);
            var outputs = ResolveOutputRuntime(spec, source, formats);
            var html = RenderHtmlPage(outputRoot, spec, rootPath, source, items, effectiveData, projectMap, menuSpecs,
                outputs, string.Empty, export.Layout);
            var target = Path.Combine(outputRoot, "_powerforge", "fragments", export.Name + ".html");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            WriteAllTextIfChanged(target, html);
        }
    }
}
