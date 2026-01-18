using System.Text.RegularExpressions;

namespace PowerForge.Blazor;

/// <summary>
/// Documentation source that loads markdown files from a folder.
/// Supports front matter for metadata, ordering, and hierarchical navigation.
/// </summary>
public class FolderDocumentationSource : IDocumentationSource
{
    private readonly string _folderPath;
    private readonly FolderDocSourceOptions _options;
    private List<DocPage>? _pages;

    public string Id { get; }
    public string DisplayName { get; }
    public string? Description { get; }
    public int Order { get; }

    public FolderDocumentationSource(string folderPath, FolderDocSourceOptions? options = null)
    {
        _folderPath = folderPath ?? throw new ArgumentNullException(nameof(folderPath));
        _options = options ?? new FolderDocSourceOptions();
        Id = _options.Id ?? Path.GetFileName(folderPath).ToLowerInvariant();
        DisplayName = _options.DisplayName ?? Path.GetFileName(folderPath);
        Description = _options.Description;
        Order = _options.Order;
    }

    public async Task<IReadOnlyList<DocPage>> LoadPagesAsync(CancellationToken cancellationToken = default)
    {
        if (_pages != null) return _pages;

        _pages = new List<DocPage>();
        if (!Directory.Exists(_folderPath)) return _pages;

        var files = Directory.GetFiles(_folderPath, "*.md", _options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await LoadPageFromFileAsync(file, cancellationToken);
            if (page != null)
            {
                _pages.Add(page);
            }
        }

        // Sort by order, then by title
        _pages = _pages.OrderBy(p => p.Order).ThenBy(p => p.Title).ToList();
        return _pages;
    }

    public async Task<DocPage?> GetPageAsync(string slug, CancellationToken cancellationToken = default)
    {
        var pages = await LoadPagesAsync(cancellationToken);
        return pages.FirstOrDefault(p => p.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<DocNavigation> GetNavigationAsync(CancellationToken cancellationToken = default)
    {
        var pages = await LoadPagesAsync(cancellationToken);
        return BuildNavigation(pages);
    }

    private async Task<DocPage?> LoadPageFromFileAsync(string filePath, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        var relativePath = Path.GetRelativePath(_folderPath, filePath);
        var slug = GenerateSlug(relativePath);

        // Parse front matter
        var (metadata, body) = ParseFrontMatter(content);

        // Extract title from front matter or first heading
        var title = GetMetadataValue<string>(metadata, "title")
                    ?? ExtractTitleFromContent(body)
                    ?? Path.GetFileNameWithoutExtension(filePath);

        // Extract order from front matter or filename prefix
        var order = GetMetadataValue<int?>(metadata, "order")
                    ?? ExtractOrderFromFilename(Path.GetFileName(filePath));

        // Extract parent from front matter or directory structure
        var parent = GetMetadataValue<string>(metadata, "parent")
                     ?? ExtractParentFromPath(relativePath);

        var page = new DocPage
        {
            Slug = $"{Id}/{slug}",
            Title = title,
            Description = GetMetadataValue<string>(metadata, "description"),
            Content = body,
            ContentType = "markdown",
            SourcePath = filePath,
            LastModified = File.GetLastWriteTimeUtc(filePath),
            Order = order,
            ParentSlug = parent != null ? $"{Id}/{parent}" : null,
            Tags = GetMetadataList(metadata, "tags"),
            Metadata = metadata
        };

        // Extract TOC from headings
        page.TableOfContents = ExtractTableOfContents(body);

        return page;
    }

    private static (Dictionary<string, object> metadata, string body) ParseFrontMatter(string content)
    {
        var metadata = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        if (!content.StartsWith("---"))
        {
            return (metadata, content);
        }

        var endIndex = content.IndexOf("---", 3);
        if (endIndex < 0)
        {
            return (metadata, content);
        }

        var frontMatter = content.Substring(3, endIndex - 3).Trim();
        var body = content.Substring(endIndex + 3).TrimStart();

        // Simple YAML-like parsing
        foreach (var line in frontMatter.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;

            var colonIdx = trimmed.IndexOf(':');
            if (colonIdx <= 0) continue;

            var key = trimmed.Substring(0, colonIdx).Trim();
            var value = trimmed.Substring(colonIdx + 1).Trim();

            // Remove quotes
            if (value.StartsWith("\"") && value.EndsWith("\""))
                value = value.Substring(1, value.Length - 2);
            else if (value.StartsWith("'") && value.EndsWith("'"))
                value = value.Substring(1, value.Length - 2);

            // Try to parse as number
            if (int.TryParse(value, out var intVal))
                metadata[key] = intVal;
            else if (bool.TryParse(value, out var boolVal))
                metadata[key] = boolVal;
            else if (value.StartsWith("[") && value.EndsWith("]"))
                metadata[key] = ParseYamlArray(value);
            else
                metadata[key] = value;
        }

        return (metadata, body);
    }

    private static List<string> ParseYamlArray(string value)
    {
        var inner = value.Substring(1, value.Length - 2);
        return inner.Split(',')
            .Select(s => s.Trim().Trim('"', '\''))
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();
    }

    private static T? GetMetadataValue<T>(Dictionary<string, object> metadata, string key)
    {
        if (metadata.TryGetValue(key, out var value))
        {
            if (value is T typed) return typed;
            if (typeof(T) == typeof(int?) && value is int i) return (T)(object)i;
        }
        return default;
    }

    private static List<string> GetMetadataList(Dictionary<string, object> metadata, string key)
    {
        if (metadata.TryGetValue(key, out var value))
        {
            if (value is List<string> list) return list;
            if (value is string str) return new List<string> { str };
        }
        return new List<string>();
    }

    private string GenerateSlug(string relativePath)
    {
        var withoutExt = Path.ChangeExtension(relativePath, null);
        var normalized = withoutExt
            .Replace('\\', '/')
            .Replace(' ', '-')
            .ToLowerInvariant();

        // Remove order prefix (e.g., "01-getting-started" -> "getting-started")
        var parts = normalized.Split('/');
        for (int i = 0; i < parts.Length; i++)
        {
            if (Regex.IsMatch(parts[i], @"^\d+-"))
            {
                parts[i] = Regex.Replace(parts[i], @"^\d+-", "");
            }
        }

        return string.Join("/", parts);
    }

    private static string? ExtractTitleFromContent(string content)
    {
        var match = Regex.Match(content, @"^#\s+(.+)$", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static int ExtractOrderFromFilename(string filename)
    {
        var match = Regex.Match(filename, @"^(\d+)-");
        return match.Success ? int.Parse(match.Groups[1].Value) : 999;
    }

    private string? ExtractParentFromPath(string relativePath)
    {
        var dir = Path.GetDirectoryName(relativePath);
        if (string.IsNullOrEmpty(dir)) return null;

        return GenerateSlug(dir);
    }

    private static List<TocEntry> ExtractTableOfContents(string content)
    {
        var toc = new List<TocEntry>();
        var headingRegex = new Regex(@"^(#{1,6})\s+(.+)$", RegexOptions.Multiline);

        foreach (Match match in headingRegex.Matches(content))
        {
            var level = match.Groups[1].Value.Length;
            var text = match.Groups[2].Value.Trim();
            var anchor = GenerateAnchor(text);

            toc.Add(new TocEntry
            {
                Level = level,
                Text = text,
                Anchor = anchor
            });
        }

        return toc;
    }

    private static string GenerateAnchor(string text)
    {
        // GitHub-style anchor generation
        return Regex.Replace(text.ToLowerInvariant(), @"[^\w\- ]", "")
            .Replace(' ', '-')
            .Trim('-');
    }

    private DocNavigation BuildNavigation(IReadOnlyList<DocPage> pages)
    {
        var nav = new DocNavigation { SourceId = Id };
        var itemsBySlug = new Dictionary<string, DocNavItem>(StringComparer.OrdinalIgnoreCase);
        var rootItems = new List<DocNavItem>();

        // First pass: create items
        foreach (var page in pages)
        {
            var item = new DocNavItem
            {
                Title = page.Title,
                Slug = page.Slug,
                Order = page.Order
            };
            itemsBySlug[page.Slug] = item;
        }

        // Second pass: build hierarchy
        foreach (var page in pages)
        {
            var item = itemsBySlug[page.Slug];
            if (!string.IsNullOrEmpty(page.ParentSlug) && itemsBySlug.TryGetValue(page.ParentSlug, out var parent))
            {
                parent.Children.Add(item);
            }
            else
            {
                rootItems.Add(item);
            }
        }

        // Sort all levels
        nav.Items = rootItems.OrderBy(i => i.Order).ThenBy(i => i.Title).ToList();
        SortChildrenRecursive(nav.Items);

        return nav;
    }

    private static void SortChildrenRecursive(List<DocNavItem> items)
    {
        foreach (var item in items)
        {
            if (item.Children.Count > 0)
            {
                item.Children = item.Children.OrderBy(i => i.Order).ThenBy(i => i.Title).ToList();
                SortChildrenRecursive(item.Children);
            }
        }
    }
}

/// <summary>
/// Options for FolderDocumentationSource.
/// </summary>
public class FolderDocSourceOptions
{
    /// <summary>
    /// Source identifier. Defaults to folder name.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Display name. Defaults to folder name.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Description of this source.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Sort order. Defaults to 0.
    /// </summary>
    public int Order { get; set; } = 0;

    /// <summary>
    /// Whether to recursively search subdirectories. Defaults to true.
    /// </summary>
    public bool Recursive { get; set; } = true;

    /// <summary>
    /// File patterns to include. Defaults to ["*.md"].
    /// </summary>
    public List<string> IncludePatterns { get; set; } = new() { "*.md" };

    /// <summary>
    /// File patterns to exclude.
    /// </summary>
    public List<string> ExcludePatterns { get; set; } = new();
}
