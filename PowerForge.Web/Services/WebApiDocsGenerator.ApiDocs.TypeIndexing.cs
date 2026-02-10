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
        if (type.Constructors.Count > 0)
            list.Add(("constructors", "Constructors"));
        if (type.Methods.Count > 0)
            list.Add(("methods", "Methods"));
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
        var label = System.Web.HttpUtility.HtmlEncode($"{link.Path}{suffix}");
        if (!string.IsNullOrWhiteSpace(link.Url))
        {
            var href = System.Web.HttpUtility.HtmlAttributeEncode(link.Url);
            return $"<a href=\"{href}\" target=\"_blank\" rel=\"noopener\">{label}</a>";
        }
        return $"<code>{label}</code>";
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

    private static IReadOnlyList<ApiTypeModel> GetMainTypes(IReadOnlyList<ApiTypeModel> types)
    {
        var results = new List<ApiTypeModel>();
        foreach (var name in MainTypeOrder)
        {
            var type = types.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            if (type != null)
                results.Add(type);
        }
        return results;
    }

    private static bool IsMainType(string name)
        => MainTypeOrder.Contains(name, StringComparer.OrdinalIgnoreCase);

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
            _ => "T"
        };

    private static readonly string[] KindOrder = { "class", "struct", "interface", "enum", "delegate" };

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

      private static string NormalizeDocsHomeUrl(string? url)
      {
          if (string.IsNullOrWhiteSpace(url))
              return "/docs/";
        var trimmed = url.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return EnsureTrailingSlash(trimmed);
        if (!trimmed.StartsWith("/"))
            trimmed = "/" + trimmed;
          return EnsureTrailingSlash(trimmed);
      }

      private static string ResolveBodyClass(string? value)
      {
          var trimmed = value?.Trim();
          if (string.IsNullOrWhiteSpace(trimmed))
              return "pf-api-docs";
          return trimmed;
      }
}
