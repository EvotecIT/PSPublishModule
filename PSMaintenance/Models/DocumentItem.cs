namespace PSMaintenance;

/// <summary>
/// Simple container for a rendered document piece (markdown content with a title and kind).
/// </summary>
internal sealed class DocumentItem
{
    /// <summary>Display title used in console/HTML renderers.</summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>Logical kind: FILE, INTRO, UPGRADE or LINKS.</summary>
    public string Kind { get; set; } = "FILE"; // FILE, INTRO, UPGRADE, LINKS
    /// <summary>Markdown content.</summary>
    public string Content { get; set; } = string.Empty; // markdown content
    /// <summary>Optional local file path when the item represents a file on disk.</summary>
    public string? Path { get; set; }
    /// <summary>Optional original filename (used for grouping e.g. Scripts/Docs).</summary>
    public string? FileName { get; set; }
    /// <summary>Optional source of the document: "Local" (Internals) or "Remote" (Repository).</summary>
    public string? Source { get; set; }
}
