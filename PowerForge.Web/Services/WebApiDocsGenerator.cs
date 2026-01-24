using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PowerForge.Web;

public sealed class WebApiDocsOptions
{
    public string XmlPath { get; set; } = string.Empty;
    public string? AssemblyPath { get; set; }
    public string OutputPath { get; set; } = string.Empty;
    public string Title { get; set; } = "API Reference";
    public string BaseUrl { get; set; } = "/api";
    public string? Format { get; set; }
    public string? CssHref { get; set; }
    public string? HeaderHtmlPath { get; set; }
    public string? FooterHtmlPath { get; set; }
}

public static class WebApiDocsGenerator
{
    public static WebApiDocsResult Generate(WebApiDocsOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.XmlPath))
            throw new ArgumentException("XmlPath is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.OutputPath))
            throw new ArgumentException("OutputPath is required.", nameof(options));

        var xmlPath = Path.GetFullPath(options.XmlPath);
        var outputPath = Path.GetFullPath(options.OutputPath);
        Directory.CreateDirectory(outputPath);

        var apiDoc = ParseXml(xmlPath);
        var assemblyName = apiDoc.AssemblyName;
        var assemblyVersion = apiDoc.AssemblyVersion;

        if (!string.IsNullOrWhiteSpace(options.AssemblyPath) && File.Exists(options.AssemblyPath))
        {
            try
            {
                var assemblyNameInfo = System.Reflection.AssemblyName.GetAssemblyName(options.AssemblyPath);
                if (!string.IsNullOrWhiteSpace(assemblyNameInfo.Name))
                    assemblyName = assemblyNameInfo.Name;
                if (assemblyNameInfo.Version is not null)
                    assemblyVersion = assemblyNameInfo.Version.ToString();
            }
            catch
            {
                // ignore assembly inspection errors
            }
        }

        var types = apiDoc.Types.Values.OrderBy(t => t.FullName, StringComparer.OrdinalIgnoreCase).ToList();
        var index = new Dictionary<string, object?>
        {
            ["title"] = options.Title,
            ["generatedAtUtc"] = DateTime.UtcNow.ToString("O"),
            ["assembly"] = new Dictionary<string, object?>
            {
                ["assemblyName"] = assemblyName ?? string.Empty,
                ["assemblyVersion"] = assemblyVersion
            },
            ["typeCount"] = types.Count,
            ["types"] = types.Select(t => new Dictionary<string, object?>
            {
                ["name"] = t.Name,
                ["fullName"] = t.FullName,
                ["namespace"] = t.Namespace,
                ["kind"] = t.Kind,
                ["slug"] = t.Slug,
                ["summary"] = t.Summary
            }).ToList()
        };

        var indexPath = Path.Combine(outputPath, "index.json");
        WriteJson(indexPath, index);

        var search = types.Select(t => new Dictionary<string, object?>
        {
            ["title"] = t.FullName,
            ["summary"] = t.Summary ?? string.Empty,
            ["kind"] = t.Kind,
            ["namespace"] = t.Namespace,
            ["slug"] = t.Slug,
            ["url"] = $"{options.BaseUrl.TrimEnd('/')}/types/{t.Slug}.json"
        }).ToList();

        var searchPath = Path.Combine(outputPath, "search.json");
        WriteJson(searchPath, search);

        var typesDir = Path.Combine(outputPath, "types");
        Directory.CreateDirectory(typesDir);
        foreach (var type in types)
        {
            var typeModel = new Dictionary<string, object?>
            {
                ["name"] = type.Name,
                ["fullName"] = type.FullName,
                ["namespace"] = type.Namespace,
                ["kind"] = type.Kind,
                ["slug"] = type.Slug,
                ["summary"] = type.Summary,
                ["remarks"] = type.Remarks,
                ["methods"] = type.Methods.Select(m => new Dictionary<string, object?>
                {
                    ["name"] = m.Name,
                    ["summary"] = m.Summary,
                    ["returns"] = m.Returns,
                    ["parameters"] = m.Parameters.Select(p => new Dictionary<string, object?>
                    {
                        ["name"] = p.Name,
                        ["type"] = p.Type,
                        ["summary"] = p.Summary
                    }).ToList()
                }).ToList(),
                ["properties"] = type.Properties.Select(p => new Dictionary<string, object?>
                {
                    ["name"] = p.Name,
                    ["summary"] = p.Summary
                }).ToList(),
                ["fields"] = type.Fields.Select(f => new Dictionary<string, object?>
                {
                    ["name"] = f.Name,
                    ["summary"] = f.Summary
                }).ToList(),
                ["events"] = type.Events.Select(e => new Dictionary<string, object?>
                {
                    ["name"] = e.Name,
                    ["summary"] = e.Summary
                }).ToList()
            };

            var typePath = Path.Combine(typesDir, $"{type.Slug}.json");
            WriteJson(typePath, typeModel);
        }

        var format = (options.Format ?? "json").Trim().ToLowerInvariant();
        if (format is "hybrid" or "html" or "both")
        {
            GenerateHtml(outputPath, options, types);
        }

        return new WebApiDocsResult
        {
            OutputPath = outputPath,
            IndexPath = indexPath,
            SearchPath = searchPath,
            TypesPath = typesDir,
            TypeCount = types.Count
        };
    }

    private static ApiDocModel ParseXml(string xmlPath)
    {
        var apiDoc = new ApiDocModel();
        if (!File.Exists(xmlPath))
            return apiDoc;

        using var stream = File.OpenRead(xmlPath);
        var doc = XDocument.Load(stream);
        var docElement = doc.Element("doc");
        if (docElement is null) return apiDoc;

        var assembly = docElement.Element("assembly");
        if (assembly is not null)
        {
            apiDoc.AssemblyName = assembly.Element("name")?.Value ?? string.Empty;
        }

        var members = docElement.Element("members");
        if (members is null) return apiDoc;

        foreach (var member in members.Elements("member"))
        {
            var name = member.Attribute("name")?.Value;
            if (string.IsNullOrWhiteSpace(name) || name.Length < 2) continue;

            var prefix = name[0];
            var fullName = name.Substring(2);

            switch (prefix)
            {
                case 'T':
                    var type = ParseType(member, fullName);
                    apiDoc.Types[type.FullName] = type;
                    break;
                case 'M':
                    AddMethod(apiDoc, member, fullName);
                    break;
                case 'P':
                    AddProperty(apiDoc, member, fullName);
                    break;
                case 'F':
                    AddField(apiDoc, member, fullName);
                    break;
                case 'E':
                    AddEvent(apiDoc, member, fullName);
                    break;
            }
        }

        return apiDoc;
    }

    private static ApiTypeModel ParseType(XElement member, string fullName)
    {
        var lastDot = fullName.LastIndexOf('.');
        var ns = lastDot > 0 ? fullName.Substring(0, lastDot) : string.Empty;
        var name = lastDot > 0 ? fullName.Substring(lastDot + 1) : fullName;

        return new ApiTypeModel
        {
            Name = name,
            FullName = fullName,
            Namespace = ns,
            Summary = GetSummary(member),
            Remarks = GetElement(member, "remarks"),
            Kind = InferTypeKind(name),
            Slug = Slugify(fullName)
        };
    }

    private static void AddMethod(ApiDocModel doc, XElement member, string fullName)
    {
        var typeName = ExtractTypeName(fullName);
        if (!doc.Types.TryGetValue(typeName, out var type)) return;

        var name = ExtractMemberName(fullName);
        if (string.IsNullOrWhiteSpace(name)) return;

        var parameterTypes = ParseParameterTypes(fullName);
        var parameters = ParseParameters(member, parameterTypes);

        type.Methods.Add(new ApiMemberModel
        {
            Name = name,
            Summary = GetSummary(member),
            Parameters = parameters,
            Returns = GetElement(member, "returns")
        });
    }

    private static void AddProperty(ApiDocModel doc, XElement member, string fullName)
    {
        var typeName = ExtractTypeName(fullName);
        if (!doc.Types.TryGetValue(typeName, out var type)) return;

        var name = ExtractMemberName(fullName);
        if (string.IsNullOrWhiteSpace(name)) return;

        type.Properties.Add(new ApiMemberModel
        {
            Name = name,
            Summary = GetSummary(member)
        });
    }

    private static void AddField(ApiDocModel doc, XElement member, string fullName)
    {
        var typeName = ExtractTypeName(fullName);
        if (!doc.Types.TryGetValue(typeName, out var type)) return;

        var name = ExtractMemberName(fullName);
        if (string.IsNullOrWhiteSpace(name)) return;

        type.Fields.Add(new ApiMemberModel
        {
            Name = name,
            Summary = GetSummary(member)
        });
    }

    private static void AddEvent(ApiDocModel doc, XElement member, string fullName)
    {
        var typeName = ExtractTypeName(fullName);
        if (!doc.Types.TryGetValue(typeName, out var type)) return;

        var name = ExtractMemberName(fullName);
        if (string.IsNullOrWhiteSpace(name)) return;

        type.Events.Add(new ApiMemberModel
        {
            Name = name,
            Summary = GetSummary(member)
        });
    }

    private static string ExtractTypeName(string fullName)
    {
        var trimmed = fullName;
        var parenIdx = trimmed.IndexOf('(');
        if (parenIdx > 0)
            trimmed = trimmed.Substring(0, parenIdx);
        var lastDot = trimmed.LastIndexOf('.');
        return lastDot > 0 ? trimmed.Substring(0, lastDot) : trimmed;
    }

    private static string ExtractMemberName(string fullName)
    {
        var trimmed = fullName;
        var parenIdx = trimmed.IndexOf('(');
        if (parenIdx > 0)
            trimmed = trimmed.Substring(0, parenIdx);
        var lastDot = trimmed.LastIndexOf('.');
        return lastDot > 0 ? trimmed.Substring(lastDot + 1) : trimmed;
    }

    private static List<ApiParameterModel> ParseParameters(XElement member, IReadOnlyList<string> parameterTypes)
    {
        var results = new List<ApiParameterModel>();
        var paramElements = member.Elements("param").ToList();
        var count = Math.Max(paramElements.Count, parameterTypes.Count);
        for (var i = 0; i < count; i++)
        {
            var paramName = i < paramElements.Count ? paramElements[i].Attribute("name")?.Value ?? $"arg{i + 1}" : $"arg{i + 1}";
            var summary = i < paramElements.Count ? Normalize(paramElements[i].Value) : null;
            var type = i < parameterTypes.Count ? parameterTypes[i] : string.Empty;
            results.Add(new ApiParameterModel
            {
                Name = paramName,
                Type = type,
                Summary = summary
            });
        }
        return results;
    }

    private static List<string> ParseParameterTypes(string fullName)
    {
        var start = fullName.IndexOf('(');
        if (start < 0) return new List<string>();
        var end = fullName.LastIndexOf(')');
        if (end <= start) return new List<string>();
        var segment = fullName.Substring(start + 1, end - start - 1);
        if (string.IsNullOrWhiteSpace(segment)) return new List<string>();

        var results = new List<string>();
        var sb = new StringBuilder();
        var depth = 0;
        foreach (var ch in segment)
        {
            if (ch == '{' || ch == '[')
                depth++;
            if (ch == '}' || ch == ']')
                depth = Math.Max(0, depth - 1);

            if (ch == ',' && depth == 0)
            {
                results.Add(sb.ToString().Trim());
                sb.Clear();
                continue;
            }
            sb.Append(ch);
        }
        if (sb.Length > 0) results.Add(sb.ToString().Trim());
        return results;
    }

    private static string? GetSummary(XElement member)
    {
        var summary = member.Element("summary")?.Value;
        return string.IsNullOrWhiteSpace(summary) ? null : Normalize(summary);
    }

    private static string? GetElement(XElement member, string name)
    {
        var value = member.Element(name)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : Normalize(value);
    }

    private static string Normalize(string value)
    {
        return Regex.Replace(value, "\\s+", " ").Trim();
    }

    private static string InferTypeKind(string name)
    {
        if (name.StartsWith("I") && name.Length > 1 && char.IsUpper(name[1]))
            return "Interface";
        return "Class";
    }

    private static string Slugify(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var slug = value.ToLowerInvariant();
        slug = slug.Replace('+', '-');
        slug = Regex.Replace(slug, "`\\d+", string.Empty);
        slug = Regex.Replace(slug, "[^a-z0-9]+", "-");
        slug = Regex.Replace(slug, "-{2,}", "-").Trim('-');
        return slug;
    }

    private static void WriteJson(string path, object payload)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json, Encoding.UTF8);
    }

    private static void GenerateHtml(string outputPath, WebApiDocsOptions options, IReadOnlyList<ApiTypeModel> types)
    {
        var header = LoadOptionalHtml(options.HeaderHtmlPath);
        var footer = LoadOptionalHtml(options.FooterHtmlPath);
        var cssLink = string.IsNullOrWhiteSpace(options.CssHref) ? string.Empty : $"<link rel=\"stylesheet\" href=\"{options.CssHref}\" />";

        var indexHtml = new StringBuilder();
        indexHtml.AppendLine("<!doctype html>");
        indexHtml.AppendLine("<html lang=\"en\">");
        indexHtml.AppendLine("<head>");
        indexHtml.AppendLine("  <meta charset=\"utf-8\" />");
        indexHtml.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        indexHtml.AppendLine($"  <title>{System.Web.HttpUtility.HtmlEncode(options.Title)}</title>");
        if (!string.IsNullOrWhiteSpace(cssLink)) indexHtml.AppendLine($"  {cssLink}");
        if (string.IsNullOrWhiteSpace(cssLink)) indexHtml.AppendLine(FallbackCss);
        indexHtml.AppendLine("</head>");
        indexHtml.AppendLine("<body>");
        if (!string.IsNullOrWhiteSpace(header)) indexHtml.AppendLine(header);
        indexHtml.AppendLine("  <main class=\"pf-api\">");
        indexHtml.AppendLine($"    <h1>{System.Web.HttpUtility.HtmlEncode(options.Title)}</h1>");
        indexHtml.AppendLine($"    <p>Types: {types.Count}</p>");
        indexHtml.AppendLine("    <div class=\"pf-api-search\">");
        indexHtml.AppendLine("      <input id=\"api-search\" type=\"search\" placeholder=\"Search API\" autocomplete=\"off\" />");
        indexHtml.AppendLine("      <div id=\"api-results\" class=\"pf-api-results\"></div>");
        indexHtml.AppendLine("    </div>");
        indexHtml.AppendLine("    <div class=\"pf-api-types\">");
        foreach (var type in types)
        {
            indexHtml.AppendLine($"      <a class=\"pf-api-type\" href=\"types/{type.Slug}.html\">{System.Web.HttpUtility.HtmlEncode(type.FullName)}</a>");
        }
        indexHtml.AppendLine("    </div>");
        indexHtml.AppendLine("  </main>");
        indexHtml.AppendLine(ApiSearchScript);
        if (!string.IsNullOrWhiteSpace(footer)) indexHtml.AppendLine(footer);
        indexHtml.AppendLine("</body>");
        indexHtml.AppendLine("</html>");

        File.WriteAllText(Path.Combine(outputPath, "index.html"), indexHtml.ToString(), Encoding.UTF8);

        var typesDir = Path.Combine(outputPath, "types");
        Directory.CreateDirectory(typesDir);
        foreach (var type in types)
        {
            var typeHtml = new StringBuilder();
            typeHtml.AppendLine("<!doctype html>");
            typeHtml.AppendLine("<html lang=\"en\">");
            typeHtml.AppendLine("<head>");
            typeHtml.AppendLine("  <meta charset=\"utf-8\" />");
            typeHtml.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
            typeHtml.AppendLine($"  <title>{System.Web.HttpUtility.HtmlEncode(type.FullName)} - {System.Web.HttpUtility.HtmlEncode(options.Title)}</title>");
            if (!string.IsNullOrWhiteSpace(cssLink)) typeHtml.AppendLine($"  {cssLink}");
            if (string.IsNullOrWhiteSpace(cssLink)) typeHtml.AppendLine(FallbackCss);
            typeHtml.AppendLine("</head>");
            typeHtml.AppendLine("<body>");
            if (!string.IsNullOrWhiteSpace(header)) typeHtml.AppendLine(header);
            typeHtml.AppendLine("  <main class=\"pf-api\">");
            typeHtml.AppendLine($"    <a href=\"../index.html\">← API Index</a>");
            typeHtml.AppendLine($"    <h1>{System.Web.HttpUtility.HtmlEncode(type.FullName)}</h1>");
            if (!string.IsNullOrWhiteSpace(type.Summary))
                typeHtml.AppendLine($"    <p>{System.Web.HttpUtility.HtmlEncode(type.Summary)}</p>");
            if (!string.IsNullOrWhiteSpace(type.Remarks))
                typeHtml.AppendLine($"    <div class=\"pf-api-remarks\">{System.Web.HttpUtility.HtmlEncode(type.Remarks)}</div>");

            AppendMembers(typeHtml, "Methods", type.Methods);
            AppendMembers(typeHtml, "Properties", type.Properties);
            AppendMembers(typeHtml, "Fields", type.Fields);
            AppendMembers(typeHtml, "Events", type.Events);

            typeHtml.AppendLine("  </main>");
            if (!string.IsNullOrWhiteSpace(footer)) typeHtml.AppendLine(footer);
            typeHtml.AppendLine("</body>");
            typeHtml.AppendLine("</html>");

            File.WriteAllText(Path.Combine(typesDir, $"{type.Slug}.html"), typeHtml.ToString(), Encoding.UTF8);
        }

        var sitemapPath = Path.Combine(outputPath, "sitemap.xml");
        GenerateApiSitemap(sitemapPath, options.BaseUrl, types);
    }

    private static void AppendMembers(StringBuilder sb, string label, List<ApiMemberModel> members)
    {
        if (members.Count == 0) return;
        sb.AppendLine($"    <section class=\"pf-api-section\">");
        sb.AppendLine($"      <h2>{label}</h2>");
        sb.AppendLine("      <ul>");
        foreach (var member in members)
        {
            var summary = string.IsNullOrWhiteSpace(member.Summary)
                ? string.Empty
                : $" - {System.Web.HttpUtility.HtmlEncode(member.Summary)}";
            sb.AppendLine("        <li>");
            sb.AppendLine($"          <strong>{System.Web.HttpUtility.HtmlEncode(member.Name)}</strong>{summary}");
            if (member.Parameters.Count > 0)
            {
                sb.AppendLine("          <div class=\"pf-api-params\">");
                sb.AppendLine("            <ul>");
                foreach (var param in member.Parameters)
                {
                    var type = string.IsNullOrWhiteSpace(param.Type) ? string.Empty : $" ({System.Web.HttpUtility.HtmlEncode(param.Type)})";
                    var psummary = string.IsNullOrWhiteSpace(param.Summary) ? string.Empty : $": {System.Web.HttpUtility.HtmlEncode(param.Summary)}";
                    sb.AppendLine($"              <li><code>{System.Web.HttpUtility.HtmlEncode(param.Name)}</code>{type}{psummary}</li>");
                }
                sb.AppendLine("            </ul>");
                sb.AppendLine("          </div>");
            }
            if (!string.IsNullOrWhiteSpace(member.Returns))
            {
                sb.AppendLine($"          <div class=\"pf-api-returns\">Returns: {System.Web.HttpUtility.HtmlEncode(member.Returns)}</div>");
            }
            sb.AppendLine("        </li>");
        }
        sb.AppendLine("      </ul>");
        sb.AppendLine("    </section>");
    }

    private static void GenerateApiSitemap(string outputPath, string baseUrl, IReadOnlyList<ApiTypeModel> types)
    {
        var sb = new StringBuilder();
        var baseTrim = baseUrl.TrimEnd('/');
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
        foreach (var type in types)
        {
            sb.AppendLine("  <url>");
            sb.AppendLine($"    <loc>{baseTrim}/types/{type.Slug}.html</loc>");
            sb.AppendLine($"    <lastmod>{today}</lastmod>");
            sb.AppendLine("    <changefreq>monthly</changefreq>");
            sb.AppendLine("    <priority>0.5</priority>");
            sb.AppendLine("  </url>");
        }
        sb.AppendLine("</urlset>");
        File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
    }

    private static string LoadOptionalHtml(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var full = Path.GetFullPath(path);
        if (!File.Exists(full)) return string.Empty;
        return File.ReadAllText(full);
    }

    private const string ApiSearchScript = @"<script>
(() => {
  const input = document.getElementById('api-search');
  const results = document.getElementById('api-results');
  if (!input || !results) return;
  let data = [];
  fetch('search.json')
    .then(r => r.json())
    .then(items => { data = items || []; });
  const render = (items) => {
    results.innerHTML = '';
    if (!items.length) {
      results.innerHTML = '<div class=""pf-api-empty"">No results</div>';
      return;
    }
    const frag = document.createDocumentFragment();
    items.slice(0, 24).forEach(item => {
      const link = document.createElement('a');
      link.className = 'pf-api-result';
      const slug = item.slug || '';
      link.href = 'types/' + slug + '.html';
      link.innerHTML = '<strong>' + (item.title || '') + '</strong><span>' + (item.summary || '') + '</span>';
      frag.appendChild(link);
    });
    results.appendChild(frag);
  };
  input.addEventListener('input', () => {
    const q = input.value.trim().toLowerCase();
    if (!q) { results.innerHTML = ''; return; }
    const filtered = data.filter(x => (x.title || '').toLowerCase().includes(q) || (x.summary || '').toLowerCase().includes(q));
    render(filtered);
  });
})();
</script>";

    private const string FallbackCss = @"<style>
body{margin:0;font-family:Segoe UI,Arial,sans-serif;background:#0b0b12;color:#e6e9f3}
a{color:inherit;text-decoration:none}
a:hover{color:#a78bfa}
.pf-api{max-width:1100px;margin:0 auto;padding:32px 24px}
.pf-api-types{display:grid;grid-template-columns:repeat(auto-fit,minmax(260px,1fr));gap:10px;margin-top:16px}
.pf-api-type{background:#111827;border:1px solid rgba(148,163,184,.2);border-radius:10px;padding:10px 12px}
.pf-api-section{margin-top:24px}
.pf-api-section h2{margin-bottom:8px}
.pf-api-section ul{list-style:none;padding:0;margin:0;display:grid;gap:8px}
.pf-api-section li{background:#0f172a;border:1px solid rgba(148,163,184,.18);border-radius:10px;padding:10px 12px}
.pf-api-params ul{list-style:none;padding:6px 0 0;margin:0;display:grid;gap:6px}
.pf-api-returns{margin-top:8px;font-size:.9rem;color:#94a3b8}
.pf-api-search{margin:18px 0}
.pf-api-search input{width:100%;padding:10px 12px;border-radius:10px;border:1px solid rgba(148,163,184,.3);background:#0f172a;color:#e6e9f3}
.pf-api-results{display:grid;gap:8px;margin-top:10px}
.pf-api-result{background:#111827;border:1px solid rgba(148,163,184,.18);border-radius:10px;padding:10px 12px;display:grid;gap:4px}
.pf-api-result span{color:#94a3b8;font-size:.85rem}
.pf-api-empty{color:#94a3b8;font-size:.9rem}
</style>";

    private sealed class ApiDocModel
    {
        public string? AssemblyName { get; set; }
        public string? AssemblyVersion { get; set; }
        public Dictionary<string, ApiTypeModel> Types { get; } = new(StringComparer.Ordinal);
    }

    private sealed class ApiTypeModel
    {
        public string Name { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public string? Summary { get; set; }
        public string? Remarks { get; set; }
        public string Kind { get; set; } = "Class";
        public string Slug { get; set; } = string.Empty;
        public List<ApiMemberModel> Methods { get; } = new();
        public List<ApiMemberModel> Properties { get; } = new();
        public List<ApiMemberModel> Fields { get; } = new();
        public List<ApiMemberModel> Events { get; } = new();
    }

    private sealed class ApiMemberModel
    {
        public string Name { get; set; } = string.Empty;
        public string? Summary { get; set; }
        public List<ApiParameterModel> Parameters { get; set; } = new();
        public string? Returns { get; set; }
    }

    private sealed class ApiParameterModel
    {
        public string Name { get; set; } = string.Empty;
        public string? Type { get; set; }
        public string? Summary { get; set; }
    }
}
