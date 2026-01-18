namespace PowerForge.Blazor;

/// <summary>
/// Aggregates multiple documentation sources and provides unified access.
/// </summary>
public class DocumentationService
{
    private readonly List<IDocumentationSource> _sources = new();
    private readonly Dictionary<string, IDocumentationRenderer> _renderers = new();
    private readonly IDocumentationRenderer _defaultRenderer;

    public DocumentationService()
    {
        _defaultRenderer = new MarkdownRenderer();
        RegisterRenderer(_defaultRenderer);
    }

    /// <summary>
    /// All registered documentation sources.
    /// </summary>
    public IReadOnlyList<IDocumentationSource> Sources => _sources.AsReadOnly();

    /// <summary>
    /// Registers a documentation source.
    /// </summary>
    public DocumentationService AddSource(IDocumentationSource source)
    {
        _sources.Add(source);
        _sources.Sort((a, b) => a.Order.CompareTo(b.Order));
        return this;
    }

    /// <summary>
    /// Adds an XML documentation source.
    /// </summary>
    public DocumentationService AddXmlDoc(string xmlPath, Action<XmlDocSourceOptions>? configure = null)
    {
        var options = new XmlDocSourceOptions();
        configure?.Invoke(options);
        return AddSource(new XmlDocumentationSource(xmlPath, options));
    }

    /// <summary>
    /// Adds a folder documentation source.
    /// </summary>
    public DocumentationService AddFolder(string folderPath, Action<FolderDocSourceOptions>? configure = null)
    {
        var options = new FolderDocSourceOptions();
        configure?.Invoke(options);
        return AddSource(new FolderDocumentationSource(folderPath, options));
    }

    /// <summary>
    /// Registers a content renderer.
    /// </summary>
    public DocumentationService RegisterRenderer(IDocumentationRenderer renderer)
    {
        foreach (var contentType in renderer.SupportedContentTypes)
        {
            _renderers[contentType.ToLowerInvariant()] = renderer;
        }
        return this;
    }

    /// <summary>
    /// Gets a documentation source by ID.
    /// </summary>
    public IDocumentationSource? GetSource(string id)
    {
        return _sources.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets a page by its full slug (sourceId/pageSlug).
    /// </summary>
    public async Task<DocPage?> GetPageAsync(string fullSlug, CancellationToken cancellationToken = default)
    {
        var slashIdx = fullSlug.IndexOf('/');
        if (slashIdx <= 0) return null;

        var sourceId = fullSlug.Substring(0, slashIdx);
        var source = GetSource(sourceId);
        if (source == null) return null;

        var page = await source.GetPageAsync(fullSlug, cancellationToken);
        if (page != null && string.IsNullOrEmpty(page.RenderedHtml))
        {
            page.RenderedHtml = await RenderContentAsync(page.Content, page.ContentType, cancellationToken: cancellationToken);
        }

        return page;
    }

    /// <summary>
    /// Gets all pages from all sources.
    /// </summary>
    public async Task<IReadOnlyList<DocPage>> GetAllPagesAsync(CancellationToken cancellationToken = default)
    {
        var allPages = new List<DocPage>();
        foreach (var source in _sources)
        {
            var pages = await source.LoadPagesAsync(cancellationToken);
            allPages.AddRange(pages);
        }
        return allPages;
    }

    /// <summary>
    /// Gets the combined navigation from all sources.
    /// </summary>
    public async Task<List<DocNavigation>> GetNavigationAsync(CancellationToken cancellationToken = default)
    {
        var navs = new List<DocNavigation>();
        foreach (var source in _sources)
        {
            var nav = await source.GetNavigationAsync(cancellationToken);
            navs.Add(nav);
        }
        return navs;
    }

    /// <summary>
    /// Renders content to HTML using the appropriate renderer.
    /// </summary>
    public string RenderContent(string content, string contentType, RenderOptions? options = null)
    {
        var renderer = GetRenderer(contentType);
        return renderer.RenderToHtml(content, contentType, options);
    }

    /// <summary>
    /// Renders content to HTML asynchronously.
    /// </summary>
    public Task<string> RenderContentAsync(string content, string contentType, RenderOptions? options = null, CancellationToken cancellationToken = default)
    {
        var renderer = GetRenderer(contentType);
        return renderer.RenderToHtmlAsync(content, contentType, options, cancellationToken);
    }

    /// <summary>
    /// Searches all documentation for a query.
    /// </summary>
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int maxResults = 20, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<SearchResult>();

        var results = new List<SearchResult>();
        var terms = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var source in _sources)
        {
            var pages = await source.LoadPagesAsync(cancellationToken);
            foreach (var page in pages)
            {
                var score = CalculateRelevance(page, terms);
                if (score > 0)
                {
                    results.Add(new SearchResult
                    {
                        Page = page,
                        SourceId = source.Id,
                        SourceName = source.DisplayName,
                        Score = score,
                        Snippet = GenerateSnippet(page.Content, terms)
                    });
                }
            }
        }

        return results
            .OrderByDescending(r => r.Score)
            .Take(maxResults)
            .ToList();
    }

    private IDocumentationRenderer GetRenderer(string contentType)
    {
        var key = contentType.ToLowerInvariant();
        return _renderers.TryGetValue(key, out var renderer) ? renderer : _defaultRenderer;
    }

    private static double CalculateRelevance(DocPage page, string[] terms)
    {
        double score = 0;
        var title = page.Title.ToLowerInvariant();
        var content = page.Content.ToLowerInvariant();
        var description = page.Description?.ToLowerInvariant() ?? "";

        foreach (var term in terms)
        {
            // Title matches are weighted heavily
            if (title.Contains(term))
            {
                score += title == term ? 100 : 50;
            }

            // Description matches
            if (description.Contains(term))
            {
                score += 20;
            }

            // Content matches
            var count = CountOccurrences(content, term);
            score += Math.Min(count * 2, 30); // Cap at 30 for content

            // Tag matches
            if (page.Tags.Any(t => t.ToLowerInvariant().Contains(term)))
            {
                score += 25;
            }
        }

        return score;
    }

    private static int CountOccurrences(string text, string term)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(term, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += term.Length;
        }
        return count;
    }

    private static string GenerateSnippet(string content, string[] terms)
    {
        const int snippetLength = 200;
        var lower = content.ToLowerInvariant();

        // Find first occurrence of any term
        var firstIndex = int.MaxValue;
        foreach (var term in terms)
        {
            var idx = lower.IndexOf(term, StringComparison.Ordinal);
            if (idx >= 0 && idx < firstIndex)
            {
                firstIndex = idx;
            }
        }

        if (firstIndex == int.MaxValue)
        {
            // No match found, return start of content
            return content.Length > snippetLength
                ? content.Substring(0, snippetLength) + "..."
                : content;
        }

        // Get context around the match
        var start = Math.Max(0, firstIndex - 50);
        var end = Math.Min(content.Length, firstIndex + snippetLength - 50);

        var snippet = content.Substring(start, end - start);
        if (start > 0) snippet = "..." + snippet;
        if (end < content.Length) snippet += "...";

        return snippet;
    }
}

/// <summary>
/// Search result from documentation search.
/// </summary>
public class SearchResult
{
    /// <summary>
    /// The matched page.
    /// </summary>
    public DocPage Page { get; set; } = null!;

    /// <summary>
    /// Source ID.
    /// </summary>
    public string SourceId { get; set; } = string.Empty;

    /// <summary>
    /// Source display name.
    /// </summary>
    public string SourceName { get; set; } = string.Empty;

    /// <summary>
    /// Relevance score.
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// Content snippet showing matched context.
    /// </summary>
    public string? Snippet { get; set; }
}
