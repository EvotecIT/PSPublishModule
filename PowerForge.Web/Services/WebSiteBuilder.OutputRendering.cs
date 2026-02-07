using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;

namespace PowerForge.Web;

/// <summary>HTML/JSON/RSS output rendering helpers.</summary>
public static partial class WebSiteBuilder
{
    private static void WriteContentItem(
        string outputRoot,
        SiteSpec spec,
        string rootPath,
        ContentItem item,
        IReadOnlyList<ContentItem> allItems,
        IReadOnlyDictionary<string, object?> data,
        IReadOnlyDictionary<string, ProjectSpec> projectMap,
        MenuSpec[] menuSpecs)
    {
        if (item.Draft) return;

        var targetDir = ResolveOutputDirectory(outputRoot, item.OutputPath);
        var isNotFoundRoute = string.Equals(NormalizePath(item.OutputPath), "404", StringComparison.OrdinalIgnoreCase);

        var effectiveData = ResolveDataForProject(data, item.ProjectSlug);
        var formats = ResolveOutputFormats(spec, item);
        var outputs = ResolveOutputRuntime(spec, item, formats);
        var alternateHeadLinksHtml = RenderAlternateOutputHeadLinks(outputs);
        foreach (var format in formats)
        {
            var outputFileName = ResolveOutputFileName(format);
            var outputFile = isNotFoundRoute && string.Equals(outputFileName, "index.html", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(outputRoot, "404.html")
                : Path.Combine(targetDir, outputFileName);
            var outputDirectory = Path.GetDirectoryName(outputFile);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
                Directory.CreateDirectory(outputDirectory);
            var content = RenderOutput(spec, rootPath, item, allItems, effectiveData, projectMap, menuSpecs, format, outputs, alternateHeadLinksHtml);
            File.WriteAllText(outputFile, content);
        }
        CopyPageResources(item, isNotFoundRoute ? outputRoot : targetDir);
    }

    private static string RenderHtmlPage(
        SiteSpec spec,
        string rootPath,
        ContentItem item,
        IReadOnlyList<ContentItem> allItems,
        IReadOnlyDictionary<string, object?> data,
        IReadOnlyDictionary<string, ProjectSpec> projectMap,
        MenuSpec[] menuSpecs,
        IReadOnlyList<OutputRuntime> outputs,
        string alternateHeadLinksHtml)
    {
        var themeRoot = ResolveThemeRoot(spec, rootPath);
        var loader = new ThemeLoader();
        var manifest = !string.IsNullOrWhiteSpace(themeRoot) && Directory.Exists(themeRoot)
            ? loader.Load(themeRoot, ResolveThemesRoot(spec, rootPath))
            : null;
        var assetRegistry = BuildAssetRegistry(spec, themeRoot, manifest);

        var cssLinks = ResolveCssLinks(assetRegistry, item.OutputPath);
        var jsLinks = ResolveJsLinks(assetRegistry, item.OutputPath);
        var preloads = RenderPreloads(assetRegistry);
        var criticalCss = RenderCriticalCss(assetRegistry, rootPath);
        var canonical = !string.IsNullOrWhiteSpace(item.Canonical) ? $"<link rel=\"canonical\" href=\"{item.Canonical}\" />" : string.Empty;

        var cssHtml = RenderCssLinks(cssLinks, assetRegistry);
        var jsHtml = string.Join(Environment.NewLine, jsLinks.Select(j => $"<script src=\"{j}\" defer></script>"));
        var descriptionMeta = string.IsNullOrWhiteSpace(item.Description) ? string.Empty : $"<meta name=\"description\" content=\"{System.Web.HttpUtility.HtmlEncode(item.Description)}\" />";
        projectMap.TryGetValue(item.ProjectSlug ?? string.Empty, out var projectSpec);
        var breadcrumbs = BuildBreadcrumbs(spec, item, menuSpecs);
        var fullListItems = ResolveListItems(item, allItems);
        var pagination = ResolvePaginationRuntime(spec, item, fullListItems);
        var listItems = ApplyPagination(fullListItems, pagination);
        var headHtml = BuildHeadHtml(spec, item, rootPath);
        var bodyClass = BuildBodyClass(spec, item);
        var openGraph = BuildOpenGraphHtml(spec, item);
        var structuredData = BuildStructuredDataHtml(spec, item, breadcrumbs);
        var extraCss = GetMetaString(item.Meta, "extra_css");
        var extraScripts = BuildExtraScriptsHtml(item, rootPath);
        var taxonomyTerms = BuildTaxonomyTermsRuntime(item, allItems);
        var taxonomyIndex = BuildTaxonomyIndexRuntime(item, allItems);
        var taxonomyTermSummary = BuildTaxonomyTermSummaryRuntime(item, allItems);

        var renderContext = new ThemeRenderContext
        {
            Site = spec,
            Page = item,
            Items = listItems,
            Data = data,
            Project = projectSpec,
            Navigation = BuildNavigation(spec, item, menuSpecs),
            Localization = BuildLocalizationRuntime(spec, item, allItems),
            Versioning = BuildVersioningRuntime(spec, item.OutputPath),
            Outputs = outputs.ToArray(),
            FeedUrl = outputs.FirstOrDefault(o => string.Equals(o.Name, "rss", StringComparison.OrdinalIgnoreCase))?.Url,
            Breadcrumbs = breadcrumbs,
            CurrentPath = item.OutputPath,
            CssHtml = cssHtml,
            JsHtml = jsHtml,
            PreloadsHtml = preloads,
            CriticalCssHtml = criticalCss,
            CanonicalHtml = canonical,
            DescriptionMetaHtml = descriptionMeta,
            HeadHtml = string.IsNullOrWhiteSpace(alternateHeadLinksHtml)
                ? headHtml
                : string.Join(Environment.NewLine, new[] { headHtml, alternateHeadLinksHtml }.Where(v => !string.IsNullOrWhiteSpace(v))),
            OpenGraphHtml = openGraph,
            StructuredDataHtml = structuredData,
            ExtraCssHtml = extraCss,
            ExtraScriptsHtml = extraScripts,
            BodyClass = bodyClass,
            Taxonomy = ResolveTaxonomy(spec, item),
            Term = ResolveTerm(item),
            TaxonomyIndex = taxonomyIndex,
            TaxonomyTerms = taxonomyTerms,
            TaxonomyTermSummary = taxonomyTermSummary,
            Pagination = pagination
        };

        if (!string.IsNullOrWhiteSpace(themeRoot) && Directory.Exists(themeRoot))
        {
            var layoutName = item.Template ?? item.Layout ?? manifest?.DefaultLayout ?? "base";
            var layoutPath = loader.ResolveLayoutPath(themeRoot, manifest, layoutName);
            if (!string.IsNullOrWhiteSpace(layoutPath))
            {
                var template = File.ReadAllText(layoutPath);
                var engine = ThemeEngineRegistry.Resolve(spec.ThemeEngine ?? manifest?.Engine);
                return engine.Render(template, renderContext, name =>
                {
                    var partialPath = loader.ResolvePartialPath(themeRoot, manifest, name);
                    return partialPath is null ? null : File.ReadAllText(partialPath);
                });
            }
        }

        var htmlLang = System.Web.HttpUtility.HtmlEncode(renderContext.Localization.Current.Code ?? "en");
        return $@"<!doctype html>
<html lang=""{htmlLang}"">
<head>
  <meta charset=""utf-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
  <title>{System.Web.HttpUtility.HtmlEncode(item.Title)}</title>
  {descriptionMeta}
  {canonical}
  {preloads}
  {criticalCss}
  {headHtml}
  {openGraph}
  {structuredData}
  {extraCss}
  {cssHtml}
</head>
<body{(string.IsNullOrWhiteSpace(bodyClass) ? string.Empty : $" class=\"{bodyClass}\"")}>
  <main class=""pf-web-content"">
{item.HtmlContent}
  </main>
  {jsHtml}
  {extraScripts}
</body>
</html>";
    }

    private static OutputFormatSpec[] ResolveOutputFormats(SiteSpec spec, ContentItem item)
    {
        var formatNames = item.Outputs.Length > 0 ? item.Outputs : ResolveOutputRule(spec, item);
        if (formatNames.Length == 0)
            formatNames = new[] { "html" };

        var formats = new List<OutputFormatSpec>();
        foreach (var name in formatNames)
        {
            var format = ResolveOutputFormatSpec(spec, name);
            if (format is not null)
                formats.Add(format);
        }

        if (formats.Count == 0)
            formats.Add(new OutputFormatSpec { Name = "html", MediaType = "text/html", Suffix = "html" });

        return formats.ToArray();
    }

    private static string[] ResolveOutputRule(SiteSpec spec, ContentItem item)
    {
        if (spec.Outputs?.Rules is null || spec.Outputs.Rules.Length == 0)
            return ResolveImplicitOutputRule(spec, item);

        var kind = item.Kind.ToString().ToLowerInvariant();
        foreach (var rule in spec.Outputs.Rules)
        {
            if (rule is null || string.IsNullOrWhiteSpace(rule.Kind)) continue;
            if (string.Equals(rule.Kind, kind, StringComparison.OrdinalIgnoreCase))
                return rule.Formats ?? Array.Empty<string>();
        }

        return ResolveImplicitOutputRule(spec, item);
    }

    private static string[] ResolveImplicitOutputRule(SiteSpec spec, ContentItem item)
    {
        if (item is null)
            return Array.Empty<string>();
        if (spec.Feed?.Enabled == false)
            return Array.Empty<string>();

        if (item.Kind == PageKind.Section &&
            string.Equals(item.Collection, "blog", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { "html", "rss" };
        }

        if ((item.Kind == PageKind.Taxonomy || item.Kind == PageKind.Term) &&
            (string.Equals(item.Collection, "tags", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(item.Collection, "categories", StringComparison.OrdinalIgnoreCase)))
        {
            return new[] { "html", "rss" };
        }

        return Array.Empty<string>();
    }

    private static OutputFormatSpec? ResolveOutputFormatSpec(SiteSpec spec, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (spec.Outputs?.Formats is not null)
        {
            var match = spec.Outputs.Formats.FirstOrDefault(f =>
                string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }

        return name.ToLowerInvariant() switch
        {
            "html" => new OutputFormatSpec { Name = "html", MediaType = "text/html", Suffix = "html" },
            "rss" => new OutputFormatSpec { Name = "rss", MediaType = "application/rss+xml", Suffix = "xml", Rel = "alternate" },
            "json" => new OutputFormatSpec { Name = "json", MediaType = "application/json", Suffix = "json" },
            _ => new OutputFormatSpec { Name = name, MediaType = "text/plain", Suffix = name, IsPlainText = true }
        };
    }

    private static string ResolveOutputFileName(OutputFormatSpec format)
    {
        if (string.IsNullOrWhiteSpace(format.Suffix) || format.Suffix.Equals("html", StringComparison.OrdinalIgnoreCase))
            return "index.html";
        return $"index.{format.Suffix}";
    }

    private static OutputRuntime[] ResolveOutputRuntime(SiteSpec spec, ContentItem item, IReadOnlyList<OutputFormatSpec> formats)
    {
        if (formats.Count == 0)
            return Array.Empty<OutputRuntime>();

        var outputs = formats
            .Where(format => format is not null && !string.IsNullOrWhiteSpace(format.Name))
            .Select(format =>
            {
                var name = format.Name.Trim();
                var route = ResolveOutputRoute(item.OutputPath, format);
                var url = string.IsNullOrWhiteSpace(route)
                    ? string.Empty
                    : (string.IsNullOrWhiteSpace(spec.BaseUrl) ? route : CombineAbsoluteUrl(spec.BaseUrl, route));
                return new OutputRuntime
                {
                    Name = name.ToLowerInvariant(),
                    Url = url,
                    MediaType = string.IsNullOrWhiteSpace(format.MediaType) ? "text/html" : format.MediaType,
                    Rel = format.Rel,
                    IsCurrent = string.Equals(name, "html", StringComparison.OrdinalIgnoreCase)
                };
            })
            .GroupBy(output => output.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        return outputs;
    }

    private static string RenderAlternateOutputHeadLinks(IReadOnlyList<OutputRuntime> outputs)
    {
        if (outputs is null || outputs.Count == 0)
            return string.Empty;

        var lines = outputs
            .Where(output => !output.IsCurrent)
            .Where(output => !string.IsNullOrWhiteSpace(output.Url))
            .Select(output =>
            {
                var rel = string.IsNullOrWhiteSpace(output.Rel) ? "alternate" : output.Rel!;
                var relEncoded = System.Web.HttpUtility.HtmlEncode(rel);
                var typeEncoded = System.Web.HttpUtility.HtmlEncode(output.MediaType);
                var hrefEncoded = System.Web.HttpUtility.HtmlEncode(output.Url);
                var titleEncoded = System.Web.HttpUtility.HtmlEncode(output.Name.ToUpperInvariant());
                return $"<link rel=\"{relEncoded}\" type=\"{typeEncoded}\" href=\"{hrefEncoded}\" title=\"{titleEncoded}\" />";
            })
            .ToArray();

        return lines.Length == 0
            ? string.Empty
            : string.Join(Environment.NewLine, lines);
    }

    private static string ResolveOutputRoute(string outputPath, OutputFormatSpec format)
    {
        var baseRoute = NormalizeRouteForMatch(outputPath);
        if (string.IsNullOrWhiteSpace(format.Suffix) || format.Suffix.Equals("html", StringComparison.OrdinalIgnoreCase))
            return baseRoute;

        var prefix = baseRoute.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(prefix))
            prefix = "/";

        if (prefix == "/")
            return $"/index.{format.Suffix}";

        return $"{prefix}/index.{format.Suffix}";
    }

    private static string CombineAbsoluteUrl(string baseUrl, string path)
    {
        var normalizedBase = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(normalizedBase))
            return path;

        var normalizedPath = string.IsNullOrWhiteSpace(path)
            ? "/"
            : (path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path.TrimStart('/'));
        return normalizedBase + normalizedPath;
    }

    private static string RenderOutput(
        SiteSpec spec,
        string rootPath,
        ContentItem item,
        IReadOnlyList<ContentItem> allItems,
        IReadOnlyDictionary<string, object?> data,
        IReadOnlyDictionary<string, ProjectSpec> projectMap,
        MenuSpec[] menuSpecs,
        OutputFormatSpec format,
        IReadOnlyList<OutputRuntime> outputs,
        string alternateHeadLinksHtml)
    {
        var name = format.Name.ToLowerInvariant();
        return name switch
        {
            "json" => RenderJsonOutput(spec, item, allItems),
            "rss" => RenderRssOutput(spec, item, allItems),
            _ => RenderHtmlPage(spec, rootPath, item, allItems, data, projectMap, menuSpecs, outputs, alternateHeadLinksHtml)
        };
    }

    private static string RenderJsonOutput(SiteSpec spec, ContentItem item, IReadOnlyList<ContentItem> items)
    {
        var listItems = ResolveListItems(item, items);
        var payload = new Dictionary<string, object?>
        {
            ["title"] = item.Title,
            ["description"] = item.Description,
            ["url"] = item.OutputPath,
            ["kind"] = item.Kind.ToString().ToLowerInvariant(),
            ["collection"] = item.Collection,
            ["tags"] = item.Tags,
            ["date"] = item.Date?.ToString("O"),
            ["content"] = item.HtmlContent,
            ["items"] = listItems.Select(i => new Dictionary<string, object?>
            {
                ["title"] = i.Title,
                ["url"] = i.OutputPath,
                ["description"] = i.Description,
                ["date"] = i.Date?.ToString("O"),
                ["tags"] = i.Tags
            }).ToList()
        };

        return JsonSerializer.Serialize(payload, WebJson.Options);
    }

    private static string RenderRssOutput(SiteSpec spec, ContentItem item, IReadOnlyList<ContentItem> items)
    {
        var listItems = ResolveListItems(item, items);
        var feedSpec = spec.Feed;
        var maxItems = feedSpec?.MaxItems ?? 0;
        var includeContent = feedSpec?.IncludeContent == true;
        var includeCategories = feedSpec?.IncludeCategories != false;
        var baseUrl = spec.BaseUrl?.TrimEnd('/') ?? string.Empty;
        var channelTitle = string.IsNullOrWhiteSpace(item.Title) ? spec.Name : item.Title;
        var channelLink = string.IsNullOrWhiteSpace(baseUrl) ? item.OutputPath : baseUrl + item.OutputPath;
        var channelDescription = string.IsNullOrWhiteSpace(item.Description) ? spec.Name : item.Description;
        var feedRoute = ResolveOutputRoute(item.OutputPath, new OutputFormatSpec { Name = "rss", Suffix = "xml" });
        var feedSelfLink = string.IsNullOrWhiteSpace(baseUrl) ? feedRoute : baseUrl + feedRoute;

        IEnumerable<ContentItem> orderedItems = listItems
            .OrderByDescending(i => i.Date ?? DateTime.MinValue)
            .ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase);
        if (maxItems > 0)
            orderedItems = orderedItems.Take(maxItems);

        var contentNs = XNamespace.Get("http://purl.org/rss/1.0/modules/content/");
        var atomNs = XNamespace.Get("http://www.w3.org/2005/Atom");

        var feedItems = orderedItems
            .Select(i =>
            {
                var link = string.IsNullOrWhiteSpace(baseUrl) ? i.OutputPath : baseUrl + i.OutputPath;
                var description = string.IsNullOrWhiteSpace(i.Description) ? BuildSnippet(i.HtmlContent, 200) : i.Description;
                var pubDate = i.Date?.ToUniversalTime().ToString("r") ?? DateTime.UtcNow.ToString("r");
                var element = new XElement("item",
                    new XElement("title", i.Title),
                    new XElement("link", link),
                    new XElement("description", description),
                    new XElement("pubDate", pubDate),
                    new XElement("guid", link));
                if (includeCategories)
                {
                    foreach (var category in ResolveRssCategories(i))
                        element.Add(new XElement("category", category));
                }
                if (includeContent && !string.IsNullOrWhiteSpace(i.HtmlContent))
                    element.Add(new XElement(contentNs + "encoded", new XCData(i.HtmlContent)));
                return element;
            })
            .ToArray();

        var root = new XElement("rss",
            new XAttribute("version", "2.0"),
            new XAttribute(XNamespace.Xmlns + "atom", atomNs));
        if (includeContent)
            root.Add(new XAttribute(XNamespace.Xmlns + "content", contentNs));

        root.Add(
            new XElement("channel",
                new XElement("title", channelTitle),
                new XElement("link", channelLink),
                new XElement("description", channelDescription),
                new XElement(atomNs + "link",
                    new XAttribute("href", feedSelfLink),
                    new XAttribute("rel", "self"),
                    new XAttribute("type", "application/rss+xml")),
                feedItems));

        var doc = new XDocument(root);
        return doc.ToString(SaveOptions.DisableFormatting);
    }

    private static IEnumerable<string> ResolveRssCategories(ContentItem item)
    {
        var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tag in item.Tags ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(tag))
                categories.Add(tag.Trim());
        }

        foreach (var value in GetTaxonomyValues(item, new TaxonomySpec { Name = "categories" }))
        {
            if (!string.IsNullOrWhiteSpace(value))
                categories.Add(value.Trim());
        }

        return categories.OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
    }

    private static void CopyPageResources(ContentItem item, string targetDir)
    {
        if (item.Resources is null || item.Resources.Length == 0)
            return;

        foreach (var resource in item.Resources)
        {
            if (string.IsNullOrWhiteSpace(resource.SourcePath) || !File.Exists(resource.SourcePath))
                continue;

            var relative = resource.RelativePath ?? string.Empty;
            var target = string.IsNullOrWhiteSpace(relative)
                ? Path.Combine(targetDir, resource.Name)
                : Path.Combine(targetDir, relative.Replace('/', Path.DirectorySeparatorChar));
            var targetFolder = Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(targetFolder))
                Directory.CreateDirectory(targetFolder);
            File.Copy(resource.SourcePath, target, overwrite: true);
        }
    }

    private static IReadOnlyList<ContentItem> ResolveListItems(ContentItem item, IReadOnlyList<ContentItem> items)
    {
        if (item.Kind == PageKind.Section)
        {
            var sectionRoot = item.OutputPath;
            if (IsGeneratedPaginationItem(item))
            {
                var firstUrl = GetMetaString(item.Meta, PaginationFirstUrlMetaKey);
                if (!string.IsNullOrWhiteSpace(firstUrl))
                    sectionRoot = firstUrl;
            }

            var current = NormalizeRouteForMatch(sectionRoot);
            return items
                .Where(i => !i.Draft)
                .Where(i => i.Collection == item.Collection)
                .Where(i => i.OutputPath != item.OutputPath)
                .Where(i => i.Kind == PageKind.Page || i.Kind == PageKind.Home)
                .Where(i => NormalizeRouteForMatch(i.OutputPath).StartsWith(current, StringComparison.OrdinalIgnoreCase))
                .OrderBy(i => i.Order ?? int.MaxValue)
                .ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (item.Kind == PageKind.Taxonomy)
        {
            var taxonomy = GetMetaString(item.Meta, "taxonomy");
            return items
                .Where(i => i.Kind == PageKind.Term)
                .Where(i => string.Equals(GetMetaString(i.Meta, "taxonomy"), taxonomy, StringComparison.OrdinalIgnoreCase))
                .OrderBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (item.Kind == PageKind.Term)
        {
            var taxonomy = GetMetaString(item.Meta, "taxonomy");
            var term = GetMetaString(item.Meta, "term");
            if (string.IsNullOrWhiteSpace(term))
                return Array.Empty<ContentItem>();

            return items
                .Where(i => !i.Draft)
                .Where(i => i.Kind == PageKind.Page || i.Kind == PageKind.Home)
                .Where(i => GetTaxonomyValues(i, new TaxonomySpec { Name = taxonomy }).Any(t =>
                    string.Equals(t, term, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(i => i.Order ?? int.MaxValue)
                .ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return Array.Empty<ContentItem>();
    }

    private static TaxonomySpec? ResolveTaxonomy(SiteSpec spec, ContentItem item)
    {
        var key = GetMetaString(item.Meta, "taxonomy");
        if (string.IsNullOrWhiteSpace(key)) return null;
        return spec.Taxonomies.FirstOrDefault(t => string.Equals(t.Name, key, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ResolveTerm(ContentItem item)
    {
        var term = GetMetaString(item.Meta, "term");
        return string.IsNullOrWhiteSpace(term) ? null : term;
    }
}

