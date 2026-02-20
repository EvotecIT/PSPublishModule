using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;

namespace PowerForge.Web;

/// <summary>
/// Helper methods exposed to Scriban templates as <c>pf</c>.
/// These helpers exist to keep theme navigation rendering consistent across sites.
/// </summary>
internal sealed class ScribanThemeHelpers
{
    private readonly ThemeRenderContext _context;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex WhitespaceRegex = new("\\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex AspectRatioRegex = new("^\\s*(?<w>\\d+(?:\\.\\d+)?)\\s*(?:/|:)\\s*(?<h>\\d+(?:\\.\\d+)?)\\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex ObjectPositionTokenRegex = new("^-?(?:\\d+(?:\\.\\d+)?)(?:%|px|rem|em|vw|vh)?$", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);

    public ScribanThemeHelpers(ThemeRenderContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    internal static int ParseInt(object? value, int defaultValue)
    {
        if (value is null)
            return defaultValue;

        if (value is int i)
            return i;

        if (value is long l)
            return unchecked((int)l);

        if (value is double d)
            return (int)Math.Round(d);

        if (value is string s && int.TryParse(s, out var parsed))
            return parsed;

        try
        {
            return Convert.ToInt32(value);
        }
        catch
        {
            return defaultValue;
        }
    }

    internal static bool ParseBool(object? value, bool defaultValue)
    {
        if (value is null)
            return defaultValue;

        if (value is bool b)
            return b;

        if (value is int i)
            return i != 0;

        if (value is long l)
            return l != 0;

        if (value is double d)
            return d != 0d;

        if (value is string s)
        {
            if (bool.TryParse(s, out var parsedBool))
                return parsedBool;
            if (int.TryParse(s, out var parsedInt))
                return parsedInt != 0;
        }

        return defaultValue;
    }

    public NavigationMenu? Menu(string? name)
    {
        var key = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key))
            return null;

        return (_context.Navigation.Menus ?? Array.Empty<NavigationMenu>())
            .FirstOrDefault(m => m is not null && string.Equals(m.Name, key, StringComparison.OrdinalIgnoreCase));
    }

    public NavigationSurfaceRuntime? Surface(string? name)
    {
        var key = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key))
            return null;

        return (_context.Navigation.Surfaces ?? Array.Empty<NavigationSurfaceRuntime>())
            .FirstOrDefault(s => s is not null && string.Equals(s.Name, key, StringComparison.OrdinalIgnoreCase));
    }

    public string NavLinks(string? menuName = "main", int maxDepth = 1)
    {
        var menu = Menu(menuName);
        if (menu?.Items is null || menu.Items.Length == 0)
            return string.Empty;

        var depth = Math.Clamp(maxDepth, 1, 6);
        var sb = new StringBuilder();
        foreach (var item in menu.Items)
        {
            RenderNavItem(sb, item, depth, 1);
        }
        return sb.ToString();
    }

    public string NavActions()
    {
        var actions = _context.Navigation.Actions ?? Array.Empty<NavigationItem>();
        if (actions.Length == 0)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var action in actions)
        {
            RenderAction(sb, action);
        }
        return sb.ToString();
    }

    public string MenuTree(string? menuName = "main", int maxDepth = 3)
    {
        var menu = Menu(menuName);
        if (menu?.Items is null || menu.Items.Length == 0)
            return string.Empty;

        var depth = Math.Clamp(maxDepth, 1, 10);
        var sb = new StringBuilder();
        sb.Append("<ul data-pf-menu=\"").Append(Html(menu.Name)).Append("\">");
        foreach (var item in menu.Items)
        {
            RenderMenuTreeItem(sb, item, depth, 1);
        }
        sb.Append("</ul>");
        return sb.ToString();
    }

    public string EditorialCards(
        int maxItems = 0,
        int excerptLength = 160,
        bool showCollection = true,
        bool showDate = true,
        bool showTags = true,
        bool showImage = true,
        string? imageAspect = null,
        string? fallbackImage = null,
        string? variant = null,
        string? gridClass = null,
        string? cardClass = null,
        bool? showCategories = null,
        bool? linkTaxonomy = null)
    {
        var items = _context.Items ?? Array.Empty<ContentItem>();
        if (items.Count == 0)
            return string.Empty;

        var currentCollection = ResolveCurrentCollection();
        var collectionCards = currentCollection?.EditorialCards;
        var take = maxItems > 0 ? maxItems : int.MaxValue;
        var maxExcerptLength = Math.Clamp(excerptLength, 40, 600);
        var normalizedAspect = NormalizeAspectRatio(CoalesceTrimmed(imageAspect, collectionCards?.ImageAspect));
        var normalizedVariant = NormalizeEditorialVariant(CoalesceTrimmed(variant, collectionCards?.Variant));
        var resolvedGridClass = CoalesceTrimmed(gridClass, collectionCards?.GridClass);
        var resolvedCardClass = CoalesceTrimmed(cardClass, collectionCards?.CardClass);
        var resolvedShowCategories = showCategories ?? collectionCards?.ShowCategories ?? false;
        var resolvedLinkTaxonomy = linkTaxonomy ?? collectionCards?.LinkTaxonomy ?? false;
        var defaultFallbackImage = CoalesceTrimmed(fallbackImage, collectionCards?.Image, _context.Site?.Social?.Image) ?? string.Empty;

        var selected = items
            .Where(static item => item is not null && !item.Draft && !string.IsNullOrWhiteSpace(item.OutputPath))
            .Take(take)
            .ToArray();
        if (selected.Length == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.Append("<div class=\"").Append(Html(BuildEditorialGridClass(normalizedVariant, resolvedGridClass))).Append("\">");
        for (var index = 0; index < selected.Length; index++)
        {
            var item = selected[index];
            var title = string.IsNullOrWhiteSpace(item.Title) ? item.OutputPath : item.Title;
            var summary = ResolveSummary(item, maxExcerptLength);
            var image = showImage ? ResolveCardImage(item.Meta, defaultFallbackImage) : string.Empty;
            var imageAlt = ResolveCardImageAlt(item.Meta, title);
            var imageFitRaw = ResolveCardImageFit(item.Meta, collectionCards?.ImageFit);
            var imagePositionRaw = ResolveCardImagePosition(item.Meta, collectionCards?.ImagePosition);
            var imageFit = NormalizeObjectFit(imageFitRaw);
            var imagePosition = NormalizeObjectPosition(imagePositionRaw);
            var imageStyle = BuildCardImageStyle(imageFitRaw, imageFit, imagePositionRaw, imagePosition);

            sb.Append("<a class=\"").Append(Html(BuildEditorialCardClass(normalizedVariant, index, resolvedCardClass))).Append("\" href=\"").Append(Html(item.OutputPath)).Append("\">");
            if (!string.IsNullOrWhiteSpace(image))
            {
                sb.Append("<span class=\"pf-editorial-card-media\"");
                if (!string.IsNullOrWhiteSpace(normalizedAspect))
                    sb.Append(" style=\"aspect-ratio: ").Append(Html(normalizedAspect)).Append(";\"");
                sb.Append(">");
                sb.Append("<img class=\"pf-editorial-card-image\" src=\"").Append(Html(image)).Append("\" alt=\"").Append(Html(imageAlt)).Append("\"");
                if (!string.IsNullOrWhiteSpace(imageStyle))
                    sb.Append(" style=\"").Append(Html(imageStyle)).Append("\"");
                sb.Append(" loading=\"lazy\" decoding=\"async\" />");
                sb.Append("</span>");
            }

            if (showCollection || showDate)
            {
                sb.Append("<p class=\"pf-editorial-meta\">");
                if (showCollection && !string.IsNullOrWhiteSpace(item.Collection))
                    sb.Append("<span>").Append(Html(item.Collection)).Append("</span>");
                if (showDate && item.Date.HasValue)
                    sb.Append("<time datetime=\"").Append(item.Date.Value.ToString("yyyy-MM-dd")).Append("\">")
                      .Append(item.Date.Value.ToString("yyyy-MM-dd"))
                      .Append("</time>");
                sb.Append("</p>");
            }

            sb.Append("<h3>").Append(Html(title)).Append("</h3>");
            if (!string.IsNullOrWhiteSpace(summary))
                sb.Append("<p class=\"pf-editorial-summary\">").Append(Html(summary)).Append("</p>");

            if (showTags || resolvedShowCategories)
            {
                var tags = showTags
                    ? (item.Tags ?? Array.Empty<string>())
                        .Where(static tag => !string.IsNullOrWhiteSpace(tag))
                        .Select(static tag => tag.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(6)
                        .ToArray()
                    : Array.Empty<string>();
                var categories = resolvedShowCategories
                    ? ResolveTaxonomyValues(item, "categories")
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(6)
                        .ToArray()
                    : Array.Empty<string>();

                if (tags.Length > 0 || categories.Length > 0)
                {
                    sb.Append("<div class=\"pf-editorial-tags\">");
                    foreach (var category in categories)
                        AppendTaxonomyChip(sb, "categories", category, "pf-chip pf-chip--category", resolvedLinkTaxonomy);
                    foreach (var tag in tags)
                        AppendTaxonomyChip(sb, "tags", tag, "pf-chip pf-chip--tag", resolvedLinkTaxonomy);
                    sb.Append("</div>");
                }
            }

            sb.Append("</a>");
        }
        sb.Append("</div>");
        return sb.ToString();
    }

    public string EditorialPager(string newerLabel = "Newer posts", string olderLabel = "Older posts", string cssClass = "pf-pagination")
    {
        var pagination = _context.Pagination;
        if (pagination is null || pagination.TotalPages <= 1)
            return string.Empty;

        var newer = string.IsNullOrWhiteSpace(newerLabel) ? "Newer posts" : newerLabel.Trim();
        var older = string.IsNullOrWhiteSpace(olderLabel) ? "Older posts" : olderLabel.Trim();
        var classes = string.IsNullOrWhiteSpace(cssClass) ? "pf-pagination" : cssClass.Trim();

        var sb = new StringBuilder();
        sb.Append("<nav class=\"").Append(Html(classes)).Append("\" aria-label=\"Pagination\">");
        sb.Append("<div>");
        if (pagination.HasPrevious && !string.IsNullOrWhiteSpace(pagination.PreviousUrl))
        {
            sb.Append("<a href=\"").Append(Html(pagination.PreviousUrl)).Append("\">")
              .Append(Html(newer))
              .Append("</a>");
        }
        sb.Append("</div>");

        sb.Append("<div>");
        if (pagination.HasNext && !string.IsNullOrWhiteSpace(pagination.NextUrl))
        {
            sb.Append("<a href=\"").Append(Html(pagination.NextUrl)).Append("\">")
              .Append(Html(older))
              .Append("</a>");
        }
        sb.Append("</div>");
        sb.Append("</nav>");
        return sb.ToString();
    }

    public string EditorialPostNav(
        string backLabel = "Back to list",
        string newerLabel = "Newer post",
        string olderLabel = "Older post",
        string relatedHeading = "Related posts",
        int relatedCount = 3,
        string cssClass = "pf-post-nav")
    {
        var page = _context.Page;
        if (page is null || string.IsNullOrWhiteSpace(page.OutputPath) || string.IsNullOrWhiteSpace(page.Collection))
            return string.Empty;

        var collection = ResolveCurrentCollection();
        var collectionHref = ResolveCollectionHref(collection, page.Collection);
        var backText = string.IsNullOrWhiteSpace(backLabel) ? "Back to list" : backLabel.Trim();
        var newerText = string.IsNullOrWhiteSpace(newerLabel) ? "Newer post" : newerLabel.Trim();
        var olderText = string.IsNullOrWhiteSpace(olderLabel) ? "Older post" : olderLabel.Trim();
        var relatedTitle = string.IsNullOrWhiteSpace(relatedHeading) ? "Related posts" : relatedHeading.Trim();
        var classes = string.IsNullOrWhiteSpace(cssClass) ? "pf-post-nav" : cssClass.Trim();

        var sourceItems = _context.AllItems.Count > 0
            ? _context.AllItems
            : _context.Items;
        var posts = sourceItems
            .Where(item =>
                item is not null &&
                !item.Draft &&
                string.Equals(item.Collection, page.Collection, StringComparison.OrdinalIgnoreCase) &&
                item.Kind == PageKind.Page &&
                !string.IsNullOrWhiteSpace(item.OutputPath))
            .OrderByDescending(item => item.Date ?? DateTime.MinValue)
            .ThenBy(item => item.Title ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (posts.Length == 0)
            return string.Empty;

        var currentIndex = Array.FindIndex(posts, candidate =>
            string.Equals(NormalizePath(candidate.OutputPath), NormalizePath(page.OutputPath), StringComparison.OrdinalIgnoreCase));

        ContentItem? newer = null;
        ContentItem? older = null;
        if (currentIndex >= 0)
        {
            if (currentIndex > 0)
                newer = posts[currentIndex - 1];
            if (currentIndex < posts.Length - 1)
                older = posts[currentIndex + 1];
        }

        var related = ResolveRelatedPosts(posts, page, currentIndex, Math.Clamp(relatedCount, 1, 8));
        if (string.IsNullOrWhiteSpace(collectionHref) && newer is null && older is null && related.Length == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.Append("<section class=\"").Append(Html(classes)).Append("\">");

        sb.Append("<div class=\"pf-post-nav-top\">");
        if (!string.IsNullOrWhiteSpace(collectionHref))
        {
            sb.Append("<a class=\"pf-post-nav-back\" href=\"").Append(Html(collectionHref)).Append("\">")
              .Append(Html(backText))
              .Append("</a>");
        }
        sb.Append("<nav class=\"pf-post-nav-links\" aria-label=\"Post navigation\">");
        if (newer is not null)
        {
            var newerTitle = string.IsNullOrWhiteSpace(newer.Title) ? newer.OutputPath : newer.Title;
            sb.Append("<a class=\"pf-post-nav-newer\" href=\"").Append(Html(newer.OutputPath)).Append("\">")
              .Append(Html(newerText))
              .Append(": ")
              .Append(Html(newerTitle))
              .Append("</a>");
        }
        if (older is not null)
        {
            var olderTitle = string.IsNullOrWhiteSpace(older.Title) ? older.OutputPath : older.Title;
            sb.Append("<a class=\"pf-post-nav-older\" href=\"").Append(Html(older.OutputPath)).Append("\">")
              .Append(Html(olderText))
              .Append(": ")
              .Append(Html(olderTitle))
              .Append("</a>");
        }
        sb.Append("</nav>");
        sb.Append("</div>");

        if (related.Length > 0)
        {
            sb.Append("<div class=\"pf-post-nav-related\">");
            sb.Append("<h2>").Append(Html(relatedTitle)).Append("</h2>");
            sb.Append("<ul>");
            foreach (var candidate in related)
            {
                var relatedTextValue = string.IsNullOrWhiteSpace(candidate.Title) ? candidate.OutputPath : candidate.Title;
                sb.Append("<li><a href=\"")
                  .Append(Html(candidate.OutputPath))
                  .Append("\">")
                  .Append(Html(relatedTextValue))
                  .Append("</a></li>");
            }
            sb.Append("</ul>");
            sb.Append("</div>");
        }

        sb.Append("</section>");
        return sb.ToString();
    }

    private static void RenderNavItem(StringBuilder sb, NavigationItem item, int maxDepth, int depth)
    {
        if (item is null)
            return;

        var text = string.IsNullOrWhiteSpace(item.Text) ? item.Title : item.Text;
        if (string.IsNullOrWhiteSpace(text))
            return;

        var hasUrl = !string.IsNullOrWhiteSpace(item.Url);
        var hasChildren = item.Items is { Length: > 0 } && depth < maxDepth;

        if (hasChildren)
        {
            // Minimal dropdown structure that themes can style/replace.
            sb.Append("<details class=\"pf-nav-group\"");
            if (item.IsActive || item.IsAncestor) sb.Append(" open");
            sb.Append(">");
            sb.Append("<summary class=\"pf-nav-group__summary\">");
            if (!string.IsNullOrWhiteSpace(item.IconHtml))
                sb.Append(item.IconHtml);
            sb.Append(Html(text));
            sb.Append("</summary>");
            sb.Append("<div class=\"pf-nav-group__items\">");
            foreach (var child in item.Items ?? Array.Empty<NavigationItem>())
                RenderNavItem(sb, child, maxDepth, depth + 1);
            sb.Append("</div>");
            sb.Append("</details>");
            return;
        }

        if (!hasUrl)
            return;

        sb.Append("<a href=\"").Append(Html(item.Url!)).Append("\"");

        var cls = BuildClass(item, null);
        if (!string.IsNullOrWhiteSpace(cls))
            sb.Append(" class=\"").Append(Html(cls)).Append("\"");

        if (!string.IsNullOrWhiteSpace(item.Target))
            sb.Append(" target=\"").Append(Html(item.Target!)).Append("\"");
        if (!string.IsNullOrWhiteSpace(item.Rel))
            sb.Append(" rel=\"").Append(Html(item.Rel!)).Append("\"");
        if (!string.IsNullOrWhiteSpace(item.AriaLabel))
            sb.Append(" aria-label=\"").Append(Html(item.AriaLabel!)).Append("\"");

        sb.Append(">");
        if (!string.IsNullOrWhiteSpace(item.IconHtml))
            sb.Append(item.IconHtml);
        sb.Append(Html(text));
        sb.Append("</a>");
    }

    private static void RenderMenuTreeItem(StringBuilder sb, NavigationItem item, int maxDepth, int depth)
    {
        if (item is null)
            return;

        var text = string.IsNullOrWhiteSpace(item.Text) ? item.Title : item.Text;
        if (string.IsNullOrWhiteSpace(text))
            return;

        var hasUrl = !string.IsNullOrWhiteSpace(item.Url);
        var children = item.Items ?? Array.Empty<NavigationItem>();
        var hasChildren = children.Length > 0 && depth < maxDepth;

        sb.Append("<li");
        var cls = BuildClass(item, "pf-menu__item");
        if (!string.IsNullOrWhiteSpace(cls))
            sb.Append(" class=\"").Append(Html(cls)).Append("\"");
        sb.Append(">");

        if (hasUrl)
        {
            sb.Append("<a href=\"").Append(Html(item.Url!)).Append("\">");
            sb.Append(Html(text));
            sb.Append("</a>");
        }
        else
        {
            sb.Append("<span>").Append(Html(text)).Append("</span>");
        }

        if (hasChildren)
        {
            sb.Append("<ul>");
            foreach (var child in children)
                RenderMenuTreeItem(sb, child, maxDepth, depth + 1);
            sb.Append("</ul>");
        }

        sb.Append("</li>");
    }

    private static void RenderAction(StringBuilder sb, NavigationItem action)
    {
        if (action is null)
            return;

        var isButton = string.Equals(action.Kind, "button", StringComparison.OrdinalIgnoreCase);
        var hasUrl = !string.IsNullOrWhiteSpace(action.Url);

        var title = action.Title;
        var ariaLabel = string.IsNullOrWhiteSpace(action.AriaLabel) ? title : action.AriaLabel;
        var iconHtml = string.IsNullOrWhiteSpace(action.IconHtml) ? null : action.IconHtml;
        var text = string.IsNullOrWhiteSpace(action.Text) ? null : action.Text;
        var hasIcon = !string.IsNullOrWhiteSpace(iconHtml);
        if (text is null && !hasIcon && !string.IsNullOrWhiteSpace(title))
            text = title;

        if (isButton)
        {
            sb.Append("<button type=\"button\"");
            if (!string.IsNullOrWhiteSpace(action.CssClass))
                sb.Append(" class=\"").Append(Html(action.CssClass!)).Append("\"");
            if (!string.IsNullOrWhiteSpace(title))
                sb.Append(" title=\"").Append(Html(title)).Append("\"");
            if (!string.IsNullOrWhiteSpace(ariaLabel))
                sb.Append(" aria-label=\"").Append(Html(ariaLabel!)).Append("\"");
            sb.Append(">");
            if (hasIcon)
                sb.Append(iconHtml);
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (hasIcon) sb.Append(" ");
                sb.Append(Html(text));
            }
            sb.Append("</button>");
            return;
        }

        if (!hasUrl)
            return;

        sb.Append("<a href=\"").Append(Html(action.Url!)).Append("\"");
        if (!string.IsNullOrWhiteSpace(action.CssClass))
            sb.Append(" class=\"").Append(Html(action.CssClass!)).Append("\"");
        if (!string.IsNullOrWhiteSpace(action.Target))
            sb.Append(" target=\"").Append(Html(action.Target!)).Append("\"");
        if (!string.IsNullOrWhiteSpace(action.Rel))
            sb.Append(" rel=\"").Append(Html(action.Rel!)).Append("\"");
        if (!string.IsNullOrWhiteSpace(title))
            sb.Append(" title=\"").Append(Html(title)).Append("\"");
        if (!string.IsNullOrWhiteSpace(ariaLabel))
            sb.Append(" aria-label=\"").Append(Html(ariaLabel!)).Append("\"");
        sb.Append(">");
        if (hasIcon)
            sb.Append(iconHtml);
        if (!string.IsNullOrWhiteSpace(text))
        {
            if (hasIcon) sb.Append(" ");
            sb.Append(Html(text));
        }
        sb.Append("</a>");
    }

    private static string ResolveSummary(ContentItem item, int maxLength)
    {
        if (!string.IsNullOrWhiteSpace(item.Description))
            return Truncate(item.Description.Trim(), maxLength);

        if (string.IsNullOrWhiteSpace(item.HtmlContent))
            return string.Empty;

        var plain = HtmlTagRegex.Replace(item.HtmlContent, " ");
        plain = WhitespaceRegex.Replace(plain, " ").Trim();
        return Truncate(plain, maxLength);
    }

    private CollectionSpec? ResolveCurrentCollection()
    {
        var collectionName = _context.Page?.Collection;
        if (string.IsNullOrWhiteSpace(collectionName))
            return null;

        var collections = _context.Site?.Collections;
        if (collections is null || collections.Length == 0)
            return null;

        var match = collections.FirstOrDefault(collection =>
            collection is not null &&
            !string.IsNullOrWhiteSpace(collection.Name) &&
            collection.Name.Equals(collectionName, StringComparison.OrdinalIgnoreCase));

        return match is null ? null : CollectionPresetDefaults.Apply(match);
    }

    private ContentItem[] ResolveRelatedPosts(
        IReadOnlyList<ContentItem> orderedCollectionItems,
        ContentItem page,
        int currentIndex,
        int maxItems)
    {
        if (orderedCollectionItems is null || orderedCollectionItems.Count == 0 || maxItems <= 0)
            return Array.Empty<ContentItem>();

        var currentTags = (page.Tags ?? Array.Empty<string>())
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Select(static tag => tag.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates = orderedCollectionItems
            .Where((candidate, index) => index != currentIndex && !string.IsNullOrWhiteSpace(candidate.OutputPath))
            .Select(candidate => new
            {
                Item = candidate,
                Score = currentTags.Count == 0
                    ? 0
                    : (candidate.Tags ?? Array.Empty<string>())
                        .Where(static tag => !string.IsNullOrWhiteSpace(tag))
                        .Select(static tag => tag.Trim())
                        .Count(tag => currentTags.Contains(tag))
            })
            .OrderByDescending(entry => entry.Score)
            .ThenByDescending(entry => entry.Item.Date ?? DateTime.MinValue)
            .ThenBy(entry => entry.Item.Title ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(entry => entry.Item)
            .Take(maxItems)
            .ToArray();

        return candidates;
    }

    private string ResolveCollectionHref(CollectionSpec? collection, string? collectionName)
    {
        if (collection is not null && !string.IsNullOrWhiteSpace(collection.Output))
            return EnsureTrailingSlash(collection.Output);

        if (string.IsNullOrWhiteSpace(collectionName))
            return string.Empty;

        var normalized = collectionName.Trim();
        return EnsureTrailingSlash("/" + normalized.Trim('/'));
    }

    private void AppendTaxonomyChip(
        StringBuilder sb,
        string taxonomyName,
        string value,
        string cssClass,
        bool linkTaxonomy)
    {
        if (sb is null || string.IsNullOrWhiteSpace(value))
            return;

        var text = value.Trim();
        if (!linkTaxonomy)
        {
            sb.Append("<span class=\"").Append(Html(cssClass)).Append("\">").Append(Html(text)).Append("</span>");
            return;
        }

        var href = BuildTaxonomyTermHref(taxonomyName, text);
        if (string.IsNullOrWhiteSpace(href))
        {
            sb.Append("<span class=\"").Append(Html(cssClass)).Append("\">").Append(Html(text)).Append("</span>");
            return;
        }

        sb.Append("<a class=\"").Append(Html(cssClass)).Append("\" href=\"").Append(Html(href)).Append("\">")
          .Append(Html(text))
          .Append("</a>");
    }

    private IEnumerable<string> ResolveTaxonomyValues(ContentItem item, string taxonomyName)
    {
        if (item is null || string.IsNullOrWhiteSpace(taxonomyName))
            return Array.Empty<string>();

        if (string.Equals(taxonomyName, "tags", StringComparison.OrdinalIgnoreCase))
            return item.Tags ?? Array.Empty<string>();

        if (item.Meta is null || item.Meta.Count == 0)
            return Array.Empty<string>();

        if (!item.Meta.TryGetValue(taxonomyName, out var value) || value is null)
            return Array.Empty<string>();

        if (value is string single)
        {
            if (single.Contains(',', StringComparison.Ordinal))
                return single
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(static token => !string.IsNullOrWhiteSpace(token))
                    .ToArray();
            return string.IsNullOrWhiteSpace(single) ? Array.Empty<string>() : new[] { single.Trim() };
        }

        if (value is IEnumerable<object?> values)
        {
            return values
                .Select(static entry => entry?.ToString() ?? string.Empty)
                .Where(static entry => !string.IsNullOrWhiteSpace(entry))
                .Select(static entry => entry.Trim())
                .ToArray();
        }

        var text = value.ToString();
        return string.IsNullOrWhiteSpace(text) ? Array.Empty<string>() : new[] { text.Trim() };
    }

    private string BuildTaxonomyTermHref(string taxonomyName, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var basePath = ResolveTaxonomyBasePath(taxonomyName);
        if (string.IsNullOrWhiteSpace(basePath))
            return string.Empty;

        var slug = Slugify(value);
        if (string.IsNullOrWhiteSpace(slug))
            return EnsureTrailingSlash(basePath);

        return EnsureTrailingSlash($"{basePath.TrimEnd('/')}/{slug}");
    }

    private string ResolveTaxonomyBasePath(string taxonomyName)
    {
        if (string.IsNullOrWhiteSpace(taxonomyName))
            return string.Empty;

        var configured = _context.Site?.Taxonomies?
            .FirstOrDefault(taxonomy =>
                taxonomy is not null &&
                !string.IsNullOrWhiteSpace(taxonomy.Name) &&
                taxonomy.Name.Equals(taxonomyName, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(configured?.BasePath))
            return EnsureLeadingSlash(configured.BasePath);

        if (taxonomyName.Equals("tags", StringComparison.OrdinalIgnoreCase))
            return "/tags";
        if (taxonomyName.Equals("categories", StringComparison.OrdinalIgnoreCase))
            return "/categories";
        return "/" + taxonomyName.Trim().Trim('/');
    }

    private static string EnsureLeadingSlash(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "/";
        return path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path.Trim();
    }

    private static string EnsureTrailingSlash(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "/";

        var trimmed = EnsureLeadingSlash(path).TrimEnd('/');
        if (trimmed.Length == 0)
            return "/";
        return trimmed + "/";
    }

    private static string NormalizePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "/";

        var normalized = value.Replace('\\', '/').Trim();
        if (!normalized.StartsWith("/", StringComparison.Ordinal))
            normalized = "/" + normalized;
        return normalized;
    }

    private static string Slugify(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var lower = input.Trim().ToLowerInvariant();
        var sb = new StringBuilder(lower.Length);
        foreach (var ch in lower)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                continue;
            }

            if (char.IsWhiteSpace(ch) || ch == '-' || ch == '_')
                sb.Append('-');
        }

        var slug = sb.ToString();
        while (slug.Contains("--", StringComparison.Ordinal))
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        return slug.Trim('-');
    }

    private static string? CoalesceTrimmed(params string?[] values)
    {
        if (values is null || values.Length == 0)
            return null;

        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }

    private static string ResolveCardImage(IReadOnlyDictionary<string, object?>? meta, string? fallbackImage)
    {
        if (meta is not null && meta.Count > 0)
        {
            var candidates = new[]
            {
                "card_image",
                "card.image",
                "cardImage",
                "cardImage.src",
                "card.image.src",
                "image",
                "cover",
                "thumbnail",
                "social_image",
                "social.image",
                "socialImage"
            };

            foreach (var key in candidates)
            {
                var value = TryGetMetaString(meta, key);
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
        }

        return string.IsNullOrWhiteSpace(fallbackImage) ? string.Empty : fallbackImage.Trim();
    }

    private static string ResolveCardImageAlt(IReadOnlyDictionary<string, object?>? meta, string fallbackText)
    {
        if (meta is not null && meta.Count > 0)
        {
            var candidates = new[]
            {
                "card_image_alt",
                "card.image.alt",
                "cardImageAlt",
                "cardImage.alt",
                "image_alt",
                "imageAlt",
                "social_image_alt",
                "social.image.alt"
            };

            foreach (var key in candidates)
            {
                var value = TryGetMetaString(meta, key);
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
        }

        return string.IsNullOrWhiteSpace(fallbackText) ? string.Empty : fallbackText.Trim();
    }

    private static string? ResolveCardImageFit(IReadOnlyDictionary<string, object?>? meta, string? fallbackValue)
    {
        if (meta is not null && meta.Count > 0)
        {
            var candidates = new[]
            {
                "card_image_fit",
                "card.image.fit",
                "cardImageFit",
                "image_fit",
                "imageFit"
            };

            foreach (var key in candidates)
            {
                var value = TryGetMetaString(meta, key);
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
        }

        return string.IsNullOrWhiteSpace(fallbackValue) ? null : fallbackValue.Trim();
    }

    private static string? ResolveCardImagePosition(IReadOnlyDictionary<string, object?>? meta, string? fallbackValue)
    {
        if (meta is not null && meta.Count > 0)
        {
            var candidates = new[]
            {
                "card_image_position",
                "card.image.position",
                "cardImagePosition",
                "image_position",
                "imagePosition"
            };

            foreach (var key in candidates)
            {
                var value = TryGetMetaString(meta, key);
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
        }

        return string.IsNullOrWhiteSpace(fallbackValue) ? null : fallbackValue.Trim();
    }

    private static string BuildCardImageStyle(string? fitRaw, string normalizedFit, string? positionRaw, string normalizedPosition)
    {
        var declarations = new List<string>();
        if (!string.IsNullOrWhiteSpace(fitRaw))
            declarations.Add($"object-fit: {normalizedFit};");
        if (!string.IsNullOrWhiteSpace(positionRaw))
            declarations.Add($"object-position: {normalizedPosition};");
        return declarations.Count == 0 ? string.Empty : string.Join(" ", declarations);
    }

    private static string? TryGetMetaString(IReadOnlyDictionary<string, object?> meta, string key)
    {
        if (meta is null || string.IsNullOrWhiteSpace(key))
            return null;

        if (!key.Contains('.', StringComparison.Ordinal))
        {
            if (meta.TryGetValue(key, out var value))
                return NormalizeMetaValue(value);
            return null;
        }

        var current = (object?)meta;
        var parts = key.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (current is IReadOnlyDictionary<string, object?> readOnlyMap)
            {
                if (!readOnlyMap.TryGetValue(part, out current))
                    return null;
                continue;
            }

            if (current is Dictionary<string, object?> map)
            {
                if (!map.TryGetValue(part, out current))
                    return null;
                continue;
            }

            return null;
        }

        return NormalizeMetaValue(current);
    }

    private static string? NormalizeMetaValue(object? value)
    {
        if (value is null)
            return null;
        var text = value.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text) || maxLength <= 0 || text.Length <= maxLength)
            return text;

        var safe = Math.Max(8, maxLength - 1);
        return text.Substring(0, safe).TrimEnd() + "…";
    }

    private static string NormalizeAspectRatio(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "16 / 9";

        var match = AspectRatioRegex.Match(value.Trim());
        if (!match.Success)
            return "16 / 9";

        if (!double.TryParse(match.Groups["w"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var w) || w <= 0)
            return "16 / 9";
        if (!double.TryParse(match.Groups["h"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var h) || h <= 0)
            return "16 / 9";

        return $"{w.ToString("0.####", CultureInfo.InvariantCulture)} / {h.ToString("0.####", CultureInfo.InvariantCulture)}";
    }

    private static string NormalizeEditorialVariant(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "default";

        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "compact" => "compact",
            "hero" => "hero",
            "featured" => "featured",
            _ => "default"
        };
    }

    private static string NormalizeObjectFit(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "cover";

        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "fill" => "fill",
            "contain" => "contain",
            "cover" => "cover",
            "none" => "none",
            "scale-down" => "scale-down",
            _ => "cover"
        };
    }

    private static string NormalizeObjectPosition(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "center";

        var collapsed = WhitespaceRegex.Replace(value.Trim(), " ");
        if (string.IsNullOrWhiteSpace(collapsed))
            return "center";

        var tokens = collapsed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0 || tokens.Length > 4)
            return "center";

        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            var lower = token.ToLowerInvariant();
            if (lower is "left" or "right" or "center" or "top" or "bottom")
            {
                tokens[i] = lower;
                continue;
            }

            if (!ObjectPositionTokenRegex.IsMatch(token))
                return "center";
        }

        return string.Join(" ", tokens);
    }

    private static string BuildEditorialGridClass(string variant, string? additionalClass)
    {
        var baseline = variant switch
        {
            "compact" => "pf-editorial-grid pf-editorial-grid--compact",
            "hero" => "pf-editorial-grid pf-editorial-grid--hero",
            "featured" => "pf-editorial-grid pf-editorial-grid--featured",
            _ => "pf-editorial-grid"
        };
        return MergeClassList(baseline, additionalClass);
    }

    private static string BuildEditorialCardClass(string variant, int index, string? additionalClass)
    {
        var baseline = "pf-editorial-card";
        if (variant == "compact")
            baseline = "pf-editorial-card pf-editorial-card--compact";
        else if (variant == "featured")
            baseline = "pf-editorial-card pf-editorial-card--featured";
        else if (variant == "hero" && index == 0)
            baseline = "pf-editorial-card pf-editorial-card--hero";
        return MergeClassList(baseline, additionalClass);
    }

    private static string MergeClassList(string baseline, string? additionalClass)
    {
        var ordered = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var token in TokenizeCssClassList(baseline))
        {
            if (seen.Add(token))
                ordered.Add(token);
        }

        foreach (var token in TokenizeCssClassList(additionalClass))
        {
            if (seen.Add(token))
                ordered.Add(token);
        }

        return ordered.Count == 0 ? string.Empty : string.Join(" ", ordered);
    }

    private static IEnumerable<string> TokenizeCssClassList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            yield break;

        var parts = value.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (!string.IsNullOrWhiteSpace(part))
                yield return part;
        }
    }

    private static string BuildClass(NavigationItem item, string? baseClass)
    {
        var cls = new List<string>();
        if (!string.IsNullOrWhiteSpace(baseClass))
            cls.Add(baseClass);
        if (!string.IsNullOrWhiteSpace(item.CssClass))
            cls.Add(item.CssClass!.Trim());
        if (item.IsActive)
            cls.Add("is-active");
        else if (item.IsAncestor)
            cls.Add("is-ancestor");
        return cls.Count == 0 ? string.Empty : string.Join(" ", cls);
    }

    private static string Html(string value) => System.Web.HttpUtility.HtmlEncode(value ?? string.Empty);
}
