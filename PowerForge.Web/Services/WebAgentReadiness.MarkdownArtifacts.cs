using System.Security.Cryptography;
using System.Text;

namespace PowerForge.Web;

public static partial class WebAgentReadiness
{
    private const string MarkdownArtifactStamp = "\n<!-- PowerForge markdown artifact sha256:";

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

    private static MarkdownArtifactWriteResult WriteMarkdownArtifacts(string siteRoot, AgentMarkdownArtifactsSpec spec, List<string> warnings)
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
            var outputPath = Path.ChangeExtension(htmlPath, extension);
            if (File.Exists(outputPath) && !IsUnmodifiedMarkdownArtifact(File.ReadAllText(outputPath, Encoding.UTF8)))
            {
                warnings.Add($"Preserved existing Markdown file '{ToSiteRoute(siteRoot, outputPath)}'; it is authored or modified content, not an unchanged generated artifact.");
                available.Add(outputPath);
                continue;
            }
            var markdown = ConvertHtmlDocumentToMarkdown(File.ReadAllText(htmlPath, Encoding.UTF8), spec);
            if (string.IsNullOrWhiteSpace(markdown))
                continue;

            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(markdown)));
            File.WriteAllText(outputPath, markdown + MarkdownArtifactStamp + hash + " -->\n", Encoding.UTF8);
            written.Add(outputPath);
            available.Add(outputPath);
        }

        var rootMarkdown = Path.Combine(siteRoot, "index" + extension);
        var rootRoute = File.Exists(rootMarkdown) ? ToSiteRoute(siteRoot, rootMarkdown) : null;
        return new MarkdownArtifactWriteResult(available.ToArray(), written.ToArray(), rootRoute);
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
