using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PowerForge.Web;

/// <summary>Pagination and taxonomy runtime helpers.</summary>
public static partial class WebSiteBuilder
{
    private const string PaginationPageMetaKey = "pagination_page";
    private const string PaginationTotalPagesMetaKey = "pagination_total_pages";
    private const string PaginationPageSizeMetaKey = "pagination_page_size";
    private const string PaginationTotalItemsMetaKey = "pagination_total_items";
    private const string PaginationPreviousUrlMetaKey = "pagination_previous_url";
    private const string PaginationNextUrlMetaKey = "pagination_next_url";
    private const string PaginationFirstUrlMetaKey = "pagination_first_url";
    private const string PaginationLastUrlMetaKey = "pagination_last_url";
    private const string PaginationPathSegmentMetaKey = "pagination_path_segment";
    private const string PaginationGeneratedMetaKey = "pagination_generated";

    private static List<ContentItem> BuildPaginatedItems(SiteSpec spec, IReadOnlyList<ContentItem> sourceItems)
    {
        if (sourceItems is null || sourceItems.Count == 0)
            return sourceItems?.ToList() ?? new List<ContentItem>();

        var pagination = spec.Pagination;
        if (pagination is not null && !pagination.Enabled)
            return sourceItems.ToList();

        var pageSegment = NormalizePaginationSegment(pagination?.PathSegment);
        var defaultPageSize = pagination?.DefaultPageSize ?? 0;
        var collectionPageSizes = (spec.Collections ?? Array.Empty<CollectionSpec>())
            .Select(CollectionPresetDefaults.Apply)
            .Where(collection => collection is not null && !string.IsNullOrWhiteSpace(collection.Name))
            .Where(collection => (collection.PageSize ?? 0) > 0)
            .ToDictionary(collection => collection.Name, collection => collection.PageSize!.Value, StringComparer.OrdinalIgnoreCase);
        var taxonomyPageSizes = (spec.Taxonomies ?? Array.Empty<TaxonomySpec>())
            .Where(taxonomy => taxonomy is not null && !string.IsNullOrWhiteSpace(taxonomy.Name))
            .Where(taxonomy => (taxonomy.PageSize ?? 0) > 0)
            .ToDictionary(taxonomy => taxonomy.Name, taxonomy => taxonomy.PageSize!.Value, StringComparer.OrdinalIgnoreCase);

        var result = new List<ContentItem>(sourceItems.Count);
        var knownRoutes = new HashSet<string>(
            sourceItems
                .Where(item => item is not null)
                .Select(item => NormalizeRouteForMatch(item.OutputPath)),
            StringComparer.OrdinalIgnoreCase);

        foreach (var item in sourceItems)
        {
            if (item is null)
                continue;

            result.Add(item);

            var pageSize = ResolvePaginationPageSize(item, collectionPageSizes, taxonomyPageSizes, defaultPageSize);
            if (pageSize <= 0)
                continue;

            var fullList = ResolveListItems(spec, item, sourceItems);
            var totalItems = fullList.Count;
            var totalPages = totalItems <= 0 ? 1 : (int)Math.Ceiling(totalItems / (double)pageSize);

            SetPaginationMeta(item, page: 1, totalPages, pageSize, totalItems, pageSegment, spec.TrailingSlash, item.OutputPath);
            if (totalPages <= 1)
                continue;

            for (var page = 2; page <= totalPages; page++)
            {
                var pagedRoute = BuildPaginationRoute(item.OutputPath, pageSegment, page, spec.TrailingSlash);
                var normalizedRoute = NormalizeRouteForMatch(pagedRoute);
                if (!knownRoutes.Add(normalizedRoute))
                    continue;

                var clone = CloneContentItem(item);
                clone.OutputPath = pagedRoute;
                clone.Outputs = new[] { "html" };
                clone.TranslationKey = string.IsNullOrWhiteSpace(item.TranslationKey)
                    ? null
                    : $"{item.TranslationKey}:page:{page.ToString(CultureInfo.InvariantCulture)}";
                SetPaginationMeta(clone, page, totalPages, pageSize, totalItems, pageSegment, spec.TrailingSlash, item.OutputPath);
                result.Add(clone);
            }
        }

        return result;
    }

    private static PaginationRuntime ResolvePaginationRuntime(SiteSpec spec, ContentItem item, IReadOnlyList<ContentItem> fullItems)
    {
        var page = Math.Max(1, GetMetaInt(item.Meta, PaginationPageMetaKey, 1));
        var totalPages = Math.Max(1, GetMetaInt(item.Meta, PaginationTotalPagesMetaKey, 1));
        var pageSize = Math.Max(0, GetMetaInt(item.Meta, PaginationPageSizeMetaKey, 0));
        var totalItems = Math.Max(0, GetMetaInt(item.Meta, PaginationTotalItemsMetaKey, fullItems?.Count ?? 0));
        var pathSegment = GetMetaString(item.Meta, PaginationPathSegmentMetaKey);
        if (string.IsNullOrWhiteSpace(pathSegment))
            pathSegment = NormalizePaginationSegment(spec.Pagination?.PathSegment);

        var firstUrl = GetMetaString(item.Meta, PaginationFirstUrlMetaKey);
        if (string.IsNullOrWhiteSpace(firstUrl))
            firstUrl = BuildPaginationRoute(item.OutputPath, pathSegment, 1, spec.TrailingSlash);

        var lastUrl = GetMetaString(item.Meta, PaginationLastUrlMetaKey);
        if (string.IsNullOrWhiteSpace(lastUrl))
            lastUrl = BuildPaginationRoute(item.OutputPath, pathSegment, totalPages, spec.TrailingSlash);

        var previousUrl = GetMetaString(item.Meta, PaginationPreviousUrlMetaKey);
        var nextUrl = GetMetaString(item.Meta, PaginationNextUrlMetaKey);
        if (string.IsNullOrWhiteSpace(previousUrl) && page > 1)
            previousUrl = BuildPaginationRoute(item.OutputPath, pathSegment, page - 1, spec.TrailingSlash);
        if (string.IsNullOrWhiteSpace(nextUrl) && page < totalPages)
            nextUrl = BuildPaginationRoute(item.OutputPath, pathSegment, page + 1, spec.TrailingSlash);

        return new PaginationRuntime
        {
            Page = page,
            TotalPages = totalPages,
            PageSize = pageSize,
            TotalItems = totalItems,
            HasPrevious = page > 1,
            HasNext = page < totalPages,
            PreviousUrl = previousUrl,
            NextUrl = nextUrl,
            FirstUrl = firstUrl,
            LastUrl = lastUrl,
            PathSegment = pathSegment
        };
    }

    private static IReadOnlyList<ContentItem> ApplyPagination(IReadOnlyList<ContentItem> fullItems, PaginationRuntime pagination)
    {
        if (fullItems is null || fullItems.Count == 0)
            return Array.Empty<ContentItem>();
        if (pagination.PageSize <= 0)
            return fullItems;

        var skip = (pagination.Page - 1) * pagination.PageSize;
        if (skip < 0)
            skip = 0;
        if (skip >= fullItems.Count)
            return Array.Empty<ContentItem>();

        return fullItems.Skip(skip).Take(pagination.PageSize).ToList();
    }

    private static TaxonomyIndexRuntime? BuildTaxonomyIndexRuntime(SiteSpec spec, ContentItem item, IReadOnlyList<ContentItem> allItems)
    {
        if (item.Kind != PageKind.Taxonomy)
            return null;

        var taxonomyKey = GetMetaString(item.Meta, "taxonomy");
        if (string.IsNullOrWhiteSpace(taxonomyKey))
            return null;

        var terms = BuildTaxonomyTermsRuntime(spec, item, allItems);
        var totalItems = terms.Sum(term => term.Count);
        return new TaxonomyIndexRuntime
        {
            Name = taxonomyKey,
            Language = item.Language ?? string.Empty,
            TotalTerms = terms.Length,
            TotalItems = totalItems,
            Terms = terms
        };
    }

    private static TaxonomyTermRuntime[] BuildTaxonomyTermsRuntime(SiteSpec spec, ContentItem item, IReadOnlyList<ContentItem> allItems)
    {
        if (item.Kind != PageKind.Taxonomy)
            return Array.Empty<TaxonomyTermRuntime>();

        var taxonomyKey = GetMetaString(item.Meta, "taxonomy");
        if (string.IsNullOrWhiteSpace(taxonomyKey))
            return Array.Empty<TaxonomyTermRuntime>();

        return allItems
            .Where(candidate => candidate.Kind == PageKind.Term)
            .Where(candidate => !IsGeneratedPaginationItem(candidate))
            .Where(candidate => string.Equals(
                NormalizeLanguageToken(candidate.Language),
                NormalizeLanguageToken(item.Language),
                StringComparison.OrdinalIgnoreCase))
            .Where(candidate => string.Equals(GetMetaString(candidate.Meta, "taxonomy"), taxonomyKey, StringComparison.OrdinalIgnoreCase))
            .Select(candidate =>
            {
                var termItems = ResolveListItems(spec, candidate, allItems);
                return new TaxonomyTermRuntime
                {
                    Name = GetMetaString(candidate.Meta, "term"),
                    Url = candidate.OutputPath,
                    Count = termItems.Count,
                    LatestDateUtc = termItems
                        .Where(entry => entry.Date.HasValue)
                        .Select(entry => entry.Date!.Value.ToUniversalTime())
                        .OrderByDescending(value => value)
                        .FirstOrDefault()
                };
            })
            .OrderByDescending(term => term.Count)
            .ThenBy(term => term.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static TaxonomyTermSummaryRuntime? BuildTaxonomyTermSummaryRuntime(SiteSpec spec, ContentItem item, IReadOnlyList<ContentItem> allItems)
    {
        if (item.Kind != PageKind.Term)
            return null;

        var term = GetMetaString(item.Meta, "term");
        if (string.IsNullOrWhiteSpace(term))
            return null;

        var termItems = ResolveListItems(spec, item, allItems);
        return new TaxonomyTermSummaryRuntime
        {
            Name = term,
            Count = termItems.Count,
            LatestDateUtc = termItems
                .Where(entry => entry.Date.HasValue)
                .Select(entry => entry.Date!.Value.ToUniversalTime())
                .OrderByDescending(value => value)
                .FirstOrDefault()
        };
    }

    private static bool IsGeneratedPaginationItem(ContentItem item)
    {
        if (item?.Meta is null)
            return false;
        if (!TryGetMetaValue(item.Meta, PaginationGeneratedMetaKey, out var value) || value is null)
            return false;

        return value switch
        {
            bool flag => flag,
            string text when bool.TryParse(text, out var parsed) => parsed,
            _ => false
        };
    }

    private static int ResolvePaginationPageSize(
        ContentItem item,
        IReadOnlyDictionary<string, int> collectionPageSizes,
        IReadOnlyDictionary<string, int> taxonomyPageSizes,
        int defaultPageSize)
    {
        if (item.Kind == PageKind.Section)
        {
            if (!string.IsNullOrWhiteSpace(item.Collection) &&
                collectionPageSizes.TryGetValue(item.Collection, out var sectionPageSize))
                return sectionPageSize;
            return Math.Max(0, defaultPageSize);
        }

        if (item.Kind == PageKind.Taxonomy || item.Kind == PageKind.Term)
        {
            if (!string.IsNullOrWhiteSpace(item.Collection) &&
                taxonomyPageSizes.TryGetValue(item.Collection, out var taxonomyPageSize))
                return taxonomyPageSize;
            return Math.Max(0, defaultPageSize);
        }

        return 0;
    }

    private static ContentItem CloneContentItem(ContentItem item)
    {
        return new ContentItem
        {
            SourcePath = item.SourcePath,
            Collection = item.Collection,
            OutputPath = item.OutputPath,
            Language = item.Language,
            TranslationKey = item.TranslationKey,
            Title = item.Title,
            Description = item.Description,
            Date = item.Date,
            Order = item.Order,
            Slug = item.Slug,
            Tags = item.Tags,
            Aliases = item.Aliases,
            Draft = item.Draft,
            Canonical = item.Canonical,
            EditPath = item.EditPath,
            EditUrl = item.EditUrl,
            Layout = item.Layout,
            Template = item.Template,
            Kind = item.Kind,
            HtmlContent = item.HtmlContent,
            TocHtml = item.TocHtml,
            Resources = item.Resources,
            ProjectSlug = item.ProjectSlug,
            Meta = new Dictionary<string, object?>(item.Meta, StringComparer.OrdinalIgnoreCase),
            Outputs = item.Outputs
        };
    }

    private static void SetPaginationMeta(
        ContentItem item,
        int page,
        int totalPages,
        int pageSize,
        int totalItems,
        string pathSegment,
        TrailingSlashMode slashMode,
        string baseRoute)
    {
        item.Meta ??= new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        item.Meta[PaginationPageMetaKey] = page;
        item.Meta[PaginationTotalPagesMetaKey] = totalPages;
        item.Meta[PaginationPageSizeMetaKey] = pageSize;
        item.Meta[PaginationTotalItemsMetaKey] = totalItems;
        item.Meta[PaginationPathSegmentMetaKey] = pathSegment;
        item.Meta[PaginationGeneratedMetaKey] = page > 1;

        var firstUrl = BuildPaginationRoute(baseRoute, pathSegment, 1, slashMode);
        var lastUrl = BuildPaginationRoute(baseRoute, pathSegment, totalPages, slashMode);
        var previousUrl = page > 1 ? BuildPaginationRoute(baseRoute, pathSegment, page - 1, slashMode) : string.Empty;
        var nextUrl = page < totalPages ? BuildPaginationRoute(baseRoute, pathSegment, page + 1, slashMode) : string.Empty;

        item.Meta[PaginationFirstUrlMetaKey] = firstUrl;
        item.Meta[PaginationLastUrlMetaKey] = lastUrl;
        item.Meta[PaginationPreviousUrlMetaKey] = previousUrl;
        item.Meta[PaginationNextUrlMetaKey] = nextUrl;
    }

    private static string BuildPaginationRoute(string baseRoute, string pathSegment, int page, TrailingSlashMode slashMode)
    {
        var normalizedBase = NormalizeRouteForMatch(baseRoute);
        if (page <= 1)
            return EnsureTrailingSlash(normalizedBase, slashMode);

        var combined = normalizedBase.TrimEnd('/') + "/" + pathSegment + "/" + page.ToString(CultureInfo.InvariantCulture);
        if (!combined.StartsWith("/", StringComparison.Ordinal))
            combined = "/" + combined.TrimStart('/');
        return EnsureTrailingSlash(combined, slashMode);
    }

    private static string NormalizePaginationSegment(string? value)
    {
        var segment = string.IsNullOrWhiteSpace(value) ? "page" : value.Trim();
        segment = segment.Trim('/').Replace('\\', '/');
        if (segment.Contains('/'))
            segment = segment.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "page";
        if (string.IsNullOrWhiteSpace(segment))
            segment = "page";
        return segment;
    }

    private static int GetMetaInt(Dictionary<string, object?>? meta, string key, int fallback)
    {
        if (meta is null || meta.Count == 0)
            return fallback;
        if (!TryGetMetaValue(meta, key, out var value) || value is null)
            return fallback;

        return value switch
        {
            int intValue => intValue,
            long longValue => (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, longValue)),
            short shortValue => shortValue,
            byte byteValue => byteValue,
            string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => fallback
        };
    }
}
