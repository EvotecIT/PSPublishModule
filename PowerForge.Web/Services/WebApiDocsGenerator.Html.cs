using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Diagnostics;
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
    private static void GenerateHtml(string outputPath, WebApiDocsOptions options, IReadOnlyList<ApiTypeModel> types, List<string> warnings)
    {
        var template = (options.Template ?? string.Empty).Trim().ToLowerInvariant();
        if (template is "docs" or "sidebar")
        {
            GenerateDocsHtml(outputPath, options, types, warnings);
            return;
        }

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
        var typeLinks = new StringBuilder();
        foreach (var type in types)
        {
            typeLinks.AppendLine($"      <a class=\"pf-api-type\" href=\"types/{type.Slug}.html\">{System.Web.HttpUtility.HtmlEncode(type.FullName)}</a>");
        }
        var searchScript = JoinHtmlFragments(
            WrapScript(LoadAsset(options, "search.js", options.SearchScriptPath)),
            prismScripts);
        var indexHtml = ApplyTemplate(indexTemplate, new Dictionary<string, string?>
        {
            ["TITLE"] = System.Web.HttpUtility.HtmlEncode(options.Title),
            ["DESCRIPTION_META"] = BuildDescriptionMetaTag($"API reference for {options.Title}."),
            ["OPEN_GRAPH_META"] = BuildApiOpenGraphMetaTags(options, social, options.Title, $"API reference for {options.Title}.", indexRoute),
            ["CRITICAL_CSS"] = criticalCss,
            ["CSS"] = cssBlock,
            ["HEADER"] = header,
            ["FOOTER"] = footer,
            ["BODY_CLASS"] = bodyClass,
            ["TYPE_COUNT"] = types.Count.ToString(),
            ["TYPE_LINKS"] = typeLinks.ToString().TrimEnd(),
            ["SEARCH_SCRIPT"] = searchScript
        });

        File.WriteAllText(Path.Combine(outputPath, "index.html"), indexHtml.ToString(), Encoding.UTF8);

        var typesDir = Path.Combine(outputPath, "types");
        Directory.CreateDirectory(typesDir);
        foreach (var type in types)
        {
            var memberHtml = new StringBuilder();
            AppendMembers(memberHtml, "Methods", type.Methods, codeLanguage);
            AppendMembers(memberHtml, "Properties", type.Properties, codeLanguage);
            AppendMembers(memberHtml, "Fields", type.Fields, codeLanguage);
            AppendMembers(memberHtml, "Events", type.Events, codeLanguage);

            var summaryHtml = string.IsNullOrWhiteSpace(type.Summary)
                ? string.Empty
                : $"    <p>{System.Web.HttpUtility.HtmlEncode(type.Summary)}</p>";
            var remarksHtml = string.IsNullOrWhiteSpace(type.Remarks)
                ? string.Empty
                : $"    <div class=\"pf-api-remarks\">{System.Web.HttpUtility.HtmlEncode(type.Remarks)}</div>";

            var typeTitle = $"{type.FullName} - {options.Title}";
            var typeTemplate = LoadTemplate(options, "type.html", options.TypeTemplatePath);
            var typeRoute = $"{NormalizeApiRoute(options.BaseUrl).TrimEnd('/')}/types/{type.Slug}.html";
            var typeHtml = ApplyTemplate(typeTemplate, new Dictionary<string, string?>
            {
                ["TYPE_TITLE"] = System.Web.HttpUtility.HtmlEncode(typeTitle),
                ["TYPE_FULLNAME"] = System.Web.HttpUtility.HtmlEncode(type.FullName),
                ["DESCRIPTION_META"] = BuildDescriptionMetaTag($"API reference for {type.FullName} in {options.Title}."),
                ["OPEN_GRAPH_META"] = BuildApiOpenGraphMetaTags(options, social, typeTitle, $"API reference for {type.FullName} in {options.Title}.", typeRoute),
                ["CRITICAL_CSS"] = criticalCss,
                ["CSS"] = cssBlock,
                ["HEADER"] = header,
                ["FOOTER"] = footer,
                ["BODY_CLASS"] = bodyClass,
                ["TYPE_SUMMARY"] = summaryHtml,
                ["TYPE_REMARKS"] = remarksHtml,
                ["MEMBERS"] = memberHtml.ToString().TrimEnd(),
                ["TYPE_SCRIPT"] = prismScripts
            });

            File.WriteAllText(Path.Combine(typesDir, $"{type.Slug}.html"), typeHtml, Encoding.UTF8);
        }

        var sitemapPath = Path.Combine(outputPath, "sitemap.xml");
        GenerateApiSitemap(sitemapPath, options.BaseUrl, types);
    }

    private static void GenerateDocsHtml(string outputPath, WebApiDocsOptions options, IReadOnlyList<ApiTypeModel> types, List<string> warnings)
    {
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
        var docsScript = JoinHtmlFragments(
            WrapScript(LoadAsset(options, "docs.js", options.DocsScriptPath)),
            prismScripts);
        var docsHomeUrl = NormalizeDocsHomeUrl(options.DocsHomeUrl, baseUrl);
        var legacyAliasMode = ResolveLegacyAliasMode(options.LegacyAliasMode);
        var social = ResolveApiSocialProfile(options);
        var typeDisplayNames = BuildTypeDisplayNameMap(types, options, warnings);
        var sidebarHtml = BuildDocsSidebar(options, types, baseUrl, string.Empty, docsHomeUrl, typeDisplayNames);
        var sidebarClass = BuildSidebarClass(options.SidebarPosition);
        var overviewHtml = BuildDocsOverview(options, types, baseUrl, typeDisplayNames);
        var slugMap = BuildTypeSlugMap(types);
        var typeIndex = BuildTypeIndex(types);
        var derivedMap = BuildDerivedTypeMap(types, typeIndex);

        var indexTemplate = LoadTemplate(options, "docs-index.html", options.DocsIndexTemplatePath);
        var indexHtml = ApplyTemplate(indexTemplate, new Dictionary<string, string?>
        {
            ["TITLE"] = System.Web.HttpUtility.HtmlEncode(options.Title),
            ["DESCRIPTION_META"] = BuildDescriptionMetaTag($"API reference for {options.Title}."),
            ["OPEN_GRAPH_META"] = BuildApiOpenGraphMetaTags(options, social, options.Title, $"API reference for {options.Title}.", NormalizeApiRoute(baseUrl)),
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
            var sidebar = BuildDocsSidebar(options, types, baseUrl, type.Slug, docsHomeUrl, typeDisplayNames);
            var sidebarClassForType = BuildSidebarClass(options.SidebarPosition);
            var displayName = ResolveTypeDisplayName(type, typeDisplayNames);
            var typeMain = BuildDocsTypeDetail(type, baseUrl, slugMap, typeIndex, derivedMap, codeLanguage, displayName);
            var typeTemplate = LoadTemplate(options, "docs-type.html", options.DocsTypeTemplatePath);
            var pageTitle = $"{displayName} - {options.Title}";
            var typeRoute = $"{NormalizeApiRoute(baseUrl).TrimEnd('/')}/{type.Slug}/";
            var typeHtml = ApplyTemplate(typeTemplate, new Dictionary<string, string?>
            {
                ["TITLE"] = System.Web.HttpUtility.HtmlEncode(pageTitle),
                ["DESCRIPTION_META"] = BuildDescriptionMetaTag($"API reference for {displayName} in {options.Title}."),
                ["OPEN_GRAPH_META"] = BuildApiOpenGraphMetaTags(options, social, pageTitle, $"API reference for {displayName} in {options.Title}.", typeRoute),
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

    private static void ValidateCssContract(string outputPath, WebApiDocsOptions options, List<string> warnings)
    {
        if (options is null || warnings is null) return;
        if (string.IsNullOrWhiteSpace(options.CssHref)) return;

        var template = (options.Template ?? string.Empty).Trim().ToLowerInvariant();
        var required = template is "docs" or "sidebar" ? RequiredSelectorsDocs : RequiredSelectorsSimple;
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

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(url))
            sb.AppendLine($"<link rel=\"canonical\" href=\"{System.Web.HttpUtility.HtmlEncode(url)}\" />");
        sb.AppendLine("<!-- Open Graph -->");
        sb.AppendLine($"<meta property=\"og:title\" content=\"{System.Web.HttpUtility.HtmlEncode(title)}\" />");
        if (!string.IsNullOrWhiteSpace(desc))
            sb.AppendLine($"<meta property=\"og:description\" content=\"{System.Web.HttpUtility.HtmlEncode(desc)}\" />");
        sb.AppendLine("<meta property=\"og:type\" content=\"website\" />");
        if (!string.IsNullOrWhiteSpace(url))
            sb.AppendLine($"<meta property=\"og:url\" content=\"{System.Web.HttpUtility.HtmlEncode(url)}\" />");
        if (!string.IsNullOrWhiteSpace(image))
            sb.AppendLine($"<meta property=\"og:image\" content=\"{System.Web.HttpUtility.HtmlEncode(image)}\" />");
        if (!string.IsNullOrWhiteSpace(image) && !string.IsNullOrWhiteSpace(imageAlt))
            sb.AppendLine($"<meta property=\"og:image:alt\" content=\"{System.Web.HttpUtility.HtmlEncode(imageAlt)}\" />");
        if (imageWidth > 0)
            sb.AppendLine($"<meta property=\"og:image:width\" content=\"{imageWidth}\" />");
        if (imageHeight > 0)
            sb.AppendLine($"<meta property=\"og:image:height\" content=\"{imageHeight}\" />");
        if (!string.IsNullOrWhiteSpace(siteName))
            sb.AppendLine($"<meta property=\"og:site_name\" content=\"{System.Web.HttpUtility.HtmlEncode(siteName)}\" />");

        sb.AppendLine();
        sb.AppendLine("<!-- Twitter Card -->");
        sb.AppendLine($"<meta name=\"twitter:card\" content=\"{System.Web.HttpUtility.HtmlEncode(twitterCard)}\" />");
        sb.AppendLine($"<meta name=\"twitter:title\" content=\"{System.Web.HttpUtility.HtmlEncode(title)}\" />");
        if (!string.IsNullOrWhiteSpace(twitterSite))
            sb.AppendLine($"<meta name=\"twitter:site\" content=\"{System.Web.HttpUtility.HtmlEncode(twitterSite)}\" />");
        if (!string.IsNullOrWhiteSpace(twitterCreator))
            sb.AppendLine($"<meta name=\"twitter:creator\" content=\"{System.Web.HttpUtility.HtmlEncode(twitterCreator)}\" />");
        if (!string.IsNullOrWhiteSpace(desc))
            sb.AppendLine($"<meta name=\"twitter:description\" content=\"{System.Web.HttpUtility.HtmlEncode(desc)}\" />");
        if (!string.IsNullOrWhiteSpace(url))
            sb.AppendLine($"<meta name=\"twitter:url\" content=\"{System.Web.HttpUtility.HtmlEncode(url)}\" />");
        if (!string.IsNullOrWhiteSpace(image))
            sb.AppendLine($"<meta name=\"twitter:image\" content=\"{System.Web.HttpUtility.HtmlEncode(image)}\" />");
        if (!string.IsNullOrWhiteSpace(image) && !string.IsNullOrWhiteSpace(imageAlt))
            sb.AppendLine($"<meta name=\"twitter:image:alt\" content=\"{System.Web.HttpUtility.HtmlEncode(imageAlt)}\" />");

        return sb.ToString().TrimEnd();
    }

    private static ApiSocialProfile ResolveApiSocialProfile(WebApiDocsOptions options)
    {
        var nav = LoadNavConfig(options);
        var siteName = FirstNonEmpty(options.SiteName, nav?.SiteName, options.Title);
        var siteBaseUrl = FirstNonEmpty(nav?.SiteBaseUrl);
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

        var sb = new StringBuilder();
        foreach (var href in hrefs)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.Append($"<link rel=\"stylesheet\" href=\"{href}\" />");
        }
        return sb.ToString();
    }

    private static string BuildCssBlockWithFallback(string fallbackCss, string cssLinks, string extraCssLinks = "")
    {
        var fallbackBlock = WrapStyle(fallbackCss);
        return JoinHtmlFragments(fallbackBlock, cssLinks, extraCssLinks);
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
            var scripts = JoinHtmlFragments(
                $"<script src=\"{core}\"></script>",
                $"<script src=\"{autoloader}\"></script>",
                BuildApiPrismInitScript(languagesPath));
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
        var scriptsLinks = JoinHtmlFragments(
            $"<script src=\"{cdn}/components/prism-core.min.js\"></script>",
            $"<script src=\"{cdn}/plugins/autoloader/prism-autoloader.min.js\"></script>",
            BuildApiPrismInitScript($"{cdn}/components/"));
        return (cssLinks, scriptsLinks);
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
            "var p=window.Prism;" +
            "if(!p){return;}" +
            "if(p.plugins&&p.plugins.autoloader){p.plugins.autoloader.languages_path='" + safePath + "';}" +
            "var run=function(){" +
            "var root=document.querySelector('.api-content')||document;" +
            "if(!root.querySelector('code[class*=\\\"language-\\\"]')){return;}" +
            "try{if(p.highlightAllUnder){p.highlightAllUnder(root);}else{p.highlightAll();}}" +
            "catch(e){if(window.console&&console.warn){console.warn('Prism highlighting failed.',e);}}" +
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

    private static void AppendMembers(StringBuilder sb, string label, List<ApiMemberModel> members, string codeLanguage)
    {
        if (members.Count == 0) return;
        sb.AppendLine($"    <section class=\"pf-api-section\">");
        sb.AppendLine($"      <h2>{label}</h2>");
        sb.AppendLine("      <ul>");
        foreach (var member in members)
        {
            var summaryText = StripCrefTokens(member.Summary);
            var summary = string.IsNullOrWhiteSpace(summaryText)
                ? string.Empty
                : $" - {System.Web.HttpUtility.HtmlEncode(summaryText)}";
            sb.AppendLine("        <li>");
            var signature = !string.IsNullOrWhiteSpace(member.Signature)
                ? member.Signature
                : BuildSignature(member, label);
            sb.AppendLine($"          <strong>{System.Web.HttpUtility.HtmlEncode(signature)}</strong>{summary}");
            if (member.Parameters.Count > 0)
            {
                sb.AppendLine("          <div class=\"pf-api-params\">");
                sb.AppendLine("            <ul>");
                foreach (var param in member.Parameters)
                {
                    var type = string.IsNullOrWhiteSpace(param.Type) ? string.Empty : $" ({System.Web.HttpUtility.HtmlEncode(param.Type)})";
                    var psummaryText = StripCrefTokens(param.Summary);
                    var psummary = string.IsNullOrWhiteSpace(psummaryText) ? string.Empty : $": {System.Web.HttpUtility.HtmlEncode(psummaryText)}";
                    sb.AppendLine($"              <li><code>{System.Web.HttpUtility.HtmlEncode(param.Name)}</code>{type}{psummary}</li>");
                }
                sb.AppendLine("            </ul>");
                sb.AppendLine("          </div>");
            }
            if (!string.IsNullOrWhiteSpace(member.Returns))
            {
                var returnsText = StripCrefTokens(member.Returns);
                sb.AppendLine($"          <div class=\"pf-api-returns\">Returns: {System.Web.HttpUtility.HtmlEncode(returnsText)}</div>");
            }
            sb.AppendLine("        </li>");
        }
        sb.AppendLine("      </ul>");
        sb.AppendLine("    </section>");
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
        IReadOnlyDictionary<string, string> typeDisplayNames)
    {
        var indexUrl = EnsureTrailingSlash(baseUrl);
        var sb = new StringBuilder();
        sb.AppendLine("    <div class=\"sidebar-header\">");
        // Keep API reference chrome consistent between index and type pages.
        var active = " active";
        sb.AppendLine($"      <a href=\"{indexUrl}\" class=\"sidebar-title{active}\">");
        sb.AppendLine("        <svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" width=\"18\" height=\"18\">");
        sb.AppendLine("          <path d=\"M4 19.5A2.5 2.5 0 0 1 6.5 17H20\"/>");
        sb.AppendLine("          <path d=\"M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2z\"/>");
        sb.AppendLine("        </svg>");
        sb.AppendLine("        <span>API Reference</span>");
        sb.AppendLine("      </a>");
        sb.AppendLine("    </div>");
          var totalTypes = types.Count;
          sb.AppendLine("    <div class=\"sidebar-search\">");
          sb.AppendLine("      <svg viewBox=\"0 0 24 24\" width=\"16\" height=\"16\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">");
          sb.AppendLine("        <circle cx=\"11\" cy=\"11\" r=\"8\"/>");
          sb.AppendLine("        <path d=\"M21 21l-4.35-4.35\"/>");
          sb.AppendLine("      </svg>");
          sb.AppendLine($"      <input id=\"api-filter\" type=\"text\" placeholder=\"Filter types ({totalTypes})...\" />");
          sb.AppendLine("      <button class=\"clear-search\" type=\"button\" aria-label=\"Clear search\">");
          sb.AppendLine("        <svg viewBox=\"0 0 24 24\" width=\"16\" height=\"16\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">");
          sb.AppendLine("          <path d=\"M18 6L6 18M6 6l12 12\"/>");
          sb.AppendLine("        </svg>");
          sb.AppendLine("      </button>");
          sb.AppendLine("    </div>");

          var kindFilters = BuildKindFilters(types);
          if (kindFilters.Count > 0)
          {
              sb.AppendLine("    <div class=\"sidebar-filters\">");
              sb.AppendLine("      <div class=\"filter-label\">Type filters</div>");
              sb.AppendLine("      <div class=\"filter-buttons\">");
              sb.AppendLine("        <button class=\"filter-button active\" type=\"button\" data-kind=\"\">All</button>");
              foreach (var kind in kindFilters)
              {
                  sb.AppendLine($"        <button class=\"filter-button\" type=\"button\" data-kind=\"{kind.Kind}\">{GetKindLabel(kind.Kind, kind.Count)}</button>");
              }
              sb.AppendLine("      </div>");
              var namespaceGroups = types
                  .GroupBy(t => string.IsNullOrWhiteSpace(t.Namespace) ? "(global)" : t.Namespace)
                  .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                  .ToList();
              if (namespaceGroups.Count > 0)
              {
                  sb.AppendLine("      <div class=\"filter-row\">");
                  sb.AppendLine("        <label for=\"api-namespace\" class=\"filter-label\">Namespace</label>");
                  sb.AppendLine("        <select id=\"api-namespace\" class=\"namespace-select\">");
                  sb.AppendLine("          <option value=\"\">All namespaces</option>");
                  foreach (var group in namespaceGroups)
                  {
                      var encoded = System.Web.HttpUtility.HtmlEncode(group.Key);
                      sb.AppendLine($"          <option value=\"{encoded}\">{encoded} ({group.Count()})</option>");
                  }
                  sb.AppendLine("        </select>");
                  sb.AppendLine("      </div>");
              }
              sb.AppendLine("      <div class=\"filter-row\">");
              sb.AppendLine("        <button class=\"sidebar-reset\" type=\"button\">Reset filters</button>");
              sb.AppendLine("      </div>");
              sb.AppendLine("    </div>");
          }
          sb.AppendLine($"    <div class=\"sidebar-count\" data-total=\"{totalTypes}\">Showing {totalTypes} of {totalTypes} types</div>");
          sb.AppendLine("    <div class=\"sidebar-tools\">");
          sb.AppendLine("      <button class=\"sidebar-expand-all\" type=\"button\">Expand all</button>");
          sb.AppendLine("      <button class=\"sidebar-collapse-all\" type=\"button\">Collapse all</button>");
          sb.AppendLine("    </div>");
          sb.AppendLine("    <nav class=\"sidebar-nav\">");

        var mainTypes = GetMainTypes(types, options);
        var mainTypeNames = new HashSet<string>(mainTypes.Select(static t => t.Name), StringComparer.OrdinalIgnoreCase);
        if (mainTypes.Count > 0)
        {
            sb.AppendLine("      <div class=\"nav-section\">");
            sb.AppendLine("        <div class=\"nav-section-header main-api\">");
            sb.AppendLine("          <svg class=\"chevron expanded\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">");
            sb.AppendLine("            <path d=\"M9 18l6-6-6-6\"/>");
            sb.AppendLine("          </svg>");
            sb.AppendLine("          <span>Main API</span>");
            sb.AppendLine($"          <span class=\"type-count\">{mainTypes.Count}</span>");
            sb.AppendLine("        </div>");
            sb.AppendLine("        <div class=\"nav-section-content\">");
            foreach (var type in mainTypes)
            {
                sb.AppendLine(BuildSidebarTypeItem(type, baseUrl, activeSlug, typeDisplayNames));
            }
            sb.AppendLine("        </div>");
            sb.AppendLine("      </div>");
        }

        var grouped = types
            .Where(t => !mainTypeNames.Contains(t.Name))
            .GroupBy(t => string.IsNullOrWhiteSpace(t.Namespace) ? "(global)" : t.Namespace)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var group in grouped)
        {
            sb.AppendLine("      <div class=\"nav-section\">");
            sb.AppendLine("        <div class=\"nav-section-header\">");
            sb.AppendLine("          <svg class=\"chevron expanded\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">");
            sb.AppendLine("            <path d=\"M9 18l6-6-6-6\"/>");
            sb.AppendLine("          </svg>");
            sb.AppendLine($"          <span>{System.Web.HttpUtility.HtmlEncode(GetShortNamespace(group.Key))}</span>");
            sb.AppendLine($"          <span class=\"type-count\">{group.Count()}</span>");
            sb.AppendLine("        </div>");
            sb.AppendLine("        <div class=\"nav-section-content\">");
            foreach (var type in group.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine(BuildSidebarTypeItem(type, baseUrl, activeSlug, typeDisplayNames));
            }
            sb.AppendLine("        </div>");
            sb.AppendLine("      </div>");
        }

          sb.AppendLine("    </nav>");
          sb.AppendLine("    <div class=\"sidebar-empty\" hidden>No matching types.</div>");
          sb.AppendLine("    <div class=\"sidebar-footer\">");
          sb.AppendLine($"      <a href=\"{docsHomeUrl}\" class=\"back-link\">");
        sb.AppendLine("        <svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" width=\"14\" height=\"14\">");
        sb.AppendLine("          <path d=\"M19 12H5M12 19l-7-7 7-7\"/>");
        sb.AppendLine("        </svg>");
        sb.AppendLine("        Back to Docs");
        sb.AppendLine("      </a>");
        sb.AppendLine("    </div>");
        return sb.ToString().TrimEnd();
    }

    private static string BuildSidebarTypeItem(
        ApiTypeModel type,
        string baseUrl,
        string activeSlug,
        IReadOnlyDictionary<string, string> typeDisplayNames)
    {
        var active = string.Equals(activeSlug, type.Slug, StringComparison.OrdinalIgnoreCase) ? " active" : string.Empty;
        var summary = StripCrefTokens(type.Summary);
        var displayName = ResolveTypeDisplayName(type, typeDisplayNames);
        var search = $"{displayName} {type.Name} {type.FullName} {summary}".Trim();
        var searchAttr = System.Web.HttpUtility.HtmlEncode(search);
        var name = System.Web.HttpUtility.HtmlEncode(displayName);
        var kind = NormalizeKind(type.Kind);
        var icon = RenderApiGlyphSpan($"type-icon {kind}", "type-icon-glyph", GetTypeIcon(type.Kind));
        var ns = System.Web.HttpUtility.HtmlEncode(string.IsNullOrWhiteSpace(type.Namespace) ? "(global)" : type.Namespace);
        var href = BuildDocsTypeUrl(baseUrl, type.Slug);
        return $"          <a href=\"{href}\" class=\"type-item{active}\" data-search=\"{searchAttr}\" data-kind=\"{kind}\" data-namespace=\"{ns}\">" +
               $"{icon}<span class=\"type-name\">{name}</span></a>";
    }

    private static string BuildDocsOverview(
        WebApiDocsOptions options,
        IReadOnlyList<ApiTypeModel> types,
        string baseUrl,
        IReadOnlyDictionary<string, string> typeDisplayNames)
    {
        var sb = new StringBuilder();
        var overviewTitle = string.IsNullOrWhiteSpace(options.Title) ? "API Reference" : options.Title.Trim();
        sb.AppendLine("    <div class=\"api-overview\">");
        sb.AppendLine($"      <h1>{System.Web.HttpUtility.HtmlEncode(overviewTitle)}</h1>");
        sb.AppendLine("      <p class=\"lead\">Complete API documentation auto-generated from source documentation.</p>");

        var mainTypes = GetMainTypes(types, options);
        if (mainTypes.Count > 0)
        {
            sb.AppendLine("      <section class=\"quick-start\">");
            sb.AppendLine("        <h2>Quick Start</h2>");
            sb.AppendLine("        <p class=\"section-desc\">Frequently used types and entry points.</p>");
            sb.AppendLine("        <div class=\"quick-grid\">");
            foreach (var type in mainTypes.Take(6))
            {
                var summary = Truncate(StripCrefTokens(type.Summary), 100);
                var displayName = ResolveTypeDisplayName(type, typeDisplayNames);
                var quickHref = BuildDocsTypeUrl(baseUrl, type.Slug);
                var quickIcon = RenderApiGlyphSpan($"type-icon large {NormalizeKind(type.Kind)}", "type-icon-glyph", GetTypeIcon(type.Kind));
                sb.AppendLine($"          <a href=\"{quickHref}\" class=\"quick-card\">");
                sb.AppendLine("            <div class=\"quick-card-header\">");
                sb.AppendLine($"              {quickIcon}");
                sb.AppendLine($"              <strong>{System.Web.HttpUtility.HtmlEncode(displayName)}</strong>");
                sb.AppendLine("            </div>");
                if (!string.IsNullOrWhiteSpace(summary))
                {
                    sb.AppendLine($"            <p>{System.Web.HttpUtility.HtmlEncode(summary)}</p>");
                }
                sb.AppendLine("          </a>");
            }
            sb.AppendLine("        </div>");
            sb.AppendLine("      </section>");
        }

        sb.AppendLine("      <section class=\"all-namespaces\">");
        sb.AppendLine("        <h2>All Namespaces</h2>");
        sb.AppendLine($"        <p class=\"section-desc\">Browse all {types.Count} types organized by namespace.</p>");
        foreach (var group in types.GroupBy(t => string.IsNullOrWhiteSpace(t.Namespace) ? "(global)" : t.Namespace)
                     .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine("        <div class=\"namespace-group\">");
            var nsLabel = System.Web.HttpUtility.HtmlEncode(group.Key);
            sb.AppendLine($"          <h3>{nsLabel} <span class=\"count\">({group.Count()})</span></h3>");
            sb.AppendLine("          <div class=\"type-chips\">");
            foreach (var type in group.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
            {
                var summary = StripCrefTokens(type.Summary);
                var displayName = ResolveTypeDisplayName(type, typeDisplayNames);
                var search = $"{displayName} {type.Name} {type.FullName} {summary}".Trim();
                var searchAttr = System.Web.HttpUtility.HtmlEncode(search);
                var kind = NormalizeKind(type.Kind);
                var nsValue = System.Web.HttpUtility.HtmlEncode(string.IsNullOrWhiteSpace(type.Namespace) ? "(global)" : type.Namespace);
                var chipHref = BuildDocsTypeUrl(baseUrl, type.Slug);
                var chipIcon = RenderApiGlyphSpan("chip-icon", "chip-icon-glyph", GetTypeIcon(type.Kind));
                sb.AppendLine($"            <a href=\"{chipHref}\" class=\"type-chip {kind}\" data-search=\"{searchAttr}\" data-kind=\"{kind}\" data-namespace=\"{nsValue}\">");
                sb.AppendLine($"              {chipIcon}");
                sb.AppendLine($"              <span class=\"chip-name\">{System.Web.HttpUtility.HtmlEncode(displayName)}</span>");
                sb.AppendLine("            </a>");
            }
            sb.AppendLine("          </div>");
            sb.AppendLine("        </div>");
        }
        sb.AppendLine("      </section>");
        sb.AppendLine("    </div>");
        return sb.ToString().TrimEnd();
    }

    private static string RenderApiGlyphSpan(string containerClass, string glyphClass, string glyph)
    {
        var safeContainer = System.Web.HttpUtility.HtmlEncode(containerClass ?? string.Empty);
        var safeGlyphClass = System.Web.HttpUtility.HtmlEncode(glyphClass ?? string.Empty);
        var safeGlyph = System.Web.HttpUtility.HtmlEncode(glyph ?? string.Empty);
        return $"<span class=\"{safeContainer}\"><span class=\"{safeGlyphClass}\">{safeGlyph}</span></span>";
    }
}
