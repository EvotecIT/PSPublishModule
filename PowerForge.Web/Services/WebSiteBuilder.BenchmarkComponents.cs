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
        {
            var path = Path.Combine(outputRoot, route.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            WriteAllTextIfChanged(path, ReadBenchmarkResource(Path.GetFileName(route)));
        }
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

    // Path of an evidence entry's area files relative to the site root, such as "data/benchmarks/". The component
    // script resolves it against the site root it was loaded from, so sites served from a sub-path work too.
    private static string BenchmarkAreaFilesRoute(BenchmarkEvidenceDataSpec evidence)
        => evidence.Output!.Trim().Replace('\\', '/').Trim('/') + "/";

    // Renders each area with the area component, honouring theme overrides, so a page can load it on demand.
    private static void WriteBenchmarkAreaFragments(
        SiteSpec spec,
        string rootPath,
        BenchmarkComparisonCatalogView view,
        string directory,
        Action<string> guard)
    {
        var resolver = BuildBenchmarkPartialResolver(spec, rootPath);
        var template = resolver(BenchmarkPartialPrefix + "area")
            ?? throw new InvalidOperationException("The benchmark area component could not be resolved.");
        var engine = new ScribanTemplateEngine();
        foreach (var area in view.Index.Areas)
        {
            var path = Path.Combine(directory, Path.ChangeExtension(area.File, ".html"));
            guard(path);
            var areaView = new BenchmarkAreaView
            {
                Id = area.Id,
                Comparisons = Array.FindAll(view.Comparisons, comparison => string.Equals(comparison.Area ?? string.Empty, area.Id, StringComparison.OrdinalIgnoreCase))
            };
            using var document = JsonDocument.Parse(BenchmarkJson.Serialize(areaView));
            var context = new ThemeRenderContext
            {
                Site = spec,
                RootPath = rootPath,
                Data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["area"] = ConvertJsonElement(document.RootElement) }
            };
            var html = engine.Render("{{ include \"" + BenchmarkPartialPrefix + "area\" data.area }}", context, resolver);
            WriteAllTextIfChanged(path, html);
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
