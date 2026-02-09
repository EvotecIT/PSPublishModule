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
        var cssLinks = BuildCssLinks(options.CssHref);
        var fallbackCss = LoadAsset(options, "fallback.css", null);
        var cssBlock = string.IsNullOrWhiteSpace(cssLinks)
            ? WrapStyle(fallbackCss)
            : cssLinks;

        var indexTemplate = LoadTemplate(options, "index.html", options.IndexTemplatePath);
        var typeLinks = new StringBuilder();
        foreach (var type in types)
        {
            typeLinks.AppendLine($"      <a class=\"pf-api-type\" href=\"types/{type.Slug}.html\">{System.Web.HttpUtility.HtmlEncode(type.FullName)}</a>");
        }
        var searchScript = WrapScript(LoadAsset(options, "search.js", options.SearchScriptPath));
        var indexHtml = ApplyTemplate(indexTemplate, new Dictionary<string, string?>
        {
            ["TITLE"] = System.Web.HttpUtility.HtmlEncode(options.Title),
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
            var codeLanguage = GetDefaultCodeLanguage(options);
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
            var typeHtml = ApplyTemplate(typeTemplate, new Dictionary<string, string?>
            {
                ["TYPE_TITLE"] = System.Web.HttpUtility.HtmlEncode(typeTitle),
                ["TYPE_FULLNAME"] = System.Web.HttpUtility.HtmlEncode(type.FullName),
                ["CSS"] = cssBlock,
                ["HEADER"] = header,
                ["FOOTER"] = footer,
                ["BODY_CLASS"] = bodyClass,
                ["TYPE_SUMMARY"] = summaryHtml,
                ["TYPE_REMARKS"] = remarksHtml,
                ["MEMBERS"] = memberHtml.ToString().TrimEnd()
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
        var cssLinks = BuildCssLinks(options.CssHref);
        var fallbackCss = LoadAsset(options, "fallback.css", null);
        var cssBlock = string.IsNullOrWhiteSpace(cssLinks)
            ? WrapStyle(fallbackCss)
            : cssLinks;

        var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl) ? "/api" : options.BaseUrl.TrimEnd('/');
        var docsScript = WrapScript(LoadAsset(options, "docs.js", options.DocsScriptPath));
        var docsHomeUrl = NormalizeDocsHomeUrl(options.DocsHomeUrl);
        var sidebarHtml = BuildDocsSidebar(types, baseUrl, string.Empty, docsHomeUrl);
        var sidebarClass = BuildSidebarClass(options.SidebarPosition);
        var overviewHtml = BuildDocsOverview(types, baseUrl);
        var slugMap = BuildTypeSlugMap(types);
        var typeIndex = BuildTypeIndex(types);
        var derivedMap = BuildDerivedTypeMap(types, typeIndex);

        var indexTemplate = LoadTemplate(options, "docs-index.html", options.DocsIndexTemplatePath);
        var indexHtml = ApplyTemplate(indexTemplate, new Dictionary<string, string?>
        {
            ["TITLE"] = System.Web.HttpUtility.HtmlEncode(options.Title),
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
            var sidebar = BuildDocsSidebar(types, baseUrl, type.Slug, docsHomeUrl);
            var sidebarClassForType = BuildSidebarClass(options.SidebarPosition);
            var typeMain = BuildDocsTypeDetail(type, baseUrl, slugMap, typeIndex, derivedMap, GetDefaultCodeLanguage(options));
            var typeTemplate = LoadTemplate(options, "docs-type.html", options.DocsTypeTemplatePath);
            var pageTitle = $"{type.Name} - {options.Title}";
            var typeHtml = ApplyTemplate(typeTemplate, new Dictionary<string, string?>
            {
                ["TITLE"] = System.Web.HttpUtility.HtmlEncode(pageTitle),
                ["CSS"] = cssBlock,
                ["HEADER"] = header,
                ["FOOTER"] = footer,
                ["BODY_CLASS"] = bodyClass,
                ["SIDEBAR"] = sidebar,
                ["SIDEBAR_CLASS"] = sidebarClassForType,
                ["MAIN"] = typeMain,
                ["DOCS_SCRIPT"] = docsScript
            });

            var htmlPath = Path.Combine(outputPath, $"{type.Slug}.html");
            File.WriteAllText(htmlPath, typeHtml, Encoding.UTF8);

            var typeDir = Path.Combine(outputPath, type.Slug);
            Directory.CreateDirectory(typeDir);
            File.WriteAllText(Path.Combine(typeDir, "index.html"), typeHtml, Encoding.UTF8);
        }

        var sitemapPath = Path.Combine(outputPath, "sitemap.xml");
        GenerateDocsSitemap(sitemapPath, baseUrl, types);
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

    private static string BuildDocsSidebar(IReadOnlyList<ApiTypeModel> types, string baseUrl, string activeSlug, string docsHomeUrl)
    {
        var indexUrl = EnsureTrailingSlash(baseUrl);
        var sb = new StringBuilder();
        sb.AppendLine("    <div class=\"sidebar-header\">");
        var active = string.IsNullOrWhiteSpace(activeSlug) ? " active" : string.Empty;
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
          sb.AppendLine($"    <div class=\"sidebar-count\" data-total=\"{totalTypes}\">Showing {totalTypes} types</div>");
          sb.AppendLine("    <div class=\"sidebar-tools\">");
          sb.AppendLine("      <button class=\"sidebar-expand-all\" type=\"button\">Expand all</button>");
          sb.AppendLine("      <button class=\"sidebar-collapse-all\" type=\"button\">Collapse all</button>");
          sb.AppendLine("    </div>");
          sb.AppendLine("    <nav class=\"sidebar-nav\">");

        var mainTypes = GetMainTypes(types);
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
                sb.AppendLine(BuildSidebarTypeItem(type, baseUrl, activeSlug));
            }
            sb.AppendLine("        </div>");
            sb.AppendLine("      </div>");
        }

        var grouped = types
            .Where(t => !IsMainType(t.Name))
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
                sb.AppendLine(BuildSidebarTypeItem(type, baseUrl, activeSlug));
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

    private static string BuildSidebarTypeItem(ApiTypeModel type, string baseUrl, string activeSlug)
    {
        var active = string.Equals(activeSlug, type.Slug, StringComparison.OrdinalIgnoreCase) ? " active" : string.Empty;
        var summary = StripCrefTokens(type.Summary);
        var search = $"{type.Name} {type.FullName} {summary}".Trim();
        var searchAttr = System.Web.HttpUtility.HtmlEncode(search);
        var name = System.Web.HttpUtility.HtmlEncode(type.Name);
        var kind = NormalizeKind(type.Kind);
        var icon = GetTypeIcon(type.Kind);
        var ns = System.Web.HttpUtility.HtmlEncode(string.IsNullOrWhiteSpace(type.Namespace) ? "(global)" : type.Namespace);
        var href = BuildDocsTypeUrl(baseUrl, type.Slug);
        return $"          <a href=\"{href}\" class=\"type-item{active}\" data-search=\"{searchAttr}\" data-kind=\"{kind}\" data-namespace=\"{ns}\">" +
               $"<span class=\"type-icon {kind}\">{icon}</span><span class=\"type-name\">{name}</span></a>";
    }

    private static string BuildDocsOverview(IReadOnlyList<ApiTypeModel> types, string baseUrl)
    {
        var sb = new StringBuilder();
        sb.AppendLine("    <div class=\"api-overview\">");
        sb.AppendLine("      <h1>API Reference</h1>");
        sb.AppendLine("      <p class=\"lead\">Complete API documentation auto-generated from source documentation.</p>");

        var mainTypes = GetMainTypes(types);
        if (mainTypes.Count > 0)
        {
            sb.AppendLine("      <section class=\"quick-start\">");
            sb.AppendLine("        <h2>Quick Start</h2>");
            sb.AppendLine("        <p class=\"section-desc\">Frequently used types and entry points.</p>");
            sb.AppendLine("        <div class=\"quick-grid\">");
            foreach (var type in mainTypes.Take(6))
            {
                var summary = Truncate(StripCrefTokens(type.Summary), 100);
                var quickHref = BuildDocsTypeUrl(baseUrl, type.Slug);
                sb.AppendLine($"          <a href=\"{quickHref}\" class=\"quick-card\">");
                sb.AppendLine("            <div class=\"quick-card-header\">");
                sb.AppendLine($"              <span class=\"type-icon large {NormalizeKind(type.Kind)}\">{GetTypeIcon(type.Kind)}</span>");
                sb.AppendLine($"              <strong>{System.Web.HttpUtility.HtmlEncode(type.Name)}</strong>");
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
                var search = $"{type.Name} {type.FullName} {summary}".Trim();
                var searchAttr = System.Web.HttpUtility.HtmlEncode(search);
                var kind = NormalizeKind(type.Kind);
                var nsValue = System.Web.HttpUtility.HtmlEncode(string.IsNullOrWhiteSpace(type.Namespace) ? "(global)" : type.Namespace);
                var chipHref = BuildDocsTypeUrl(baseUrl, type.Slug);
                sb.AppendLine($"            <a href=\"{chipHref}\" class=\"type-chip {kind}\" data-search=\"{searchAttr}\" data-kind=\"{kind}\" data-namespace=\"{nsValue}\">");
                sb.AppendLine($"              <span class=\"chip-icon\">{GetTypeIcon(type.Kind)}</span>");
                sb.AppendLine($"              {System.Web.HttpUtility.HtmlEncode(type.Name)}");
                sb.AppendLine("            </a>");
            }
            sb.AppendLine("          </div>");
            sb.AppendLine("        </div>");
        }
        sb.AppendLine("      </section>");
        sb.AppendLine("    </div>");
        return sb.ToString().TrimEnd();
    }
}
