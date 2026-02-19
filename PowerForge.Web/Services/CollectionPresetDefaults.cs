using System;
using System.Collections.Generic;

namespace PowerForge.Web;

internal static class CollectionPresetDefaults
{
    private sealed class Template
    {
        public string? DefaultLayout { get; init; }
        public string? ListLayout { get; init; }
        public string? SortBy { get; init; }
        public SortOrder? SortOrder { get; init; }
        public int? PageSize { get; init; }
        public bool? AutoGenerateSectionIndex { get; init; }
        public string? AutoSectionTitle { get; init; }
    }

    private static readonly IReadOnlyDictionary<string, Template> Templates =
        new Dictionary<string, Template>(StringComparer.OrdinalIgnoreCase)
        {
            ["blog"] = new Template
            {
                DefaultLayout = "post",
                ListLayout = "list",
                SortBy = "date",
                SortOrder = SortOrder.Desc,
                PageSize = 10,
                AutoGenerateSectionIndex = true,
                AutoSectionTitle = "Blog"
            },
            ["news"] = new Template
            {
                DefaultLayout = "post",
                ListLayout = "list",
                SortBy = "date",
                SortOrder = SortOrder.Desc,
                PageSize = 15,
                AutoGenerateSectionIndex = true,
                AutoSectionTitle = "News"
            },
            ["changelog"] = new Template
            {
                DefaultLayout = "post",
                ListLayout = "list",
                SortBy = "date",
                SortOrder = SortOrder.Desc,
                PageSize = 30,
                AutoGenerateSectionIndex = true,
                AutoSectionTitle = "Changelog"
            },
            ["editorial"] = new Template
            {
                DefaultLayout = "post",
                ListLayout = "list",
                SortBy = "date",
                SortOrder = SortOrder.Desc,
                PageSize = 12,
                AutoGenerateSectionIndex = true
            },
            ["docs"] = new Template
            {
                DefaultLayout = "docs",
                ListLayout = "docs-list",
                SortBy = "order",
                SortOrder = SortOrder.Asc
            },
            ["knowledgebase"] = new Template
            {
                DefaultLayout = "docs",
                ListLayout = "docs-list",
                SortBy = "order",
                SortOrder = SortOrder.Asc
            },
            ["kb"] = new Template
            {
                DefaultLayout = "docs",
                ListLayout = "docs-list",
                SortBy = "order",
                SortOrder = SortOrder.Asc
            },
            ["pages"] = new Template
            {
                DefaultLayout = "page",
                SortBy = "order",
                SortOrder = SortOrder.Asc
            },
            ["landing"] = new Template
            {
                DefaultLayout = "page",
                SortBy = "order",
                SortOrder = SortOrder.Asc
            }
        };

    internal static CollectionSpec Apply(CollectionSpec collection)
    {
        if (collection is null)
            throw new ArgumentNullException(nameof(collection));

        var preset = ResolvePresetKey(collection.Preset);
        if (string.IsNullOrWhiteSpace(preset) || !Templates.TryGetValue(preset, out var template))
            return collection;

        // Avoid mutating user-provided object graph.
        var resolved = new CollectionSpec
        {
            Name = collection.Name,
            Preset = collection.Preset,
            Input = collection.Input,
            Output = collection.Output,
            DefaultLayout = collection.DefaultLayout,
            ListLayout = collection.ListLayout,
            TocFile = collection.TocFile,
            UseToc = collection.UseToc,
            Include = collection.Include ?? Array.Empty<string>(),
            Exclude = collection.Exclude ?? Array.Empty<string>(),
            SortBy = collection.SortBy,
            SortOrder = collection.SortOrder,
            Outputs = collection.Outputs ?? Array.Empty<string>(),
            Seo = collection.Seo,
            PageSize = collection.PageSize,
            AutoGenerateSectionIndex = collection.AutoGenerateSectionIndex,
            AutoSectionTitle = collection.AutoSectionTitle,
            AutoSectionDescription = collection.AutoSectionDescription
        };

        if (string.IsNullOrWhiteSpace(resolved.DefaultLayout))
            resolved.DefaultLayout = template.DefaultLayout;
        if (string.IsNullOrWhiteSpace(resolved.ListLayout))
            resolved.ListLayout = template.ListLayout;
        if (string.IsNullOrWhiteSpace(resolved.SortBy))
            resolved.SortBy = template.SortBy;
        if (!resolved.SortOrder.HasValue && template.SortOrder.HasValue)
            resolved.SortOrder = template.SortOrder.Value;
        if (!resolved.PageSize.HasValue && template.PageSize.HasValue)
            resolved.PageSize = template.PageSize.Value;
        if (!resolved.AutoGenerateSectionIndex && template.AutoGenerateSectionIndex == true)
            resolved.AutoGenerateSectionIndex = true;
        if (string.IsNullOrWhiteSpace(resolved.AutoSectionTitle) && !string.IsNullOrWhiteSpace(template.AutoSectionTitle))
            resolved.AutoSectionTitle = template.AutoSectionTitle;

        return resolved;
    }

    internal static string? ResolvePresetKey(string? preset)
    {
        if (string.IsNullOrWhiteSpace(preset))
            return null;
        var normalized = preset.Trim().ToLowerInvariant();
        return normalized switch
        {
            "release-notes" or "release_notes" or "releases" => "changelog",
            "knowledge-base" or "knowledge_base" => "knowledgebase",
            _ => normalized
        };
    }

    internal static bool IsEditorialCollection(CollectionSpec? collection, string? fallbackCollectionName)
    {
        var name = fallbackCollectionName;
        var output = string.Empty;
        if (collection is not null)
        {
            name = string.IsNullOrWhiteSpace(collection.Name) ? name : collection.Name;
            output = collection.Output ?? string.Empty;
            var preset = ResolvePresetKey(collection.Preset);
            if (preset is "blog" or "news" or "changelog" or "editorial")
                return true;
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            var normalized = name.Trim();
            if (normalized.Equals("blog", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("news", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("changelog", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return !string.IsNullOrWhiteSpace(output) &&
               (output.StartsWith("/blog", StringComparison.OrdinalIgnoreCase) ||
                output.StartsWith("/news", StringComparison.OrdinalIgnoreCase) ||
                output.StartsWith("/changelog", StringComparison.OrdinalIgnoreCase));
    }
}
