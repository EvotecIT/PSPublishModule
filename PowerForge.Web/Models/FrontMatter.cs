namespace PowerForge.Web;

public sealed class FrontMatter
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public DateTime? Date { get; set; }
    public string[] Tags { get; set; } = Array.Empty<string>();
    public string? Slug { get; set; }
    public int? Order { get; set; }
    public bool Draft { get; set; }
    public string? Collection { get; set; }
    public string[] Aliases { get; set; } = Array.Empty<string>();
    public string? Canonical { get; set; }
    public string? EditPath { get; set; }
    public string? Layout { get; set; }
    public string? Template { get; set; }
    public Dictionary<string, object?> Meta { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ContentItem
{
    public string SourcePath { get; set; } = string.Empty;
    public string Collection { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime? Date { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string[] Tags { get; set; } = Array.Empty<string>();
    public string[] Aliases { get; set; } = Array.Empty<string>();
    public bool Draft { get; set; }
    public string? Canonical { get; set; }
    public string? EditPath { get; set; }
    public string? Layout { get; set; }
    public string? Template { get; set; }
    public string HtmlContent { get; set; } = string.Empty;
    public string TocHtml { get; set; } = string.Empty;
    public string? ProjectSlug { get; set; }
    public Dictionary<string, object?> Meta { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
