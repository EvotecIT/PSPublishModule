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
    private static IReadOnlyDictionary<string, string> BuildTypeSlugMap(IReadOnlyList<ApiTypeModel> types)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var shortNameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in types)
        {
            if (!string.IsNullOrWhiteSpace(type.FullName))
                map[type.FullName] = type.Slug;
            if (!string.IsNullOrWhiteSpace(type.Name))
            {
                shortNameCounts.TryGetValue(type.Name, out var count);
                shortNameCounts[type.Name] = count + 1;
            }
        }
        foreach (var type in types)
        {
            if (string.IsNullOrWhiteSpace(type.Name)) continue;
            if (shortNameCounts.TryGetValue(type.Name, out var count) && count == 1)
                map[type.Name] = type.Slug;
        }
        return map;
    }

    private static IReadOnlyDictionary<string, ApiTypeModel> BuildTypeIndex(IReadOnlyList<ApiTypeModel> types)
    {
        var map = new Dictionary<string, ApiTypeModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in types)
        {
            var key = NormalizeTypeName(type.FullName);
            if (string.IsNullOrWhiteSpace(key)) continue;
            map[key] = type;
        }
        return map;
    }

    private static IReadOnlyDictionary<string, List<ApiTypeModel>> BuildDerivedTypeMap(
        IReadOnlyList<ApiTypeModel> types,
        IReadOnlyDictionary<string, ApiTypeModel> typeIndex)
    {
        var map = new Dictionary<string, List<ApiTypeModel>>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in types)
        {
            var baseKey = NormalizeTypeName(type.BaseType);
            if (string.IsNullOrWhiteSpace(baseKey)) continue;
            if (!typeIndex.ContainsKey(baseKey)) continue;
            if (!map.TryGetValue(baseKey, out var list))
            {
                list = new List<ApiTypeModel>();
                map[baseKey] = list;
            }
            list.Add(type);
        }

        foreach (var list in map.Values)
        {
            list.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.OrdinalIgnoreCase));
        }
        return map;
    }

    private static List<string> BuildInheritanceChain(ApiTypeModel type, IReadOnlyDictionary<string, ApiTypeModel> typeIndex)
    {
        var chain = new List<string>();
        var current = type.BaseType;
        var guard = 0;
        while (!string.IsNullOrWhiteSpace(current) && guard++ < 32)
        {
            chain.Add(current);
            var key = NormalizeTypeName(current);
            if (!typeIndex.TryGetValue(key, out var baseType) || string.IsNullOrWhiteSpace(baseType.BaseType))
                break;
            if (string.Equals(baseType.BaseType, current, StringComparison.OrdinalIgnoreCase))
                break;
            current = baseType.BaseType;
        }
        if (chain.Count == 0 && string.Equals(type.Kind, "Class", StringComparison.OrdinalIgnoreCase))
            chain.Add("System.Object");
        chain.Reverse();
        return chain;
    }

    private static List<ApiTypeModel> GetDerivedTypes(
        ApiTypeModel type,
        IReadOnlyDictionary<string, List<ApiTypeModel>> derivedMap)
    {
        var key = NormalizeTypeName(type.FullName);
        if (string.IsNullOrWhiteSpace(key)) return new List<ApiTypeModel>();
        return derivedMap.TryGetValue(key, out var list)
            ? list
            : new List<ApiTypeModel>();
    }

    private static List<(string id, string label)> BuildTypeToc(ApiTypeModel type, bool hasInheritance, bool hasDerived)
    {
        var isPowerShellCommand = IsPowerShellCommandType(type);
        var hasPowerShellCommonParameters = isPowerShellCommand && HasPowerShellCommonParameters(type);
        var methodSectionId = isPowerShellCommand ? "syntax" : "methods";
        var methodSectionLabel = isPowerShellCommand ? "Syntax" : "Methods";
        var list = new List<(string id, string label)>
        {
            ("overview", "Overview")
        };
        if (hasInheritance)
            list.Add(("inheritance", "Inheritance"));
        if (hasDerived)
            list.Add(("derived-types", "Derived Types"));
        if (!string.IsNullOrWhiteSpace(type.Remarks))
            list.Add(("remarks", "Remarks"));
        if (type.TypeParameters.Count > 0)
            list.Add(("type-parameters", "Type Parameters"));
        if (type.Examples.Count > 0)
            list.Add(("examples", "Examples"));
        if (type.SeeAlso.Count > 0)
            list.Add(("see-also", "See Also"));
        if (hasPowerShellCommonParameters)
            list.Add(("common-parameters", "Common Parameters"));
        if (type.Constructors.Count > 0)
            list.Add(("constructors", "Constructors"));
        if (type.Methods.Count > 0)
            list.Add((methodSectionId, methodSectionLabel));
        if (type.Properties.Count > 0)
            list.Add(("properties", "Properties"));
        if (type.Fields.Count > 0)
            list.Add((type.Kind == "Enum" ? "values" : "fields", type.Kind == "Enum" ? "Values" : "Fields"));
        if (type.Events.Count > 0)
            list.Add(("events", "Events"));
        if (type.ExtensionMethods.Count > 0)
            list.Add(("extensions", "Extension Methods"));
        return list;
    }

    private static bool HasPowerShellCommonParameters(ApiTypeModel type)
    {
        if (type is null || type.Methods.Count == 0)
            return false;

        return type.Methods.Any(static member => member.IncludesCommonParameters);
    }

    private static string RenderLinkedText(string text, string baseUrl, IReadOnlyDictionary<string, string> slugMap)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var encoded = System.Web.HttpUtility.HtmlEncode(text);
        var linked = CrefTokenRegex.Replace(encoded, match =>
        {
            var name = match.Groups["name"].Value;
            return LinkifyType(name, baseUrl, slugMap);
        });
        linked = HrefTokenRegex.Replace(linked, match =>
        {
            var href = TryDecodeHrefToken(match.Groups["url"].Value);
            var label = TryDecodeHrefToken(match.Groups["label"].Value);
            if (string.IsNullOrWhiteSpace(href))
                return System.Web.HttpUtility.HtmlEncode(label);

            var safeHref = System.Web.HttpUtility.HtmlAttributeEncode(href);
            var safeLabel = System.Web.HttpUtility.HtmlEncode(string.IsNullOrWhiteSpace(label) ? href : label);
            if (IsExternal(href))
                return $"<a href=\"{safeHref}\" target=\"_blank\" rel=\"noopener\">{safeLabel}</a>";
            return $"<a href=\"{safeHref}\">{safeLabel}</a>";
        });
        linked = AboutTokenRegex.Replace(linked, match =>
        {
            var token = match.Value;
            if (!slugMap.TryGetValue(token, out var slug))
                return token;

            var href = BuildDocsTypeUrl(baseUrl, slug);
            var safeHref = System.Web.HttpUtility.HtmlAttributeEncode(href);
            var safeLabel = System.Web.HttpUtility.HtmlEncode(token);
            return $"<a href=\"{safeHref}\">{safeLabel}</a>";
        });
        return linked;
    }

    private static string StripCrefTokens(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var cleaned = CrefTokenRegex.Replace(text, match =>
        {
            var name = match.Groups["name"].Value;
            return GetDisplayTypeName(name);
        });
        cleaned = HrefTokenRegex.Replace(cleaned, match =>
        {
            var href = TryDecodeHrefToken(match.Groups["url"].Value);
            var label = TryDecodeHrefToken(match.Groups["label"].Value);
            if (!string.IsNullOrWhiteSpace(label))
                return label;
            return href;
        });
        return cleaned;
    }

    private static string TryDecodeHrefToken(string encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
            return string.Empty;
        try
        {
            return Uri.UnescapeDataString(encoded);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string LinkifyType(string? name, string baseUrl, IReadOnlyDictionary<string, string> slugMap)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;
        var cleaned = name.Replace("+", ".").Trim();
        var display = GetDisplayTypeName(cleaned);
        if (slugMap.TryGetValue(cleaned, out var slug))
        {
            var href = BuildDocsTypeUrl(baseUrl, slug);
            var safeHref = System.Web.HttpUtility.HtmlAttributeEncode(href);
            return $"<a href=\"{safeHref}\">{System.Web.HttpUtility.HtmlEncode(display)}</a>";
        }
        if (slugMap.TryGetValue(display, out var shortSlug))
        {
            var href = BuildDocsTypeUrl(baseUrl, shortSlug);
            var safeHref = System.Web.HttpUtility.HtmlAttributeEncode(href);
            return $"<a href=\"{safeHref}\">{System.Web.HttpUtility.HtmlEncode(display)}</a>";
        }
        return System.Web.HttpUtility.HtmlEncode(display);
    }

    private static string RenderSourceLink(ApiSourceLink link)
    {
        var suffix = link.Line > 0 ? $":{link.Line}" : string.Empty;
        var displayPath = NormalizeSourceDisplayPath(link.Path);
        var label = System.Web.HttpUtility.HtmlEncode($"{displayPath}{suffix}");
        if (!string.IsNullOrWhiteSpace(link.Url))
        {
            var href = System.Web.HttpUtility.HtmlAttributeEncode(link.Url);
            return $"<a href=\"{href}\" target=\"_blank\" rel=\"noopener\">{label}</a>";
        }
        return $"<code>{label}</code>";
    }

    private static string NormalizeSourceDisplayPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var normalized = path.Replace('\\', '/').Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 2 && string.Equals(parts[0], parts[1], StringComparison.OrdinalIgnoreCase))
            return string.Join("/", parts.Skip(1));

        return string.Join("/", parts);
    }

    private static string? RenderTypeSourceAction(ApiSourceLink? link)
    {
        if (link is null || string.IsNullOrWhiteSpace(link.Url))
            return null;

        if (TryBuildGitHubEditUrl(link.Url, out var editUrl))
        {
            var href = System.Web.HttpUtility.HtmlAttributeEncode(editUrl);
            return $"<a class=\"type-source-action\" href=\"{href}\" target=\"_blank\" rel=\"noopener\">Edit on GitHub</a>";
        }

        var sourceHref = System.Web.HttpUtility.HtmlAttributeEncode(link.Url);
        return $"<a class=\"type-source-action\" href=\"{sourceHref}\" target=\"_blank\" rel=\"noopener\">View source</a>";
    }

    private static bool TryBuildGitHubEditUrl(string sourceUrl, out string editUrl)
    {
        editUrl = string.Empty;
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri))
            return false;
        if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            return false;

        var path = uri.AbsolutePath;
        var blobMarker = "/blob/";
        var blobIndex = path.IndexOf(blobMarker, StringComparison.OrdinalIgnoreCase);
        if (blobIndex < 0)
            return false;

        var repoPath = path.Substring(0, blobIndex);
        var filePath = path.Substring(blobIndex + blobMarker.Length);
        if (string.IsNullOrWhiteSpace(repoPath) || string.IsNullOrWhiteSpace(filePath))
            return false;

        editUrl = $"{uri.Scheme}://{uri.Host}{repoPath}/edit/{filePath}";
        return true;
    }

    private static Dictionary<string, object?>? BuildSourceJson(ApiSourceLink? source)
    {
        if (source is null) return null;
        return new Dictionary<string, object?>
        {
            ["path"] = source.Path,
            ["line"] = source.Line,
            ["url"] = source.Url
        };
    }

    private static string GetDisplayTypeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name;
        var normalized = name.Replace("{", "<").Replace("}", ">");
        normalized = GenericArityRegex.Replace(normalized, string.Empty);
        var lastDot = normalized.LastIndexOf('.');
        return lastDot >= 0 ? normalized.Substring(lastDot + 1) : normalized;
    }

    private static IReadOnlyList<ApiTypeModel> GetMainTypes(IReadOnlyList<ApiTypeModel> types, WebApiDocsOptions options)
    {
        if (types.Count == 0)
            return Array.Empty<ApiTypeModel>();

        var results = new List<ApiTypeModel>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1) Explicit step-level quickstart types (highest precedence).
        AddMainTypeMatches(results, seen, types, options.QuickStartTypeNames);
        if (results.Count > 0)
            return results;

        // 2) Built-in curated defaults (keeps existing behavior for CodeGlyphX).
        AddMainTypeMatches(results, seen, types, MainTypeOrder);
        if (results.Count > 0)
            return results;

        // 3) Generic fallback: pick short, likely entry-point names from top namespaces.
        var candidates = types
            .Where(static t => string.Equals(t.Kind, "Class", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(t.Kind, "Struct", StringComparison.OrdinalIgnoreCase))
            .Select(type => new
            {
                Type = type,
                Score = ScoreMainTypeCandidate(type)
            })
            .OrderByDescending(static x => x.Score)
            .ThenBy(static x => x.Type.Name, StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .Select(static x => x.Type)
            .ToList();

        results.AddRange(candidates);
        return results;
    }

    private static void ValidateConfiguredQuickStartTypes(IReadOnlyList<ApiTypeModel> types, WebApiDocsOptions options, List<string> warnings)
    {
        if (types is null || options is null || warnings is null)
            return;
        if (options.QuickStartTypeNames.Count == 0)
            return;

        var available = new HashSet<string>(
            types
                .Where(static t => !string.IsNullOrWhiteSpace(t.Name))
                .Select(static t => t.Name),
            StringComparer.OrdinalIgnoreCase);

        var missing = options.QuickStartTypeNames
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => !available.Contains(name))
            .ToList();

        if (missing.Count == 0)
            return;

        var preview = string.Join(", ", missing.Take(8));
        var suffix = missing.Count > 8 ? $" (+{missing.Count - 8} more)" : string.Empty;
        warnings.Add($"API docs: quickStartTypes configured names not found in generated types: {preview}{suffix}.");
    }

    private static void AddMainTypeMatches(
        List<ApiTypeModel> results,
        HashSet<string> seen,
        IReadOnlyList<ApiTypeModel> types,
        IEnumerable<string> preferredNames)
    {
        foreach (var name in preferredNames)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var type = types.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            if (type is null)
                continue;

            if (seen.Add(type.FullName))
                results.Add(type);
        }
    }

    private static int ScoreMainTypeCandidate(ApiTypeModel type)
    {
        var score = 0;
        var name = type.Name ?? string.Empty;

        if (name.Length <= 12) score += 12;
        if (name.Length <= 20) score += 6;

        if (name.EndsWith("Options", StringComparison.OrdinalIgnoreCase)) score -= 12;
        if (name.EndsWith("Settings", StringComparison.OrdinalIgnoreCase)) score -= 12;
        if (name.EndsWith("Extensions", StringComparison.OrdinalIgnoreCase)) score -= 18;
        if (name.EndsWith("Builder", StringComparison.OrdinalIgnoreCase)) score -= 10;
        if (name.EndsWith("Result", StringComparison.OrdinalIgnoreCase)) score -= 8;
        if (name.EndsWith("Exception", StringComparison.OrdinalIgnoreCase)) score -= 18;
        if (name.EndsWith("Attribute", StringComparison.OrdinalIgnoreCase)) score -= 14;

        if (!string.IsNullOrWhiteSpace(type.Summary)) score += 5;

        var ns = type.Namespace ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(ns))
        {
            var depth = ns.Split('.', StringSplitOptions.RemoveEmptyEntries).Length;
            if (depth <= 2) score += 8;
            else if (depth == 3) score += 4;
        }

        return score;
    }

    private static string GetShortNamespace(string ns)
    {
        if (string.IsNullOrWhiteSpace(ns)) return "(global)";
        var parts = ns.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? ns : parts[^1];
    }

    private static string GetTypeIcon(string? kind)
        => kind switch
        {
            "Class" => "C",
            "Struct" => "S",
            "Interface" => "I",
            "Enum" => "E",
            "Delegate" => "D",
            "Cmdlet" => "PS",
            "Function" => "Fn",
            "Alias" => "Al",
            "About" => "?",
            "Command" => "PS",
            _ => "T"
        };

    private static readonly string[] KindOrder = { "class", "struct", "interface", "enum", "delegate", "cmdlet", "function", "alias", "about", "command" };

      private static List<KindFilter> BuildKindFilters(IReadOnlyList<ApiTypeModel> types)
      {
          var available = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
          foreach (var type in types)
          {
              var kind = NormalizeKind(type.Kind);
              available.TryGetValue(kind, out var count);
              available[kind] = count + 1;
          }
          return KindOrder
              .Where(k => available.ContainsKey(k))
              .Select(k => new KindFilter(k, available[k]))
              .ToList();
      }

      private static string GetKindLabel(string kind, int count)
          => kind switch
          {
              "class" => $"Classes ({count})",
              "struct" => $"Structs ({count})",
              "interface" => $"Interfaces ({count})",
              "enum" => $"Enums ({count})",
              "delegate" => $"Delegates ({count})",
              "cmdlet" => $"Cmdlets ({count})",
              "function" => $"Functions ({count})",
              "alias" => $"Aliases ({count})",
              "about" => $"About ({count})",
              "command" => $"Commands ({count})",
              _ => $"Types ({count})"
          };

      private static string NormalizeKind(string? kind)
          => string.IsNullOrWhiteSpace(kind) ? "class" : kind.ToLowerInvariant();

      private sealed class KindFilter
      {
          public KindFilter(string kind, int count)
          {
              Kind = kind;
              Count = count;
          }
          public string Kind { get; }
          public int Count { get; }
      }

    private static string Truncate(string? value, int length)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return value.Length <= length ? value : value.Substring(0, length).Trim() + "...";
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

    private static void GenerateDocsSitemap(string outputPath, string baseUrl, IReadOnlyList<ApiTypeModel> types)
    {
        var sb = new StringBuilder();
        var baseTrim = baseUrl.TrimEnd('/');
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
        foreach (var type in types)
        {
            sb.AppendLine("  <url>");
            sb.AppendLine($"    <loc>{baseTrim}/{type.Slug}/</loc>");
            sb.AppendLine($"    <lastmod>{today}</lastmod>");
            sb.AppendLine("    <changefreq>monthly</changefreq>");
            sb.AppendLine("    <priority>0.5</priority>");
            sb.AppendLine("  </url>");
        }
        sb.AppendLine("</urlset>");
        File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
    }

      private static string BuildDocsTypeUrl(string baseUrl, string slug)
      {
          var baseTrim = baseUrl.TrimEnd('/');
          return EnsureTrailingSlash($"{baseTrim}/{slug}");
      }

      private static string BuildSidebarClass(string? position)
      {
          if (string.IsNullOrWhiteSpace(position))
              return string.Empty;
          var normalized = position.Trim().ToLowerInvariant();
          return normalized == "right" ? " sidebar-right" : string.Empty;
      }

    private static string EnsureTrailingSlash(string url)
        => url.EndsWith("/", StringComparison.Ordinal) ? url : $"{url}/";

      private static string NormalizeDocsHomeUrl(string? url, string? baseUrl)
      {
          if (string.IsNullOrWhiteSpace(url))
              return InferDocsHomeUrlFromBaseUrl(baseUrl);

          var trimmed = url.Trim();
          if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
              trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
              return EnsureTrailingSlash(trimmed);

          if (!trimmed.StartsWith("/", StringComparison.Ordinal))
              trimmed = "/" + trimmed;

          return EnsureTrailingSlash(trimmed);
      }

      private static string InferDocsHomeUrlFromBaseUrl(string? baseUrl)
      {
          if (string.IsNullOrWhiteSpace(baseUrl))
              return "/docs/";

          var trimmed = baseUrl.Trim();
          if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute) &&
              (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
          {
              var inferredPath = InferDocsPathFromApiPath(absolute.AbsolutePath);
              var builder = new UriBuilder(absolute)
              {
                  Path = EnsureTrailingSlash(inferredPath),
                  Query = string.Empty,
                  Fragment = string.Empty
              };
              return builder.Uri.ToString();
          }

          if (!trimmed.StartsWith("/", StringComparison.Ordinal))
              trimmed = "/" + trimmed;

          return EnsureTrailingSlash(InferDocsPathFromApiPath(trimmed));
      }

      private static string InferDocsPathFromApiPath(string apiPath)
      {
          if (string.IsNullOrWhiteSpace(apiPath))
              return "/docs/";

          var normalized = apiPath.Trim();
          if (!normalized.StartsWith("/", StringComparison.Ordinal))
              normalized = "/" + normalized;

          if (!normalized.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
              return "/docs/";

          if (normalized.Length > 4 && normalized[4] != '/')
              return "/docs/";

          var suffix = normalized.Length > 4 ? normalized.Substring(4) : string.Empty;
          return $"/docs{suffix}";
      }

      private static string ResolveBodyClass(string? value)
      {
          var trimmed = value?.Trim();
          if (string.IsNullOrWhiteSpace(trimmed))
              return "pf-api-docs";
          return trimmed;
      }
}
