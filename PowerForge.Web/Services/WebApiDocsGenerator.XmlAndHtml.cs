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
    private static bool TryParseGenericParameterToken(string typeName, out bool isMethodParameter, out int position)
    {
        isMethodParameter = false;
        position = -1;
        if (string.IsNullOrWhiteSpace(typeName)) return false;
        if (typeName.StartsWith("``", StringComparison.Ordinal))
        {
            isMethodParameter = true;
            if (typeName.Length <= 2) return false;
            return int.TryParse(typeName.Substring(2), out position) && position >= 0 && position < 128;
        }
        if (typeName.StartsWith("`", StringComparison.Ordinal))
        {
            isMethodParameter = false;
            if (typeName.Length <= 1) return false;
            return int.TryParse(typeName.Substring(1), out position) && position >= 0 && position < 128;
        }
        return false;
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

    private static string StripGenericArity(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name;
        return GenericArityRegex.Replace(name, string.Empty);
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

    private static string? GetSummary(
        XElement member,
        string memberKey,
        IReadOnlyDictionary<string, XElement> memberLookup)
    {
        var summary = member.Element("summary");
        if (summary is not null)
            return NormalizeXmlText(summary);
        return GetInheritedElement(member, memberKey, "summary", memberLookup);
    }

    private static string? GetElement(
        XElement member,
        string name,
        string memberKey,
        IReadOnlyDictionary<string, XElement> memberLookup)
    {
        var element = member.Element(name);
        if (element is not null)
            return NormalizeXmlText(element);
        return GetInheritedElement(member, memberKey, name, memberLookup);
    }

    private static List<ApiTypeParameterModel> GetTypeParameters(
        XElement member,
        string memberKey,
        IReadOnlyDictionary<string, XElement> memberLookup)
    {
        var results = ParseTypeParameters(member);
        if (results.Count > 0)
            return results;

        var inheritedTypeParams = GetInheritedElements(member, memberKey, "typeparam", memberLookup);
        if (inheritedTypeParams.Count == 0)
            return results;

        return ParseTypeParameters(inheritedTypeParams);
    }

    private static List<ApiExceptionModel> GetExceptions(
        XElement member,
        string memberKey,
        IReadOnlyDictionary<string, XElement> memberLookup)
    {
        var results = ParseExceptions(member);
        if (results.Count > 0)
            return results;

        var inheritedExceptions = GetInheritedElements(member, memberKey, "exception", memberLookup);
        if (inheritedExceptions.Count == 0)
            return results;

        return ParseExceptions(inheritedExceptions);
    }

    private static List<ApiExampleModel> GetExamples(
        XElement member,
        string memberKey,
        IReadOnlyDictionary<string, XElement> memberLookup)
    {
        var results = new List<ApiExampleModel>();
        foreach (var example in member.Elements("example"))
        {
            results.AddRange(ParseExampleBlocks(example));
        }
        if (results.Count > 0)
            return results;

        var inheritedExamples = GetInheritedElements(member, memberKey, "example", memberLookup);
        foreach (var example in inheritedExamples)
        {
            results.AddRange(ParseExampleBlocks(example));
        }
        return results;
    }

    private static List<ApiExampleModel> ParseExampleBlocks(XElement example)
    {
        var results = new List<ApiExampleModel>();
        foreach (var node in example.Nodes())
        {
            switch (node)
            {
                case XText text:
                    var normalized = Normalize(text.Value);
                    if (!string.IsNullOrWhiteSpace(normalized))
                        results.Add(new ApiExampleModel { Kind = "text", Text = normalized });
                    break;
                case XElement el:
                    var local = el.Name.LocalName;
                    if (local.Equals("code", StringComparison.OrdinalIgnoreCase))
                    {
                        var code = Dedent(el.Value.Trim('\r', '\n'));
                        if (!string.IsNullOrWhiteSpace(code))
                            results.Add(new ApiExampleModel { Kind = "code", Text = code });
                    }
                    else
                    {
                        var textBlock = NormalizeXmlText(el);
                        if (!string.IsNullOrWhiteSpace(textBlock))
                            results.Add(new ApiExampleModel { Kind = "text", Text = textBlock });
                    }
                    break;
            }
        }

        if (results.Count == 0)
        {
            var fallback = NormalizeXmlText(example);
            if (!string.IsNullOrWhiteSpace(fallback))
                results.Add(new ApiExampleModel { Kind = "text", Text = fallback });
        }

        return results;
    }

    private static List<string> GetSeeAlso(
        XElement member,
        string memberKey,
        IReadOnlyDictionary<string, XElement> memberLookup)
    {
        var results = new List<string>();
        var ownSeeAlso = member.Elements("seealso").ToList();
        foreach (var see in ownSeeAlso)
        {
            var text = NormalizeSeeAlsoElement(see);
            if (!string.IsNullOrWhiteSpace(text))
                results.Add(text);
        }
        if (results.Count > 0)
            return results;

        var inheritedSeeAlso = GetInheritedElements(member, memberKey, "seealso", memberLookup);
        foreach (var see in inheritedSeeAlso)
        {
            var text = NormalizeSeeAlsoElement(see);
            if (!string.IsNullOrWhiteSpace(text))
                results.Add(text);
        }
        return results;
    }

    private static string? NormalizeSeeAlsoElement(XElement see)
    {
        var href = NormalizeInlineHref(see.Attribute("href")?.Value);
        if (!string.IsNullOrWhiteSpace(href))
        {
            var label = Normalize(see.Value);
            if (string.IsNullOrWhiteSpace(label))
                label = href;
            return BuildHrefToken(href, label);
        }
        return NormalizeXmlText(see);
    }

    private static List<ApiTypeParameterModel> ParseTypeParameters(XElement member)
        => ParseTypeParameters(member.Elements("typeparam"));

    private static List<ApiTypeParameterModel> ParseTypeParameters(IEnumerable<XElement> typeParameters)
    {
        var results = new List<ApiTypeParameterModel>();
        foreach (var tp in typeParameters)
        {
            var name = tp.Attribute("name")?.Value;
            if (string.IsNullOrWhiteSpace(name))
                continue;
            results.Add(new ApiTypeParameterModel
            {
                Name = name,
                Summary = NormalizeXmlText(tp)
            });
        }
        return results;
    }

    private static List<ApiExceptionModel> ParseExceptions(XElement member)
        => ParseExceptions(member.Elements("exception"));

    private static List<ApiExceptionModel> ParseExceptions(IEnumerable<XElement> exceptions)
    {
        var results = new List<ApiExceptionModel>();
        foreach (var ex in exceptions)
        {
            var cref = ex.Attribute("cref")?.Value;
            var typeName = CleanCref(cref);
            if (string.IsNullOrWhiteSpace(typeName))
                continue;
            results.Add(new ApiExceptionModel
            {
                Type = typeName,
                Summary = NormalizeXmlText(ex)
            });
        }
        return results;
    }

    private static string? GetInheritedElement(
        XElement member,
        string memberKey,
        string elementName,
        IReadOnlyDictionary<string, XElement> memberLookup)
    {
        foreach (var inherited in EnumerateInheritedMembers(member, memberKey, memberLookup))
        {
            var value = inherited.Element(elementName);
            if (value is null)
                continue;
            var normalized = NormalizeXmlText(value);
            if (!string.IsNullOrWhiteSpace(normalized))
                return normalized;
        }
        return null;
    }

    private static List<XElement> GetInheritedElements(
        XElement member,
        string memberKey,
        string elementName,
        IReadOnlyDictionary<string, XElement> memberLookup)
    {
        foreach (var inherited in EnumerateInheritedMembers(member, memberKey, memberLookup))
        {
            var values = inherited.Elements(elementName).ToList();
            if (values.Count > 0)
                return values;
        }
        return new List<XElement>();
    }

    private static IEnumerable<XElement> EnumerateInheritedMembers(
        XElement member,
        string memberKey,
        IReadOnlyDictionary<string, XElement> memberLookup)
    {
        var current = member;
        var currentKey = memberKey;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var depth = 0; depth < 8; depth++)
        {
            if (!visited.Add(currentKey))
                yield break;

            var inheritDoc = current.Element("inheritdoc");
            if (inheritDoc is null)
                yield break;

            var cref = inheritDoc.Attribute("cref")?.Value;
            if (string.IsNullOrWhiteSpace(cref))
                yield break;

            var targetKey = ResolveInheritDocKey(cref, currentKey, memberLookup);
            if (string.IsNullOrWhiteSpace(targetKey))
                yield break;

            if (!memberLookup.TryGetValue(targetKey, out var inherited))
                yield break;

            yield return inherited;
            current = inherited;
            currentKey = targetKey;
        }
    }

    private static string? ResolveInheritDocKey(
        string cref,
        string currentKey,
        IReadOnlyDictionary<string, XElement> memberLookup)
    {
        var trimmed = cref.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        if (memberLookup.ContainsKey(trimmed))
            return trimmed;

        var currentPrefix = currentKey.Length > 1 && currentKey[1] == ':' ? currentKey[0] : '\0';
        if (trimmed.Length > 2 && trimmed[1] == ':')
            return trimmed;

        if (currentPrefix != '\0')
        {
            var samePrefix = $"{currentPrefix}:{trimmed}";
            if (memberLookup.ContainsKey(samePrefix))
                return samePrefix;
        }

        var typeKey = $"T:{trimmed}";
        if (memberLookup.ContainsKey(typeKey))
            return typeKey;

        return null;
    }

    private static string Dedent(string code)
    {
        var lines = code.Split('\n');
        var minIndent = int.MaxValue;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;
            var indent = 0;
            while (indent < line.Length && line[indent] == ' ') indent++;
            if (indent < minIndent) minIndent = indent;
        }
        if (minIndent == 0 || minIndent == int.MaxValue) return code;
        var sb = new StringBuilder();
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0) sb.Append('\n');
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
                sb.Append("");
            else
                sb.Append(line.Substring(Math.Min(minIndent, line.Length)));
        }
        return sb.ToString();
    }

    private static string Normalize(string value)
    {
        return WhitespaceRegex.Replace(value, " ").Trim();
    }

    private static string CleanCref(string? cref)
    {
        if (string.IsNullOrWhiteSpace(cref)) return string.Empty;
        var cleaned = cref;
        var colonIdx = cleaned.IndexOf(':');
        if (colonIdx >= 0 && colonIdx + 1 < cleaned.Length)
            cleaned = cleaned.Substring(colonIdx + 1);
        return cleaned.Trim();
    }

    private static bool IsConstructorName(string name)
        => name == "#ctor" || name == ".ctor" || name == ".cctor";

    private static string GetShortTypeName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return fullName;
        var lastDot = fullName.LastIndexOf('.');
        var name = lastDot > -1 ? fullName.Substring(lastDot + 1) : fullName;
        return StripGenericArity(name);
    }

    private static string? NormalizeXmlText(XElement element)
    {
        var text = string.Concat(element.Nodes().Select(n =>
        {
            if (n is XText txt) return txt.Value;
            if (n is XElement el)
            {
                return el.Name.LocalName switch
                {
                    "see" => RenderSeeLikeElement(el),
                    "seealso" => RenderSeeLikeElement(el),
                    "a" => RenderAnchorElement(el),
                    "paramref" => el.Attribute("name")?.Value ?? el.Value,
                    "typeparamref" => el.Attribute("name")?.Value ?? el.Value,
                    "c" => el.Value,
                    "code" => $" {el.Value} ",
                    "para" => $" {el.Value} ",
                    _ => el.Value
                };
            }
            return string.Empty;
        }));

        return string.IsNullOrWhiteSpace(text) ? null : Normalize(text);
    }

    private static string RenderSeeLikeElement(XElement el)
    {
        var href = NormalizeInlineHref(el.Attribute("href")?.Value);
        if (!string.IsNullOrWhiteSpace(href))
        {
            var label = Normalize(el.Value);
            if (string.IsNullOrWhiteSpace(label))
                label = href;
            return BuildHrefToken(href, label);
        }
        return RenderCref(el);
    }

    private static string RenderAnchorElement(XElement el)
    {
        var href = NormalizeInlineHref(el.Attribute("href")?.Value);
        if (string.IsNullOrWhiteSpace(href))
            return Normalize(el.Value);

        var label = Normalize(el.Value);
        if (string.IsNullOrWhiteSpace(label))
            label = href;
        return BuildHrefToken(href, label);
    }

    private static string RenderCref(XElement el)
    {
        var cref = el.Attribute("cref")?.Value;
        if (!string.IsNullOrWhiteSpace(cref))
        {
            var cleaned = cref;
            var colonIdx = cleaned.IndexOf(':');
            if (colonIdx >= 0 && colonIdx + 1 < cleaned.Length)
                cleaned = cleaned.Substring(colonIdx + 1);
            return $"[[cref:{cleaned}]]";
        }

        var langword = el.Attribute("langword")?.Value;
        if (!string.IsNullOrWhiteSpace(langword))
            return langword;

        return el.Value;
    }

    private static string BuildHrefToken(string href, string label)
    {
        var encodedHref = Uri.EscapeDataString(href);
        var encodedLabel = Uri.EscapeDataString(label);
        return $"[[href:{encodedHref}|{encodedLabel}]]";
    }

    private static string NormalizeInlineHref(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
            return string.Empty;

        var trimmed = href.Trim();
        if (trimmed.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        return trimmed;
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
        slug = GenericArityRegex.Replace(slug, string.Empty);
        slug = SlugNonAlnumRegex.Replace(slug, "-");
        slug = SlugDashRegex.Replace(slug, "-").Trim('-');
        return slug;
    }

    private static void WriteJson(string path, object payload)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json, Encoding.UTF8);
    }
}
