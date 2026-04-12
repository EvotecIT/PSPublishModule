using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.Loader;
using System.Xml;
using System.Xml.Linq;

namespace PowerForge.Web;

/// <summary>Generates API documentation artifacts from XML docs.</summary>
public static partial class WebApiDocsGenerator
{
    private static void GenerateHtml(
        string outputPath,
        WebApiDocsOptions options,
        IReadOnlyList<ApiTypeModel> types,
        IReadOnlyDictionary<string, ApiTypeUsageModel> typeUsageMap,
        IReadOnlyDictionary<string, ApiTypeRelatedContentModel> typeRelatedContentMap,
        List<string> warnings)
    {
        var template = (options.Template ?? string.Empty).Trim().ToLowerInvariant();
        if (template is "docs" or "sidebar")
        {
            GenerateDocsHtml(outputPath, options, types, typeUsageMap, typeRelatedContentMap, warnings);
            return;
        }

        var head = GetApiDocsResolvedHeadHtml(options);
        var header = LoadOptionalHtml(options.HeaderHtmlPath);
        var footer = LoadOptionalHtml(options.FooterHtmlPath);
        ApplyNavFallback(options, warnings, ref header, ref footer);
        ApplyNavTokens(options, warnings, ref header, ref footer);
        var bodyClass = ResolveBodyClass(options.BodyClass);
        var criticalCss = ResolveCriticalCss(options, warnings);
        var codeLanguage = GetDefaultCodeLanguage(options);
        var (prismCss, prismScripts) = BuildApiPrismAssets(options, codeLanguage);
        var cssLinks = BuildCssLinks(options.CssHref);
        var fallbackCss = LoadAsset(options, "fallback.css", null);
        var cssBlock = BuildCssBlockWithFallback(fallbackCss, cssLinks, prismCss);
        var social = ResolveApiSocialProfile(options);
        var indexRoute = NormalizeApiRoute(options.BaseUrl);

        var indexTemplate = LoadTemplate(options, "index.html", options.IndexTemplatePath);
        var searchScript = JoinHtmlFragments(
            WrapScript(LoadAsset(options, "search.js", options.SearchScriptPath)),
            prismScripts);
        var indexHtml = ApplyTemplate(indexTemplate, new Dictionary<string, string?>
        {
            ["TITLE"] = System.Web.HttpUtility.HtmlEncode(options.Title),
            ["DESCRIPTION_META"] = BuildDescriptionMetaTag($"API reference for {options.Title}."),
            ["OPEN_GRAPH_META"] = BuildApiOpenGraphMetaTags(options, social, options.Title, $"API reference for {options.Title}.", indexRoute),
            ["HEAD_HTML"] = head,
            ["CRITICAL_CSS"] = criticalCss,
            ["CSS"] = cssBlock,
            ["HEADER"] = header,
            ["FOOTER"] = footer,
            ["BODY_CLASS"] = bodyClass,
            ["TYPE_COUNT"] = types.Count.ToString(),
            ["TYPE_LINKS"] = BuildSimpleTypeLinks(types),
            ["SEARCH_SCRIPT"] = searchScript
        });

        File.WriteAllText(Path.Combine(outputPath, "index.html"), indexHtml.ToString(), Encoding.UTF8);

        var typesDir = Path.Combine(outputPath, "types");
        Directory.CreateDirectory(typesDir);
        foreach (var type in types)
        {
            var typeTitle = $"{type.FullName} - {options.Title}";
            var typeTemplate = LoadTemplate(options, "type.html", options.TypeTemplatePath);
            var typeRoute = $"{NormalizeApiRoute(options.BaseUrl).TrimEnd('/')}/types/{type.Slug}.html";
            var typeHtml = ApplyTemplate(typeTemplate, new Dictionary<string, string?>
            {
                ["TYPE_TITLE"] = System.Web.HttpUtility.HtmlEncode(typeTitle),
                ["TYPE_FULLNAME"] = System.Web.HttpUtility.HtmlEncode(type.FullName),
                ["DESCRIPTION_META"] = BuildDescriptionMetaTag($"API reference for {type.FullName} in {options.Title}."),
                ["OPEN_GRAPH_META"] = BuildApiOpenGraphMetaTags(options, social, typeTitle, $"API reference for {type.FullName} in {options.Title}.", typeRoute),
                ["HEAD_HTML"] = head,
                ["CRITICAL_CSS"] = criticalCss,
                ["CSS"] = cssBlock,
                ["HEADER"] = header,
                ["FOOTER"] = footer,
                ["BODY_CLASS"] = bodyClass,
                ["TYPE_SUMMARY"] = BuildSimpleTypeSummaryHtml(type),
                ["TYPE_REMARKS"] = BuildSimpleTypeRemarksHtml(type),
                ["MEMBERS"] = BuildSimpleTypeMemberSections(type, codeLanguage),
                ["TYPE_SCRIPT"] = prismScripts
            });

            File.WriteAllText(Path.Combine(typesDir, $"{type.Slug}.html"), typeHtml, Encoding.UTF8);
        }

        var sitemapPath = Path.Combine(outputPath, "sitemap.xml");
        GenerateApiSitemap(sitemapPath, options.BaseUrl, types);
    }

    private static void GenerateDocsHtml(
        string outputPath,
        WebApiDocsOptions options,
        IReadOnlyList<ApiTypeModel> types,
        IReadOnlyDictionary<string, ApiTypeUsageModel> typeUsageMap,
        IReadOnlyDictionary<string, ApiTypeRelatedContentModel> typeRelatedContentMap,
        List<string> warnings)
    {
        var head = GetApiDocsResolvedHeadHtml(options);
        var header = LoadOptionalHtml(options.HeaderHtmlPath);
        var footer = LoadOptionalHtml(options.FooterHtmlPath);
        ApplyNavFallback(options, warnings, ref header, ref footer);
        ApplyNavTokens(options, warnings, ref header, ref footer);
        var bodyClass = ResolveBodyClass(options.BodyClass);
        var criticalCss = ResolveCriticalCss(options, warnings);
        var codeLanguage = GetDefaultCodeLanguage(options);
        var (prismCss, prismScripts) = BuildApiPrismAssets(options, codeLanguage);
        var cssLinks = BuildCssLinks(options.CssHref);
        var fallbackCss = LoadAsset(options, "fallback.css", null);
        var cssBlock = BuildCssBlockWithFallback(fallbackCss, cssLinks, prismCss);

        var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl) ? "/api" : options.BaseUrl.TrimEnd('/');
        var suite = BuildApiSuiteContext(options, baseUrl);
        var docsScript = JoinHtmlFragments(
            WrapScript(LoadAsset(options, "docs.js", options.DocsScriptPath)),
            prismScripts);
        var docsHomeUrl = NormalizeDocsHomeUrl(options.DocsHomeUrl, baseUrl);
        var legacyAliasMode = ResolveLegacyAliasMode(options.LegacyAliasMode);
        var social = ResolveApiSocialProfile(options);
        var typeDisplayNames = BuildTypeDisplayNameMap(types, options, warnings);
        var sidebarHtml = BuildDocsSidebar(options, types, baseUrl, string.Empty, docsHomeUrl, typeDisplayNames, suite);
        var sidebarClass = BuildSidebarClass(options.SidebarPosition);
        var overviewHtml = BuildDocsOverview(options, types, baseUrl, typeDisplayNames, suite);
        var slugMap = BuildTypeSlugMap(types);
        var typeIndex = BuildTypeIndex(types);
        var derivedMap = BuildDerivedTypeMap(types, typeIndex);

        var indexTemplate = LoadTemplate(options, "docs-index.html", options.DocsIndexTemplatePath);
        var indexHtml = ApplyTemplate(indexTemplate, new Dictionary<string, string?>
        {
            ["TITLE"] = System.Web.HttpUtility.HtmlEncode(options.Title),
            ["DESCRIPTION_META"] = BuildDescriptionMetaTag($"API reference for {options.Title}."),
            ["OPEN_GRAPH_META"] = BuildApiOpenGraphMetaTags(options, social, options.Title, $"API reference for {options.Title}.", NormalizeApiRoute(baseUrl)),
            ["HEAD_HTML"] = head,
            ["CRITICAL_CSS"] = criticalCss,
            ["CSS"] = cssBlock,
            ["HEADER"] = header,
            ["FOOTER"] = footer,
            ["BODY_CLASS"] = bodyClass,
            ["SIDEBAR"] = sidebarHtml,
            ["SIDEBAR_CLASS"] = sidebarClass,
            ["MAIN"] = overviewHtml,
            ["DOCS_SCRIPT"] = docsScript
        });
        File.WriteAllText(Path.Combine(outputPath, "index.html"), indexHtml.ToString(), Encoding.UTF8);

        foreach (var type in types)
        {
            var sidebar = BuildDocsSidebar(options, types, baseUrl, type.Slug, docsHomeUrl, typeDisplayNames, suite);
            var sidebarClassForType = BuildSidebarClass(options.SidebarPosition);
            var displayName = ResolveTypeDisplayName(type, typeDisplayNames);
            typeUsageMap.TryGetValue(type.FullName, out var usage);
            typeRelatedContentMap.TryGetValue(type.FullName, out var relatedContent);
            var typeMain = BuildDocsTypeDetail(type, baseUrl, slugMap, typeIndex, derivedMap, usage, relatedContent, codeLanguage, displayName);
            var typeTemplate = LoadTemplate(options, "docs-type.html", options.DocsTypeTemplatePath);
            var pageTitle = $"{displayName} - {options.Title}";
            var typeRoute = $"{NormalizeApiRoute(baseUrl).TrimEnd('/')}/{type.Slug}/";
            var typeHtml = ApplyTemplate(typeTemplate, new Dictionary<string, string?>
            {
                ["TITLE"] = System.Web.HttpUtility.HtmlEncode(pageTitle),
                ["DESCRIPTION_META"] = BuildDescriptionMetaTag($"API reference for {displayName} in {options.Title}."),
                ["OPEN_GRAPH_META"] = BuildApiOpenGraphMetaTags(options, social, pageTitle, $"API reference for {displayName} in {options.Title}.", typeRoute),
                ["HEAD_HTML"] = head,
                ["CRITICAL_CSS"] = criticalCss,
                ["CSS"] = cssBlock,
                ["HEADER"] = header,
                ["FOOTER"] = footer,
                ["BODY_CLASS"] = bodyClass,
                ["SIDEBAR"] = sidebar,
                ["SIDEBAR_CLASS"] = sidebarClassForType,
                ["MAIN"] = typeMain,
                ["DOCS_SCRIPT"] = docsScript
            });

            var typeDir = Path.Combine(outputPath, type.Slug);
            Directory.CreateDirectory(typeDir);
            File.WriteAllText(Path.Combine(typeDir, "index.html"), typeHtml, Encoding.UTF8);

            if (!string.Equals(legacyAliasMode, "omit", StringComparison.Ordinal))
            {
                var htmlPath = Path.Combine(outputPath, $"{type.Slug}.html");
                var aliasHtml = string.Equals(legacyAliasMode, "redirect", StringComparison.Ordinal)
                    ? BuildLegacyAliasRedirectHtml(typeRoute)
                    : InjectNoIndexRobotsMeta(typeHtml);
                File.WriteAllText(htmlPath, aliasHtml, Encoding.UTF8);
            }
        }

        var sitemapPath = Path.Combine(outputPath, "sitemap.xml");
        GenerateDocsSitemap(sitemapPath, baseUrl, types);
    }

    private static string InjectNoIndexRobotsMeta(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return html;

        if (Regex.IsMatch(html, "<meta\\s+name\\s*=\\s*[\"']robots[\"'][^>]*\\bnoindex\\b", RegexOptions.IgnoreCase))
            return html;

        const string noIndexMeta = "<meta name=\"robots\" content=\"noindex,follow\" data-pf=\"api-docs-legacy-alias\" />";
        var headMatch = Regex.Match(html, "<head\\b[^>]*>", RegexOptions.IgnoreCase);
        if (!headMatch.Success)
            return $"{noIndexMeta}{Environment.NewLine}{html}";

        return html.Insert(headMatch.Index + headMatch.Length, $"{Environment.NewLine}  {noIndexMeta}");
    }

    private static string ResolveLegacyAliasMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "noindex";

        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "noindex" => "noindex",
            "noindex-file" => "noindex",
            "file" => "noindex",
            "default" => "noindex",
            "redirect" => "redirect",
            "redirects" => "redirect",
            "omit" => "omit",
            "none" => "omit",
            "disabled" => "omit",
            "off" => "omit",
            _ => throw new ArgumentException($"Unsupported legacy alias mode '{value}'. Use noindex, redirect, or omit.")
        };
    }

    private static string BuildLegacyAliasRedirectHtml(string canonicalUrl)
    {
        var canonical = string.IsNullOrWhiteSpace(canonicalUrl) ? "/" : canonicalUrl.Trim();
        if (!canonical.StartsWith("/", StringComparison.Ordinal) &&
            !canonical.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !canonical.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            canonical = "/" + canonical;
        }

        var encodedCanonical = System.Web.HttpUtility.HtmlEncode(canonical);
        var scriptCanonical = System.Web.HttpUtility.JavaScriptStringEncode(canonical);
        return
$@"<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
  <title>Redirecting...</title>
  <meta name=""robots"" content=""noindex,follow"" data-pf=""api-docs-legacy-alias"" />
  <meta http-equiv=""refresh"" content=""0;url={encodedCanonical}"" />
  <link rel=""canonical"" href=""{encodedCanonical}"" />
</head>
<body>
  <p>This page moved to <a href=""{encodedCanonical}"">{encodedCanonical}</a>.</p>
  <script>
    window.location.replace('{scriptCanonical}');
  </script>
</body>
</html>
";
    }

    private static void ApplyNavFallback(WebApiDocsOptions options, List<string> warnings, ref string header, ref string footer)
    {
        if (options is null || warnings is null) return;
        if (string.IsNullOrWhiteSpace(options.NavJsonPath)) return;

        var template = (options.Template ?? string.Empty).Trim().ToLowerInvariant();
        var wantsNavShell = template is "docs" or "sidebar" or "simple" || string.IsNullOrWhiteSpace(template);
        if (!wantsNavShell) return;

        var used = false;
        if (string.IsNullOrWhiteSpace(header))
        {
            header = LoadAsset(options, "api-header.html", null);
            used |= !string.IsNullOrWhiteSpace(header);
        }
        if (string.IsNullOrWhiteSpace(footer))
        {
            footer = LoadAsset(options, "api-footer.html", null);
            used |= !string.IsNullOrWhiteSpace(footer);
        }

        if (used)
        {
            warnings.Add("API docs: using embedded header/footer fragments (provide headerHtml/footerHtml to override).");
        }
    }

    private static void ValidateCssContract(
        string outputPath,
        WebApiDocsOptions options,
        IReadOnlyDictionary<string, ApiTypeUsageModel> typeUsageMap,
        IReadOnlyDictionary<string, ApiTypeRelatedContentModel> typeRelatedContentMap,
        List<string> warnings)
    {
        if (options is null || warnings is null) return;
        if (string.IsNullOrWhiteSpace(options.CssHref)) return;

        var template = (options.Template ?? string.Empty).Trim().ToLowerInvariant();
        var required = GetRequiredCssSelectors(template, options, typeUsageMap, typeRelatedContentMap);
        if (required.Length == 0) return;

        var hrefs = SplitCssHrefs(options.CssHref);
        if (hrefs.Length == 0) return;

        var cssCombined = new StringBuilder();
        var checkedHrefs = new List<string>();
        foreach (var href in hrefs)
        {
            if (Uri.TryCreate(href, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                continue; // don't try to validate remote CSS

            var cssPath = TryResolveCssPath(outputPath, options.BaseUrl, href);
            if (string.IsNullOrWhiteSpace(cssPath))
                continue; // best-effort: avoid false positives when we can't resolve the href to disk
            if (!File.Exists(cssPath))
            {
                warnings.Add($"API docs CSS contract: CSS '{href}' was not found on disk at '{cssPath}'.");
                continue;
            }

            string css;
            try
            {
                css = File.ReadAllText(cssPath);
            }
            catch (Exception ex)
            {
                warnings.Add($"API docs CSS contract: failed to read CSS '{href}' ({ex.GetType().Name}: {ex.Message}).");
                continue;
            }

            if (cssCombined.Length > 0) cssCombined.AppendLine();
            cssCombined.Append(css);
            checkedHrefs.Add(href);
        }

        if (checkedHrefs.Count == 0)
            return;

        var cssText = cssCombined.ToString();

        var missing = required
            .Where(selector => !string.IsNullOrWhiteSpace(selector) &&
                               cssText.IndexOf(selector, StringComparison.OrdinalIgnoreCase) < 0)
            .ToArray();

        if (missing.Length == 0)
            return;

        var preview = string.Join(", ", missing.Take(6));
        var more = missing.Length > 6 ? $" (+{missing.Length - 6} more)" : string.Empty;
        warnings.Add($"API docs CSS contract: combined CSS ({string.Join(", ", checkedHrefs)}) is missing expected selectors: {preview}{more}.");
    }

    private static string[] GetRequiredCssSelectors(
        string template,
        WebApiDocsOptions options,
        IReadOnlyDictionary<string, ApiTypeUsageModel> typeUsageMap,
        IReadOnlyDictionary<string, ApiTypeRelatedContentModel> typeRelatedContentMap)
    {
        if (template is not ("docs" or "sidebar"))
            return RequiredSelectorsSimple;

        var selectors = new List<string>(RequiredSelectorsDocs);
        var suite = BuildApiSuiteContext(options, options.BaseUrl);
        if (suite?.HasEntries == true)
            AddRequiredCssSelectors(selectors, RequiredSelectorsDocsSuite);

        if (typeUsageMap is not null && typeUsageMap.Values.Any(static usage => usage?.HasEntries == true))
            AddRequiredCssSelectors(selectors, RequiredSelectorsDocsUsage);

        if (typeRelatedContentMap is not null && typeRelatedContentMap.Values.Any(static relatedContent => relatedContent is not null && relatedContent.Entries.Count > 0))
            AddRequiredCssSelectors(selectors, RequiredSelectorsDocsRelatedContentType);

        if (typeRelatedContentMap is not null && typeRelatedContentMap.Values.Any(static relatedContent => relatedContent is not null && relatedContent.MemberEntries.Count > 0))
            AddRequiredCssSelectors(selectors, RequiredSelectorsDocsRelatedContentMember);

        return selectors.ToArray();
    }

    private static void AddRequiredCssSelectors(List<string> selectors, IReadOnlyList<string> group)
    {
        if (selectors is null || group is null)
            return;

        foreach (var selector in group)
        {
            if (string.IsNullOrWhiteSpace(selector) || selectors.Contains(selector, StringComparer.OrdinalIgnoreCase))
                continue;

            selectors.Add(selector);
        }
    }

    private static string BuildDescriptionMetaTag(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return string.Empty;

        var encoded = System.Web.HttpUtility.HtmlEncode(description.Trim());
        return $"<meta name=\"description\" content=\"{encoded}\" />";
    }

    private static string BuildApiOpenGraphMetaTags(WebApiDocsOptions options, ApiSocialProfile social, string pageTitle, string description, string route)
    {
        if (string.IsNullOrWhiteSpace(pageTitle))
            pageTitle = options.Title;

        var title = pageTitle?.Trim() ?? string.Empty;
        var desc = description?.Trim() ?? string.Empty;
        var siteName = string.IsNullOrWhiteSpace(social.SiteName) ? options.Title : social.SiteName;
        var url = ResolveApiAbsoluteUrl(social.SiteBaseUrl, route);
        var imagePath = ResolveApiSocialImagePath(options, social, title, desc, route);
        var image = ResolveApiAbsoluteUrl(social.SiteBaseUrl, imagePath);
        var (imageWidth, imageHeight) = ResolveApiSocialImageDimensions(options, social, imagePath);
        var imageAlt = title;
        var twitterCard = string.IsNullOrWhiteSpace(social.TwitterCard)
            ? (string.IsNullOrWhiteSpace(image) ? "summary" : "summary_large_image")
            : social.TwitterCard;
        var twitterSite = NormalizeTwitterHandle(social.TwitterSite);
        var twitterCreator = NormalizeTwitterHandle(social.TwitterCreator);
        var languageCode = NormalizeApiLanguageCode(options.LanguageCode);

        var html = new HtmlFragmentBuilder();
        if (!string.IsNullOrWhiteSpace(url))
        {
            html.Line($"<link rel=\"canonical\" href=\"{System.Web.HttpUtility.HtmlEncode(url)}\" />");
            if (!string.IsNullOrWhiteSpace(languageCode))
            {
                html.Line($"<link rel=\"alternate\" hreflang=\"{System.Web.HttpUtility.HtmlEncode(languageCode)}\" href=\"{System.Web.HttpUtility.HtmlEncode(url)}\" />");
                html.Line($"<link rel=\"alternate\" hreflang=\"x-default\" href=\"{System.Web.HttpUtility.HtmlEncode(url)}\" />");
            }
        }
        html.Line("<!-- Open Graph -->");
        html.Line($"<meta property=\"og:title\" content=\"{System.Web.HttpUtility.HtmlEncode(title)}\" />");
        if (!string.IsNullOrWhiteSpace(desc))
            html.Line($"<meta property=\"og:description\" content=\"{System.Web.HttpUtility.HtmlEncode(desc)}\" />");
        html.Line("<meta property=\"og:type\" content=\"website\" />");
        if (!string.IsNullOrWhiteSpace(url))
            html.Line($"<meta property=\"og:url\" content=\"{System.Web.HttpUtility.HtmlEncode(url)}\" />");
        if (!string.IsNullOrWhiteSpace(image))
            html.Line($"<meta property=\"og:image\" content=\"{System.Web.HttpUtility.HtmlEncode(image)}\" />");
        if (!string.IsNullOrWhiteSpace(image) && !string.IsNullOrWhiteSpace(imageAlt))
            html.Line($"<meta property=\"og:image:alt\" content=\"{System.Web.HttpUtility.HtmlEncode(imageAlt)}\" />");
        if (imageWidth > 0)
            html.Line($"<meta property=\"og:image:width\" content=\"{imageWidth}\" />");
        if (imageHeight > 0)
            html.Line($"<meta property=\"og:image:height\" content=\"{imageHeight}\" />");
        if (!string.IsNullOrWhiteSpace(siteName))
            html.Line($"<meta property=\"og:site_name\" content=\"{System.Web.HttpUtility.HtmlEncode(siteName)}\" />");

        html.BlankLine();
        html.Line("<!-- Twitter Card -->");
        html.Line($"<meta name=\"twitter:card\" content=\"{System.Web.HttpUtility.HtmlEncode(twitterCard)}\" />");
        html.Line($"<meta name=\"twitter:title\" content=\"{System.Web.HttpUtility.HtmlEncode(title)}\" />");
        if (!string.IsNullOrWhiteSpace(twitterSite))
            html.Line($"<meta name=\"twitter:site\" content=\"{System.Web.HttpUtility.HtmlEncode(twitterSite)}\" />");
        if (!string.IsNullOrWhiteSpace(twitterCreator))
            html.Line($"<meta name=\"twitter:creator\" content=\"{System.Web.HttpUtility.HtmlEncode(twitterCreator)}\" />");
        if (!string.IsNullOrWhiteSpace(desc))
            html.Line($"<meta name=\"twitter:description\" content=\"{System.Web.HttpUtility.HtmlEncode(desc)}\" />");
        if (!string.IsNullOrWhiteSpace(url))
            html.Line($"<meta name=\"twitter:url\" content=\"{System.Web.HttpUtility.HtmlEncode(url)}\" />");
        if (!string.IsNullOrWhiteSpace(image))
            html.Line($"<meta name=\"twitter:image\" content=\"{System.Web.HttpUtility.HtmlEncode(image)}\" />");
        if (!string.IsNullOrWhiteSpace(image) && !string.IsNullOrWhiteSpace(imageAlt))
            html.Line($"<meta name=\"twitter:image:alt\" content=\"{System.Web.HttpUtility.HtmlEncode(imageAlt)}\" />");

        return html.ToString().TrimEnd();
    }

    private static string NormalizeApiLanguageCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Trim().Replace('_', '-').Trim('/').ToLowerInvariant();
    }

    private static ApiSocialProfile ResolveApiSocialProfile(WebApiDocsOptions options)
    {
        var nav = LoadNavConfig(options);
        var siteName = FirstNonEmpty(options.SiteName, nav?.SiteName, options.Title);
        var siteBaseUrl = FirstNonEmpty(options.SiteBaseUrl, nav?.SiteBaseUrl);
        var image = FirstNonEmpty(options.SocialImage, nav?.SocialImage, nav?.BrandIcon);
        var imageWidth = options.SocialImageWidth ?? nav?.SocialImageWidth;
        var imageHeight = options.SocialImageHeight ?? nav?.SocialImageHeight;
        var twitterCard = FirstNonEmpty(options.SocialTwitterCard, nav?.SocialTwitterCard, "summary");
        var twitterSite = FirstNonEmpty(options.SocialTwitterSite, nav?.SocialTwitterSite);
        var twitterCreator = FirstNonEmpty(options.SocialTwitterCreator, nav?.SocialTwitterCreator);

        return new ApiSocialProfile(
            siteName ?? string.Empty,
            siteBaseUrl ?? string.Empty,
            image ?? string.Empty,
            imageWidth,
            imageHeight,
            twitterCard ?? "summary",
            twitterSite ?? string.Empty,
            twitterCreator ?? string.Empty);
    }

    private static (int Width, int Height) ResolveApiSocialImageDimensions(
        WebApiDocsOptions options,
        ApiSocialProfile social,
        string imagePath)
    {
        var generatedPrefix = NormalizeApiSocialCardPath(options.SocialCardPath) + "/";
        var normalizedImagePath = NormalizeApiImagePath(imagePath);
        if (!string.IsNullOrWhiteSpace(normalizedImagePath) &&
            normalizedImagePath.StartsWith(generatedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return (Math.Max(0, options.SocialCardWidth), Math.Max(0, options.SocialCardHeight));
        }

        return (Math.Max(0, social.ImageWidth ?? 0), Math.Max(0, social.ImageHeight ?? 0));
    }

    private static string NormalizeApiImagePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute))
            trimmed = absolute.AbsolutePath;
        if (!trimmed.StartsWith("/", StringComparison.Ordinal))
            trimmed = "/" + trimmed.TrimStart('/');
        return trimmed;
    }

    private static string NormalizeTwitterHandle(string? handle)
    {
        if (string.IsNullOrWhiteSpace(handle))
            return string.Empty;

        var trimmed = handle.Trim();
        if (!trimmed.StartsWith("@", StringComparison.Ordinal))
            trimmed = "@" + trimmed.TrimStart('@');
        return trimmed;
    }

    private static string NormalizeApiRoute(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
            return "/";

        var trimmed = route.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        if (!trimmed.StartsWith("/"))
            trimmed = "/" + trimmed;
        return trimmed;
    }

    private static string ResolveApiAbsoluteUrl(string? siteBaseUrl, string? routeOrUrl)
    {
        if (string.IsNullOrWhiteSpace(routeOrUrl))
            return string.Empty;

        var value = routeOrUrl.Trim();
        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return value;

        if (string.IsNullOrWhiteSpace(siteBaseUrl))
            return value;

        var trimmedBase = siteBaseUrl.Trim().TrimEnd('/');
        if (!trimmedBase.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !trimmedBase.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return value;

        var path = value.StartsWith("/") ? value : "/" + value;
        return trimmedBase + path;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }

    private static string ResolveCriticalCss(WebApiDocsOptions options, List<string> warnings)
    {
        if (options is null) return string.Empty;

        if (!string.IsNullOrWhiteSpace(options.CriticalCssHtml))
            return options.CriticalCssHtml!;

        if (string.IsNullOrWhiteSpace(options.CriticalCssPath))
            return string.Empty;

        try
        {
            var path = Path.GetFullPath(options.CriticalCssPath);
            if (!File.Exists(path))
            {
                warnings?.Add($"API docs: critical CSS not found: {options.CriticalCssPath}.");
                return string.Empty;
            }

            var css = File.ReadAllText(path);
            return WrapStyle(css);
        }
        catch (Exception ex)
        {
            warnings?.Add($"API docs: failed to read critical CSS '{options.CriticalCssPath}' ({ex.GetType().Name}: {ex.Message}).");
            return string.Empty;
        }
    }

    private static string BuildCssLinks(string? cssHref)
    {
        var hrefs = SplitCssHrefs(cssHref);
        if (hrefs.Length == 0) return string.Empty;

        return JoinHtmlFragments(hrefs.Select(static href => $"<link rel=\"stylesheet\" href=\"{href}\" />").ToArray());
    }

    private static string BuildCssBlockWithFallback(string fallbackCss, string cssLinks, string extraCssLinks = "")
    {
        if (!string.IsNullOrWhiteSpace(cssLinks))
            return JoinHtmlFragments(cssLinks, extraCssLinks);

        var fallbackBlock = WrapStyle(fallbackCss);
        return JoinHtmlFragments(fallbackBlock, extraCssLinks);
    }

    private static string JoinHtmlFragments(params string?[] fragments)
    {
        var parts = fragments
            .Where(static fragment => !string.IsNullOrWhiteSpace(fragment))
            .Select(static fragment => fragment!.Trim())
            .ToList();
        return parts.Count == 0 ? string.Empty : string.Join(Environment.NewLine, parts);
    }

    private static (string cssLinks, string scripts) BuildApiPrismAssets(WebApiDocsOptions options, string codeLanguage)
    {
        if (options is null || !options.InjectPrismAssets || string.IsNullOrWhiteSpace(codeLanguage))
            return (string.Empty, string.Empty);
        if (string.Equals(options.Prism?.Mode, "off", StringComparison.OrdinalIgnoreCase))
            return (string.Empty, string.Empty);

        var source = options.Prism?.Source;
        if (string.IsNullOrWhiteSpace(source))
            source = options.AssetPolicyMode;
        if (string.IsNullOrWhiteSpace(source))
            source = "cdn";

        var useLocal = source.Equals("local", StringComparison.OrdinalIgnoreCase) ||
                       source.Equals("hybrid", StringComparison.OrdinalIgnoreCase);

        if (useLocal)
        {
            var local = options.Prism?.Local;
            var light = ResolveApiPrismThemeHref(
                local?.ThemeLight ?? options.Prism?.ThemeLight,
                isCdn: false,
                cdnBase: null,
                defaultCdnName: "prism",
                defaultLocalPath: "/assets/prism/prism.css");
            var dark = ResolveApiPrismThemeHref(
                local?.ThemeDark ?? options.Prism?.ThemeDark,
                isCdn: false,
                cdnBase: null,
                defaultCdnName: "prism-okaidia",
                defaultLocalPath: "/assets/prism/prism-okaidia.css");
            var core = local?.Core ?? "/assets/prism/prism-core.js";
            var autoloader = local?.Autoloader ?? "/assets/prism/prism-autoloader.js";
            var languagesPath = local?.LanguagesPath ?? "/assets/prism/components/";

            var css = JoinHtmlFragments(
                $"<link rel=\"stylesheet\" href=\"{light}\" media=\"(prefers-color-scheme: light)\" />",
                $"<link rel=\"stylesheet\" href=\"{dark}\" media=\"(prefers-color-scheme: dark)\" />");
            var scripts = BuildApiPrismScriptBundle(core, autoloader, languagesPath);
            return (css, scripts);
        }

        var cdn = options.Prism?.CdnBase;
        if (string.IsNullOrWhiteSpace(cdn))
            cdn = "https://cdn.jsdelivr.net/npm/prismjs@1.29.0";
        cdn = cdn.TrimEnd('/');

        var cdnLight = ResolveApiPrismThemeHref(
            options.Prism?.ThemeLight,
            isCdn: true,
            cdnBase: cdn,
            defaultCdnName: "prism",
            defaultLocalPath: "/assets/prism/prism.css");
        var cdnDark = ResolveApiPrismThemeHref(
            options.Prism?.ThemeDark,
            isCdn: true,
            cdnBase: cdn,
            defaultCdnName: "prism-okaidia",
            defaultLocalPath: "/assets/prism/prism-okaidia.css");
        var cssLinks = JoinHtmlFragments(
            $"<link rel=\"stylesheet\" href=\"{cdnLight}\" media=\"(prefers-color-scheme: light)\" />",
            $"<link rel=\"stylesheet\" href=\"{cdnDark}\" media=\"(prefers-color-scheme: dark)\" />");
        var scriptsLinks = BuildApiPrismScriptBundle(
            $"{cdn}/components/prism-core.min.js",
            $"{cdn}/plugins/autoloader/prism-autoloader.min.js",
            $"{cdn}/components/");
        return (cssLinks, scriptsLinks);
    }

    private static string BuildApiPrismScriptBundle(string coreScript, string autoloaderScript, string languagesPath)
    {
        return JoinHtmlFragments(
            BuildApiPrismManualScript(),
            $"<script src=\"{coreScript}\"></script>",
            $"<script src=\"{autoloaderScript}\"></script>",
            BuildApiPrismInitScript(languagesPath));
    }

    private static string BuildApiPrismManualScript()
    {
        return "<script>(function(){window.Prism=window.Prism||{};window.Prism.manual=true;})();</script>";
    }

    private static string ResolveApiPrismThemeHref(
        string? value,
        bool isCdn,
        string? cdnBase,
        string defaultCdnName,
        string defaultLocalPath)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (!isCdn)
                return defaultLocalPath;
            var cdn = (cdnBase ?? string.Empty).TrimEnd('/');
            return $"{cdn}/themes/{defaultCdnName}.min.css";
        }

        var trimmed = value.Trim();
        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("//", StringComparison.OrdinalIgnoreCase) &&
            trimmed.Contains("..", StringComparison.Ordinal))
        {
            if (!isCdn)
                return defaultLocalPath;
            var safeCdn = (cdnBase ?? string.Empty).TrimEnd('/');
            return $"{safeCdn}/themes/{defaultCdnName}.min.css";
        }

        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("//", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        if (trimmed.StartsWith("/", StringComparison.Ordinal))
            return trimmed;

        if (trimmed.Contains("/", StringComparison.Ordinal))
            return "/" + trimmed.TrimStart('/');

        if (!isCdn)
        {
            if (trimmed.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
                return "/" + trimmed.TrimStart('/');
            return "/assets/prism/prism-" + trimmed + ".css";
        }

        var name = trimmed.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^4]
            : trimmed;
        var cdnRoot = (cdnBase ?? string.Empty).TrimEnd('/');
        return $"{cdnRoot}/themes/{name}.min.css";
    }

    private static string BuildApiPrismInitScript(string languagesPath)
    {
        var safePath = (languagesPath ?? string.Empty).Replace("'", "\\'", StringComparison.Ordinal);
        return
            "<script>(function(){" +
            "var targetPath='" + safePath + "';" +
            "var attempts=0;" +
            "var maxAttempts=8;" +
            "var delayMs=40;" +
            "var warned=false;" +
            "var hasCode=function(root){return !!(root&&root.querySelector&&root.querySelector('code[class*=\\\"language-\\\"]'));};" +
            "var hasTokens=function(root){return !!(root&&root.querySelector&&root.querySelector('code[class*=\\\"language-\\\"] .token'));};" +
            "var run=function(){" +
            "attempts++;" +
            "var root=document.querySelector('.api-content')||document;" +
            "var p=window.Prism;" +
            "if(!p||!hasCode(root)){return;}" +
            "if(p.plugins&&p.plugins.autoloader){p.plugins.autoloader.languages_path=targetPath;}" +
            "if(hasTokens(root)){return;}" +
            "try{if(p.highlightAllUnder){p.highlightAllUnder(root);}else if(p.highlightAll){p.highlightAll();}}" +
            "catch(e){if(!warned&&window.console&&console.warn){warned=true;console.warn('Prism highlighting failed.',e);}}" +
            "if(!hasTokens(root)&&attempts<maxAttempts){setTimeout(run,delayMs);}" +
            "};" +
            "if(document.readyState==='loading'){document.addEventListener('DOMContentLoaded',run);}else{run();}" +
            "})();</script>";
    }

    private static string[] SplitCssHrefs(string? cssHref)
    {
        if (string.IsNullOrWhiteSpace(cssHref))
            return Array.Empty<string>();

        return cssHref
            .Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();
    }

    private static string? TryResolveCssPath(string outputPath, string? baseUrl, string cssHref)
    {
        if (string.IsNullOrWhiteSpace(outputPath) || string.IsNullOrWhiteSpace(cssHref))
            return null;

        var href = cssHref.Trim();
        if (!href.StartsWith("/", StringComparison.Ordinal))
        {
            if (Path.IsPathRooted(href))
                return Path.GetFullPath(href);

            // Relative hrefs are ambiguous in API pages; best-effort: interpret relative to output folder.
            return Path.GetFullPath(Path.Combine(outputPath, href.Replace('/', Path.DirectorySeparatorChar)));
        }

        // Root-relative href: infer site root from outputPath + baseUrl.
        var segments = (baseUrl ?? string.Empty)
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Length;
        if (segments <= 0)
            return null;

        var root = Path.GetFullPath(outputPath);
        for (var i = 0; i < segments; i++)
        {
            var parent = Directory.GetParent(root);
            if (parent is null)
                return null;
            root = parent.FullName;
        }

        var relative = href.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(root, relative));
    }

    private static string BuildSimpleTypeLinks(IReadOnlyList<ApiTypeModel> types)
    {
        if (types is null || types.Count == 0)
            return string.Empty;

        var html = new HtmlFragmentBuilder(initialIndent: 6);
        foreach (var type in types)
        {
            html.Line($"<a class=\"pf-api-type\" href=\"types/{type.Slug}.html\">{System.Web.HttpUtility.HtmlEncode(type.FullName)}</a>");
        }

        return html.ToString().TrimEnd();
    }

    private static string BuildSimpleTypeSummaryHtml(ApiTypeModel type)
    {
        if (type is null || string.IsNullOrWhiteSpace(type.Summary))
            return string.Empty;

        var html = new HtmlFragmentBuilder(initialIndent: 4);
        html.Line($"<p>{System.Web.HttpUtility.HtmlEncode(type.Summary)}</p>");
        return html.ToString().TrimEnd();
    }

    private static string BuildSimpleTypeRemarksHtml(ApiTypeModel type)
    {
        if (type is null || string.IsNullOrWhiteSpace(type.Remarks))
            return string.Empty;

        var html = new HtmlFragmentBuilder(initialIndent: 4);
        html.Line($"<div class=\"pf-api-remarks\">{System.Web.HttpUtility.HtmlEncode(type.Remarks)}</div>");
        return html.ToString().TrimEnd();
    }

    private static string BuildSimpleTypeMemberSections(ApiTypeModel type, string codeLanguage)
    {
        if (type is null)
            return string.Empty;

        var html = new HtmlFragmentBuilder(initialIndent: 4);
        AppendMembers(html, "Methods", type.Methods, codeLanguage);
        AppendMembers(html, "Properties", type.Properties, codeLanguage);
        AppendMembers(html, "Fields", type.Fields, codeLanguage);
        AppendMembers(html, "Events", type.Events, codeLanguage);
        return html.ToString().TrimEnd();
    }

    private static void AppendMembers(HtmlFragmentBuilder html, string label, List<ApiMemberModel> members, string codeLanguage)
    {
        if (html is null || members.Count == 0)
            return;

        html.Line($"<section class=\"pf-api-section\">");
        using (html.Indent())
        {
            html.Line($"<h2>{label}</h2>");
            html.Line("<ul>");
            using (html.Indent())
            {
                foreach (var member in members)
                {
                    var summaryText = StripCrefTokens(member.Summary);
                    var summary = string.IsNullOrWhiteSpace(summaryText)
                        ? string.Empty
                        : $" - {System.Web.HttpUtility.HtmlEncode(summaryText)}";
                    var signature = !string.IsNullOrWhiteSpace(member.Signature)
                        ? member.Signature
                        : BuildSignature(member, label);

                    html.Line("<li>");
                    using (html.Indent())
                    {
                        html.Line($"<strong>{System.Web.HttpUtility.HtmlEncode(signature)}</strong>{summary}");
                        if (member.Parameters.Count > 0)
                        {
                            html.Line("<div class=\"pf-api-params\">");
                            using (html.Indent())
                            {
                                html.Line("<ul>");
                                using (html.Indent())
                                {
                                    foreach (var param in member.Parameters)
                                    {
                                        var type = string.IsNullOrWhiteSpace(param.Type) ? string.Empty : $" ({System.Web.HttpUtility.HtmlEncode(param.Type)})";
                                        var psummaryText = StripCrefTokens(param.Summary);
                                        var psummary = string.IsNullOrWhiteSpace(psummaryText) ? string.Empty : $": {System.Web.HttpUtility.HtmlEncode(psummaryText)}";
                                        html.Line($"<li><code>{System.Web.HttpUtility.HtmlEncode(param.Name)}</code>{type}{psummary}</li>");
                                    }
                                }
                                html.Line("</ul>");
                            }
                            html.Line("</div>");
                        }
                        if (!string.IsNullOrWhiteSpace(member.Returns))
                        {
                            var returnsText = StripCrefTokens(member.Returns);
                            html.Line($"<div class=\"pf-api-returns\">Returns: {System.Web.HttpUtility.HtmlEncode(returnsText)}</div>");
                        }
                    }
                    html.Line("</li>");
                }
            }
            html.Line("</ul>");
        }
        html.Line("</section>");
    }

    private static readonly string[] MainTypeOrder =
    {
        "QR",
        "Barcode",
        "QrEasy",
        "BarcodeEasy",
        "QrImageDecoder",
        "DataMatrixCode",
        "Pdf417Code",
        "AztecCode"
    };

    private static string BuildDocsSidebar(
        WebApiDocsOptions options,
        IReadOnlyList<ApiTypeModel> types,
        string baseUrl,
        string activeSlug,
        string docsHomeUrl,
        IReadOnlyDictionary<string, string> typeDisplayNames,
        ApiSuiteContext? suite)
    {
        var indexUrl = EnsureTrailingSlash(baseUrl);
        var html = new HtmlFragmentBuilder(initialIndent: 4);
        var primaryKindPluralLabel = ResolvePrimaryKindPluralLabel(types);
        var primaryKindFilterLabel = ResolvePrimaryKindFilterLabel(types);
        var totalTypes = types.Count;
        var kindFilters = BuildKindFilters(types);
        var namespaceGroups = types
            .GroupBy(static t => string.IsNullOrWhiteSpace(t.Namespace) ? "(global)" : t.Namespace)
            .OrderBy(static g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var mainTypes = GetMainTypes(types, options);
        var mainTypeNames = new HashSet<string>(mainTypes.Select(static t => t.Name), StringComparer.OrdinalIgnoreCase);

        html.Line("<div class=\"ev-docs-menu api-sidebar-shell\">");
        using (html.Indent())
        {
            html.Line("<div class=\"sidebar-project-indicator ev-docs-project-indicator\">");
            using (html.Indent())
            {
                html.Line($"<a href=\"{docsHomeUrl}\" class=\"back-link sidebar-back-link ev-docs-project-back\">");
                using (html.Indent())
                {
                    html.Line("<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" width=\"14\" height=\"14\">");
                    using (html.Indent())
                    {
                        html.Line("<path d=\"M19 12H5M12 19l-7-7 7-7\"/>");
                    }
                    html.Line("</svg>");
                    html.Line("Back to project");
                }
                html.Line("</a>");
            }
            html.Line("</div>");
            html.Line("<div class=\"sidebar-header\">");
            using (html.Indent())
            {
                // Keep API reference chrome consistent between index and type pages.
                html.Line($"<a href=\"{indexUrl}\" class=\"sidebar-title sidebar-menu-title ev-docs-menu-title active\">");
                using (html.Indent())
                {
                    html.Line("<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" width=\"18\" height=\"18\">");
                    using (html.Indent())
                    {
                        html.Line("<path d=\"M4 19.5A2.5 2.5 0 0 1 6.5 17H20\"/>");
                        html.Line("<path d=\"M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2z\"/>");
                    }
                    html.Line("</svg>");
                    html.Line("<span>API Reference</span>");
                }
                html.Line("</a>");
            }
            html.Line("</div>");
            AppendApiSuiteSidebar(html, suite, indexUrl);
            html.Line("<div class=\"sidebar-search\">");
            using (html.Indent())
            {
                html.Line("<svg viewBox=\"0 0 24 24\" width=\"16\" height=\"16\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">");
                using (html.Indent())
                {
                    html.Line("<circle cx=\"11\" cy=\"11\" r=\"8\"/>");
                    html.Line("<path d=\"M21 21l-4.35-4.35\"/>");
                }
                html.Line("</svg>");
                html.Line($"<input id=\"api-filter\" type=\"text\" placeholder=\"Filter {primaryKindPluralLabel} ({totalTypes})...\" />");
                html.Line("<button class=\"clear-search\" type=\"button\" aria-label=\"Clear search\">");
                using (html.Indent())
                {
                    html.Line("<svg viewBox=\"0 0 24 24\" width=\"16\" height=\"16\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">");
                    using (html.Indent())
                    {
                        html.Line("<path d=\"M18 6L6 18M6 6l12 12\"/>");
                    }
                    html.Line("</svg>");
                }
                html.Line("</button>");
            }
            html.Line("</div>");
            if (kindFilters.Count > 0)
            {
                html.Line("<div class=\"sidebar-filters\">");
                using (html.Indent())
                {
                    html.Line($"<div class=\"filter-label\">{System.Web.HttpUtility.HtmlEncode(primaryKindFilterLabel)} filters</div>");
                    html.Line("<div class=\"filter-buttons\">");
                    using (html.Indent())
                    {
                        html.Line("<button class=\"filter-button active\" type=\"button\" data-kind=\"\">All</button>");
                        foreach (var kind in kindFilters)
                        {
                            html.Line($"<button class=\"filter-button\" type=\"button\" data-kind=\"{kind.Kind}\">{GetKindLabel(kind.Kind, kind.Count)}</button>");
                        }
                    }
                    html.Line("</div>");
                    if (namespaceGroups.Count > 0)
                    {
                        html.Line("<div class=\"filter-row\">");
                        using (html.Indent())
                        {
                            html.Line("<label for=\"api-namespace\" class=\"filter-label\">Namespace</label>");
                            html.Line("<select id=\"api-namespace\" class=\"namespace-select\">");
                            using (html.Indent())
                            {
                                html.Line("<option value=\"\">All namespaces</option>");
                                foreach (var group in namespaceGroups)
                                {
                                    var encoded = System.Web.HttpUtility.HtmlEncode(group.Key);
                                    html.Line($"<option value=\"{encoded}\">{encoded} ({group.Count()})</option>");
                                }
                            }
                            html.Line("</select>");
                        }
                        html.Line("</div>");
                    }
                    html.Line("<div class=\"filter-row\">");
                    using (html.Indent())
                    {
                        html.Line("<button class=\"sidebar-reset\" type=\"button\">Reset filters</button>");
                    }
                    html.Line("</div>");
                }
                html.Line("</div>");
            }
            html.Line($"<div class=\"sidebar-count\" data-total=\"{totalTypes}\">Showing {totalTypes} of {totalTypes} {primaryKindPluralLabel}</div>");
            html.Line("<div class=\"sidebar-tools\">");
            using (html.Indent())
            {
                html.Line("<button class=\"sidebar-expand-all\" type=\"button\">Expand all</button>");
                html.Line("<button class=\"sidebar-collapse-all\" type=\"button\">Collapse all</button>");
            }
            html.Line("</div>");
            html.Line("<nav class=\"sidebar-nav\">");
            using (html.Indent())
            {
                if (mainTypes.Count > 0)
                {
                    html.Line("<div class=\"nav-section\">");
                    using (html.Indent())
                    {
                        html.Line("<div class=\"nav-section-header main-api\">");
                        using (html.Indent())
                        {
                            html.Line("<svg class=\"chevron expanded\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">");
                            using (html.Indent())
                            {
                                html.Line("<path d=\"M9 18l6-6-6-6\"/>");
                            }
                            html.Line("</svg>");
                            html.Line("<span>Main API</span>");
                            html.Line($"<span class=\"type-count\">{mainTypes.Count}</span>");
                        }
                        html.Line("</div>");
                        html.Line("<div class=\"nav-section-content\">");
                        using (html.Indent())
                        {
                            foreach (var type in mainTypes)
                            {
                                html.Line(BuildSidebarTypeItem(type, baseUrl, activeSlug, typeDisplayNames));
                            }
                        }
                        html.Line("</div>");
                    }
                    html.Line("</div>");
                }
                var grouped = types
                    .Where(t => !mainTypeNames.Contains(t.Name))
                    .GroupBy(t => string.IsNullOrWhiteSpace(t.Namespace) ? "(global)" : t.Namespace)
                    .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);
                foreach (var group in grouped)
                {
                    html.Line("<div class=\"nav-section\">");
                    using (html.Indent())
                    {
                        html.Line("<div class=\"nav-section-header\">");
                        using (html.Indent())
                        {
                            html.Line("<svg class=\"chevron expanded\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">");
                            using (html.Indent())
                            {
                                html.Line("<path d=\"M9 18l6-6-6-6\"/>");
                            }
                            html.Line("</svg>");
                            html.Line($"<span>{System.Web.HttpUtility.HtmlEncode(GetShortNamespace(group.Key))}</span>");
                            html.Line($"<span class=\"type-count\">{group.Count()}</span>");
                        }
                        html.Line("</div>");
                        html.Line("<div class=\"nav-section-content\">");
                        using (html.Indent())
                        {
                            foreach (var type in group.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
                            {
                                html.Line(BuildSidebarTypeItem(type, baseUrl, activeSlug, typeDisplayNames));
                            }
                        }
                        html.Line("</div>");
                    }
                    html.Line("</div>");
                }
            }
            html.Line("</nav>");
            html.Line($"<div class=\"sidebar-empty\" hidden>No matching {primaryKindPluralLabel}.</div>");
        }
        html.Line("</div>");
        return html.ToString().TrimEnd();
    }

    private static string BuildSidebarTypeItem(
        ApiTypeModel type,
        string baseUrl,
        string activeSlug,
        IReadOnlyDictionary<string, string> typeDisplayNames)
    {
        var active = string.Equals(activeSlug, type.Slug, StringComparison.OrdinalIgnoreCase) ? " active" : string.Empty;
        var displayName = ResolveTypeDisplayName(type, typeDisplayNames);
        var search = BuildTypeSearchText(type, displayName);
        var searchAttr = System.Web.HttpUtility.HtmlEncode(search);
        var name = System.Web.HttpUtility.HtmlEncode(displayName);
        var kind = NormalizeKind(type.Kind);
        var icon = RenderApiGlyphSpan($"type-icon {kind}", "type-icon-glyph", GetTypeIcon(type.Kind));
        var ns = System.Web.HttpUtility.HtmlEncode(string.IsNullOrWhiteSpace(type.Namespace) ? "(global)" : type.Namespace);
        var href = BuildDocsTypeUrl(baseUrl, type.Slug);
        var aliasAttr = BuildAliasTitleAttribute(type);
        var freshnessBadge = BuildFreshnessBadgeHtml(type.Freshness, "type-list-freshness");
        return $"<a href=\"{href}\" class=\"type-item{active}\" data-search=\"{searchAttr}\" data-kind=\"{kind}\" data-namespace=\"{ns}\"{aliasAttr}>" +
               $"{icon}<span class=\"type-name\">{name}</span>{freshnessBadge}</a>";
    }

    private static string BuildDocsOverview(
        WebApiDocsOptions options,
        IReadOnlyList<ApiTypeModel> types,
        string baseUrl,
        IReadOnlyDictionary<string, string> typeDisplayNames,
        ApiSuiteContext? suite)
    {
        var html = new HtmlFragmentBuilder(initialIndent: 4);
        var overviewTitle = string.IsNullOrWhiteSpace(options.Title) ? "API Reference" : options.Title.Trim();
        var namespaceGroups = types
            .GroupBy(static t => string.IsNullOrWhiteSpace(t.Namespace) ? "(global)" : t.Namespace)
            .OrderBy(static g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var primaryKindPluralLabel = ResolvePrimaryKindPluralLabel(types);

        html.Line("<div class=\"api-overview ev-page-body\">");
        using (html.Indent())
        {
            html.Line("<header class=\"ev-docs-header api-overview-header\">");
            using (html.Indent())
            {
                html.Line("<p class=\"ev-eyebrow\">API Reference</p>");
                html.Line($"<h1>{System.Web.HttpUtility.HtmlEncode(overviewTitle)}</h1>");
                html.Line("<p class=\"lead\">Complete API documentation auto-generated from source documentation.</p>");
            }
            html.Line("</header>");
            html.Line("<div class=\"api-overview-main api-overview-main--full\">");
            using (html.Indent())
            {
                AppendApiSuiteOverview(html, suite, EnsureTrailingSlash(baseUrl));

                var mainTypes = GetMainTypes(types, options);
                if (mainTypes.Count > 0)
                {
                    html.Line("<section class=\"quick-start\">");
                    using (html.Indent())
                    {
                        html.Line("<h2>Quick Start</h2>");
                        html.Line("<p class=\"section-desc\">Frequently used types and entry points.</p>");
                        html.Line("<div class=\"quick-grid\">");
                        using (html.Indent())
                        {
                            foreach (var type in mainTypes.Take(6))
                            {
                                var summary = Truncate(StripCrefTokens(type.Summary), 100);
                                var displayName = ResolveTypeDisplayName(type, typeDisplayNames);
                                var quickHref = BuildDocsTypeUrl(baseUrl, type.Slug);
                                var quickIcon = RenderApiGlyphSpan($"type-icon large {NormalizeKind(type.Kind)}", "type-icon-glyph", GetTypeIcon(type.Kind));
                                html.Line($"<a href=\"{quickHref}\" class=\"quick-card\">");
                                using (html.Indent())
                                {
                                    html.Line("<div class=\"quick-card-header\">");
                                    using (html.Indent())
                                    {
                                        html.Line(quickIcon);
                                        html.Line($"<strong>{System.Web.HttpUtility.HtmlEncode(displayName)}</strong>");
                                        var freshnessBadge = BuildFreshnessBadgeHtml(type.Freshness, "quick-card-freshness");
                                        if (!string.IsNullOrWhiteSpace(freshnessBadge))
                                            html.Line(freshnessBadge);
                                    }
                                    html.Line("</div>");
                                    AppendAliasInlineMeta(html, type, "quick-card-meta", "quick-card-aliases");
                                    if (!string.IsNullOrWhiteSpace(summary))
                                        html.Line($"<p>{System.Web.HttpUtility.HtmlEncode(summary)}</p>");
                                }
                                html.Line("</a>");
                            }
                        }
                        html.Line("</div>");
                    }
                    html.Line("</section>");
                }

                html.Line("<section class=\"all-namespaces\">");
                using (html.Indent())
                {
                    html.Line("<h2>All Namespaces</h2>");
                    html.Line($"<p class=\"section-desc\">Browse all {types.Count} {primaryKindPluralLabel} organized by namespace.</p>");
                    foreach (var group in namespaceGroups)
                    {
                        AppendOverviewNamespaceGroup(html, group, baseUrl, typeDisplayNames);
                    }
                }
                html.Line("</section>");
            }
            html.Line("</div>");
        }
        html.Line("</div>");
        return html.ToString().TrimEnd();
    }

    private static void AppendOverviewNamespaceGroup(
        HtmlFragmentBuilder html,
        IGrouping<string, ApiTypeModel> group,
        string baseUrl,
        IReadOnlyDictionary<string, string> typeDisplayNames)
    {
        if (html is null)
            return;

        const int visibleLimit = 24;
        var namespaceName = group.Key;
        var anchor = BuildNamespaceAnchorId(namespaceName);
        var ordered = group.OrderBy(static t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var total = ordered.Count;
        var hasOverflow = total > visibleLimit;
        var namespaceLabel = System.Web.HttpUtility.HtmlEncode(namespaceName);

        html.Line($"<div class=\"namespace-group\" id=\"{anchor}\" data-overview-group>");
        using (html.Indent())
        {
            html.Line("<div class=\"namespace-group-header\">");
            using (html.Indent())
            {
                html.Line("<div class=\"namespace-group-heading\">");
                using (html.Indent())
                {
                    html.Line($"<h3>{namespaceLabel} <span class=\"count\">({total.ToString("N0", CultureInfo.InvariantCulture)})</span></h3>");
                }
                html.Line("</div>");
            }
            html.Line("</div>");

            html.Line("<div class=\"type-chips\" data-overview-group-list>");
            using (html.Indent())
            {
                for (var index = 0; index < ordered.Count; index++)
                {
                    var type = ordered[index];
                    var displayName = ResolveTypeDisplayName(type, typeDisplayNames);
                    var search = BuildTypeSearchText(type, displayName);
                    var searchAttr = System.Web.HttpUtility.HtmlEncode(search);
                    var kind = NormalizeKind(type.Kind);
                    var nsValue = System.Web.HttpUtility.HtmlEncode(string.IsNullOrWhiteSpace(type.Namespace) ? "(global)" : type.Namespace);
                    var chipHref = BuildDocsTypeUrl(baseUrl, type.Slug);
                    var chipIcon = RenderApiGlyphSpan("chip-icon", "chip-icon-glyph", GetTypeIcon(type.Kind));
                    var aliasAttr = BuildAliasTitleAttribute(type);
                    var overflowAttr = hasOverflow && index >= visibleLimit ? " data-overview-extra hidden" : string.Empty;
                    var freshnessBadge = BuildFreshnessBadgeHtml(type.Freshness, "type-chip-freshness");
                    html.Line($"<a href=\"{chipHref}\" class=\"type-chip {kind}\" data-search=\"{searchAttr}\" data-kind=\"{kind}\" data-namespace=\"{nsValue}\"{aliasAttr}{overflowAttr}>");
                    using (html.Indent())
                    {
                        html.Line(chipIcon);
                        html.Line($"<span class=\"chip-name\">{System.Web.HttpUtility.HtmlEncode(displayName)}</span>");
                        if (!string.IsNullOrWhiteSpace(freshnessBadge))
                            html.Line(freshnessBadge);
                        AppendAliasInlineMeta(html, type, "type-chip-meta", "type-chip-aliases");
                    }
                    html.Line("</a>");
                }
            }
            html.Line("</div>");

            if (hasOverflow)
            {
                var expandLabel = $"Show all {total.ToString("N0", CultureInfo.InvariantCulture)} entries";
                html.Line("<div class=\"namespace-group-actions\">");
                using (html.Indent())
                {
                    html.Line($"<button class=\"overview-group-toggle\" type=\"button\" data-overview-group-toggle data-expand-label=\"{System.Web.HttpUtility.HtmlAttributeEncode(expandLabel)}\" data-collapse-label=\"Show fewer\" aria-expanded=\"false\">{System.Web.HttpUtility.HtmlEncode(expandLabel)}</button>");
                }
                html.Line("</div>");
            }
        }
        html.Line("</div>");
    }

    private static string BuildNamespaceAnchorId(string namespaceName)
    {
        var normalized = string.IsNullOrWhiteSpace(namespaceName) ? "global" : namespaceName.Trim().ToLowerInvariant();
        normalized = normalized.Replace("(global)", "global", StringComparison.OrdinalIgnoreCase);
        normalized = SlugDashRegex.Replace(SlugNonAlnumRegex.Replace(normalized, "-"), "-").Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? "namespace-global" : "namespace-" + normalized;
    }

    private static string RenderApiGlyphSpan(string containerClass, string glyphClass, string glyph)
    {
        var safeContainer = System.Web.HttpUtility.HtmlEncode(containerClass ?? string.Empty);
        var safeGlyphClass = System.Web.HttpUtility.HtmlEncode(glyphClass ?? string.Empty);
        var safeGlyph = System.Web.HttpUtility.HtmlEncode(glyph ?? string.Empty);
        return $"<span class=\"{safeContainer}\"><span class=\"{safeGlyphClass}\">{safeGlyph}</span></span>";
    }

    private static string BuildTypeSearchText(ApiTypeModel type, string displayName)
    {
        var summary = StripCrefTokens(type.Summary);
        var aliasText = type.Aliases.Count == 0
            ? string.Empty
            : string.Join(' ', type.Aliases
                .Where(static alias => !string.IsNullOrWhiteSpace(alias))
                .Select(static alias => alias.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase));
        return $"{displayName} {type.Name} {type.FullName} {aliasText} {summary}".Trim();
    }

    private static string BuildAliasTitleAttribute(ApiTypeModel type)
    {
        var aliases = type.Aliases
            .Where(static alias => !string.IsNullOrWhiteSpace(alias))
            .Select(static alias => alias.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (aliases.Length == 0)
            return string.Empty;

        var title = System.Web.HttpUtility.HtmlAttributeEncode($"Aliases: {string.Join(", ", aliases)}");
        return $" title=\"{title}\"";
    }

    private static string ResolvePrimaryKindLabel(IReadOnlyList<ApiTypeModel> types)
    {
        if (types is null || types.Count == 0)
            return "Type";

        var distinctKinds = types
            .Select(static type => NormalizeKind(type.Kind))
            .Where(static kind => !string.IsNullOrWhiteSpace(kind))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (distinctKinds.Length != 1)
            return "Type";

        return distinctKinds[0].Equals("function", StringComparison.OrdinalIgnoreCase)
            ? "Function"
            : GetKindLabel(distinctKinds[0], 0).Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0];
    }

    private static string ResolvePrimaryKindFilterLabel(IReadOnlyList<ApiTypeModel> types)
    {
        if (types is null || types.Count == 0)
            return "Type";

        var distinctKinds = types
            .Select(static type => NormalizeKind(type.Kind))
            .Where(static kind => !string.IsNullOrWhiteSpace(kind))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (distinctKinds.Length != 1)
            return "Type";

        return distinctKinds[0].ToLowerInvariant() switch
        {
            "class" => "Class",
            "struct" => "Struct",
            "interface" => "Interface",
            "enum" => "Enum",
            "delegate" => "Delegate",
            "cmdlet" => "Cmdlet",
            "function" => "Function",
            "alias" => "Alias",
            "about" => "About",
            "command" => "Command",
            _ => "Type"
        };
    }

    private static string ResolvePrimaryKindPluralLabel(IReadOnlyList<ApiTypeModel> types)
    {
        if (types is null || types.Count == 0)
            return "types";

        var distinctKinds = types
            .Select(static type => NormalizeKind(type.Kind))
            .Where(static kind => !string.IsNullOrWhiteSpace(kind))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (distinctKinds.Length != 1)
            return "types";

        return distinctKinds[0].ToLowerInvariant() switch
        {
            "class" => "classes",
            "struct" => "structs",
            "interface" => "interfaces",
            "enum" => "enums",
            "delegate" => "delegates",
            "cmdlet" => "cmdlets",
            "function" => "functions",
            "alias" => "aliases",
            "about" => "about topics",
            "command" => "commands",
            _ => "types"
        };
    }

    private static string BuildFreshnessBadgeHtml(ApiFreshnessModel? freshness, string cssClass)
    {
        if (freshness is null)
            return string.Empty;

        var status = (freshness.Status ?? string.Empty).Trim().ToLowerInvariant();
        if (!status.Equals("new", StringComparison.Ordinal) &&
            !status.Equals("updated", StringComparison.Ordinal))
            return string.Empty;

        var label = status.Equals("new", StringComparison.Ordinal) ? "New" : "Updated";
        var title = System.Web.HttpUtility.HtmlAttributeEncode(RenderFreshnessText(freshness));
        return $"<span class=\"freshness-badge {System.Web.HttpUtility.HtmlEncode(status)} {System.Web.HttpUtility.HtmlEncode(cssClass)}\" title=\"{title}\">{System.Web.HttpUtility.HtmlEncode(label)}</span>";
    }

}
