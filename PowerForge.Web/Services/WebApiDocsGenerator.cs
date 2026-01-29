using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PowerForge.Web;

/// <summary>Options for API documentation generation.</summary>
public sealed class WebApiDocsOptions
{
    /// <summary>Path to the XML documentation file.</summary>
    public string XmlPath { get; set; } = string.Empty;
    /// <summary>Optional assembly path for version metadata.</summary>
    public string? AssemblyPath { get; set; }
    /// <summary>Output directory for generated docs.</summary>
    public string OutputPath { get; set; } = string.Empty;
    /// <summary>Documentation title.</summary>
    public string Title { get; set; } = "API Reference";
    /// <summary>Base URL for API documentation routes.</summary>
    public string BaseUrl { get; set; } = "/api";
    /// <summary>Output format hint (json, html, hybrid, both).</summary>
    public string? Format { get; set; }
    /// <summary>Optional stylesheet href for HTML output.</summary>
    public string? CssHref { get; set; }
    /// <summary>Optional path to header HTML fragment.</summary>
    public string? HeaderHtmlPath { get; set; }
    /// <summary>Optional path to footer HTML fragment.</summary>
    public string? FooterHtmlPath { get; set; }
    /// <summary>Optional list of namespace prefixes to include.</summary>
    public List<string> IncludeNamespacePrefixes { get; } = new();
    /// <summary>Optional list of namespace prefixes to exclude.</summary>
    public List<string> ExcludeNamespacePrefixes { get; } = new();
}

/// <summary>Generates API documentation artifacts from XML docs.</summary>
public static class WebApiDocsGenerator
{
    /// <summary>Generates API documentation output.</summary>
    /// <param name="options">Generation options.</param>
    /// <returns>Result payload describing generated artifacts.</returns>
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

        Assembly? assembly = null;
        if (!string.IsNullOrWhiteSpace(options.AssemblyPath) && File.Exists(options.AssemblyPath))
        {
            try
            {
                assembly = Assembly.LoadFrom(options.AssemblyPath);
            }
            catch
            {
                assembly = null;
            }
        }

        var apiDoc = ParseXml(xmlPath, assembly, options);
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

        var types = apiDoc.Types.Values
            .Where(t => ShouldIncludeType(t, options))
            .OrderBy(t => t.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    private static ApiDocModel ParseXml(string xmlPath, Assembly? assembly, WebApiDocsOptions options)
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
                    AddMethod(apiDoc, member, fullName, assembly);
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

    private static void AddMethod(ApiDocModel doc, XElement member, string fullName, Assembly? assembly)
    {
        var typeName = ExtractTypeName(fullName);
        if (!doc.Types.TryGetValue(typeName, out var type)) return;

        var name = ExtractMemberName(fullName);
        if (string.IsNullOrWhiteSpace(name)) return;

        var parameterTypes = ParseParameterTypes(fullName);
        var parameterNames = TryResolveParameterNames(assembly, typeName, name, parameterTypes);
        var parameters = ParseParameters(member, parameterTypes, parameterNames);

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

    private static List<ApiParameterModel> ParseParameters(XElement member, IReadOnlyList<string> parameterTypes, IReadOnlyList<string>? parameterNames)
    {
        var results = new List<ApiParameterModel>();
        var paramElements = member.Elements("param").ToList();
        var count = Math.Max(paramElements.Count, parameterTypes.Count);
        for (var i = 0; i < count; i++)
        {
            var paramName = i < paramElements.Count
                ? paramElements[i].Attribute("name")?.Value ?? $"arg{i + 1}"
                : (parameterNames != null && i < parameterNames.Count && !string.IsNullOrWhiteSpace(parameterNames[i])
                    ? parameterNames[i]
                    : $"arg{i + 1}");
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

    private static bool ShouldIncludeType(ApiTypeModel type, WebApiDocsOptions options)
    {
        var ns = type.Namespace ?? string.Empty;
        if (options.IncludeNamespacePrefixes.Count > 0)
        {
            var matches = options.IncludeNamespacePrefixes.Any(prefix =>
                ns.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                type.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (!matches) return false;
        }

        if (options.ExcludeNamespacePrefixes.Count > 0)
        {
            var excluded = options.ExcludeNamespacePrefixes.Any(prefix =>
                ns.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                type.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (excluded) return false;
        }

        return true;
    }

    private static IReadOnlyList<string>? TryResolveParameterNames(Assembly? assembly, string typeName, string memberName, IReadOnlyList<string> parameterTypes)
    {
        if (assembly is null) return null;
        var type = ResolveType(assembly, typeName);
        if (type is null) return null;

        if (memberName == "#ctor")
        {
            var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            return ResolveParameterNamesFromCandidates(ctors, parameterTypes, assembly);
        }

        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => string.Equals(m.Name, memberName, StringComparison.Ordinal))
            .ToArray();

        return ResolveParameterNamesFromCandidates(methods, parameterTypes, assembly);
    }

    private static IReadOnlyList<string>? ResolveParameterNamesFromCandidates(MethodBase[] candidates, IReadOnlyList<string> parameterTypes, Assembly assembly)
    {
        foreach (var candidate in candidates)
        {
            var parameters = candidate.GetParameters();
            if (!ParameterTypesMatch(parameters, parameterTypes, assembly)) continue;
            return parameters.Select(p => p.Name ?? string.Empty).ToList();
        }

        var countMatches = candidates
            .Where(m => m.GetParameters().Length == parameterTypes.Count)
            .ToArray();
        if (countMatches.Length == 1)
        {
            return countMatches[0].GetParameters().Select(p => p.Name ?? string.Empty).ToList();
        }

        return null;
    }

    private static bool ParameterTypesMatch(ParameterInfo[] parameters, IReadOnlyList<string> parameterTypes, Assembly assembly)
    {
        if (parameters.Length != parameterTypes.Count) return false;
        for (var i = 0; i < parameterTypes.Count; i++)
        {
            var resolved = ResolveXmlType(parameterTypes[i], assembly);
            if (resolved is null) return false;
            if (parameters[i].ParameterType != resolved) return false;
        }
        return true;
    }

    private static Type? ResolveXmlType(string xmlType, Assembly assembly)
    {
        if (string.IsNullOrWhiteSpace(xmlType)) return null;
        var typeName = xmlType.Trim();
        var byRef = false;
        if (typeName.EndsWith("@", StringComparison.Ordinal) || typeName.EndsWith("&", StringComparison.Ordinal))
        {
            byRef = true;
            typeName = typeName.TrimEnd('@', '&');
        }

        var arrayRanks = 0;
        while (typeName.EndsWith("[]", StringComparison.Ordinal))
        {
            arrayRanks++;
            typeName = typeName.Substring(0, typeName.Length - 2);
        }

        Type? resolved;
        var genericStart = typeName.IndexOf('{');
        if (genericStart >= 0 && typeName.EndsWith("}", StringComparison.Ordinal))
        {
            var outer = typeName.Substring(0, genericStart);
            var argsText = typeName.Substring(genericStart + 1, typeName.Length - genericStart - 2);
            var argTokens = SplitTypeArguments(argsText);
            var argTypes = new List<Type>();
            foreach (var token in argTokens)
            {
                var arg = ResolveXmlType(token, assembly);
                if (arg is null) return null;
                argTypes.Add(arg);
            }
            var genericName = $"{outer}`{argTypes.Count}";
            var genericType = ResolveType(assembly, genericName);
            if (genericType is null) return null;
            resolved = genericType.MakeGenericType(argTypes.ToArray());
        }
        else
        {
            resolved = ResolveType(assembly, typeName);
        }

        if (resolved is null) return null;
        for (var i = 0; i < arrayRanks; i++)
        {
            resolved = resolved.MakeArrayType();
        }
        if (byRef) resolved = resolved.MakeByRefType();
        return resolved;
    }

    private static List<string> SplitTypeArguments(string argsText)
    {
        var results = new List<string>();
        if (string.IsNullOrWhiteSpace(argsText)) return results;
        var sb = new StringBuilder();
        var depth = 0;
        foreach (var ch in argsText)
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

    private static Type? ResolveType(Assembly assembly, string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return null;
        var candidate = fullName;
        var type = assembly.GetType(candidate) ?? Type.GetType(candidate);
        if (type is not null) return type;

        while (true)
        {
            var lastDot = candidate.LastIndexOf('.');
            if (lastDot <= 0) break;
            candidate = candidate.Substring(0, lastDot) + "+" + candidate.Substring(lastDot + 1);
            type = assembly.GetType(candidate) ?? Type.GetType(candidate);
            if (type is not null) return type;
        }

        foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = loaded.GetType(fullName);
            if (type is not null) return type;
        }

        return null;
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
        var fallbackCss = LoadEmbeddedRaw("fallback.css");
        var cssBlock = string.IsNullOrWhiteSpace(cssLink)
            ? WrapStyle(fallbackCss)
            : cssLink;

        var indexTemplate = LoadEmbeddedRaw("index.html");
        var typeLinks = new StringBuilder();
        foreach (var type in types)
        {
            typeLinks.AppendLine($"      <a class=\"pf-api-type\" href=\"types/{type.Slug}.html\">{System.Web.HttpUtility.HtmlEncode(type.FullName)}</a>");
        }
        var searchScript = WrapScript(LoadEmbeddedRaw("search.js"));
        var indexHtml = ApplyTemplate(indexTemplate, new Dictionary<string, string?>
        {
            ["TITLE"] = System.Web.HttpUtility.HtmlEncode(options.Title),
            ["CSS"] = cssBlock,
            ["HEADER"] = header,
            ["FOOTER"] = footer,
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
            AppendMembers(memberHtml, "Methods", type.Methods);
            AppendMembers(memberHtml, "Properties", type.Properties);
            AppendMembers(memberHtml, "Fields", type.Fields);
            AppendMembers(memberHtml, "Events", type.Events);

            var summaryHtml = string.IsNullOrWhiteSpace(type.Summary)
                ? string.Empty
                : $"    <p>{System.Web.HttpUtility.HtmlEncode(type.Summary)}</p>";
            var remarksHtml = string.IsNullOrWhiteSpace(type.Remarks)
                ? string.Empty
                : $"    <div class=\"pf-api-remarks\">{System.Web.HttpUtility.HtmlEncode(type.Remarks)}</div>";

            var typeTitle = $"{type.FullName} - {options.Title}";
            var typeTemplate = LoadEmbeddedRaw("type.html");
            var typeHtml = ApplyTemplate(typeTemplate, new Dictionary<string, string?>
            {
                ["TYPE_TITLE"] = System.Web.HttpUtility.HtmlEncode(typeTitle),
                ["TYPE_FULLNAME"] = System.Web.HttpUtility.HtmlEncode(type.FullName),
                ["CSS"] = cssBlock,
                ["HEADER"] = header,
                ["FOOTER"] = footer,
                ["TYPE_SUMMARY"] = summaryHtml,
                ["TYPE_REMARKS"] = remarksHtml,
                ["MEMBERS"] = memberHtml.ToString().TrimEnd()
            });

            File.WriteAllText(Path.Combine(typesDir, $"{type.Slug}.html"), typeHtml, Encoding.UTF8);
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

    private static string LoadEmbeddedRaw(string fileName)
    {
        var assembly = typeof(WebApiDocsGenerator).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith($"Assets.ApiDocs.{fileName}", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(resourceName)) return string.Empty;
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null) return string.Empty;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string WrapStyle(string content)
        => string.IsNullOrWhiteSpace(content) ? string.Empty : $"<style>{content}</style>";

    private static string WrapScript(string content)
        => string.IsNullOrWhiteSpace(content) ? string.Empty : $"<script>{content}</script>";

    private static string ApplyTemplate(string template, IReadOnlyDictionary<string, string?> replacements)
    {
        if (string.IsNullOrWhiteSpace(template)) return string.Empty;
        var result = template;
        foreach (var kvp in replacements)
        {
            result = result.Replace($"{{{{{kvp.Key}}}}}", kvp.Value ?? string.Empty);
        }
        return result;
    }

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
