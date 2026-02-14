using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge.Web;

/// <summary>Generates API documentation artifacts from XML docs.</summary>
public static partial class WebApiDocsGenerator
{
    private static IReadOnlyDictionary<string, string> BuildTypeDisplayNameMap(
        IReadOnlyList<ApiTypeModel> types,
        WebApiDocsOptions? options,
        List<string>? warnings)
    {
        var mode = ResolveDisplayNameMode(options?.DisplayNameMode, warnings);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var type in types)
        {
            if (string.Equals(mode, "full", StringComparison.Ordinal))
            {
                map[type.Slug] = !string.IsNullOrWhiteSpace(type.FullName) ? type.FullName : type.Name;
                continue;
            }

            map[type.Slug] = !string.IsNullOrWhiteSpace(type.Name) ? type.Name : type.FullName;
        }

        if (!string.Equals(mode, "namespace-suffix", StringComparison.Ordinal))
            return map;

        var duplicateGroups = types
            .Where(type => !string.IsNullOrWhiteSpace(type.Name))
            .GroupBy(type => type.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1);

        foreach (var group in duplicateGroups)
        {
            var candidates = group.ToList();
            var maxDepth = candidates
                .Select(static type => (type.Namespace ?? string.Empty).Split('.', StringSplitOptions.RemoveEmptyEntries).Length)
                .DefaultIfEmpty(0)
                .Max();
            var hasUniqueNamespaceLabels = false;
            var namespaceLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (var depth = 1; depth <= Math.Max(1, maxDepth); depth++)
            {
                namespaceLabels.Clear();
                var unique = true;
                foreach (var type in candidates)
                {
                    var label = BuildNamespaceSuffixLabel(type.Namespace, depth);
                    if (namespaceLabels.Values.Contains(label, StringComparer.OrdinalIgnoreCase))
                    {
                        unique = false;
                        break;
                    }

                    namespaceLabels[type.Slug] = label;
                }

                if (!unique)
                    continue;

                hasUniqueNamespaceLabels = true;
                break;
            }

            foreach (var type in candidates)
            {
                if (hasUniqueNamespaceLabels && namespaceLabels.TryGetValue(type.Slug, out var namespaceLabel))
                {
                    map[type.Slug] = $"{type.Name} ({namespaceLabel})";
                }
                else if (!string.IsNullOrWhiteSpace(type.FullName))
                {
                    map[type.Slug] = type.FullName;
                }
                else
                {
                    map[type.Slug] = type.Name;
                }
            }
        }

        return map;
    }

    private static IReadOnlyDictionary<string, string[]> BuildTypeAliasMap(
        IReadOnlyList<ApiTypeModel> types,
        IReadOnlyDictionary<string, string> typeDisplayNames)
    {
        var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in types)
        {
            var displayName = ResolveTypeDisplayName(type, typeDisplayNames);
            var aliases = ResolveTypeAliases(type, displayName).ToArray();
            map[type.Slug] = aliases;
        }

        return map;
    }

    private static IReadOnlyList<string> ResolveTypeAliases(ApiTypeModel type, string displayName)
    {
        var aliases = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddAlias(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            var normalized = value.Trim();
            if (seen.Add(normalized))
                aliases.Add(normalized);
        }

        AddAlias(displayName);
        AddAlias(type.Name);
        if (!string.IsNullOrWhiteSpace(type.Name) && !string.IsNullOrWhiteSpace(type.Namespace))
            AddAlias($"{type.Name} ({type.Namespace})");
        AddAlias(type.FullName);

        return aliases;
    }

    private static string ResolveTypeDisplayName(ApiTypeModel type, IReadOnlyDictionary<string, string> typeDisplayNames)
    {
        if (typeDisplayNames.TryGetValue(type.Slug, out var displayName) && !string.IsNullOrWhiteSpace(displayName))
            return displayName;

        if (!string.IsNullOrWhiteSpace(type.Name))
            return type.Name;

        return string.IsNullOrWhiteSpace(type.FullName) ? "Type" : type.FullName;
    }

    private static void ValidateDuplicateMemberSignatures(IReadOnlyList<ApiTypeModel> types, List<string> warnings)
    {
        if (types is null || warnings is null || types.Count == 0)
            return;

        const int maxSamples = 10;
        var samples = new List<string>();
        var duplicateGroups = 0;
        var affectedTypes = 0;

        foreach (var type in types)
        {
            var local = new List<string>();
            CollectDuplicateMemberSignatureSamples(local, type.Methods, "method");
            CollectDuplicateMemberSignatureSamples(local, type.Constructors, "constructor");
            CollectDuplicateMemberSignatureSamples(local, type.Properties, "property");
            CollectDuplicateMemberSignatureSamples(local, type.Fields, "field");
            CollectDuplicateMemberSignatureSamples(local, type.Events, "event");
            CollectDuplicateMemberSignatureSamples(local, type.ExtensionMethods, "extension");

            if (local.Count == 0)
                continue;

            affectedTypes++;
            duplicateGroups += local.Count;
            foreach (var sample in local)
            {
                if (samples.Count >= maxSamples)
                    break;
                samples.Add($"{type.FullName}: {sample}");
            }
        }

        if (duplicateGroups == 0)
            return;

        var preview = samples.Count == 0 ? "(no samples)" : string.Join("; ", samples);
        var suffix = duplicateGroups > samples.Count ? $" (+{duplicateGroups - samples.Count} more)" : string.Empty;
        warnings.Add($"API docs member signatures: detected {duplicateGroups} duplicate signature group(s) across {affectedTypes} type(s). Samples: {preview}{suffix}.");
    }

    private static void CollectDuplicateMemberSignatureSamples(List<string> target, IReadOnlyList<ApiMemberModel> members, string kind)
    {
        if (target is null || members is null || members.Count < 2)
            return;

        var groups = members
            .Select(member => new
            {
                Key = BuildMemberSignatureKey(member),
                Label = BuildMemberSignatureLabel(member)
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var sampleLabel = group.First().Label;
            if (string.IsNullOrWhiteSpace(sampleLabel))
                sampleLabel = group.Key;
            target.Add($"{kind} {sampleLabel} ({group.Count()} entries)");
        }
    }

    private static string BuildMemberSignatureKey(ApiMemberModel member)
    {
        if (member is null)
            return string.Empty;

        var signature = NormalizeSignatureKey(member.Signature);
        if (!string.IsNullOrWhiteSpace(signature))
            return signature;

        var name = string.IsNullOrWhiteSpace(member.DisplayName) ? member.Name : member.DisplayName;
        name = NormalizeSignatureKey(name);
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        if (member.Parameters is not { Count: > 0 })
            return name;

        var parameterTypes = member.Parameters
            .Select(parameter => NormalizeSignatureKey(parameter.Type))
            .ToArray();
        return $"{name}({string.Join(",", parameterTypes)})";
    }

    private static string BuildMemberSignatureLabel(ApiMemberModel member)
    {
        if (!string.IsNullOrWhiteSpace(member.Signature))
            return member.Signature.Trim();

        var name = string.IsNullOrWhiteSpace(member.DisplayName) ? member.Name : member.DisplayName;
        if (member.Parameters is not { Count: > 0 })
            return name ?? string.Empty;

        var parameterTypes = member.Parameters
            .Select(parameter => string.IsNullOrWhiteSpace(parameter.Type) ? "?" : parameter.Type!.Trim())
            .ToArray();
        return $"{name}({string.Join(", ", parameterTypes)})";
    }

    private static string NormalizeSignatureKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        return WhitespaceRegex.Replace(value.Trim(), " ");
    }

    private static string ResolveDisplayNameMode(string? configuredMode, List<string>? warnings)
    {
        if (string.IsNullOrWhiteSpace(configuredMode))
            return "namespace-suffix";

        var normalized = configuredMode.Trim().ToLowerInvariant();
        if (normalized is "namespace-suffix" or "namespace_suffix" or "namespace" or "suffix" or "auto" or "default")
            return "namespace-suffix";
        if (normalized is "short" or "name")
            return "short";
        if (normalized is "full" or "fullname" or "full-name")
            return "full";

        warnings?.Add($"API docs display names: unknown displayNameMode '{configuredMode}'. Using 'namespace-suffix'. Supported values: short, namespace-suffix, full.");
        return "namespace-suffix";
    }

    private static string BuildNamespaceSuffixLabel(string? ns, int depth)
    {
        if (string.IsNullOrWhiteSpace(ns))
            return "(global)";

        var parts = ns
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return "(global)";

        var take = Math.Min(Math.Max(1, depth), parts.Length);
        return string.Join(".", parts.Skip(parts.Length - take));
    }
}
