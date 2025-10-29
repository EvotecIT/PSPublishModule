namespace PowerGuardian;

internal sealed class DocumentItem
{
    public string Title { get; set; } = string.Empty;
    public string Kind { get; set; } = "FILE"; // FILE, INTRO, UPGRADE, LINKS
    public string Content { get; set; } = string.Empty; // markdown content
}

