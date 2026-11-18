using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PowerForge.Web;

/// <summary>Area pages rendered from benchmark evidence with the benchmark components.</summary>
public static partial class WebSiteBuilder
{
    private const string BenchmarkFragmentsRoot = "_powerforge/fragments/benchmarks/";

    // Folder of an evidence entry's area pages relative to the site root. Audits, sitemaps and search skip
    // _powerforge/fragments. The folder is a readable slug of the data key plus a hash of the exact key, so keys such
    // as "benchmarks.csv" and "benchmarks-csv" never share a folder.
    private static string BenchmarkAreaFragmentsRoute(BenchmarkEvidenceDataSpec evidence)
        => BenchmarkFragmentsRoot + BenchmarkFragmentFolder(evidence.Name) + "/";

    internal static string BenchmarkFragmentFolder(string name)
    {
        var key = (name ?? string.Empty).Trim();
        var slug = new StringBuilder();
        foreach (var character in key.ToLowerInvariant())
            slug.Append(char.IsLetterOrDigit(character) && character < 128 ? character : '-');
        var readable = slug.ToString().Trim('-');
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
        var suffix = string.Concat(hash.Take(4).Select(value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
        return (readable.Length == 0 ? "benchmarks" : readable) + "-" + suffix;
    }

    // Two entries must never write the same area pages; any shared folder fails the build before anything is written.
    private static void EnsureDistinctBenchmarkFragmentFolders(SiteSpec spec)
    {
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var evidence in spec.BenchmarkEvidence ?? Array.Empty<BenchmarkEvidenceDataSpec>())
        {
            if (!evidence.AreaFragments)
                continue;
            var folder = BenchmarkFragmentFolder(evidence.Name);
            if (owners.TryGetValue(folder, out var other))
                throw new InvalidOperationException(
                    $"BenchmarkEvidence '{evidence.Name}' and '{other}' would write their area pages to the same folder. Give each entry a distinct Name.");
            owners[folder] = evidence.Name;
        }
    }

    // Writes one small page per area. The theme's area component renders it; the comparisons are rendered one at a
    // time beforehand and handed to it as comparisonsHtml, so an area of any size stays clear of Scriban's loop
    // limit while the site's LoopLimit still applies inside each comparison. The page is readable when opened
    // directly, and the component script loads its body into the index.
    private static void WriteBenchmarkAreaFragments(SiteSpec spec, string rootPath, string outputRoot, BenchmarkEvidenceDataSpec evidence)
    {
        var catalogPath = Path.IsPathRooted(evidence.Catalog) ? evidence.Catalog : Path.Combine(rootPath, evidence.Catalog);
        var view = new BenchmarkComparisonViewBuilder().Build(catalogPath);
        var fullOutputRoot = Path.GetFullPath(outputRoot);
        var route = BenchmarkAreaFragmentsRoute(evidence);
        var directory = Path.Combine(fullOutputRoot, route.TrimEnd('/').Replace('/', Path.DirectorySeparatorChar));
        var label = $"'{evidence.Name}' area pages";
        VisualStoryPathGuard.EnsureContainedPath(fullOutputRoot, directory, label, subject: "BenchmarkEvidence");

        var resolver = BuildBenchmarkPartialResolver(spec, rootPath);
        var engine = new ScribanTemplateEngine();
        var toRoot = string.Concat(Enumerable.Repeat("../", route.Trim('/').Split('/').Length));
        foreach (var area in view.Index.Areas)
        {
            var path = Path.Combine(directory, Path.ChangeExtension(area.File, ".html"));
            VisualStoryPathGuard.EnsureContainedPath(fullOutputRoot, path, label, subject: "BenchmarkEvidence");

            var comparisons = new StringBuilder();
            foreach (var comparison in view.Comparisons)
            {
                if (string.Equals(comparison.Area ?? string.Empty, area.Id, StringComparison.OrdinalIgnoreCase))
                    comparisons.AppendLine(RenderBenchmarkComponent(engine, resolver, spec, rootPath, "comparison", comparison, null));
            }
            var body = RenderBenchmarkComponent(engine, resolver, spec, rootPath, "area", area, comparisons.ToString());

            var title = System.Web.HttpUtility.HtmlEncode((area.Id.Length > 0 ? area.Id : "Other") + " benchmark results");
            var page = new StringBuilder()
                .AppendLine("<!doctype html>")
                .AppendLine("<html lang=\"en\">")
                .AppendLine("<head>")
                .AppendLine("<meta charset=\"utf-8\" />")
                .AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />")
                .AppendLine("<meta name=\"robots\" content=\"noindex\" />")
                .Append("<title>").Append(title).AppendLine("</title>")
                .Append("<link rel=\"stylesheet\" href=\"").Append(toRoot).Append(BenchmarkCssRoute).AppendLine("\" />")
                .AppendLine("<style>body{font-family:system-ui,-apple-system,\"Segoe UI\",sans-serif;line-height:1.5;max-width:64rem;margin:2rem auto;padding:0 16px}</style>")
                .AppendLine("</head>")
                .AppendLine("<body>")
                .AppendLine(body)
                .Append("<script src=\"").Append(toRoot).Append(BenchmarkJsRoute).AppendLine("\" defer></script>")
                .AppendLine("</body>")
                .AppendLine("</html>");
            Directory.CreateDirectory(directory);
            WriteAllTextIfChanged(path, page.ToString());
        }
    }

    // Renders one component for one model. The area receives its index entry plus the rendered comparisons.
    private static string RenderBenchmarkComponent(
        ScribanTemplateEngine engine,
        Func<string, string?> resolver,
        SiteSpec spec,
        string rootPath,
        string component,
        object model,
        string? comparisonsHtml)
    {
        using var document = JsonDocument.Parse(BenchmarkJson.Serialize(model));
        var value = ConvertJsonElement(document.RootElement);
        if (comparisonsHtml is not null && value is Dictionary<string, object?> map)
        {
            // The area index entry lists the comparisons as summaries; they are exposed as summaries so a template
            // never mistakes them for full comparisons.
            if (map.Remove("comparisons", out var summaries))
                map["summaries"] = summaries;
            map["comparisonsHtml"] = comparisonsHtml;
        }
        var context = new ThemeRenderContext
        {
            Site = spec,
            RootPath = rootPath,
            Data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["model"] = value }
        };
        return engine.Render("{{ include \"" + BenchmarkPartialPrefix + component + "\" data.model }}", context, resolver);
    }
}
