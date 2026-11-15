namespace PowerForge.Web;

public static partial class WebSiteBuilder
{
    private static void WriteContentMarkdownArtifacts(string outputRoot, SiteSpec spec, IReadOnlyList<ContentItem> items)
    {
        if (spec.AgentReadiness is not { Enabled: true, MarkdownArtifacts.Enabled: true })
            return;

        var sources = new Dictionary<string, ContentItem>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var item in items.Where(item => !item.Draft))
        {
            foreach (var format in ResolveOutputFormats(spec, item))
            {
                var fileName = ResolveOutputFileName(format);
                if (!fileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) continue;
                var path = string.Equals(NormalizePath(item.OutputPath), "404", StringComparison.OrdinalIgnoreCase) && fileName == "index.html"
                    ? Path.Combine(outputRoot, "404.html")
                    : Path.Combine(ResolveOutputDirectory(outputRoot, item.OutputPath), fileName);
                sources[path] = item;
            }
        }
        foreach (var path in WebAgentReadiness.WriteContentMarkdownArtifacts(outputRoot, spec.AgentReadiness.MarkdownArtifacts, sources))
            UpdatedSink.Value?.Invoke(path);
    }
}
