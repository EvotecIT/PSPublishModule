using System.Text;
using System.Text.Json;

namespace PowerForge.Web;

public static partial class WebSiteBuilder
{
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

    private static string BuildSearchText(ContentItem item, string snippet)
    {
        var parts = new List<string>
        {
            item.Title,
            item.Description,
            snippet,
            item.Collection,
            item.Kind.ToString(),
            item.ProjectSlug ?? string.Empty,
            string.Join(" ", item.Tags ?? Array.Empty<string>())
        };
        if (item.Meta.Count > 0)
        {
            foreach (var value in item.Meta.Values)
            {
                if (value is null)
                    continue;
                if (value is string text)
                {
                    if (!string.IsNullOrWhiteSpace(text))
                        parts.Add(text);
                    continue;
                }
                parts.Add(value.ToString() ?? string.Empty);
            }
        }
        return string.Join(" ", parts.Where(static part => !string.IsNullOrWhiteSpace(part))).Trim();
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
        else if (string.Equals(collection, "blog", StringComparison.OrdinalIgnoreCase))
            weight += 0.2;

        return weight;
    }

    private static void EnsureSearchPage(string outputRoot, IReadOnlyList<SearchIndexEntry> entries)
    {
        if (entries.Count == 0)
            return;

        var searchPath = Path.Combine(outputRoot, "search", "index.html");
        if (File.Exists(searchPath))
            return;

        var cssHref = TryResolveSearchSurfaceCssHref(outputRoot);
        var html = BuildSearchSurfaceHtml(cssHref);
        Directory.CreateDirectory(Path.GetDirectoryName(searchPath) ?? outputRoot);
        WriteAllTextIfChanged(searchPath, html);
    }

    private static string? TryResolveSearchSurfaceCssHref(string outputRoot)
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
                        var relative = Path.GetRelativePath(outputRoot, cssPath).Replace('\\', '/');
                        return "/" + relative.TrimStart('/');
                    }

                    if (Directory.Exists(Path.Combine(themeRoot, "assets")))
                    {
                        var firstThemeCss = Directory
                            .EnumerateFiles(Path.Combine(themeRoot, "assets"), "*.css", SearchOption.AllDirectories)
                            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                            .FirstOrDefault();
                        if (!string.IsNullOrWhiteSpace(firstThemeCss))
                        {
                            var relative = Path.GetRelativePath(outputRoot, firstThemeCss).Replace('\\', '/');
                            return "/" + relative.TrimStart('/');
                        }
                    }
                }
            }
            catch
            {
                // Best-effort only.
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
            var relative = Path.GetRelativePath(outputRoot, candidate).Replace('\\', '/');
            return "/" + relative.TrimStart('/');
        }

        return null;
    }

    private static string BuildSearchSurfaceHtml(string? cssHref)
    {
        var cssLink = string.IsNullOrWhiteSpace(cssHref)
            ? string.Empty
            : $"  <link rel=\"stylesheet\" href=\"{System.Web.HttpUtility.HtmlEncode(cssHref)}\" />{Environment.NewLine}";

        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html>");
        sb.AppendLine("<html lang=\"en\">");
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
        sb.AppendLine("  <main class=\"pf-search-wrap\">");
        sb.AppendLine("    <h1>Search</h1>");
        sb.AppendLine("    <input id=\"pf-search-query\" class=\"pf-search-box\" type=\"search\" autocomplete=\"off\" placeholder=\"Search docs, blogs, pages...\" />");
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
        sb.AppendLine("      try {");
        sb.AppendLine("        const manifestRes = await fetch('./manifest.json', { cache: 'no-cache' });");
        sb.AppendLine("        let indexPath = './index.json';");
        sb.AppendLine("        if (manifestRes.ok){");
        sb.AppendLine("          const manifest = await manifestRes.json();");
        sb.AppendLine("          if (manifest && typeof manifest.searchIndexPath === 'string' && manifest.searchIndexPath.trim())");
        sb.AppendLine("            indexPath = manifest.searchIndexPath;");
        sb.AppendLine("        }");
        sb.AppendLine("        const indexRes = await fetch(indexPath, { cache: 'no-cache' });");
        sb.AppendLine("        if (!indexRes.ok) throw new Error('Failed to load search index: ' + indexRes.status);");
        sb.AppendLine("        entries = await indexRes.json();");
        sb.AppendLine("      } catch (error){");
        sb.AppendLine("        meta.textContent = 'Search index unavailable.';");
        sb.AppendLine("        results.innerHTML = '<p>' + esc(error && error.message ? error.message : error) + '</p>';");
        sb.AppendLine("        return;");
        sb.AppendLine("      }");
        sb.AppendLine("      function run(){");
        sb.AppendLine("        const q = input.value.trim().toLowerCase();");
        sb.AppendLine("        if (!q){ render(entries, ''); return; }");
        sb.AppendLine("        const ranked = entries");
        sb.AppendLine("          .map(item => ({ item, hay: toText(item), weight: Number(item.weight || 1) }))");
        sb.AppendLine("          .filter(row => row.hay.indexOf(q) >= 0)");
        sb.AppendLine("          .sort((a, b) => b.weight - a.weight || String(a.item.title || '').localeCompare(String(b.item.title || '')))");
        sb.AppendLine("          .map(row => row.item);");
        sb.AppendLine("        render(ranked, q);");
        sb.AppendLine("      }");
        sb.AppendLine("      input.addEventListener('input', run);");
        sb.AppendLine("      run();");
        sb.AppendLine("    })();");
        sb.AppendLine("  </script>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }
}
