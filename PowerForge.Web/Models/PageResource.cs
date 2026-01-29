namespace PowerForge.Web;

/// <summary>Represents a resource attached to a page bundle.</summary>
public sealed class PageResource
{
    /// <summary>Absolute source path on disk.</summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>Resource name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Relative path from the page output root.</summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>Resolved media type.</summary>
    public string? MediaType { get; set; }
}
