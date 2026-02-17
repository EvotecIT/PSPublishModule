using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge.Web;

public static partial class WebApiDocsGenerator
{
    private sealed class ApiXrefReference
    {
        public string Uid { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Href { get; set; } = string.Empty;
        public string[] Aliases { get; set; } = Array.Empty<string>();
    }

    private static string? WriteXrefMap(
        string outputPath,
        WebApiDocsOptions options,
        IReadOnlyList<ApiTypeModel> types,
        string? assemblyName,
        string? assemblyVersion,
        List<string> warnings)
    {
        if (options is null || !options.GenerateXrefMap)
            return null;

        var configuredPath = string.IsNullOrWhiteSpace(options.XrefMapPath)
            ? "xrefmap.json"
            : options.XrefMapPath!.Trim();
        var xrefPath = Path.IsPathRooted(configuredPath)
            ? Path.GetFullPath(configuredPath)
            : Path.Combine(outputPath, configuredPath);

        try
        {
            var references = BuildXrefReferences(options, types, warnings);
            var payload = new Dictionary<string, object?>
            {
                ["generatedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["assembly"] = new Dictionary<string, object?>
                {
                    ["assemblyName"] = assemblyName ?? string.Empty,
                    ["assemblyVersion"] = assemblyVersion
                },
                ["referenceCount"] = references.Count,
                ["references"] = references
                    .Select(reference => new Dictionary<string, object?>
                    {
                        ["uid"] = reference.Uid,
                        ["name"] = reference.Name,
                        ["href"] = reference.Href,
                        ["aliases"] = reference.Aliases
                    })
                    .ToArray()
            };

            var parent = Path.GetDirectoryName(xrefPath);
            if (!string.IsNullOrWhiteSpace(parent))
                Directory.CreateDirectory(parent);
            WriteJson(xrefPath, payload);
            return xrefPath;
        }
        catch (Exception ex)
        {
            warnings?.Add($"API docs xref: failed to write map '{xrefPath}' ({ex.GetType().Name}: {ex.Message})");
            return null;
        }
    }

    private static List<ApiXrefReference> BuildXrefReferences(
        WebApiDocsOptions options,
        IReadOnlyList<ApiTypeModel> types,
        List<string>? warnings)
    {
        var safeTypes = types ?? Array.Empty<ApiTypeModel>();
        var useHtmlLinks = IsHtmlXrefFormat(options.Format);
        var baseUrl = NormalizeXrefBaseUrl(options.BaseUrl);
        var shortNameCounts = BuildCSharpShortNameCounts(safeTypes, options.Type);
        var enabledMemberKinds = ResolveEnabledMemberXrefKinds(options, warnings);
        var maxPerType = options.MemberXrefMaxPerType <= 0 ? int.MaxValue : options.MemberXrefMaxPerType;
        var refs = new Dictionary<string, ApiXrefReference>(StringComparer.OrdinalIgnoreCase);

        foreach (var type in safeTypes.OrderBy(static t => t.FullName, StringComparer.OrdinalIgnoreCase))
        {
            var uid = GetXrefUid(type, options.Type);
            if (string.IsNullOrWhiteSpace(uid))
                continue;

            var slug = string.IsNullOrWhiteSpace(type.Slug) ? Slugify(uid) : type.Slug;
            var href = BuildXrefHref(baseUrl, slug, useHtmlLinks);
            if (string.IsNullOrWhiteSpace(href))
                continue;

            var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (options.Type == ApiDocsType.CSharp)
                AddCSharpXrefAliases(aliases, type, shortNameCounts);
            else
                AddPowerShellXrefAliases(aliases, type);

            AddXrefReference(
                refs,
                uid,
                string.IsNullOrWhiteSpace(type.Name) ? uid : type.Name,
                href,
                aliases);

            if (options.GenerateMemberXrefs && options.Type == ApiDocsType.CSharp)
            {
                foreach (var member in BuildCSharpMemberReferences(type, href, enabledMemberKinds, maxPerType))
                {
                    AddXrefReference(
                        refs,
                        member.Uid,
                        member.Name,
                        member.Href,
                        member.Aliases);
                }
            }
            else if (options.GenerateMemberXrefs && options.Type == ApiDocsType.PowerShell)
            {
                foreach (var parameter in BuildPowerShellParameterReferences(type, href, enabledMemberKinds, maxPerType))
                {
                    AddXrefReference(
                        refs,
                        parameter.Uid,
                        parameter.Name,
                        parameter.Href,
                        parameter.Aliases);
                }
            }
        }

        return refs.Values
            .OrderBy(static reference => reference.Uid, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, int> BuildCSharpShortNameCounts(IReadOnlyList<ApiTypeModel> types, ApiDocsType type)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (type != ApiDocsType.CSharp || types.Count == 0)
            return counts;

        foreach (var item in types)
        {
            var shortName = GetShortTypeName(item.FullName);
            if (string.IsNullOrWhiteSpace(shortName))
                continue;
            counts.TryGetValue(shortName, out var count);
            counts[shortName] = count + 1;
        }

        return counts;
    }

    private static string GetXrefUid(ApiTypeModel type, ApiDocsType typeKind)
    {
        if (typeKind == ApiDocsType.CSharp)
            return (type.FullName ?? string.Empty).Trim();

        var name = string.IsNullOrWhiteSpace(type.Name) ? type.FullName : type.Name;
        return (name ?? string.Empty).Trim();
    }

    private static string NormalizeXrefBaseUrl(string? baseUrl)
    {
        var value = (baseUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
            return "/api";
        return value.TrimEnd('/');
    }

    private static string BuildXrefHref(string baseUrl, string slug, bool useHtmlLinks)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(slug))
            return string.Empty;
        if (useHtmlLinks)
            return $"{baseUrl}/{slug}/";
        return $"{baseUrl}/types/{slug}.json";
    }

    private static bool IsHtmlXrefFormat(string? format)
    {
        var normalized = (format ?? "json").Trim().ToLowerInvariant();
        return normalized is "hybrid" or "html" or "both";
    }

    private static void AddCSharpXrefAliases(
        HashSet<string> aliases,
        ApiTypeModel type,
        IReadOnlyDictionary<string, int> shortNameCounts)
    {
        var fullName = (type.FullName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(fullName))
            return;

        aliases.Add("T:" + fullName);
        aliases.Add("global::" + fullName);

        var normalized = fullName.Replace('+', '.');
        aliases.Add(normalized);
        aliases.Add("T:" + normalized);

        var noArity = StripGenericArity(normalized);
        if (!string.Equals(noArity, normalized, StringComparison.Ordinal))
        {
            aliases.Add(noArity);
            aliases.Add("T:" + noArity);
        }

        var shortName = GetShortTypeName(fullName);
        if (!string.IsNullOrWhiteSpace(shortName) &&
            shortNameCounts.TryGetValue(shortName, out var count) &&
            count == 1)
        {
            aliases.Add(shortName);
        }
    }

    private static void AddPowerShellXrefAliases(HashSet<string> aliases, ApiTypeModel type)
    {
        var name = (string.IsNullOrWhiteSpace(type.Name) ? type.FullName : type.Name)?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;

        aliases.Add("ps:" + name);
        aliases.Add("command:" + name);

        if (!string.IsNullOrWhiteSpace(type.Namespace))
        {
            var module = type.Namespace.Trim();
            aliases.Add(module + "\\" + name);
            aliases.Add(module + "::" + name);
            aliases.Add(module + ":" + name);
        }

        if (name.StartsWith("about_", StringComparison.OrdinalIgnoreCase))
        {
            aliases.Add("about:" + name);
            var simple = name.Substring("about_".Length);
            if (!string.IsNullOrWhiteSpace(simple))
                aliases.Add("about:" + simple);
        }
    }

    private static ISet<string> ResolveEnabledMemberXrefKinds(WebApiDocsOptions options, List<string>? warnings)
    {
        var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "constructors",
            "methods",
            "properties",
            "fields",
            "events",
            "extensions",
            "parameters"
        };

        if (options.MemberXrefKinds.Count == 0)
            return all;

        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kind in options.MemberXrefKinds)
        {
            if (string.IsNullOrWhiteSpace(kind))
                continue;

            var normalized = kind.Trim().ToLowerInvariant();
            if (normalized == "all")
            {
                foreach (var value in all)
                    selected.Add(value);
                continue;
            }
            if (normalized == "none")
                continue;

            if (!all.Contains(normalized))
            {
                warnings?.Add($"API docs xref: unsupported member kind '{kind}' ignored. Supported: constructors, methods, properties, fields, events, extensions, parameters.");
                continue;
            }

            selected.Add(normalized);
        }

        return selected;
    }

    private static IEnumerable<ApiXrefReference> BuildCSharpMemberReferences(
        ApiTypeModel type,
        string typeHref,
        ISet<string> enabledKinds,
        int maxPerType)
    {
        if (type is null || string.IsNullOrWhiteSpace(type.FullName) || string.IsNullOrWhiteSpace(typeHref))
            return Array.Empty<ApiXrefReference>();
        if (enabledKinds.Count == 0 || maxPerType <= 0)
            return Array.Empty<ApiXrefReference>();

        var references = new List<ApiXrefReference>();
        var memberAnchors = BuildMemberAnchorsByReference(type);
        var emitted = 0;

        if (enabledKinds.Contains("constructors"))
            emitted += AddCSharpMemberList("M", "constructor", type, type.Constructors, typeHref, memberAnchors, references, maxPerType - emitted);
        if (enabledKinds.Contains("methods") && emitted < maxPerType)
            emitted += AddCSharpMemberList("M", "method", type, type.Methods, typeHref, memberAnchors, references, maxPerType - emitted);
        if (enabledKinds.Contains("properties") && emitted < maxPerType)
            emitted += AddCSharpMemberList("P", "property", type, type.Properties, typeHref, memberAnchors, references, maxPerType - emitted);
        if (enabledKinds.Contains("fields") && emitted < maxPerType)
            emitted += AddCSharpMemberList("F", "field", type, type.Fields, typeHref, memberAnchors, references, maxPerType - emitted);
        if (enabledKinds.Contains("events") && emitted < maxPerType)
            emitted += AddCSharpMemberList("E", "event", type, type.Events, typeHref, memberAnchors, references, maxPerType - emitted);
        if (enabledKinds.Contains("extensions") && emitted < maxPerType)
            emitted += AddCSharpMemberList("M", "extension", type, type.ExtensionMethods, typeHref, memberAnchors, references, maxPerType - emitted);

        return references;
    }

    private static IEnumerable<ApiXrefReference> BuildPowerShellParameterReferences(
        ApiTypeModel type,
        string typeHref,
        ISet<string> enabledKinds,
        int maxPerType)
    {
        if (type is null || string.IsNullOrWhiteSpace(type.Name) || string.IsNullOrWhiteSpace(typeHref))
            return Array.Empty<ApiXrefReference>();
        if (!enabledKinds.Contains("parameters") || maxPerType <= 0)
            return Array.Empty<ApiXrefReference>();

        if (type.Methods.Count == 0)
            return Array.Empty<ApiXrefReference>();

        var command = type.Name.Trim();
        var module = (type.Namespace ?? string.Empty).Trim();
        var memberAnchors = BuildMemberAnchorsByReference(type);
        var refs = new Dictionary<string, ApiXrefReference>(StringComparer.OrdinalIgnoreCase);
        var emitted = 0;
        foreach (var member in type.Methods)
        {
            if (emitted >= maxPerType)
                break;
            if (!memberAnchors.TryGetValue(member, out var anchor) || string.IsNullOrWhiteSpace(anchor))
                continue;

            var href = AppendMemberAnchor(typeHref, anchor);
            foreach (var parameter in member.Parameters)
            {
                if (emitted >= maxPerType)
                    break;
                var paramName = parameter?.Name?.Trim();
                if (string.IsNullOrWhiteSpace(paramName))
                    continue;

                var uid = $"parameter:{command}.{paramName}";
                var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    $"{command}.{paramName}",
                    $"{command}/{paramName}",
                    $"param:{command}.{paramName}",
                    $"parameter:{command}/{paramName}"
                };
                if (!string.IsNullOrWhiteSpace(module))
                {
                    aliases.Add($"{module}\\{command}.{paramName}");
                    aliases.Add($"{module}:{command}.{paramName}");
                    aliases.Add($"{module}::{command}.{paramName}");
                }
                if (parameter?.Aliases is not null)
                {
                    foreach (var parameterAlias in parameter.Aliases)
                    {
                        if (string.IsNullOrWhiteSpace(parameterAlias))
                            continue;
                        aliases.Add($"{command}.{parameterAlias}");
                        aliases.Add($"{command}/-{parameterAlias}");
                        aliases.Add($"param:{command}.{parameterAlias}");
                        if (!string.IsNullOrWhiteSpace(module))
                        {
                            aliases.Add($"{module}\\{command}.{parameterAlias}");
                            aliases.Add($"{module}:{command}.{parameterAlias}");
                        }
                    }
                }

                refs[uid] = new ApiXrefReference
                {
                    Uid = uid,
                    Name = $"{command} -{paramName}",
                    Href = href,
                    Aliases = aliases.OrderBy(static a => a, StringComparer.OrdinalIgnoreCase).ToArray()
                };
                emitted++;
            }
        }

        return refs.Values
            .OrderBy(static reference => reference.Uid, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int AddCSharpMemberList(
        string prefix,
        string memberKind,
        ApiTypeModel type,
        List<ApiMemberModel> members,
        string typeHref,
        IReadOnlyDictionary<ApiMemberModel, string> memberAnchors,
        List<ApiXrefReference> output,
        int remaining)
    {
        if (members is null || members.Count == 0 || remaining <= 0)
            return 0;

        var emitted = 0;
        foreach (var member in members)
        {
            if (emitted >= remaining)
                break;
            if (member is null)
                continue;
            var uid = BuildCSharpMemberUid(type, member, prefix);
            if (string.IsNullOrWhiteSpace(uid))
                continue;

            var name = BuildCSharpMemberName(type, member);
            var aliases = BuildCSharpMemberAliases(type, member, uid);

            var href = typeHref;
            if (memberAnchors.TryGetValue(member, out var anchor) && !string.IsNullOrWhiteSpace(anchor))
                href = AppendMemberAnchor(typeHref, anchor);

            output.Add(new ApiXrefReference
            {
                Uid = uid,
                Name = string.IsNullOrWhiteSpace(name) ? uid : name,
                Href = href,
                Aliases = aliases
                    .Where(static alias => !string.IsNullOrWhiteSpace(alias))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static alias => alias, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            });
            emitted++;
        }
        return emitted;
    }

    private static string BuildCSharpMemberUid(ApiTypeModel type, ApiMemberModel member, string prefix)
    {
        var typeName = (type.FullName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(typeName))
            return string.Empty;

        var memberName = ResolveMemberUidName(member);
        if (string.IsNullOrWhiteSpace(memberName))
            return string.Empty;

        var includeParameters = string.Equals(prefix, "M", StringComparison.OrdinalIgnoreCase);
        var suffix = includeParameters ? BuildCSharpMemberParameterList(member) : string.Empty;
        return string.IsNullOrWhiteSpace(suffix)
            ? $"{prefix}:{typeName}.{memberName}"
            : $"{prefix}:{typeName}.{memberName}({suffix})";
    }

    private static string ResolveMemberUidName(ApiMemberModel member)
    {
        if (member is null)
            return string.Empty;

        if (member.IsConstructor || string.Equals(member.Name, "#ctor", StringComparison.OrdinalIgnoreCase) || string.Equals(member.Name, ".ctor", StringComparison.OrdinalIgnoreCase))
            return "#ctor";
        if (string.Equals(member.Name, ".cctor", StringComparison.OrdinalIgnoreCase))
            return ".cctor";
        return (member.Name ?? string.Empty).Trim();
    }

    private static string BuildCSharpMemberParameterList(ApiMemberModel member)
    {
        if (member?.Parameters is null || member.Parameters.Count == 0)
            return string.Empty;

        return string.Join(
            ",",
            member.Parameters
                .Select(static parameter => NormalizeCSharpMemberParameterType(parameter?.Type))
                .Where(static value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string NormalizeCSharpMemberParameterType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();
        if (trimmed.StartsWith("global::", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed.Substring("global::".Length);

        var tokens = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var filtered = tokens
            .Where(static token =>
                !token.Equals("ref", StringComparison.OrdinalIgnoreCase) &&
                !token.Equals("out", StringComparison.OrdinalIgnoreCase) &&
                !token.Equals("in", StringComparison.OrdinalIgnoreCase) &&
                !token.Equals("params", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var normalized = string.Concat(filtered);
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        normalized = normalized.Replace("&", string.Empty, StringComparison.Ordinal);
        if (normalized.EndsWith("@", StringComparison.Ordinal))
            normalized = normalized[..^1];

        normalized = NormalizeCSharpPrimitiveType(normalized);
        return normalized.Replace('<', '{').Replace('>', '}');
    }

    private static string NormalizeCSharpPrimitiveType(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value switch
        {
            "bool" => "System.Boolean",
            "byte" => "System.Byte",
            "sbyte" => "System.SByte",
            "char" => "System.Char",
            "decimal" => "System.Decimal",
            "double" => "System.Double",
            "float" => "System.Single",
            "int" => "System.Int32",
            "uint" => "System.UInt32",
            "long" => "System.Int64",
            "ulong" => "System.UInt64",
            "short" => "System.Int16",
            "ushort" => "System.UInt16",
            "string" => "System.String",
            "object" => "System.Object",
            "void" => "System.Void",
            "Boolean" => "System.Boolean",
            "Byte" => "System.Byte",
            "SByte" => "System.SByte",
            "Char" => "System.Char",
            "Decimal" => "System.Decimal",
            "Double" => "System.Double",
            "Single" => "System.Single",
            "Int32" => "System.Int32",
            "UInt32" => "System.UInt32",
            "Int64" => "System.Int64",
            "UInt64" => "System.UInt64",
            "Int16" => "System.Int16",
            "UInt16" => "System.UInt16",
            "String" => "System.String",
            "Object" => "System.Object",
            "Void" => "System.Void",
            _ => value
        };
    }

    private static string BuildCSharpMemberName(ApiTypeModel type, ApiMemberModel member)
    {
        var typeName = string.IsNullOrWhiteSpace(type?.Name) ? GetShortTypeName(type?.FullName ?? string.Empty) : type!.Name;
        var displayName = (member?.DisplayName ?? member?.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(typeName))
            return displayName;
        if (string.IsNullOrWhiteSpace(displayName))
            return typeName;
        return $"{typeName}.{displayName}";
    }

    private static string[] BuildCSharpMemberAliases(ApiTypeModel type, ApiMemberModel member, string uid)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var noPrefix = uid.Length > 2 && uid[1] == ':' ? uid.Substring(2) : uid;
        if (!string.IsNullOrWhiteSpace(noPrefix))
            aliases.Add(noPrefix);

        var memberName = ResolveMemberUidName(member);
        var typeName = (type?.FullName ?? string.Empty).Trim();
        var includeParameters = uid.StartsWith("M:", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(typeName) && !string.IsNullOrWhiteSpace(memberName))
        {
            var simpleUid = includeParameters
                ? $"{typeName}.{memberName}"
                : noPrefix;
            if (!string.IsNullOrWhiteSpace(simpleUid))
                aliases.Add(simpleUid);
            if (!string.IsNullOrWhiteSpace(simpleUid))
                aliases.Add("global::" + simpleUid);
        }

        aliases.RemoveWhere(static alias => string.IsNullOrWhiteSpace(alias));
        aliases.Remove(uid);
        return aliases.OrderBy(static alias => alias, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyDictionary<ApiMemberModel, string> BuildMemberAnchorsByReference(ApiTypeModel type)
    {
        var map = new Dictionary<ApiMemberModel, string>();
        if (type is null)
            return map;

        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AssignMemberAnchors(type.Constructors, "constructor", usedIds, map);
        AssignMemberAnchors(type.Methods, "method", usedIds, map);
        AssignMemberAnchors(type.Properties, "property", usedIds, map);
        AssignMemberAnchors(type.Fields, "field", usedIds, map);
        AssignMemberAnchors(type.Events, "event", usedIds, map);
        AssignMemberAnchors(type.ExtensionMethods, "extension", usedIds, map);

        return map;
    }

    private static void AssignMemberAnchors(
        List<ApiMemberModel> members,
        string memberKind,
        ISet<string> usedIds,
        Dictionary<ApiMemberModel, string> output)
    {
        if (members is null || members.Count == 0)
            return;

        foreach (var member in members)
        {
            if (member is null || output.ContainsKey(member))
                continue;

            var preferred = BuildMemberId(memberKind, member);
            if (string.IsNullOrWhiteSpace(preferred))
                continue;

            output[member] = BuildUniqueMemberId(preferred, usedIds);
        }
    }

    private static string AppendMemberAnchor(string href, string anchor)
    {
        if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(anchor))
            return href;

        var sanitized = anchor.Trim().TrimStart('#');
        if (string.IsNullOrWhiteSpace(sanitized))
            return href;

        if (href.Contains('#', StringComparison.Ordinal))
            return href;
        return href + "#" + sanitized;
    }

    private static void AddXrefReference(
        Dictionary<string, ApiXrefReference> refs,
        string uid,
        string name,
        string href,
        IEnumerable<string> aliases)
    {
        if (string.IsNullOrWhiteSpace(uid) || string.IsNullOrWhiteSpace(href))
            return;

        var normalizedAliases = (aliases ?? Array.Empty<string>())
            .Where(static alias => !string.IsNullOrWhiteSpace(alias))
            .Select(static alias => alias.Trim())
            .Where(alias => !alias.Equals(uid, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static alias => alias, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (refs.TryGetValue(uid, out var existing))
        {
            var mergedAliases = existing.Aliases
                .Concat(normalizedAliases)
                .Where(static alias => !string.IsNullOrWhiteSpace(alias))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static alias => alias, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            existing.Aliases = mergedAliases;

            if (!string.IsNullOrWhiteSpace(name))
                existing.Name = name;
            if (!string.IsNullOrWhiteSpace(href))
                existing.Href = href;
            return;
        }

        refs[uid] = new ApiXrefReference
        {
            Uid = uid,
            Name = string.IsNullOrWhiteSpace(name) ? uid : name,
            Href = href,
            Aliases = normalizedAliases
        };
    }
}
