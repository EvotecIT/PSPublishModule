using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

/// <summary>SEO template and preview helpers.</summary>
public static partial class WebSiteBuilder
{
    private static readonly Regex SeoTokenRegex = new(
        "\\{(?<token>[a-zA-Z][a-zA-Z0-9_]*)\\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static void WriteSeoPreviewReport(SiteSpec spec, IReadOnlyList<ContentItem> items, string metaDir)
    {
        if (spec is null || items is null || string.IsNullOrWhiteSpace(metaDir))
            return;

        var pagePreview = items
            .Where(static item => !item.Draft)
            .OrderBy(static item => NormalizeRouteForMatch(item.OutputPath), StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.SourcePath, StringComparer.OrdinalIgnoreCase)
            .Select(item =>
            {
                var titleTemplate = ResolveEffectiveSeoTitleTemplate(spec, item);
                var descriptionTemplate = ResolveEffectiveSeoDescriptionTemplate(spec, item);
                var canonicalOrOutput = string.IsNullOrWhiteSpace(item.Canonical) ? item.OutputPath : item.Canonical;
                return new
                {
                    sourcePath = item.SourcePath,
                    outputPath = NormalizeRouteForMatch(item.OutputPath),
                    canonicalUrl = ResolveAbsoluteUrl(spec.BaseUrl, canonicalOrOutput),
                    collection = item.Collection,
                    language = ResolveSeoLanguage(spec, item),
                    project = item.ProjectSlug,
                    date = item.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    title = item.Title,
                    seoTitle = ResolveSeoTitle(spec, item),
                    description = item.Description,
                    seoDescription = ResolveMetaDescription(spec, item),
                    titleTemplate,
                    descriptionTemplate
                };
            })
            .ToArray();

        var payload = new
        {
            site = spec.Name,
            templates = new
            {
                title = spec.Seo?.Templates?.Title,
                description = spec.Seo?.Templates?.Description
            },
            pages = pagePreview
        };

        var seoPreviewPath = Path.Combine(metaDir, "seo-preview.json");
        WriteAllTextIfChanged(seoPreviewPath, JsonSerializer.Serialize(payload, WebJson.Options));
    }

    private static string ResolveSeoTitle(SiteSpec spec, ContentItem item)
    {
        var overrideTitle = GetMetaString(item.Meta, "seo_title");
        if (!string.IsNullOrWhiteSpace(overrideTitle))
            return overrideTitle.Trim();

        var fallback = ResolveSeoTitleDefault(spec, item);

        var template = ResolveEffectiveSeoTitleTemplate(spec, item);
        if (string.IsNullOrWhiteSpace(template))
            return fallback;

        var rendered = ApplySeoTemplate(template, spec, item, fallback, ResolveMetaDescriptionDefault(spec, item));
        return string.IsNullOrWhiteSpace(rendered) ? fallback : rendered;
    }

    private static string ResolveMetaDescription(SiteSpec spec, ContentItem item)
    {
        var overrideDescription = GetMetaString(item.Meta, "seo_description");
        if (!string.IsNullOrWhiteSpace(overrideDescription))
            return overrideDescription.Trim();

        var fallback = ResolveMetaDescriptionDefault(spec, item);
        var template = ResolveEffectiveSeoDescriptionTemplate(spec, item);
        if (string.IsNullOrWhiteSpace(template))
            return fallback;

        var rendered = ApplySeoTemplate(template, spec, item, ResolveSeoTitleDefault(spec, item), fallback);
        return string.IsNullOrWhiteSpace(rendered) ? fallback : rendered;
    }

    private static string? ResolveEffectiveSeoTitleTemplate(SiteSpec spec, ContentItem item)
    {
        if (spec.Seo?.Enabled == false)
            return null;

        var collectionTemplate = ResolveCollectionSeoTemplate(spec, item, static templates => templates.Title);
        if (!string.IsNullOrWhiteSpace(collectionTemplate))
            return collectionTemplate.Trim();

        return string.IsNullOrWhiteSpace(spec.Seo?.Templates?.Title)
            ? null
            : spec.Seo.Templates.Title.Trim();
    }

    private static string? ResolveEffectiveSeoDescriptionTemplate(SiteSpec spec, ContentItem item)
    {
        if (spec.Seo?.Enabled == false)
            return null;

        var collectionTemplate = ResolveCollectionSeoTemplate(spec, item, static templates => templates.Description);
        if (!string.IsNullOrWhiteSpace(collectionTemplate))
            return collectionTemplate.Trim();

        return string.IsNullOrWhiteSpace(spec.Seo?.Templates?.Description)
            ? null
            : spec.Seo.Templates.Description.Trim();
    }

    private static string? ResolveCollectionSeoTemplate(SiteSpec spec, ContentItem item, Func<SeoTemplatesSpec, string?> selector)
    {
        var collection = spec.Collections
            .FirstOrDefault(collection =>
                collection is not null &&
                !string.IsNullOrWhiteSpace(collection.Name) &&
                string.Equals(collection.Name, item.Collection, StringComparison.OrdinalIgnoreCase));
        if (collection?.Seo?.Enabled == false)
            return null;
        return collection?.Seo?.Templates is null ? null : selector(collection.Seo.Templates);
    }

    private static string ApplySeoTemplate(
        string template,
        SiteSpec spec,
        ContentItem item,
        string fallbackTitle,
        string fallbackDescription)
    {
        var rendered = SeoTokenRegex.Replace(template, match =>
        {
            var token = match.Groups["token"].Value;
            return ResolveSeoTokenValue(token, spec, item, fallbackTitle, fallbackDescription) ?? match.Value;
        });

        return WhitespaceRegex.Replace(rendered, " ").Trim();
    }

    private static string? ResolveSeoTokenValue(
        string token,
        SiteSpec spec,
        ContentItem item,
        string fallbackTitle,
        string fallbackDescription)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        return token.Trim().ToLowerInvariant() switch
        {
            "title" => fallbackTitle,
            "site" => spec.Name ?? string.Empty,
            "collection" => item.Collection ?? string.Empty,
            "date" => item.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
            "project" => item.ProjectSlug ?? string.Empty,
            "lang" => ResolveSeoLanguage(spec, item),
            "description" => fallbackDescription,
            "path" => NormalizeRouteForMatch(item.OutputPath),
            _ => null
        };
    }

    private static string ResolveSeoLanguage(SiteSpec spec, ContentItem item)
    {
        var normalized = NormalizeLanguageToken(item.Language);
        if (!string.IsNullOrWhiteSpace(normalized))
            return normalized;

        normalized = NormalizeLanguageToken(spec.Localization?.DefaultLanguage);
        return string.IsNullOrWhiteSpace(normalized) ? "en" : normalized;
    }

    private static string ResolveSeoTitleDefault(SiteSpec spec, ContentItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Title))
            return item.Title.Trim();
        if (!string.IsNullOrWhiteSpace(spec.Name))
            return spec.Name.Trim();
        return "Documentation";
    }
}
