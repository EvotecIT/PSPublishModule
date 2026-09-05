using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace PowerForge.Web;

public static partial class WebSiteBuilder
{
    private const string GeneratedSearchFallbackMarker = "data-powerforge-generated-search-fallback";

    private static bool HasFeature(string[]? features, string feature)
    {
        if (features is null || features.Length == 0 || string.IsNullOrWhiteSpace(feature))
            return false;
        foreach (var item in features)
        {
            if (string.Equals(item?.Trim(), feature, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static double ResolveSearchWeight(PageKind kind, string? collection)
    {
        var weight = kind switch
        {
            PageKind.Home => 6.0,
            PageKind.Page => 3.5,
            PageKind.Section => 2.8,
            PageKind.Term => 2.2,
            PageKind.Taxonomy => 2.0,
            _ => 1.0
        };

        if (string.Equals(collection, "docs", StringComparison.OrdinalIgnoreCase))
            weight += 0.4;
        else if (IsEditorialCollection(collection))
            weight += 0.2;

        return weight;
    }

    private static void EnsureSearchPage(
        string outputRoot,
        IReadOnlyList<SearchIndexEntry> entries,
        AgentWebMcpToolSpec? webMcpTool)
    {
        if (entries.Count == 0)
            return;

        var route = webMcpTool is null ? "/search/" : WebAgentReadiness.NormalizeWebMcpRoute(webMcpTool.Route);
        var searchPath = WebAgentReadiness.ResolveWebMcpHtmlPath(outputRoot, route);
        if (File.Exists(searchPath) && !IsGeneratedSearchFallback(searchPath))
            return;

        var cssHref = TryResolveSearchSurfaceCssHref(outputRoot, searchPath);
        var searchIndexHref = ToPageRelativeHref(searchPath, Path.Combine(outputRoot, "search", "index.json"));
        var runtimeHref = webMcpTool is null
            ? null
            : ToPageRelativeHref(searchPath, GetWebMcpSiteSearchAssetPath(outputRoot));
        var html = BuildSearchSurfaceHtml(cssHref, webMcpTool, searchIndexHref, runtimeHref);
        Directory.CreateDirectory(Path.GetDirectoryName(searchPath) ?? outputRoot);
        WriteAllTextIfChanged(searchPath, html);
    }

    private static bool IsGeneratedSearchFallback(string path)
    {
        try
        {
            var html = File.ReadAllText(path);
            return html.Contains(GeneratedSearchFallbackMarker, StringComparison.Ordinal) ||
                   (html.Contains("class=\"pf-search-wrap\"", StringComparison.Ordinal) &&
                    html.Contains("id=\"pf-search-query\"", StringComparison.Ordinal) &&
                    html.Contains("id=\"pf-search-results\"", StringComparison.Ordinal) &&
                    html.Contains("Loading search index...", StringComparison.Ordinal));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Failed to inspect generated search fallback '{path}': {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static string ToPageRelativeHref(string pagePath, string targetPath)
    {
        var pageDirectory = Path.GetDirectoryName(pagePath)
            ?? throw new InvalidOperationException($"Search page '{pagePath}' has no parent directory.");
        var relative = Path.GetRelativePath(pageDirectory, targetPath).Replace('\\', '/');
        return relative.StartsWith(".", StringComparison.Ordinal) ? relative : "./" + relative;
    }

    private static string? TryResolveSearchSurfaceCssHref(string outputRoot, string searchPath)
    {
        var specPath = Path.Combine(outputRoot, "_powerforge", "site-spec.json");
        if (File.Exists(specPath))
        {
            try
            {
                var spec = JsonSerializer.Deserialize<SiteSpec>(File.ReadAllText(specPath), WebJson.Options);
                if (!string.IsNullOrWhiteSpace(spec?.DefaultTheme))
                {
                    var themesFolder = ResolveThemesFolder(spec);
                    var themeRoot = Path.Combine(outputRoot, themesFolder, spec.DefaultTheme);
                    var preferred = new[]
                    {
                        Path.Combine(themeRoot, "assets", "app.css"),
                        Path.Combine(themeRoot, "assets", "site.css")
                    };
                    foreach (var cssPath in preferred)
                    {
                        if (!File.Exists(cssPath))
                            continue;
                        return ToPageRelativeHref(searchPath, cssPath);
                    }

                    if (Directory.Exists(Path.Combine(themeRoot, "assets")))
                    {
                        var firstThemeCss = Directory
                            .EnumerateFiles(Path.Combine(themeRoot, "assets"), "*.css", SearchOption.AllDirectories)
                            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                            .FirstOrDefault();
                        if (!string.IsNullOrWhiteSpace(firstThemeCss))
                        {
                            return ToPageRelativeHref(searchPath, firstThemeCss);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Failed to resolve search page CSS from site spec '{specPath}': {ex.GetType().Name}: {ex.Message}");
            }
        }

        var fallbackCandidates = new[]
        {
            Path.Combine(outputRoot, "css", "app.css"),
            Path.Combine(outputRoot, "assets", "app.css")
        };
        foreach (var candidate in fallbackCandidates)
        {
            if (!File.Exists(candidate))
                continue;
            return ToPageRelativeHref(searchPath, candidate);
        }

        return null;
    }

    private static string BuildSearchSurfaceHtml(
        string? cssHref,
        AgentWebMcpToolSpec? webMcpTool,
        string searchIndexHref,
        string? runtimeHref)
    {
        var cssLink = string.IsNullOrWhiteSpace(cssHref)
            ? string.Empty
            : $"  <link rel=\"stylesheet\" href=\"{System.Web.HttpUtility.HtmlEncode(cssHref)}\" />{Environment.NewLine}";
        var webMcpAttributes = webMcpTool is null
            ? string.Empty
            : $" data-webmcp-site-search data-webmcp-tool-name=\"{System.Web.HttpUtility.HtmlAttributeEncode(webMcpTool.Name)}\" data-webmcp-tool-description=\"{System.Web.HttpUtility.HtmlAttributeEncode(webMcpTool.Description)}\" data-webmcp-search-index=\"{System.Web.HttpUtility.HtmlAttributeEncode(searchIndexHref)}\"";

        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html>");
        sb.Append("<html lang=\"en\" ").Append(GeneratedSearchFallbackMarker).AppendLine(">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"utf-8\" />");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        sb.AppendLine("  <title>Search</title>");
        sb.Append("  <meta name=\"robots\" content=\"noindex\" />").AppendLine();
        sb.Append(cssLink);
        sb.AppendLine("  <style>");
        sb.AppendLine("    :root { color-scheme: light dark; }");
        sb.AppendLine("    body { margin: 0; font-family: Segoe UI, Arial, sans-serif; }");
        sb.AppendLine("    .pf-search-wrap { max-width: 1000px; margin: 0 auto; padding: 32px 20px 40px; }");
        sb.AppendLine("    .pf-search-box { width: 100%; box-sizing: border-box; padding: 12px 14px; border-radius: 10px; border: 1px solid rgba(120,120,120,0.4); font-size: 1rem; }");
        sb.AppendLine("    .pf-search-meta { margin-top: 10px; opacity: 0.8; font-size: 0.9rem; }");
        sb.AppendLine("    .pf-search-results { margin-top: 20px; display: grid; gap: 12px; }");
        sb.AppendLine("    .pf-search-item { padding: 12px 14px; border-radius: 12px; border: 1px solid rgba(120,120,120,0.35); }");
        sb.AppendLine("    .pf-search-item a { text-decoration: none; font-weight: 600; }");
        sb.AppendLine("    .pf-search-desc, .pf-search-snippet { margin-top: 6px; font-size: 0.92rem; opacity: 0.9; }");
        sb.AppendLine("    .pf-search-tags { margin-top: 8px; display: flex; gap: 6px; flex-wrap: wrap; }");
        sb.AppendLine("    .pf-search-tag { font-size: 0.78rem; padding: 2px 8px; border-radius: 999px; border: 1px solid rgba(120,120,120,0.4); }");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.Append("  <main class=\"pf-search-wrap\"").Append(webMcpAttributes).AppendLine(">");
        sb.AppendLine("    <h1>Search</h1>");
        sb.AppendLine("    <input id=\"pf-search-query\" class=\"pf-search-box\" type=\"search\" autocomplete=\"off\" placeholder=\"Search docs, blogs, news, pages...\" />");
        sb.AppendLine("    <div id=\"pf-search-meta\" class=\"pf-search-meta\">Loading search index...</div>");
        sb.AppendLine("    <div id=\"pf-search-results\" class=\"pf-search-results\"></div>");
        sb.AppendLine("  </main>");
        sb.AppendLine("  <script>");
        sb.AppendLine("    (async function(){");
        sb.AppendLine("      const input = document.getElementById('pf-search-query');");
        sb.AppendLine("      const meta = document.getElementById('pf-search-meta');");
        sb.AppendLine("      const results = document.getElementById('pf-search-results');");
        sb.AppendLine("      const params = new URLSearchParams(window.location.search);");
        sb.AppendLine("      const seed = (params.get('q') || '').trim();");
        sb.AppendLine("      if (seed) input.value = seed;");
        sb.AppendLine("      function esc(value){");
        sb.AppendLine("        return String(value || '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/\\\"/g, '&quot;');");
        sb.AppendLine("      }");
        sb.AppendLine("      function toText(entry){");
        sb.AppendLine("        const tags = Array.isArray(entry.tags) ? entry.tags.join(' ') : '';");
        sb.AppendLine("        return [entry.title, entry.description, entry.snippet, entry.searchText, entry.collection, entry.kind, tags].join(' ').toLowerCase();");
        sb.AppendLine("      }");
        sb.AppendLine("      function render(rows, q){");
        sb.AppendLine("        if (!rows.length){");
        sb.AppendLine("          results.innerHTML = '<p>No results found.</p>';");
        sb.AppendLine("          meta.textContent = q ? '0 results for \"' + q + '\"' : 'No index entries found.';");
        sb.AppendLine("          return;");
        sb.AppendLine("        }");
        sb.AppendLine("        meta.textContent = q ? (rows.length + ' results for \"' + q + '\"') : (rows.length + ' pages indexed');");
        sb.AppendLine("        results.innerHTML = rows.map(item => {");
        sb.AppendLine("          const tags = Array.isArray(item.tags) && item.tags.length");
        sb.AppendLine("            ? '<div class=\"pf-search-tags\">' + item.tags.map(tag => '<span class=\"pf-search-tag\">' + esc(tag) + '</span>').join('') + '</div>'");
        sb.AppendLine("            : '';");
        sb.AppendLine("          const desc = item.description ? '<div class=\"pf-search-desc\">' + esc(item.description) + '</div>' : '';");
        sb.AppendLine("          const snippet = item.snippet ? '<div class=\"pf-search-snippet\">' + esc(item.snippet) + '</div>' : '';");
        sb.AppendLine("          const title = item.title || item.url || '/';");
        sb.AppendLine("          return '<article class=\"pf-search-item\"><a href=\"' + esc(item.url || '/') + '\">' + esc(title) + '</a>' + desc + snippet + tags + '</article>'; ");
        sb.AppendLine("        }).join('');");
        sb.AppendLine("      }");
        sb.AppendLine("      let entries = [];");
        sb.AppendLine("      let indexState = 'loading';");
        sb.AppendLine("      let indexError = null;");
        sb.AppendLine("      let webMcpResultsVisible = false;");
        sb.AppendLine("      window.PowerForgeWebMcpSearch = window.PowerForgeWebMcpSearch || {};");
        sb.AppendLine("      window.PowerForgeWebMcpSearch.renderVisibleResults = function(response){");
        sb.AppendLine("        const q = String(response && response.query || '').trim();");
        sb.AppendLine("        const rows = Array.isArray(response && response.results) ? response.results : [];");
        sb.AppendLine("        webMcpResultsVisible = true;");
        sb.AppendLine("        input.value = q;");
        sb.AppendLine("        render(rows, q);");
        sb.AppendLine("      };");
        sb.AppendLine("      function renderIndexUnavailable(error){");
        sb.AppendLine("        meta.textContent = 'Search index unavailable.';");
        sb.AppendLine("        results.innerHTML = '<p>' + esc(error && error.message ? error.message : error) + '</p>';");
        sb.AppendLine("      }");
        sb.AppendLine("      function run(){");
        sb.AppendLine("        if (indexState === 'loading'){");
        sb.AppendLine("          meta.textContent = 'Loading search index...';");
        sb.AppendLine("          results.innerHTML = '';");
        sb.AppendLine("          return;");
        sb.AppendLine("        }");
        sb.AppendLine("        if (indexState === 'failed'){");
        sb.AppendLine("          renderIndexUnavailable(indexError);");
        sb.AppendLine("          return;");
        sb.AppendLine("        }");
        sb.AppendLine("        const q = input.value.trim().toLowerCase();");
        sb.AppendLine("        if (!q){ render(entries, ''); return; }");
        sb.AppendLine("        const ranked = entries");
        sb.AppendLine("          .map(item => ({ item, hay: toText(item), weight: Number(item.weight || 1) }))");
        sb.AppendLine("          .filter(row => row.hay.indexOf(q) >= 0)");
        sb.AppendLine("          .sort((a, b) => b.weight - a.weight || String(a.item.title || '').localeCompare(String(b.item.title || '')))");
        sb.AppendLine("          .map(row => row.item);");
        sb.AppendLine("        render(ranked, q);");
        sb.AppendLine("      }");
        sb.AppendLine("      input.addEventListener('input', function(){");
        sb.AppendLine("        webMcpResultsVisible = false;");
        sb.AppendLine("        run();");
        sb.AppendLine("      });");
        sb.AppendLine("      try {");
        sb.Append("        const indexRes = await fetch('").Append(EscapeJavaScriptSingleQuoted(searchIndexHref)).AppendLine("', { cache: 'no-cache', credentials: 'same-origin' });");
        sb.AppendLine("        if (!indexRes.ok) throw new Error('Failed to load search index: ' + indexRes.status);");
        sb.AppendLine("        entries = await indexRes.json();");
        sb.AppendLine("        indexState = 'ready';");
        sb.AppendLine("      } catch (error){");
        sb.AppendLine("        indexState = 'failed';");
        sb.AppendLine("        indexError = error;");
        sb.AppendLine("        if (!webMcpResultsVisible){");
        sb.AppendLine("          renderIndexUnavailable(error);");
        sb.AppendLine("        }");
        sb.AppendLine("        return;");
        sb.AppendLine("      }");
        sb.AppendLine("      if (!webMcpResultsVisible) run();");
        sb.AppendLine("    })();");
        sb.AppendLine("  </script>");
        if (webMcpTool is not null && !string.IsNullOrWhiteSpace(runtimeHref))
            sb.Append("  <script src=\"").Append(System.Web.HttpUtility.HtmlAttributeEncode(runtimeHref)).AppendLine("\" defer data-powerforge-webmcp></script>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    private static string EscapeJavaScriptSingleQuoted(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
}
