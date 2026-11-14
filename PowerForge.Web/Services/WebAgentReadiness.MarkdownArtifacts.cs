using System.Security.Cryptography;
using System.Text;

namespace PowerForge.Web;

public static partial class WebAgentReadiness
{
    private const string MarkdownArtifactStamp = "\n<!-- PowerForge markdown artifact sha256:";
    private const string MarkdownSourceStamp = "\n<!-- PowerForge markdown source -->\n";

    // A matching content stamp establishes ownership without a separate mutable manifest.
    // Unmarked downloads and edited sidecars are always treated as authored content.
    private static bool IsUnmodifiedMarkdownArtifact(string content)
    {
        var stamp = content.LastIndexOf(MarkdownArtifactStamp, StringComparison.Ordinal);
        if (stamp < 0)
            return false;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content[..stamp])));
        return content[stamp..] == MarkdownArtifactStamp + hash + " -->\n";
    }

    internal static string[] WriteContentMarkdownArtifacts(string siteRoot, AgentMarkdownArtifactsSpec spec, IReadOnlyDictionary<string, ContentItem> sources)
    {
        var warnings = new List<string>();
        var result = WriteMarkdownArtifacts(siteRoot, spec, warnings, sources);
        foreach (var warning in warnings)
            System.Diagnostics.Trace.TraceWarning(warning);
        return result.WrittenPaths;
    }

    private static MarkdownArtifactWriteResult WriteMarkdownArtifacts(string siteRoot, AgentMarkdownArtifactsSpec spec, List<string> warnings,
        IReadOnlyDictionary<string, ContentItem>? sources = null)
    {
        if (!Directory.Exists(siteRoot))
            return new MarkdownArtifactWriteResult(Array.Empty<string>(), Array.Empty<string>(), null);

        var extension = NormalizeMarkdownExtension(spec.Extension);
        var candidates = Directory.EnumerateFiles(siteRoot, "*.html", SearchOption.AllDirectories)
            .Where(path => !ShouldSkipMarkdownArtifactCandidate(siteRoot, path))
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var maxPages = spec.MaxPages;
        if (maxPages > 0 && candidates.Length > maxPages)
        {
            warnings.Add($"Markdown artifact generation limited to {maxPages} of {candidates.Length} HTML pages.");
            candidates = candidates.Take(maxPages).ToArray();
        }

        var written = new List<string>();
        var available = new List<string>();
        foreach (var htmlPath in candidates)
        {
            ContentItem? source = null;
            sources?.TryGetValue(htmlPath, out source);
            var outputPath = Path.ChangeExtension(htmlPath, extension);
            var previous = File.Exists(outputPath) ? File.ReadAllText(outputPath, Encoding.UTF8) : null;
            if (previous is not null && !IsUnmodifiedMarkdownArtifact(previous))
            {
                warnings.Add($"Preserved existing Markdown file '{ToSiteRoute(siteRoot, outputPath)}'; it is authored or modified content, not an unchanged generated artifact.");
                available.Add(outputPath);
                AdvertiseMarkdownArtifact(siteRoot, htmlPath, outputPath, written);
                continue;
            }
            if (source is null && previous?.Contains(MarkdownSourceStamp, StringComparison.Ordinal) == true)
            {
                available.Add(outputPath);
                AdvertiseMarkdownArtifact(siteRoot, htmlPath, outputPath, written);
                continue;
            }
            var markdown = ConvertHtmlDocumentToMarkdown(File.ReadAllText(htmlPath, Encoding.UTF8), spec, source);
            if (string.IsNullOrWhiteSpace(markdown))
                continue;

            if (source?.MarkdownContent is not null)
                markdown += MarkdownSourceStamp;
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(markdown)));
            var next = markdown + MarkdownArtifactStamp + hash + " -->\n";
            if (previous != next)
            {
                File.WriteAllText(outputPath, next, Encoding.UTF8);
                written.Add(outputPath);
            }
            available.Add(outputPath);
            AdvertiseMarkdownArtifact(siteRoot, htmlPath, outputPath, written);
        }

        var rootMarkdown = Path.Combine(siteRoot, "index" + extension);
        var rootRoute = File.Exists(rootMarkdown) ? ToSiteRoute(siteRoot, rootMarkdown) : null;
        return new MarkdownArtifactWriteResult(available.ToArray(), written.ToArray(), rootRoute);
    }

    private static void AdvertiseMarkdownArtifact(string siteRoot, string htmlPath, string markdownPath, List<string> written)
    {
        var html = File.ReadAllText(htmlPath, Encoding.UTF8);
        var route = ToSiteRoute(siteRoot, markdownPath);
        var doc = HtmlTinkerX.HtmlParser.ParseWithAngleSharp(html);
        if (doc.QuerySelectorAll("link[rel~='alternate'][type='text/markdown']").Any(link => link.GetAttribute("href") == route))
            return;
        var end = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        if (end < 0) return;
        var link = "<link rel=\"alternate\" type=\"text/markdown\" href=\"" + System.Net.WebUtility.HtmlEncode(route) + "\" />\n";
        File.WriteAllText(htmlPath, html.Insert(end, link), Encoding.UTF8);
        written.Add(htmlPath);
    }

    private static bool ShouldSkipMarkdownArtifactCandidate(string siteRoot, string path)
    {
        var relative = Path.GetRelativePath(siteRoot, path).Replace('\\', '/');
        return relative.EndsWith(".head.html", StringComparison.OrdinalIgnoreCase) ||
               relative.EndsWith(".scripts.html", StringComparison.OrdinalIgnoreCase) ||
               relative.Contains("/api-fragments/", StringComparison.OrdinalIgnoreCase) ||
               relative.StartsWith("api-fragments/", StringComparison.OrdinalIgnoreCase);
    }

}
