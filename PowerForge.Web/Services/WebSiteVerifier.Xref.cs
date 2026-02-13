using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge.Web;

/// <summary>Xref validation helpers.</summary>
public static partial class WebSiteVerifier
{
    private readonly record struct XrefReference(string File, string Id);

    private static Dictionary<string, string> BuildExternalXrefLookup(SiteSpec spec, string rootPath, List<string> warnings)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (spec.Xref?.Enabled == false)
            return lookup;

        foreach (var entry in WebXrefSupport.LoadExternalEntries(spec, rootPath, message => warnings.Add(message)))
            AddXrefLookupEntry(lookup, entry.Id, entry.Url);
        return lookup;
    }

    private static void RegisterContentXrefs(
        Dictionary<string, string> lookup,
        string rootPath,
        string filePath,
        string? collectionName,
        string slugPath,
        string route,
        string? translationKey,
        Dictionary<string, object?>? meta)
    {
        var normalizedRoute = NormalizeXrefRoute(route);
        AddXrefLookupEntry(lookup, normalizedRoute, normalizedRoute);
        if (normalizedRoute.Length > 1)
            AddXrefLookupEntry(lookup, normalizedRoute + "/", normalizedRoute);

        var normalizedSlug = NormalizePath(slugPath);
        if (!string.IsNullOrWhiteSpace(collectionName) && !string.IsNullOrWhiteSpace(normalizedSlug))
            AddXrefLookupEntry(lookup, $"{collectionName}:{normalizedSlug}", normalizedRoute);

        if (!string.IsNullOrWhiteSpace(translationKey))
            AddXrefLookupEntry(lookup, translationKey, normalizedRoute);

        foreach (var id in WebXrefSupport.ExtractIdsFromMeta(meta))
            AddXrefLookupEntry(lookup, id, normalizedRoute);

        var relativeSource = Path.GetRelativePath(rootPath, filePath).Replace('\\', '/');
        var withoutExtension = Path.ChangeExtension(relativeSource, null)?.Replace('\\', '/');
        if (!string.IsNullOrWhiteSpace(withoutExtension))
        {
            AddXrefLookupEntry(lookup, withoutExtension, normalizedRoute);
            AddXrefLookupEntry(lookup, $"file:{withoutExtension}", normalizedRoute);
        }
    }

    private static void CollectXrefReferences(string rootPath, string filePath, string body, List<XrefReference> references)
    {
        if (string.IsNullOrWhiteSpace(body))
            return;

        var relative = Path.GetRelativePath(rootPath, filePath).Replace('\\', '/');
        foreach (var id in WebXrefSupport.ExtractMarkdownReferenceIds(body))
            references.Add(new XrefReference(relative, id));
    }

    private static void ValidateXrefs(
        SiteSpec spec,
        IReadOnlyDictionary<string, string> xrefLookup,
        IReadOnlyList<XrefReference> references,
        List<string> warnings)
    {
        if (spec.Xref?.Enabled == false ||
            spec.Xref?.WarnOnMissing == false ||
            references.Count == 0)
            return;

        var maxWarnings = Math.Max(1, spec.Xref?.MaxWarnings ?? 25);
        var missing = references
            .Select(reference => new XrefReference(reference.File, WebXrefSupport.NormalizeId(reference.Id)))
            .Where(reference => !string.IsNullOrWhiteSpace(reference.Id))
            .Where(reference => !xrefLookup.ContainsKey(reference.Id))
            .Distinct()
            .OrderBy(reference => reference.File, StringComparer.OrdinalIgnoreCase)
            .ThenBy(reference => reference.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (missing.Count == 0)
            return;

        foreach (var entry in missing.Take(maxWarnings))
            warnings.Add($"Xref: unresolved id '{entry.Id}' in '{entry.File}'.");

        if (missing.Count > maxWarnings)
            warnings.Add($"Xref: {missing.Count - maxWarnings} additional unresolved references were suppressed by maxWarnings={maxWarnings}.");
    }

    private static void AddXrefLookupEntry(Dictionary<string, string> lookup, string? id, string? url)
    {
        var normalizedId = WebXrefSupport.NormalizeId(id);
        if (string.IsNullOrWhiteSpace(normalizedId) || string.IsNullOrWhiteSpace(url))
            return;

        if (!lookup.ContainsKey(normalizedId))
            lookup[normalizedId] = url;
    }

    private static string NormalizeXrefRoute(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
            return "/";

        var normalized = route.Trim();
        if (!normalized.StartsWith("/", StringComparison.Ordinal))
            normalized = "/" + normalized;
        if (normalized.Length > 1 && normalized.EndsWith("/", StringComparison.Ordinal))
            normalized = normalized.TrimEnd('/');
        return normalized;
    }
}
