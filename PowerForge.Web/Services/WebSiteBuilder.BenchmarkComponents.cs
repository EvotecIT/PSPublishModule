using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PowerForge.Web;

/// <summary>Built-in benchmark components: Scriban partials, their stylesheet and script, and rendered area files.</summary>
public static partial class WebSiteBuilder
{
    private const string BenchmarkPartialPrefix = "powerforge/benchmarks/";
    private const string BenchmarkCssRoute = "assets/powerforge/benchmarks.v1.css";
    private const string BenchmarkJsRoute = "assets/powerforge/benchmarks.v1.js";
    private const string BenchmarkComponentMarker = "data-pf-bench";
    private static readonly string[] BenchmarkPartialNames = { "comparison", "run", "area", "index" };
    private static readonly ConcurrentDictionary<string, string> BenchmarkResourceCache = new(StringComparer.Ordinal);

    /// <summary>
    /// Resolves a built-in partial such as <c>powerforge/benchmarks/comparison</c>. It is consulted after the theme,
    /// so a theme replaces a component by adding <c>partials/powerforge/benchmarks/&lt;name&gt;.html</c>.
    /// </summary>
    internal static string? ResolveBuiltInPartial(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var normalized = name.Trim().Replace('\\', '/');
        if (!normalized.StartsWith(BenchmarkPartialPrefix, StringComparison.OrdinalIgnoreCase)) return null;
        var component = normalized.Substring(BenchmarkPartialPrefix.Length);
        if (component.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            component = component.Substring(0, component.Length - ".html".Length);
        return Array.IndexOf(BenchmarkPartialNames, component.ToLowerInvariant()) < 0
            ? null
            : ReadBenchmarkResource(component.ToLowerInvariant() + ".html");
    }

    private static string ReadBenchmarkResource(string file)
        => BenchmarkResourceCache.GetOrAdd(file, static name =>
        {
            var resource = "PowerForge.Web.Assets.Benchmarks." + name;
            using var stream = typeof(WebSiteBuilder).Assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"Missing benchmark component resource: {resource}");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        });

    private static bool HasBenchmarkEvidence(SiteSpec spec)
        => spec.BenchmarkEvidence is { Length: > 0 };

    private static void EnsureBenchmarkComponentAssets(SiteSpec spec, string outputRoot)
    {
        if (!HasBenchmarkEvidence(spec)) return;
        foreach (var route in new[] { BenchmarkCssRoute, BenchmarkJsRoute })
            WriteBuiltInAsset(outputRoot, route, ReadBenchmarkResource(Path.GetFileName(route)));
    }

    // Built-in assets go under assets/powerforge. A linked folder or file there would send the write outside the
    // site output, so the destination is checked before anything is written.
    private static void WriteBuiltInAsset(string outputRoot, string route, string content)
    {
        var fullOutputRoot = Path.GetFullPath(outputRoot);
        var path = Path.Combine(fullOutputRoot, route.Replace('/', Path.DirectorySeparatorChar));
        VisualStoryPathGuard.EnsureContainedPath(fullOutputRoot, path, "asset " + route, subject: "Website");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        WriteAllTextIfChanged(path, content);
    }

    // Pages that render a component get its stylesheet and script; other pages are left alone.
    private static string EnsureBenchmarkComponentReferences(string html, SiteSpec spec, string route)
    {
        if (!HasBenchmarkEvidence(spec) || string.IsNullOrEmpty(html) ||
            html.IndexOf(BenchmarkComponentMarker, StringComparison.Ordinal) < 0)
            return html;

        if (html.IndexOf(BenchmarkCssRoute, StringComparison.Ordinal) < 0)
        {
            var css = System.Web.HttpUtility.HtmlEncode(ResolveRouteRelativeAssetHref(BenchmarkCssRoute, route));
            html = InsertBefore(html, "</head>", $"<link rel=\"stylesheet\" href=\"{css}\" />");
        }
        if (html.IndexOf(BenchmarkJsRoute, StringComparison.Ordinal) < 0)
        {
            var js = System.Web.HttpUtility.HtmlEncode(ResolveRouteRelativeAssetHref(BenchmarkJsRoute, route));
            html = InsertBefore(html, "</body>", $"<script src=\"{js}\" defer></script>");
        }
        return html;
    }

    // Folder of an evidence entry's rendered areas relative to the site root, such as
    // "_powerforge/fragments/benchmarks/benchmarks/". Audits, sitemaps and search skip _powerforge/fragments, and the
    // component script resolves the path against the site root, so sites served from a sub-path work too.
    private static string BenchmarkAreaFragmentsRoute(BenchmarkEvidenceDataSpec evidence)
        => "_powerforge/fragments/benchmarks/" + BenchmarkFragmentFolder(evidence.Name) + "/";

    private static string BenchmarkFragmentFolder(string name)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var character in (name ?? string.Empty).Trim().ToLowerInvariant())
            builder.Append(char.IsLetterOrDigit(character) && character < 128 ? character : '-');
        var folder = builder.ToString().Trim('-');
        return folder.Length == 0 ? "benchmarks" : folder;
    }

    // Renders each area one comparison at a time with the comparison component, honouring theme overrides. Rendering
    // per comparison keeps an area of any size clear of Scriban's loop limit; the site's LoopLimit still applies
    // inside each comparison.
    private static void WriteBenchmarkAreaFragments(SiteSpec spec, string rootPath, string outputRoot, BenchmarkEvidenceDataSpec evidence)
    {
        var catalogPath = Path.IsPathRooted(evidence.Catalog) ? evidence.Catalog : Path.Combine(rootPath, evidence.Catalog);
        var view = new BenchmarkComparisonViewBuilder().Build(catalogPath);
        var fullOutputRoot = Path.GetFullPath(outputRoot);
        var directory = Path.Combine(fullOutputRoot, BenchmarkAreaFragmentsRoute(evidence).TrimEnd('/').Replace('/', Path.DirectorySeparatorChar));
        var label = $"'{evidence.Name}' area fragments";
        VisualStoryPathGuard.EnsureContainedPath(fullOutputRoot, directory, label, subject: "BenchmarkEvidence");

        var resolver = BuildBenchmarkPartialResolver(spec, rootPath);
        var engine = new ScribanTemplateEngine();
        const string template = "{{ include \"" + BenchmarkPartialPrefix + "comparison\" data.comparison }}";
        foreach (var area in view.Index.Areas)
        {
            var path = Path.Combine(directory, Path.ChangeExtension(area.File, ".html"));
            VisualStoryPathGuard.EnsureContainedPath(fullOutputRoot, path, label, subject: "BenchmarkEvidence");
            var html = new System.Text.StringBuilder();
            html.Append("<div class=\"pf-bench-area\" data-pf-bench-area=\"")
                .Append(System.Web.HttpUtility.HtmlAttributeEncode(area.Id))
                .AppendLine("\">");
            foreach (var comparison in view.Comparisons)
            {
                if (!string.Equals(comparison.Area ?? string.Empty, area.Id, StringComparison.OrdinalIgnoreCase))
                    continue;
                using var document = JsonDocument.Parse(BenchmarkJson.Serialize(comparison));
                var context = new ThemeRenderContext
                {
                    Site = spec,
                    RootPath = rootPath,
                    Data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["comparison"] = ConvertJsonElement(document.RootElement) }
                };
                html.AppendLine(engine.Render(template, context, resolver));
            }
            html.AppendLine("</div>");
            Directory.CreateDirectory(directory);
            WriteAllTextIfChanged(path, html.ToString());
        }
    }

    private static Func<string, string?> BuildBenchmarkPartialResolver(SiteSpec spec, string rootPath)
    {
        var themeRoot = ResolveThemeRoot(spec, rootPath);
        if (string.IsNullOrWhiteSpace(themeRoot) || !Directory.Exists(themeRoot))
            return ResolveBuiltInPartial;

        var loader = new ThemeLoader();
        var manifest = loader.Load(themeRoot, ResolveThemesRoot(spec, rootPath));
        return name =>
        {
            var path = loader.ResolvePartialPath(themeRoot, manifest, name);
            return path is null ? ResolveBuiltInPartial(name) : File.ReadAllText(path);
        };
    }
}
