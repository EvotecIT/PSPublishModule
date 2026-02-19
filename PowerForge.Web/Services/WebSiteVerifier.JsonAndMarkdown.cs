using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

/// <summary>JSON schema and markdown hygiene validation helpers.</summary>
public static partial class WebSiteVerifier
{
    private static void ValidateFaqJson(JsonElement root, string label, List<string> warnings)
    {
        if (!TryGetArray(root, "sections", out var sections))
        {
            warnings.Add($"Data file '{label}' missing required array 'sections'.");
            return;
        }

        var sectionIndex = 0;
        foreach (var section in sections)
        {
            if (!TryGetArray(section, "items", out var items))
            {
                warnings.Add($"Data file '{label}' section[{sectionIndex}] missing required array 'items'.");
                sectionIndex++;
                continue;
            }

            var itemIndex = 0;
            foreach (var item in items)
            {
                if (!HasAnyProperty(item, "question", "q", "title"))
                    warnings.Add($"Data file '{label}' section[{sectionIndex}].items[{itemIndex}] missing 'question'.");
                if (!HasAnyProperty(item, "answer", "a", "text", "summary"))
                    warnings.Add($"Data file '{label}' section[{sectionIndex}].items[{itemIndex}] missing 'answer'.");
                itemIndex++;
            }
            sectionIndex++;
        }
    }

    private static void ValidateShowcaseJson(JsonElement root, string label, List<string> warnings)
    {
        if (TryGetArray(root, "cards", out var cards))
        {
            var cardIndex = 0;
            foreach (var card in cards)
            {
                if (!HasAnyProperty(card, "title", "name"))
                    warnings.Add($"Data file '{label}' cards[{cardIndex}] missing 'title'.");

                if (TryGetObject(card, "gallery", out var gallery))
                {
                    if (!TryGetArray(gallery, "themes", out var themes))
                    {
                        warnings.Add($"Data file '{label}' cards[{cardIndex}].gallery missing array 'themes'.");
                    }
                    else
                    {
                        var themeIndex = 0;
                        foreach (var theme in themes)
                        {
                            if (!TryGetArray(theme, "slides", out _))
                                warnings.Add($"Data file '{label}' cards[{cardIndex}].gallery.themes[{themeIndex}] missing array 'slides'.");
                            themeIndex++;
                        }
                    }
                }

                cardIndex++;
            }
            return;
        }

        if (TryGetArray(root, "items", out var items))
        {
            var itemIndex = 0;
            foreach (var item in items)
            {
                if (!HasAnyProperty(item, "title", "name"))
                    warnings.Add($"Data file '{label}' items[{itemIndex}] missing 'name'.");
                itemIndex++;
            }
            return;
        }

        warnings.Add($"Data file '{label}' missing required array 'cards' or 'items'.");
    }

    private static void ValidatePricingJson(JsonElement root, string label, List<string> warnings)
    {
        if (!TryGetArray(root, "cards", out var cards))
        {
            warnings.Add($"Data file '{label}' missing required array 'cards'.");
            return;
        }

        var cardIndex = 0;
        foreach (var card in cards)
        {
            if (!HasAnyProperty(card, "title", "name"))
                warnings.Add($"Data file '{label}' cards[{cardIndex}] missing 'title'.");
            cardIndex++;
        }
    }

    private static void ValidateBenchmarksJson(JsonElement root, string label, List<string> warnings)
    {
        if (TryGetObject(root, "hero", out var hero))
        {
            if (!HasAnyProperty(hero, "title"))
                warnings.Add($"Data file '{label}' hero missing 'title'.");
        }

        if (TryGetObject(root, "about", out var about) && TryGetArray(about, "cards", out var cards))
        {
            var cardIndex = 0;
            foreach (var card in cards)
            {
                if (!HasAnyProperty(card, "title", "name"))
                    warnings.Add($"Data file '{label}' about.cards[{cardIndex}] missing 'title'.");
                cardIndex++;
            }
        }
    }

    private static bool TryGetArray(JsonElement element, string property, out JsonElement.ArrayEnumerator items)
    {
        items = default;
        if (element.ValueKind != JsonValueKind.Object)
            return false;
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
            return false;
        items = value.EnumerateArray();
        return true;
    }

    private static bool TryGetObject(JsonElement element, string property, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object)
            return false;
        if (!element.TryGetProperty(property, out var found) || found.ValueKind != JsonValueKind.Object)
            return false;
        value = found;
        return true;
    }

    private static bool HasAnyProperty(JsonElement element, params string[] properties)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return false;

        foreach (var prop in properties)
        {
            if (element.TryGetProperty(prop, out _))
                return true;
        }

        return false;
    }

    private static void ValidateMarkdownHygiene(string rootPath, string filePath, string? collectionName, string body, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(body))
            return;

        var markdownHygieneWarnings = warnings.Count(warning =>
            warning.StartsWith("Markdown hygiene:", StringComparison.OrdinalIgnoreCase));
        if (markdownHygieneWarnings >= 10)
            return;

        var withoutCodeBlocks = MarkdownFenceRegex.Replace(body, string.Empty);
        var relative = Path.GetRelativePath(rootPath, filePath).Replace('\\', '/');

        var multilineMediaMatches = MarkdownMultilineMediaHtmlRegex.Matches(withoutCodeBlocks);
        if (multilineMediaMatches.Count > 0)
        {
            var mediaTags = multilineMediaMatches
                .Select(match => match.Groups[1].Value.ToLowerInvariant())
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToArray();

            if (mediaTags.Length > 0)
            {
                warnings.Add(
                    $"Markdown hygiene: '{relative}' contains multiline HTML media tags ({string.Join(", ", mediaTags)}). " +
                    "These can render as escaped text. Prefer markdown image syntax or keep media tags single-line.");
            }
        }

        if (string.IsNullOrWhiteSpace(collectionName) ||
            collectionName.IndexOf("doc", StringComparison.OrdinalIgnoreCase) < 0)
            return;

        var matches = MarkdownRawHtmlRegex.Matches(withoutCodeBlocks);
        if (matches.Count == 0)
            return;

        var tags = matches
            .Select(match => match.Groups[1].Value.ToLowerInvariant())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();

        if (tags.Length == 0)
            return;

        warnings.Add($"Markdown hygiene: '{relative}' contains raw HTML tags ({string.Join(", ", tags)}). Prefer Markdown syntax when possible.");
    }

    private static string ResolveSlugPath(string relativePath, string relativeDir, string? slugOverride)
    {
        var withoutExtension = NormalizePath(Path.ChangeExtension(relativePath, null) ?? string.Empty);
        return ApplySlugOverride(withoutExtension, slugOverride);
    }

    private static string ApplySlugOverride(string basePath, string? slugOverride)
    {
        if (string.IsNullOrWhiteSpace(slugOverride))
            return basePath;

        var normalized = NormalizePath(slugOverride);
        if (normalized.Contains('/'))
            return normalized;

        if (string.IsNullOrWhiteSpace(basePath))
            return normalized;

        var idx = basePath.LastIndexOf('/');
        if (idx < 0)
            return normalized;

        var parent = basePath.Substring(0, idx);
        if (string.IsNullOrWhiteSpace(parent))
            return normalized;

        return parent + "/" + normalized;
    }

    private static string? ResolveCollectionRootForFile(string rootPath, string input, string filePath)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var full = Path.IsPathRooted(input) ? input : Path.Combine(rootPath, input);
        if (!full.Contains('*'))
            return Path.GetFullPath(full);

        var normalized = full.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var parts = normalized.Split('*');
        if (parts.Length != 2)
            return Path.GetFullPath(full);

        var basePath = parts[0].TrimEnd(Path.DirectorySeparatorChar);
        var tail = parts[1].TrimStart(Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(basePath) || string.IsNullOrWhiteSpace(tail))
            return Path.GetFullPath(full);

        if (!filePath.StartsWith(basePath, FileSystemPathComparison))
            return Path.GetFullPath(full);

        var relative = Path.GetRelativePath(basePath, filePath);
        var segments = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return Path.GetFullPath(full);

        var wildcardSegment = segments[0];
        var candidate = Path.Combine(basePath, wildcardSegment, tail);
        return Path.GetFullPath(candidate);
    }

    private static string ReplaceProjectPlaceholder(string output, string? projectSlug)
    {
        if (string.IsNullOrWhiteSpace(output))
            return output;
        if (string.IsNullOrWhiteSpace(projectSlug))
            return output.Replace("{project}", string.Empty, StringComparison.OrdinalIgnoreCase);
        return output.Replace("{project}", projectSlug, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveProjectSlug(WebSitePlan plan, string filePath)
    {
        foreach (var project in plan.Projects)
        {
            if (string.IsNullOrWhiteSpace(project.ContentPath))
                continue;

            if (filePath.StartsWith(project.ContentPath, FileSystemPathComparison))
                return project.Slug;
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCollectionFilesWithWildcard(string path)
    {
        var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var parts = normalized.Split('*');
        if (parts.Length != 2)
            return Array.Empty<string>();

        var basePath = parts[0].TrimEnd(Path.DirectorySeparatorChar);
        var tail = parts[1].TrimStart(Path.DirectorySeparatorChar);
        if (!Directory.Exists(basePath))
            return Array.Empty<string>();

        var results = new List<string>();
        foreach (var dir in Directory.GetDirectories(basePath))
        {
            var candidate = string.IsNullOrEmpty(tail) ? dir : Path.Combine(dir, tail);
            if (!Directory.Exists(candidate))
                continue;
            results.AddRange(Directory.EnumerateFiles(candidate, "*.md", SearchOption.AllDirectories));
        }

        return results;
    }

}

