namespace PowerGuardian;

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
}
