using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PowerForge.Web;

/// <summary>
/// Represents one curated related-content entry loaded from an API docs manifest.
/// </summary>
public sealed class WebApiDocsRelatedContentManifestEntry
{
    /// <summary>
    /// Gets or sets the display title for the related content entry.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the public URL for the related content entry.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional summary shown alongside the entry.
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>
    /// Gets or sets the content kind, such as guide or sample.
    /// </summary>
    public string Kind { get; set; } = "guide";

    /// <summary>
    /// Gets or sets the manifest file that produced this entry.
    /// </summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the stable ordering value assigned while loading manifests.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Gets the target symbol ids that this content entry applies to.
    /// </summary>
    public List<string> Targets { get; } = new();
}

public static partial class WebApiDocsGenerator
{
    private sealed class ApiResolvedRelatedContentEntry
    {
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string? Summary { get; set; }
        public string Kind { get; set; } = "guide";
        public string TargetUid { get; set; } = string.Empty;
        public string? AnchorId { get; set; }
    }

    private sealed class ApiTypeRelatedContentModel
    {
        public List<ApiResolvedRelatedContentEntry> Entries { get; } = new();
        public Dictionary<ApiMemberModel, List<ApiResolvedRelatedContentEntry>> MemberEntries { get; } = new();
        public bool HasEntries => Entries.Count > 0 || MemberEntries.Count > 0;
    }

    private sealed class ApiRelatedContentSymbolTarget
    {
        public required ApiTypeModel OwnerType { get; init; }
        public ApiMemberModel? Member { get; init; }
        public string Uid { get; init; } = string.Empty;
        public string? AnchorId { get; init; }
        public HashSet<string> Aliases { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, ApiTypeRelatedContentModel> BuildTypeRelatedContentMap(
        IReadOnlyList<ApiTypeModel> types,
        WebApiDocsOptions options,
        List<string> warnings)
    {
        var safeTypes = types ?? Array.Empty<ApiTypeModel>();
        if (safeTypes.Count == 0 || options is null || options.RelatedContentManifestPaths.Count == 0)
            return new Dictionary<string, ApiTypeRelatedContentModel>(StringComparer.OrdinalIgnoreCase);

        var manifestEntries = LoadRelatedContentManifestEntries(options, warnings);
        if (manifestEntries.Count == 0)
            return new Dictionary<string, ApiTypeRelatedContentModel>(StringComparer.OrdinalIgnoreCase);

        var symbolTargets = BuildRelatedContentSymbolTargets(safeTypes, options);
        if (symbolTargets.Count == 0)
            return new Dictionary<string, ApiTypeRelatedContentModel>(StringComparer.OrdinalIgnoreCase);

        var aliasMap = new Dictionary<string, List<ApiRelatedContentSymbolTarget>>(StringComparer.OrdinalIgnoreCase);
        foreach (var symbol in symbolTargets)
        {
            foreach (var alias in symbol.Aliases)
            {
                if (string.IsNullOrWhiteSpace(alias))
                    continue;

                var normalizedAlias = NormalizeRelatedContentTarget(alias);
                if (string.IsNullOrWhiteSpace(normalizedAlias))
                    continue;

                if (!aliasMap.TryGetValue(normalizedAlias, out var list))
                {
                    list = new List<ApiRelatedContentSymbolTarget>();
                    aliasMap[normalizedAlias] = list;
                }

                list.Add(symbol);
            }
        }

        var result = new Dictionary<string, ApiTypeRelatedContentModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in manifestEntries.OrderBy(static item => item.Order))
        {
            var matchedTargets = new Dictionary<string, ApiRelatedContentSymbolTarget>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawTarget in entry.Targets)
            {
                var normalizedTarget = NormalizeRelatedContentTarget(rawTarget);
                if (string.IsNullOrWhiteSpace(normalizedTarget))
                    continue;

                if (!aliasMap.TryGetValue(normalizedTarget, out var matches) || matches.Count == 0)
                {
                    warnings?.Add($"[PFWEB.APIDOCS.RELATED] API docs related content: target '{rawTarget}' from '{Path.GetFileName(entry.SourcePath)}' did not resolve to a documented symbol.");
                    continue;
                }

                foreach (var match in matches)
                    matchedTargets[match.Uid] = match;
            }

            foreach (var target in matchedTargets.Values.OrderBy(static item => item.Uid, StringComparer.OrdinalIgnoreCase))
            {
                var ownerKey = target.OwnerType.FullName ?? string.Empty;
                if (string.IsNullOrWhiteSpace(ownerKey))
                    continue;

                if (!result.TryGetValue(ownerKey, out var bucket))
                {
                    bucket = new ApiTypeRelatedContentModel();
                    result[ownerKey] = bucket;
                }

                var resolved = new ApiResolvedRelatedContentEntry
                {
                    Title = entry.Title,
                    Url = entry.Url,
                    Summary = entry.Summary,
                    Kind = entry.Kind,
                    TargetUid = target.Uid,
                    AnchorId = target.AnchorId
                };

                if (target.Member is null)
                {
                    AddRelatedContentEntry(bucket.Entries, resolved);
                    continue;
                }

                if (!bucket.MemberEntries.TryGetValue(target.Member, out var memberEntries))
                {
                    memberEntries = new List<ApiResolvedRelatedContentEntry>();
                    bucket.MemberEntries[target.Member] = memberEntries;
                }

                AddRelatedContentEntry(memberEntries, resolved);
            }
        }

        return result;
    }

    /// <summary>
    /// Loads curated related-content manifest entries from the provided manifest paths.
    /// </summary>
    /// <param name="manifestPaths">Manifest files to load.</param>
    /// <param name="warnings">Optional warning sink for parse and resolution issues.</param>
    /// <returns>Resolved manifest entries in stable load order.</returns>
    public static IReadOnlyList<WebApiDocsRelatedContentManifestEntry> LoadRelatedContentManifestEntriesFromPaths(
        IEnumerable<string>? manifestPaths,
        List<string>? warnings = null)
    {
        var paths = manifestPaths?
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .ToArray() ?? Array.Empty<string>();
        if (paths.Length == 0)
            return Array.Empty<WebApiDocsRelatedContentManifestEntry>();

        var options = new WebApiDocsOptions();
        foreach (var path in paths)
            options.RelatedContentManifestPaths.Add(path);

        return LoadRelatedContentManifestEntries(options, warnings ?? new List<string>());
    }

    private static void AddRelatedContentEntry(List<ApiResolvedRelatedContentEntry> entries, ApiResolvedRelatedContentEntry resolved)
    {
        if (entries is null || resolved is null)
            return;

        var duplicate = entries.Any(existing =>
            string.Equals(existing.Title, resolved.Title, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existing.Url, resolved.Url, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existing.Kind, resolved.Kind, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existing.TargetUid, resolved.TargetUid, StringComparison.OrdinalIgnoreCase));
        if (!duplicate)
            entries.Add(resolved);
    }

    private static List<WebApiDocsRelatedContentManifestEntry> LoadRelatedContentManifestEntries(WebApiDocsOptions options, List<string> warnings)
    {
        var entries = new List<WebApiDocsRelatedContentManifestEntry>();
        var order = 0;
        foreach (var rawPath in options.RelatedContentManifestPaths)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
                continue;

            var fullPath = Path.GetFullPath(rawPath);
            if (!File.Exists(fullPath))
            {
                warnings?.Add($"[PFWEB.APIDOCS.RELATED] API docs related content: manifest was not found: {fullPath}");
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(fullPath));
                foreach (var entryElement in EnumerateRelatedContentEntryElements(document.RootElement))
                {
                    var entry = ParseRelatedContentManifestEntry(entryElement, fullPath, order, warnings);
                    if (entry is null)
                        continue;

                    entries.Add(entry);
                    order++;
                }
            }
            catch (Exception ex)
            {
                warnings?.Add($"[PFWEB.APIDOCS.RELATED] API docs related content: failed to load manifest '{fullPath}' ({ex.GetType().Name}: {ex.Message})");
            }
        }

        return entries;
    }

    private static IEnumerable<JsonElement> EnumerateRelatedContentEntryElements(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
            return root.EnumerateArray().ToArray();

        if (root.ValueKind != JsonValueKind.Object)
            return Array.Empty<JsonElement>();

        foreach (var propertyName in new[] { "entries", "items", "relatedContent", "related-content", "examples" })
        {
            if (root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Array)
                return property.EnumerateArray().ToArray();
        }

        return LooksLikeRelatedContentEntry(root) ? new[] { root } : Array.Empty<JsonElement>();
    }

    private static bool LooksLikeRelatedContentEntry(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return false;

        var hasTitle = TryGetRelatedContentString(element, "title", "name", "label") is { Length: > 0 };
        var hasUrl = TryGetRelatedContentString(element, "url", "href", "link") is { Length: > 0 };
        var hasTargets = TryGetRelatedContentString(element, "target", "uid", "symbol", "xref") is { Length: > 0 };
        if (!hasTargets)
        {
            var targetArray = TryGetRelatedContentStringArray(element, "targets", "uids", "symbols", "xrefs");
            hasTargets = targetArray.Count > 0;
        }

        return hasTitle && hasUrl && hasTargets;
    }

    private static WebApiDocsRelatedContentManifestEntry? ParseRelatedContentManifestEntry(
        JsonElement element,
        string sourcePath,
        int order,
        List<string>? warnings)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            warnings?.Add($"[PFWEB.APIDOCS.RELATED] API docs related content: manifest '{Path.GetFileName(sourcePath)}' contains a non-object entry that was ignored.");
            return null;
        }

        var title = TryGetRelatedContentString(element, "title", "name", "label");
        var url = TryGetRelatedContentString(element, "url", "href", "link");
        var summary = TryGetRelatedContentString(element, "summary", "description");
        var kind = TryGetRelatedContentString(element, "kind", "type", "category");
        var targets = TryGetRelatedContentStringArray(element, "targets", "uids", "symbols", "xrefs");
        var singleTarget = TryGetRelatedContentString(element, "target", "uid", "symbol", "xref");
        if (!string.IsNullOrWhiteSpace(singleTarget))
            targets.Add(singleTarget);

        targets = targets
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url) || targets.Count == 0)
        {
            warnings?.Add($"[PFWEB.APIDOCS.RELATED] API docs related content: manifest '{Path.GetFileName(sourcePath)}' contains an entry without title, url, or targets.");
            return null;
        }

        var entry = new WebApiDocsRelatedContentManifestEntry
        {
            Title = title.Trim(),
            Url = url.Trim(),
            Summary = string.IsNullOrWhiteSpace(summary) ? null : summary.Trim(),
            Kind = NormalizeRelatedContentKind(kind),
            SourcePath = sourcePath,
            Order = order
        };
        entry.Targets.AddRange(targets);
        return entry;
    }

    private static string? TryGetRelatedContentString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!element.TryGetProperty(propertyName, out var property))
                continue;

            if (property.ValueKind == JsonValueKind.String)
                return property.GetString();

            if (property.ValueKind == JsonValueKind.Number ||
                property.ValueKind == JsonValueKind.True ||
                property.ValueKind == JsonValueKind.False)
            {
                return property.ToString();
            }
        }

        return null;
    }

    private static List<string> TryGetRelatedContentStringArray(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!element.TryGetProperty(propertyName, out var property))
                continue;

            if (property.ValueKind == JsonValueKind.String)
                return new List<string> { property.GetString() ?? string.Empty };

            if (property.ValueKind != JsonValueKind.Array)
                continue;

            return property.EnumerateArray()
                .Where(static item => item.ValueKind == JsonValueKind.String)
                .Select(static item => item.GetString() ?? string.Empty)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .ToList();
        }

        return new List<string>();
    }

    private static string NormalizeRelatedContentKind(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "guide";

        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "sample" or "samples" => "sample",
            "guide" or "guides" => "guide",
            "tutorial" or "tutorials" => "tutorial",
            "walkthrough" or "walkthroughs" => "walkthrough",
            "reference" or "references" => "reference",
            "recipe" or "recipes" => "recipe",
            _ => Slugify(normalized)
        };
    }

    private static string NormalizeRelatedContentTarget(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }

    private static List<ApiRelatedContentSymbolTarget> BuildRelatedContentSymbolTargets(
        IReadOnlyList<ApiTypeModel> types,
        WebApiDocsOptions options)
    {
        var targets = new List<ApiRelatedContentSymbolTarget>();
        var shortNameCounts = BuildCSharpShortNameCounts(types, options.Type);

        foreach (var type in types)
        {
            var typeUid = GetXrefUid(type, options.Type);
            if (string.IsNullOrWhiteSpace(typeUid))
                continue;

            var typeTarget = new ApiRelatedContentSymbolTarget
            {
                OwnerType = type,
                Uid = typeUid
            };
            typeTarget.Aliases.Add(typeUid);
            if (options.Type == ApiDocsType.CSharp)
                AddCSharpXrefAliases(typeTarget.Aliases, type, shortNameCounts);
            else
                AddPowerShellXrefAliases(typeTarget.Aliases, type);
            targets.Add(typeTarget);

            if (options.Type != ApiDocsType.CSharp)
                continue;

            var memberAnchors = BuildMemberAnchorsByReference(type);
            AddRelatedContentMemberTargets(targets, type, type.Constructors, "M", memberAnchors);
            AddRelatedContentMemberTargets(targets, type, type.Methods, "M", memberAnchors);
            AddRelatedContentMemberTargets(targets, type, type.Properties, "P", memberAnchors);
            AddRelatedContentMemberTargets(targets, type, type.Fields, "F", memberAnchors);
            AddRelatedContentMemberTargets(targets, type, type.Events, "E", memberAnchors);
            AddRelatedContentMemberTargets(targets, type, type.ExtensionMethods, "M", memberAnchors);
        }

        return targets;
    }

    private static void AddRelatedContentMemberTargets(
        List<ApiRelatedContentSymbolTarget> output,
        ApiTypeModel type,
        List<ApiMemberModel> members,
        string prefix,
        IReadOnlyDictionary<ApiMemberModel, string> memberAnchors)
    {
        if (members is null || members.Count == 0)
            return;

        foreach (var member in members)
        {
            if (member is null)
                continue;

            var uid = BuildCSharpMemberUid(type, member, prefix);
            if (string.IsNullOrWhiteSpace(uid))
                continue;

            memberAnchors.TryGetValue(member, out var anchorId);
            var target = new ApiRelatedContentSymbolTarget
            {
                OwnerType = type,
                Member = member,
                Uid = uid,
                AnchorId = anchorId
            };
            target.Aliases.Add(uid);
            foreach (var alias in BuildCSharpMemberAliases(type, member, uid))
                target.Aliases.Add(alias);
            output.Add(target);
        }
    }

    private static void AppendRelatedContentSection(
        StringBuilder sb,
        IReadOnlyList<ApiResolvedRelatedContentEntry> entries,
        string baseUrl,
        IReadOnlyDictionary<string, string> slugMap,
        string sectionId,
        string title,
        string summary,
        string headingTag,
        string indent)
    {
        if (sb is null || entries is null || entries.Count == 0)
            return;

        sb.AppendLine($"{indent}<section class=\"type-related-content\" id=\"{sectionId}\">");
        sb.AppendLine($"{indent}  <{headingTag}>{System.Web.HttpUtility.HtmlEncode(title)}</{headingTag}>");
        if (!string.IsNullOrWhiteSpace(summary))
            sb.AppendLine($"{indent}  <p class=\"related-content-summary\">{System.Web.HttpUtility.HtmlEncode(summary)}</p>");
        AppendRelatedContentList(sb, entries, baseUrl, slugMap, indent + "  ");
        sb.AppendLine($"{indent}</section>");
    }

    private static void AppendMemberRelatedContent(
        StringBuilder sb,
        IReadOnlyList<ApiResolvedRelatedContentEntry> entries,
        string baseUrl,
        IReadOnlyDictionary<string, string> slugMap)
    {
        if (sb is null || entries is null || entries.Count == 0)
            return;

        sb.AppendLine("          <div class=\"member-related-content\">");
        sb.AppendLine("            <h3>Guides &amp; Samples</h3>");
        AppendRelatedContentList(sb, entries, baseUrl, slugMap, "            ");
        sb.AppendLine("          </div>");
    }

    private static void AppendRelatedContentList(
        StringBuilder sb,
        IReadOnlyList<ApiResolvedRelatedContentEntry> entries,
        string baseUrl,
        IReadOnlyDictionary<string, string> slugMap,
        string indent)
    {
        sb.AppendLine($"{indent}<ul class=\"related-content-list\">");
        foreach (var entry in entries)
        {
            var href = entry.Url;
            var external = IsExternal(href);
            var externalAttributes = external ? " target=\"_blank\" rel=\"noopener\"" : string.Empty;
            var kindClass = NormalizeRelatedContentKind(entry.Kind);
            var kindLabel = BuildRelatedContentKindLabel(entry.Kind);
            sb.AppendLine($"{indent}  <li class=\"related-content-item\">");
            sb.AppendLine($"{indent}    <div class=\"related-content-head\">");
            sb.AppendLine($"{indent}      <a class=\"related-content-link\" href=\"{System.Web.HttpUtility.HtmlAttributeEncode(href)}\"{externalAttributes}>{System.Web.HttpUtility.HtmlEncode(entry.Title)}</a>");
            sb.AppendLine($"{indent}      <span class=\"related-content-kind {System.Web.HttpUtility.HtmlEncode(kindClass)}\">{System.Web.HttpUtility.HtmlEncode(kindLabel)}</span>");
            sb.AppendLine($"{indent}    </div>");
            if (!string.IsNullOrWhiteSpace(entry.Summary))
                sb.AppendLine($"{indent}    <p class=\"related-content-copy\">{RenderLinkedText(entry.Summary, baseUrl, slugMap)}</p>");
            sb.AppendLine($"{indent}  </li>");
        }
        sb.AppendLine($"{indent}</ul>");
    }

    private static string BuildRelatedContentKindLabel(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
            return "Guide";

        var normalized = kind.Trim().Replace('-', ' ');
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized);
    }

    private static object? BuildRelatedContentJson(ApiTypeRelatedContentModel? relatedContent)
    {
        if (relatedContent is null || !relatedContent.HasEntries)
            return null;

        var payload = new Dictionary<string, object?>
        {
            ["entries"] = SerializeRelatedContentEntries(relatedContent.Entries)
        };

        if (relatedContent.MemberEntries.Count > 0)
        {
            payload["members"] = relatedContent.MemberEntries
                .Where(static pair => pair.Value.Count > 0)
                .OrderBy(static pair => pair.Value[0].TargetUid, StringComparer.OrdinalIgnoreCase)
                .Select(pair => new Dictionary<string, object?>
                {
                    ["uid"] = pair.Value[0].TargetUid,
                    ["anchorId"] = pair.Value[0].AnchorId,
                    ["name"] = pair.Key.DisplayName ?? pair.Key.Name,
                    ["entries"] = SerializeRelatedContentEntries(pair.Value)
                })
                .ToList();
        }

        return payload;
    }

    private static List<Dictionary<string, object?>> SerializeRelatedContentEntries(IEnumerable<ApiResolvedRelatedContentEntry>? entries)
    {
        var payload = new List<Dictionary<string, object?>>();
        if (entries is null)
            return payload;

        foreach (var entry in entries)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.Title) || string.IsNullOrWhiteSpace(entry.Url))
                continue;

            payload.Add(new Dictionary<string, object?>
            {
                ["title"] = entry.Title,
                ["url"] = entry.Url,
                ["summary"] = entry.Summary,
                ["kind"] = entry.Kind,
                ["targetUid"] = entry.TargetUid,
                ["anchorId"] = entry.AnchorId
            });
        }

        return payload;
    }
}
