using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

/// <summary>Generates API documentation artifacts from XML docs.</summary>
public static partial class WebApiDocsGenerator
{
    private static readonly Regex TypeReferenceTokenRegex = new(
        "[A-Za-z_][A-Za-z0-9_.]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private sealed class ApiTypeUsageModel
    {
        public List<ApiTypeUsageEntry> ReturnedOrExposedBy { get; } = new();
        public List<ApiTypeUsageEntry> AcceptedByParameters { get; } = new();

        public bool HasEntries => ReturnedOrExposedBy.Count > 0 || AcceptedByParameters.Count > 0;
    }

    private sealed class ApiTypeUsageEntry
    {
        public ApiTypeModel OwnerType { get; set; } = null!;
        public ApiMemberModel Member { get; set; } = null!;
        public string AnchorId { get; set; } = string.Empty;
        public string MemberKind { get; set; } = string.Empty;
        public string SectionLabel { get; set; } = string.Empty;
        public string? ParameterName { get; set; }
    }

    private sealed class ApiMemberRenderInfo
    {
        public string AnchorId { get; set; } = string.Empty;
        public string MemberKind { get; set; } = string.Empty;
        public string SectionLabel { get; set; } = string.Empty;
    }

    private static IReadOnlyDictionary<string, ApiTypeUsageModel> BuildTypeUsageMap(IReadOnlyList<ApiTypeModel> types)
    {
        var usage = types.ToDictionary(
            static type => type.FullName,
            static _ => new ApiTypeUsageModel(),
            StringComparer.OrdinalIgnoreCase);
        if (types.Count == 0)
            return usage;

        var typeLookup = BuildTypeReferenceLookup(types);
        var memberRenderInfo = BuildMemberRenderInfoMap(types);
        var returnedOrExposedSeen = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var acceptedSeen = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var ownerType in types)
        {
            foreach (var member in EnumerateUsageMembers(ownerType))
            {
                if (!memberRenderInfo.TryGetValue(member, out var renderInfo))
                    continue;

                foreach (var referencedType in ResolveReferencedTypes(member.ReturnType, typeLookup))
                {
                    if (!usage.TryGetValue(referencedType.FullName, out var typeUsage))
                        continue;

                    if (!TryTrackUsage(returnedOrExposedSeen, referencedType.FullName, ownerType.FullName, renderInfo.AnchorId, member.Name, null))
                        continue;

                    typeUsage.ReturnedOrExposedBy.Add(new ApiTypeUsageEntry
                    {
                        OwnerType = ownerType,
                        Member = member,
                        AnchorId = renderInfo.AnchorId,
                        MemberKind = renderInfo.MemberKind,
                        SectionLabel = renderInfo.SectionLabel
                    });
                }

                foreach (var parameter in member.Parameters)
                {
                    foreach (var referencedType in ResolveReferencedTypes(parameter.Type, typeLookup))
                    {
                        if (!usage.TryGetValue(referencedType.FullName, out var typeUsage))
                            continue;

                        if (!TryTrackUsage(acceptedSeen, referencedType.FullName, ownerType.FullName, renderInfo.AnchorId, member.Name, parameter.Name))
                            continue;

                        typeUsage.AcceptedByParameters.Add(new ApiTypeUsageEntry
                        {
                            OwnerType = ownerType,
                            Member = member,
                            AnchorId = renderInfo.AnchorId,
                            MemberKind = renderInfo.MemberKind,
                            SectionLabel = renderInfo.SectionLabel,
                            ParameterName = parameter.Name
                        });
                    }
                }
            }
        }

        foreach (var item in usage.Values)
        {
            SortUsageEntries(item.ReturnedOrExposedBy);
            SortUsageEntries(item.AcceptedByParameters);
        }

        return usage;
    }

    private static void SortUsageEntries(List<ApiTypeUsageEntry> entries)
    {
        entries.Sort(static (left, right) =>
        {
            var typeCompare = string.Compare(left.OwnerType.FullName, right.OwnerType.FullName, StringComparison.OrdinalIgnoreCase);
            if (typeCompare != 0)
                return typeCompare;

            var kindCompare = string.Compare(left.MemberKind, right.MemberKind, StringComparison.OrdinalIgnoreCase);
            if (kindCompare != 0)
                return kindCompare;

            var memberCompare = string.Compare(left.Member.DisplayName ?? left.Member.Name, right.Member.DisplayName ?? right.Member.Name, StringComparison.OrdinalIgnoreCase);
            if (memberCompare != 0)
                return memberCompare;

            return string.Compare(left.ParameterName, right.ParameterName, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static bool TryTrackUsage(
        IDictionary<string, HashSet<string>> seen,
        string targetType,
        string ownerType,
        string anchorId,
        string memberName,
        string? parameterName)
    {
        if (!seen.TryGetValue(targetType, out var set))
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            seen[targetType] = set;
        }

        return set.Add($"{ownerType}|{anchorId}|{memberName}|{parameterName}");
    }

    private static IReadOnlyDictionary<string, ApiTypeModel> BuildTypeReferenceLookup(IReadOnlyList<ApiTypeModel> types)
    {
        var lookup = new Dictionary<string, ApiTypeModel>(StringComparer.OrdinalIgnoreCase);
        var shortNameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in types)
        {
            if (!string.IsNullOrWhiteSpace(type.Name))
            {
                shortNameCounts.TryGetValue(type.Name, out var count);
                shortNameCounts[type.Name] = count + 1;
            }
        }

        foreach (var type in types)
        {
            var fullKey = NormalizeTypeName(type.FullName);
            if (!string.IsNullOrWhiteSpace(fullKey))
                lookup[fullKey] = type;

            if (!string.IsNullOrWhiteSpace(type.Name) &&
                shortNameCounts.TryGetValue(type.Name, out var count) &&
                count == 1)
            {
                lookup[NormalizeTypeName(type.Name)] = type;
            }
        }

        return lookup;
    }

    private static IReadOnlyDictionary<ApiMemberModel, ApiMemberRenderInfo> BuildMemberRenderInfoMap(IReadOnlyList<ApiTypeModel> types)
    {
        var map = new Dictionary<ApiMemberModel, ApiMemberRenderInfo>();
        foreach (var type in types)
        {
            var usedMemberIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (IsPowerShellCommandType(type))
            {
                AddMemberRenderInfoEntries(map, usedMemberIds, "method", "Syntax", type.Methods, treatAsInherited: false, groupOverloads: false);
                continue;
            }

            AddMemberRenderInfoEntries(map, usedMemberIds, "constructor", "Constructors", type.Constructors, groupOverloads: true);
            AddMemberRenderInfoEntries(map, usedMemberIds, "method", "Methods", type.Methods, groupOverloads: true);
            AddMemberRenderInfoEntries(map, usedMemberIds, "property", "Properties", type.Properties);
            AddMemberRenderInfoEntries(map, usedMemberIds, "field", type.Kind == "Enum" ? "Values" : "Fields", type.Fields);
            AddMemberRenderInfoEntries(map, usedMemberIds, "event", "Events", type.Events);
            AddMemberRenderInfoEntries(map, usedMemberIds, "extension", "Extension Methods", type.ExtensionMethods, treatAsInherited: false, groupOverloads: true);
        }

        return map;
    }

    private static void AddMemberRenderInfoEntries(
        IDictionary<ApiMemberModel, ApiMemberRenderInfo> map,
        ISet<string> usedMemberIds,
        string memberKind,
        string sectionLabel,
        List<ApiMemberModel> members,
        bool treatAsInherited = true,
        bool groupOverloads = false)
    {
        if (members.Count == 0)
            return;

        var direct = members.Where(static member => !member.IsInherited).ToList();
        var inherited = treatAsInherited ? members.Where(static member => member.IsInherited).ToList() : new List<ApiMemberModel>();

        AddOrderedMemberRenderInfoEntries(map, usedMemberIds, memberKind, sectionLabel, direct, groupOverloads);
        AddOrderedMemberRenderInfoEntries(map, usedMemberIds, memberKind, sectionLabel, inherited, groupOverloads);
    }

    private static void AddOrderedMemberRenderInfoEntries(
        IDictionary<ApiMemberModel, ApiMemberRenderInfo> map,
        ISet<string> usedMemberIds,
        string memberKind,
        string sectionLabel,
        IReadOnlyList<ApiMemberModel> members,
        bool groupOverloads)
    {
        foreach (var member in EnumerateMembersForAnchorAssignment(members, groupOverloads))
        {
            var anchorId = BuildUniqueMemberId(BuildMemberId(memberKind, member), usedMemberIds);
            map[member] = new ApiMemberRenderInfo
            {
                AnchorId = anchorId,
                MemberKind = memberKind,
                SectionLabel = sectionLabel
            };
        }
    }

    private static IEnumerable<ApiMemberModel> EnumerateMembersForAnchorAssignment(IReadOnlyList<ApiMemberModel> members, bool groupOverloads)
    {
        if (!groupOverloads)
        {
            foreach (var member in members)
                yield return member;
            yield break;
        }

        var grouped = members
            .GroupBy(
                static member => string.IsNullOrWhiteSpace(member.DisplayName) ? member.Name : member.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in grouped)
        {
            foreach (var member in group)
                yield return member;
        }
    }

    private static IEnumerable<ApiMemberModel> EnumerateUsageMembers(ApiTypeModel type)
    {
        foreach (var member in type.Constructors)
            yield return member;
        foreach (var member in type.Methods)
            yield return member;
        foreach (var member in type.Properties)
            yield return member;
        foreach (var member in type.Fields)
            yield return member;
        foreach (var member in type.Events)
            yield return member;
        foreach (var member in type.ExtensionMethods)
            yield return member;
    }

    private static IEnumerable<ApiTypeModel> ResolveReferencedTypes(string? typeText, IReadOnlyDictionary<string, ApiTypeModel> lookup)
    {
        if (string.IsNullOrWhiteSpace(typeText) || lookup.Count == 0)
            yield break;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in EnumerateTypeReferenceCandidates(typeText))
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            var normalized = NormalizeTypeName(candidate);
            if (string.IsNullOrWhiteSpace(normalized))
                continue;

            if (!lookup.TryGetValue(normalized, out var type))
                continue;

            if (seen.Add(type.FullName))
                yield return type;
        }
    }

    private static IEnumerable<string> EnumerateTypeReferenceCandidates(string typeText)
    {
        yield return typeText;

        foreach (Match match in TypeReferenceTokenRegex.Matches(typeText))
        {
            if (!match.Success)
                continue;

            yield return match.Value;
        }
    }

    private static void AppendUsageSection(
        StringBuilder sb,
        ApiTypeUsageModel? usage,
        string baseUrl,
        IReadOnlyDictionary<string, string> slugMap)
    {
        if (sb is null)
            return;

        var html = new HtmlFragmentBuilder(initialIndent: 6);
        AppendUsageSection(html, usage, baseUrl, slugMap);
        if (!html.IsEmpty)
            sb.AppendLine(html.ToString().TrimEnd());
    }

    private static void AppendUsageSection(
        HtmlFragmentBuilder html,
        ApiTypeUsageModel? usage,
        string baseUrl,
        IReadOnlyDictionary<string, string> slugMap)
    {
        if (html is null || usage is null || !usage.HasEntries)
            return;

        html.Line("<section class=\"type-usage\" id=\"usage\">");
        using (html.Indent())
        {
            html.Line("<h2>Usage</h2>");
            html.Line("<p class=\"type-usage-summary\">This type appears in these public API surfaces even when no hand-authored example is attached directly to the page.</p>");
            if (usage.ReturnedOrExposedBy.Count > 0)
                AppendUsageGroup(html, "Returned or exposed by", usage.ReturnedOrExposedBy, baseUrl, slugMap);
            if (usage.AcceptedByParameters.Count > 0)
                AppendUsageGroup(html, "Accepted by parameters", usage.AcceptedByParameters, baseUrl, slugMap);
        }
        html.Line("</section>");
    }

    private static void AppendUsageGroup(
        HtmlFragmentBuilder html,
        string heading,
        IReadOnlyList<ApiTypeUsageEntry> entries,
        string baseUrl,
        IReadOnlyDictionary<string, string> slugMap)
    {
        if (html is null || entries.Count == 0)
            return;

        html.Line("<div class=\"usage-group\">");
        using (html.Indent())
        {
            html.Line($"<h3>{System.Web.HttpUtility.HtmlEncode(heading)}</h3>");
            html.Line("<ul class=\"usage-list\">");
            using (html.Indent())
            {
                foreach (var entry in entries)
                {
                    var href = BuildDocsTypeUrl(baseUrl, entry.OwnerType.Slug) + "#" + entry.AnchorId;
                    var safeHref = System.Web.HttpUtility.HtmlAttributeEncode(href);
                    var ownerName = System.Web.HttpUtility.HtmlEncode(GetDisplayTypeName(entry.OwnerType.FullName));
                    var memberName = System.Web.HttpUtility.HtmlEncode(string.IsNullOrWhiteSpace(entry.Member.DisplayName) ? entry.Member.Name : entry.Member.DisplayName);
                    var kindLabel = System.Web.HttpUtility.HtmlEncode(GetUsageKindLabel(entry));

                    html.Line("<li class=\"usage-item\">");
                    using (html.Indent())
                    {
                        html.Line($"<span class=\"usage-kind\">{kindLabel}</span>");
                        html.Line($"<a class=\"usage-link\" href=\"{safeHref}\">{ownerName}.{memberName}</a>");
                        if (!string.IsNullOrWhiteSpace(entry.ParameterName))
                            html.Line($"<span class=\"usage-meta\">parameter <code>{System.Web.HttpUtility.HtmlEncode(entry.ParameterName)}</code></span>");
                    }
                    html.Line("</li>");
                }
            }
            html.Line("</ul>");
        }
        html.Line("</div>");
    }

    private static void AppendUsageGroup(
        StringBuilder sb,
        string heading,
        IReadOnlyList<ApiTypeUsageEntry> entries,
        string baseUrl,
        IReadOnlyDictionary<string, string> slugMap)
    {
        if (sb is null)
            return;

        var html = new HtmlFragmentBuilder(initialIndent: 8);
        AppendUsageGroup(html, heading, entries, baseUrl, slugMap);
        if (!html.IsEmpty)
            sb.AppendLine(html.ToString().TrimEnd());
    }

    private static string GetUsageKindLabel(ApiTypeUsageEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.ParameterName))
        {
            return entry.MemberKind switch
            {
                "constructor" => "Constructor",
                "extension" => "Extension method",
                "method" when string.Equals(entry.SectionLabel, "Syntax", StringComparison.OrdinalIgnoreCase) => "Syntax",
                _ => "Method"
            };
        }

        return entry.MemberKind switch
        {
            "property" => "Property",
            "field" => "Field",
            "event" => "Event",
            "constructor" => "Constructor",
            "extension" => "Extension method",
            "method" when string.Equals(entry.SectionLabel, "Syntax", StringComparison.OrdinalIgnoreCase) => "Syntax",
            _ => "Method"
        };
    }

    private static Dictionary<string, object?>? BuildUsageJson(
        ApiTypeUsageModel? usage,
        string baseUrl,
        IReadOnlyDictionary<string, string> typeDisplayNames)
    {
        if (usage is null || !usage.HasEntries)
            return null;

        return new Dictionary<string, object?>
        {
            ["returnedOrExposedBy"] = usage.ReturnedOrExposedBy.Select(entry => BuildUsageEntryJson(entry, baseUrl, typeDisplayNames)).ToList(),
            ["acceptedByParameters"] = usage.AcceptedByParameters.Select(entry => BuildUsageEntryJson(entry, baseUrl, typeDisplayNames)).ToList()
        };
    }

    private static Dictionary<string, object?> BuildUsageEntryJson(
        ApiTypeUsageEntry entry,
        string baseUrl,
        IReadOnlyDictionary<string, string> typeDisplayNames)
    {
        var ownerDisplayName = typeDisplayNames.TryGetValue(entry.OwnerType.Slug, out var displayName)
            ? displayName
            : GetDisplayTypeName(entry.OwnerType.FullName);

        return new Dictionary<string, object?>
        {
            ["ownerType"] = entry.OwnerType.FullName,
            ["ownerDisplayName"] = ownerDisplayName,
            ["ownerSlug"] = entry.OwnerType.Slug,
            ["memberName"] = entry.Member.Name,
            ["memberDisplayName"] = string.IsNullOrWhiteSpace(entry.Member.DisplayName) ? entry.Member.Name : entry.Member.DisplayName,
            ["memberKind"] = entry.MemberKind,
            ["sectionLabel"] = entry.SectionLabel,
            ["parameterName"] = entry.ParameterName,
            ["anchorId"] = entry.AnchorId,
            ["url"] = BuildDocsTypeUrl(baseUrl, entry.OwnerType.Slug) + "#" + entry.AnchorId
        };
    }
}
