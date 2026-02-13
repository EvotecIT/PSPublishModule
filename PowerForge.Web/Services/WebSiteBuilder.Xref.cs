using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PowerForge.Web;

/// <summary>Xref map generation and build-time xref link resolution.</summary>
public static partial class WebSiteBuilder
{
    private static void ResolveXrefs(SiteSpec spec, string rootPath, string metaDir, IReadOnlyList<ContentItem> items)
    {
        if (spec.Xref?.Enabled == false || items.Count == 0)
            return;

        var hasRenderedXrefs = items.Any(item =>
            !string.IsNullOrWhiteSpace(item.HtmlContent) &&
            item.HtmlContent.IndexOf("xref:", StringComparison.OrdinalIgnoreCase) >= 0);
        var emitMap = spec.Xref?.EmitMap == true;
        var hasExternalMaps = spec.Xref?.MapFiles is { Length: > 0 };
        if (!hasRenderedXrefs && !emitMap && !hasExternalMaps)
            return;

        var entries = BuildXrefEntries(spec, rootPath, items);
        var map = entries.ToDictionary(entry => entry.Id, entry => entry.Url, StringComparer.OrdinalIgnoreCase);
        var unresolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.HtmlContent))
                continue;

            item.HtmlContent = WebXrefSupport.ResolveHtmlXrefLinks(
                item.HtmlContent,
                map,
                missing => unresolved.Add(missing));
        }

        var maxWarnings = Math.Max(1, spec.Xref?.MaxWarnings ?? 25);
        if (spec.Xref?.WarnOnMissing != false && unresolved.Count > 0)
        {
            foreach (var missing in unresolved.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).Take(maxWarnings))
                Trace.TraceWarning($"Xref: unresolved id '{missing}' referenced from rendered content.");

            if (unresolved.Count > maxWarnings)
                Trace.TraceWarning($"Xref: {unresolved.Count - maxWarnings} additional unresolved ids were suppressed by maxWarnings={maxWarnings}.");
        }

        if (!emitMap)
            return;

        var payload = new
        {
            generatedAtUtc = DateTime.UtcNow,
            count = entries.Count,
            unresolvedCount = unresolved.Count,
            entries = entries
                .OrderBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
                .Select(entry => new
                {
                    id = entry.Id,
                    url = entry.Url,
                    title = entry.Title,
                    source = entry.Source
                })
                .ToArray(),
            unresolved = unresolved
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };

        var xrefPath = Path.Combine(metaDir, "xrefmap.json");
        WriteAllTextIfChanged(xrefPath, JsonSerializer.Serialize(payload, WebJson.Options));
    }

    private static List<XrefEntry> BuildXrefEntries(SiteSpec spec, string rootPath, IReadOnlyList<ContentItem> items)
    {
        var map = new Dictionary<string, XrefEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
            RegisterContentItemXrefs(map, item, rootPath);

        foreach (var external in WebXrefSupport.LoadExternalEntries(spec, rootPath, message => Trace.TraceWarning(message)))
        {
            if (string.IsNullOrWhiteSpace(external.Id) || string.IsNullOrWhiteSpace(external.Url))
                continue;

            if (!map.ContainsKey(external.Id))
                map[external.Id] = external;
        }

        return map.Values.ToList();
    }

    private static void RegisterContentItemXrefs(Dictionary<string, XrefEntry> map, ContentItem item, string rootPath)
    {
        if (item.Draft)
            return;

        var route = NormalizeRouteForMatch(item.OutputPath);
        if (string.IsNullOrWhiteSpace(route))
            return;

        AddXref(map, route, route, item.Title, "site");
        if (route.Length > 1)
            AddXref(map, route.TrimEnd('/'), route, item.Title, "site");

        var normalizedSlug = NormalizePath(item.Slug);
        if (!string.IsNullOrWhiteSpace(item.Collection) && !string.IsNullOrWhiteSpace(normalizedSlug))
            AddXref(map, $"{item.Collection}:{normalizedSlug}", route, item.Title, "site");

        if (!string.IsNullOrWhiteSpace(item.TranslationKey))
            AddXref(map, item.TranslationKey, route, item.Title, "site");

        foreach (var id in WebXrefSupport.ExtractIdsFromMeta(item.Meta))
            AddXref(map, id, route, item.Title, "site");

        if (string.IsNullOrWhiteSpace(item.SourcePath))
            return;

        var relativeSource = Path.GetRelativePath(rootPath, item.SourcePath).Replace('\\', '/');
        var withoutExtension = Path.ChangeExtension(relativeSource, null)?.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(withoutExtension))
            return;

        AddXref(map, $"file:{withoutExtension}", route, item.Title, "site");
        AddXref(map, withoutExtension, route, item.Title, "site");
    }

    private static void AddXref(
        Dictionary<string, XrefEntry> map,
        string? id,
        string? url,
        string? title,
        string source)
    {
        var normalizedId = WebXrefSupport.NormalizeId(id);
        if (string.IsNullOrWhiteSpace(normalizedId))
            return;

        if (string.IsNullOrWhiteSpace(url))
            return;

        if (map.ContainsKey(normalizedId))
            return;

        map[normalizedId] = new XrefEntry
        {
            Id = normalizedId,
            Url = url.Trim(),
            Title = title,
            Source = source
        };
    }
}
