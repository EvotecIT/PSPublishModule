using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

/// <summary>Data loading, normalization, and build diagnostics helpers.</summary>
public static partial class WebSiteBuilder
{
    private static IReadOnlyDictionary<string, object?> LoadData(SiteSpec spec, WebSitePlan plan, IReadOnlyList<ProjectSpec> projects)
    {
        var rootPath = plan.RootPath;
        var dataRoot = string.IsNullOrWhiteSpace(spec.DataRoot) ? "data" : spec.DataRoot;
        var basePath = Path.IsPathRooted(dataRoot) ? dataRoot : Path.Combine(rootPath, dataRoot);
        var data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var themeData = LoadThemeData(spec, rootPath);
        if (themeData is not null)
            data["theme"] = themeData;

        LoadDataFromFolder(basePath, data, spec.Markdown);
        LoadProjectData(plan, projects, data, spec.Markdown);
        NormalizeKnownData(data);

        return data;
    }

    private static void LoadProjectData(WebSitePlan plan, IReadOnlyList<ProjectSpec> projects, Dictionary<string, object?> data, MarkdownSpec? markdown)
    {
        if (plan.Projects is null || plan.Projects.Length == 0) return;

        var projectData = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var projectSpecMap = projects
            .Where(p => !string.IsNullOrWhiteSpace(p.Slug))
            .ToDictionary(p => p.Slug, StringComparer.OrdinalIgnoreCase);

        foreach (var project in plan.Projects)
        {
            if (string.IsNullOrWhiteSpace(project.Slug)) continue;
            var dataRoot = Path.Combine(project.RootPath, "data");
            if (!Directory.Exists(dataRoot)) continue;

            var projectBag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            LoadDataFromFolder(dataRoot, projectBag, markdown);

            if (projectSpecMap.TryGetValue(project.Slug, out var spec))
            {
                projectBag["spec"] = spec;
            }

            if (projectBag.Count > 0)
                projectData[project.Slug] = projectBag;
        }

        if (projectData.Count > 0)
            data["projects"] = projectData;
    }

    private static IReadOnlyDictionary<string, object?> ResolveDataForProject(
        IReadOnlyDictionary<string, object?> data,
        string? projectSlug)
    {
        if (string.IsNullOrWhiteSpace(projectSlug))
            return data;
        if (!data.TryGetValue("projects", out var projectsObj) || projectsObj is not IReadOnlyDictionary<string, object?> projects)
            return data;
        if (!projects.TryGetValue(projectSlug, out var projectData))
            return data;

        var merged = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in data)
            merged[kvp.Key] = kvp.Value;
        merged["project"] = projectData;
        return merged;
    }

    private static void LoadDataFromFolder(string basePath, Dictionary<string, object?> data, MarkdownSpec? markdown)
    {
        if (!Directory.Exists(basePath))
            return;

        // Ensure deterministic load order across platforms/filesystems (and reduce alias collision surprises).
        var files = Directory.EnumerateFiles(basePath, "*.json", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        foreach (var file in files)
        {
            object? value;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                value = ConvertJsonElement(doc.RootElement);
                value = NormalizeMarkdownData(value, markdown);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Failed to load data file {file}: {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            var relative = Path.GetRelativePath(basePath, file);
            var segments = relative.Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .ToArray();
            if (segments.Length == 0) continue;
            segments[^1] = Path.GetFileNameWithoutExtension(segments[^1]);

            // Always load the canonical key path (matches on-disk relative path).
            AddNestedData(data, segments, value, overwrite: true);

            // Also add an alias path that is safe for dot-access in Scriban (dashes/dots/spaces -> underscores).
            var aliasSegments = NormalizeDataKeySegments(segments);
            if (aliasSegments is not null)
                AddNestedData(data, aliasSegments, value, overwrite: false);
        }
    }

    private static Dictionary<string, object?>? LoadThemeData(SiteSpec spec, string rootPath)
    {
        var themeRoot = ResolveThemeRoot(spec, rootPath);
        if (string.IsNullOrWhiteSpace(themeRoot) || !Directory.Exists(themeRoot))
            return null;

        var loader = new ThemeLoader();
        var manifest = loader.Load(themeRoot, ResolveThemesRoot(spec, rootPath));
        if (manifest?.Tokens is null || manifest.Tokens.Count == 0)
            return null;

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = string.IsNullOrWhiteSpace(manifest.Name) ? spec.DefaultTheme ?? string.Empty : manifest.Name,
            ["tokens"] = manifest.Tokens
        };
    }

    private static string[]? NormalizeDataKeySegments(string[] segments)
    {
        if (segments.Length == 0) return null;

        string[]? normalized = null;
        for (var i = 0; i < segments.Length; i++)
        {
            var current = segments[i];
            var next = NormalizeDataKeySegment(current);
            if (string.Equals(current, next, StringComparison.Ordinal))
                continue;

            normalized ??= (string[])segments.Clone();
            normalized[i] = next;
        }

        return normalized;
    }

    private static string NormalizeDataKeySegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        // Keep ASCII-only and predictable. We aim for "identifier-ish" keys that work as `data.foo_bar`.
        var trimmed = value.Trim();
        var sb = new System.Text.StringBuilder(trimmed.Length);
        var previousUnderscore = false;
        for (var i = 0; i < trimmed.Length; i++)
        {
            var ch = trimmed[i];
            var isOk = (ch is >= 'a' and <= 'z') ||
                       (ch is >= 'A' and <= 'Z') ||
                       (ch is >= '0' and <= '9') ||
                       ch == '_';
            if (isOk)
            {
                sb.Append(ch);
                previousUnderscore = false;
                continue;
            }

            // Common filename separators: dash/dot/space (and anything else) becomes underscore.
            if (!previousUnderscore)
            {
                sb.Append('_');
                previousUnderscore = true;
            }
        }

        var normalized = sb.ToString().Trim('_');
        if (string.IsNullOrWhiteSpace(normalized))
            normalized = "data";

        // Scriban identifiers can start with underscore but not with a digit.
        if (normalized.Length > 0 && normalized[0] is >= '0' and <= '9')
            normalized = "_" + normalized;

        return normalized;
    }

    private static void AddNestedData(Dictionary<string, object?> data, string[] segments, object? value, bool overwrite)
    {
        var current = data;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            var key = segments[i];
            if (!current.TryGetValue(key, out var existing) || existing is not Dictionary<string, object?> child)
            {
                if (existing is not null && existing is not Dictionary<string, object?>)
                {
                    Trace.TraceWarning($"Data key collision: cannot nest under '{key}' because it is already a scalar. Path='{string.Join('/', segments)}'");
                    return;
                }
                child = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                current[key] = child;
            }
            current = child;
        }

        var leaf = segments[^1];
        if (overwrite)
        {
            current[leaf] = value;
            return;
        }

        if (current.ContainsKey(leaf))
        {
            Trace.TraceWarning($"Data alias collision: '{leaf}' already exists. AliasPath='{string.Join('/', segments)}'");
            return;
        }

        current[leaf] = value;
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in element.EnumerateObject())
                {
                    map[prop.Name] = ConvertJsonElement(prop.Value);
                }
                return map;
            }
            case JsonValueKind.Array:
            {
                var list = new List<object?>();
                foreach (var item in element.EnumerateArray())
                {
                    list.Add(ConvertJsonElement(item));
                }
                return list;
            }
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                return element.TryGetInt64(out var l) ? l : element.GetDouble();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
            default:
                return null;
        }
    }

    private static void NormalizeKnownData(Dictionary<string, object?> data)
    {
        if (data is null) return;

        NormalizeNamedData(data, "faq", NormalizeFaqData);
        NormalizeNamedData(data, "showcase", NormalizeShowcaseData);
        NormalizeNamedData(data, "benchmarks", NormalizeBenchmarksData);
        NormalizeNamedData(data, "pricing", NormalizePricingData);

        if (data.TryGetValue("projects", out var projectsObj) &&
            projectsObj is Dictionary<string, object?> projects)
        {
            foreach (var project in projects.Values)
            {
                if (project is Dictionary<string, object?> projectData)
                    NormalizeKnownData(projectData);
            }
        }
    }

    private static void NormalizeNamedData(
        Dictionary<string, object?> data,
        string key,
        Action<Dictionary<string, object?>> normalizer)
    {
        if (data.TryGetValue(key, out var value) && value is Dictionary<string, object?> map)
            normalizer(map);
    }

    private static void NormalizeFaqData(Dictionary<string, object?> faq)
    {
        if (!TryGetList(faq, "sections", out var sections))
            return;

        foreach (var sectionObj in sections)
        {
            if (sectionObj is not Dictionary<string, object?> section)
                continue;

            if (!TryGetList(section, "items", out var items))
                continue;

            foreach (var itemObj in items)
            {
                if (itemObj is not Dictionary<string, object?> item)
                    continue;

                CopyIfMissing(item, "question", item, "q", "title");
                CopyIfMissing(item, "answer", item, "a", "text", "summary");

                if (item.TryGetValue("answer", out var answer))
                    item["answer"] = NormalizeStringList(answer);
            }
        }
    }

    private static void NormalizeShowcaseData(Dictionary<string, object?> showcase)
    {
        if (!TryGetList(showcase, "cards", out var cards))
            return;

        foreach (var cardObj in cards)
        {
            if (cardObj is not Dictionary<string, object?> card)
                continue;

            CopyIfMissing(card, "icon_svg", card, "iconSvg", "iconHtml");

            if (TryGetList(card, "meta", out var metaItems))
            {
                foreach (var metaObj in metaItems)
                {
                    if (metaObj is not Dictionary<string, object?> meta)
                        continue;
                    CopyIfMissing(meta, "icon_svg", meta, "iconSvg", "iconHtml");
                }
            }

            if (card.TryGetValue("details", out var detailsObj) && detailsObj is Dictionary<string, object?> details)
            {
                if (details.TryGetValue("items", out var items))
                    details["items"] = NormalizeStringList(items);
            }

            if (card.TryGetValue("features", out var features))
                card["features"] = NormalizeStringList(features);

            if (card.TryGetValue("gallery", out var galleryObj) && galleryObj is Dictionary<string, object?> gallery)
            {
                if (TryGetList(gallery, "themes", out var themes))
                {
                    foreach (var themeObj in themes)
                    {
                        if (themeObj is not Dictionary<string, object?> theme)
                            continue;
                        if (!TryGetList(theme, "slides", out var slides))
                            continue;
                        foreach (var slideObj in slides)
                        {
                            if (slideObj is not Dictionary<string, object?> slide)
                                continue;
                            CopyIfMissing(slide, "thumb_label", slide, "thumbLabel");
                            CopyIfMissing(slide, "thumb_src", slide, "thumbSrc");
                        }
                    }
                }
            }

            if (TryGetList(card, "actions", out var actions))
            {
                foreach (var actionObj in actions)
                {
                    if (actionObj is not Dictionary<string, object?> action)
                        continue;
                    CopyIfMissing(action, "icon_svg", action, "iconSvg", "iconHtml");
                }
            }

            if (card.TryGetValue("status", out var statusObj) && statusObj is Dictionary<string, object?> status)
            {
                CopyIfMissing(status, "dot_style", status, "dotStyle");
            }
        }
    }

    private static void NormalizeBenchmarksData(Dictionary<string, object?> benchmarks)
    {
        if (benchmarks.TryGetValue("about", out var aboutObj) && aboutObj is Dictionary<string, object?> about)
        {
            if (TryGetList(about, "cards", out var cards))
            {
                foreach (var cardObj in cards)
                {
                    if (cardObj is not Dictionary<string, object?> card)
                        continue;

                    if (card.TryGetValue("paragraphs", out var paragraphs))
                        card["paragraphs"] = NormalizeStringList(paragraphs);
                    if (card.TryGetValue("list", out var list))
                        card["list"] = NormalizeStringList(list);
                }
            }
        }

        if (benchmarks.TryGetValue("notes", out var notesObj) && notesObj is Dictionary<string, object?> notes)
        {
            if (notes.TryGetValue("items", out var items))
                notes["items"] = NormalizeStringList(items);
        }
    }

    private static void NormalizePricingData(Dictionary<string, object?> pricing)
    {
        if (TryGetList(pricing, "cards", out var cards))
        {
            foreach (var cardObj in cards)
            {
                if (cardObj is not Dictionary<string, object?> card)
                    continue;

                CopyIfMissing(card, "icon_svg", card, "iconSvg", "iconHtml");
                CopyIfMissing(card, "icon_class", card, "iconClass");
                CopyIfMissing(card, "amount_class", card, "amountClass");

                if (card.TryGetValue("features", out var features))
                    card["features"] = NormalizeStringList(features);

                if (card.TryGetValue("cta", out var ctaObj) && ctaObj is Dictionary<string, object?> cta)
                {
                    CopyIfMissing(cta, "icon_svg", cta, "iconSvg", "iconHtml");
                }
                else
                {
                    var ctaLabel = ReadString(card, "cta_label", "ctaLabel");
                    var ctaHref = ReadString(card, "cta_href", "ctaHref");
                    if (!string.IsNullOrWhiteSpace(ctaLabel) || !string.IsNullOrWhiteSpace(ctaHref))
                    {
                        card["cta"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["label"] = ctaLabel ?? string.Empty,
                            ["href"] = ctaHref ?? string.Empty
                        };
                    }
                }
            }
        }

        if (pricing.TryGetValue("note", out var noteObj) && noteObj is Dictionary<string, object?> note)
        {
            if (note.TryGetValue("paragraphs", out var paragraphs))
                note["paragraphs"] = NormalizeStringList(paragraphs);
        }
    }

    private static void CopyIfMissing(
        Dictionary<string, object?> target,
        string targetKey,
        Dictionary<string, object?> source,
        params string[] sourceKeys)
    {
        if (target.ContainsKey(targetKey))
            return;

        foreach (var sourceKey in sourceKeys)
        {
            if (source.TryGetValue(sourceKey, out var value) && value is not null)
            {
                target[targetKey] = value;
                return;
            }
        }
    }

    private static bool TryGetList(Dictionary<string, object?> map, string key, out List<object?> list)
    {
        list = new List<object?>();
        if (!map.TryGetValue(key, out var value) || value is null)
            return false;

        if (value is List<object?> listValue)
        {
            list = listValue;
            return true;
        }

        if (value is IEnumerable<object?> enumerable && value is not string)
        {
            list = enumerable.ToList();
            map[key] = list;
            return true;
        }

        return false;
    }

    private static object? NormalizeStringList(object? value)
    {
        if (value is null) return null;
        if (value is List<object?> list)
            return list;
        if (value is IEnumerable<object?> enumerable && value is not string)
            return enumerable.ToList();
        if (value is string text)
            return new List<object?> { text };
        return value;
    }

    private static string? ReadString(Dictionary<string, object?> map, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (map.TryGetValue(key, out var value) && value is not null)
                return value.ToString();
        }
        return null;
    }

    private static object? NormalizeMarkdownData(object? value, MarkdownSpec? markdown)
    {
        if (value is Dictionary<string, object?> map)
        {
            foreach (var key in map.Keys.ToList())
            {
                map[key] = NormalizeMarkdownData(map[key], markdown);
            }

            foreach (var key in map.Keys.ToList())
            {
                if (!IsMarkdownKey(key))
                    continue;

                var baseKey = StripMarkdownSuffix(key);
                if (string.IsNullOrWhiteSpace(baseKey))
                    continue;

                if (!map.ContainsKey(baseKey) || map[baseKey] is null)
                    map[baseKey] = RenderMarkdownValue(map[key], markdown);
            }

            return map;
        }

        if (value is List<object?> list)
        {
            for (var i = 0; i < list.Count; i++)
            {
                list[i] = NormalizeMarkdownData(list[i], markdown);
            }
            return list;
        }

        return value;
    }

    private static bool IsMarkdownKey(string key)
        => key.EndsWith("_md", StringComparison.OrdinalIgnoreCase) ||
           key.EndsWith("_markdown", StringComparison.OrdinalIgnoreCase);

    private static string StripMarkdownSuffix(string key)
    {
        if (key.EndsWith("_markdown", StringComparison.OrdinalIgnoreCase))
            return key[..^9];
        if (key.EndsWith("_md", StringComparison.OrdinalIgnoreCase))
            return key[..^3];
        return key;
    }

    private static object? RenderMarkdownValue(object? value, MarkdownSpec? markdown)
    {
        if (value is null) return null;

        if (value is string text)
            return MarkdownRenderer.RenderToHtml(text, markdown);

        if (value is IEnumerable<object?> list && value is not string)
        {
            var rendered = new List<object?>();
            foreach (var item in list)
            {
                if (item is string itemText)
                    rendered.Add(MarkdownRenderer.RenderToHtml(itemText, markdown));
                else
                    rendered.Add(item);
            }
            return rendered;
        }

        return value;
    }

    private static string BuildTableOfContents(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var matches = TocHeaderRegex.Matches(html);
        if (matches.Count == 0)
            return string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<nav class=\"pf-toc\">");
        sb.AppendLine("  <div class=\"pf-toc-title\">On this page</div>");
        sb.AppendLine("  <ul>");
        foreach (Match match in matches)
        {
            var level = match.Groups["level"].Value;
            var text = StripTags(match.Groups["text"].Value).Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;
            var slug = Slugify(text);
            sb.AppendLine($"    <li class=\"pf-toc-l{level}\"><a href=\"#{slug}\">{System.Web.HttpUtility.HtmlEncode(text)}</a></li>");
        }
        sb.AppendLine("  </ul>");
        sb.AppendLine("</nav>");
        return sb.ToString();
    }

    private static string StripTags(string input)
    {
        return StripTagsRegex.Replace(input, string.Empty);
    }

    private static void WriteSearchIndex(SiteSpec spec, string outputRoot, IReadOnlyList<ContentItem> items)
    {
        if (items.Count == 0) return;

        var entries = new List<SearchIndexEntry>();
        foreach (var item in items)
        {
            if (item.Draft) continue;
            if (string.IsNullOrWhiteSpace(item.OutputPath))
                continue;
            if (item.OutputPath.Equals("/404.html", StringComparison.OrdinalIgnoreCase))
                continue;

            var snippet = BuildSnippet(item.HtmlContent, 240);
            var normalizedLanguage = NormalizeLanguageToken(item.Language);
            entries.Add(new SearchIndexEntry
            {
                Title = item.Title,
                Url = item.OutputPath,
                Description = item.Description,
                Snippet = snippet,
                Collection = item.Collection,
                Kind = item.Kind.ToString().ToLowerInvariant(),
                Weight = ResolveSearchWeight(item.Kind, item.Collection),
                SearchText = BuildSearchText(item, snippet),
                Tags = item.Tags,
                Project = item.ProjectSlug,
                Language = normalizedLanguage,
                TranslationKey = item.TranslationKey,
                Meta = item.Meta.Count == 0 ? null : item.Meta
            });
        }
        if (entries.Count == 0)
            return;

        entries = entries
            .OrderByDescending(static entry => entry.Weight)
            .ThenBy(static entry => entry.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.Url, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var searchDir = Path.Combine(outputRoot, "search");
        Directory.CreateDirectory(searchDir);
        var searchPath = Path.Combine(searchDir, "index.json");
        WriteAllTextIfChanged(searchPath, JsonSerializer.Serialize(entries, WebJson.Options));

        // Emit per-language search shards when localization is used to keep client filtering cheap.
        var languages = entries
            .Select(static entry => NormalizeLanguageToken(entry.Language))
            .Where(static language => !string.IsNullOrWhiteSpace(language))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (languages.Length > 1)
        {
            foreach (var language in languages)
            {
                var shard = entries
                    .Where(entry => NormalizeLanguageToken(entry.Language).Equals(language, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                var shardDir = Path.Combine(searchDir, language);
                Directory.CreateDirectory(shardDir);
                var shardPath = Path.Combine(shardDir, "index.json");
                WriteAllTextIfChanged(shardPath, JsonSerializer.Serialize(shard, WebJson.Options));
            }
        }

        var collectionShards = new List<Dictionary<string, object?>>();
        var collections = entries
            .Select(static entry => entry.Collection)
            .Where(static collection => !string.IsNullOrWhiteSpace(collection))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var collection in collections)
        {
            var token = Slugify(collection);
            if (string.IsNullOrWhiteSpace(token))
                continue;
            var shard = entries
                .Where(entry => string.Equals(entry.Collection, collection, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var collectionDir = Path.Combine(searchDir, "collections", token);
            Directory.CreateDirectory(collectionDir);
            var collectionPath = Path.Combine(collectionDir, "index.json");
            WriteAllTextIfChanged(collectionPath, JsonSerializer.Serialize(shard, WebJson.Options));
            collectionShards.Add(new Dictionary<string, object?>
            {
                ["collection"] = collection,
                ["path"] = $"/search/collections/{token}/index.json",
                ["count"] = shard.Length
            });
        }

        var languageShards = languages.Select(language => new Dictionary<string, object?>
        {
            ["language"] = language,
            ["path"] = $"/search/{language}/index.json",
            ["count"] = entries.Count(entry => NormalizeLanguageToken(entry.Language).Equals(language, StringComparison.OrdinalIgnoreCase))
        }).ToArray();

        var manifest = new Dictionary<string, object?>
        {
            ["entryCount"] = entries.Count,
            ["searchIndexPath"] = "/search/index.json",
            ["languageShards"] = languageShards,
            ["collectionShards"] = collectionShards,
            ["searchPagePath"] = "/search/"
        };
        var manifestPath = Path.Combine(searchDir, "manifest.json");
        WriteAllTextIfChanged(manifestPath, JsonSerializer.Serialize(manifest, WebJson.Options));

        if (HasFeature(spec.Features, "search"))
            EnsureSearchPage(outputRoot, entries);
    }

    private static void WriteLinkCheckReport(SiteSpec spec, IReadOnlyList<ContentItem> items, string metaDir)
    {
        if (spec.LinkCheck?.Enabled != true)
            return;

        var errors = new List<Dictionary<string, string>>();
        var routes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (item.Draft) continue;
            if (item.Kind != PageKind.Page && item.Kind != PageKind.Home)
                continue;
            routes.Add(NormalizeRouteForMatch(item.OutputPath));
        }

        var skipPatterns = spec.LinkCheck.Skip ?? Array.Empty<string>();
        foreach (var item in items)
        {
            if (item.Draft) continue;
            if (item.Kind != PageKind.Page && item.Kind != PageKind.Home)
                continue;

            foreach (var href in ExtractLinks(item.HtmlContent))
            {
                if (string.IsNullOrWhiteSpace(href)) continue;
                if (ShouldSkipLink(href, skipPatterns)) continue;
                if (IsExternalUrl(href) && spec.LinkCheck.IncludeExternal != true) continue;
                if (href.StartsWith("#")) continue;
                if (href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) continue;
                if (href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)) continue;
                if (href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)) continue;

                var normalized = ResolveLinkTarget(item.OutputPath, href);
                if (string.IsNullOrWhiteSpace(normalized)) continue;
                if (!routes.Contains(normalized))
                {
                    errors.Add(new Dictionary<string, string>
                    {
                        ["page"] = item.OutputPath,
                        ["href"] = href,
                        ["target"] = normalized
                    });
                }
            }
        }

        var payload = new Dictionary<string, object?>
        {
            ["checkedAtUtc"] = DateTime.UtcNow.ToString("O"),
            ["errorCount"] = errors.Count,
            ["errors"] = errors
        };

        var path = Path.Combine(metaDir, "linkcheck.json");
        WriteAllTextIfChanged(path, JsonSerializer.Serialize(payload, WebJson.Options));
    }

    private static IEnumerable<string> ExtractLinks(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            yield break;

        var matches = HrefRegex.Matches(html);
        foreach (Match match in matches)
        {
            if (match.Groups.Count < 2) continue;
            yield return match.Groups[1].Value;
        }
    }

    private static string ResolveLinkTarget(string pagePath, string href)
    {
        var cleaned = href;
        var hashIndex = cleaned.IndexOf('#');
        if (hashIndex >= 0)
            cleaned = cleaned.Substring(0, hashIndex);
        var queryIndex = cleaned.IndexOf('?');
        if (queryIndex >= 0)
            cleaned = cleaned.Substring(0, queryIndex);

        if (string.IsNullOrWhiteSpace(cleaned))
            return string.Empty;

        if (cleaned.StartsWith("/"))
            return NormalizeRouteForMatch(cleaned);

        var baseUri = new Uri("http://local" + NormalizeRouteForMatch(pagePath));
        var target = new Uri(baseUri, cleaned);
        return NormalizeRouteForMatch(target.AbsolutePath);
    }

    private static bool ShouldSkipLink(string href, string[] patterns)
    {
        if (patterns.Length == 0) return false;
        foreach (var pattern in patterns)
        {
            if (GlobMatch(pattern, href))
                return true;
        }
        return false;
    }

    private static string BuildSnippet(string html, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var withoutTags = StripTagsRegex.Replace(html, " ");
        var decoded = System.Web.HttpUtility.HtmlDecode(withoutTags) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(decoded))
            return string.Empty;

        var text = WhitespaceRegex.Replace(decoded, " ").Trim();
        if (text.Length <= maxLength) return text;
        return text.Substring(0, maxLength).Trim() + "...";
    }
}

