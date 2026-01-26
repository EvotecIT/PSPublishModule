namespace PowerForge.Web;

/// <summary>Defines a content collection (pages, docs, blog).</summary>
public sealed class CollectionSpec
{
    /// <summary>Collection name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Input folder path.</summary>
    public string Input { get; set; } = string.Empty;
    /// <summary>Output route prefix.</summary>
    public string Output { get; set; } = string.Empty;
    /// <summary>Default layout for items in the collection.</summary>
    public string? DefaultLayout { get; set; }
    /// <summary>Include glob patterns.</summary>
    public string[] Include { get; set; } = Array.Empty<string>();
    /// <summary>Exclude glob patterns.</summary>
    public string[] Exclude { get; set; } = Array.Empty<string>();
    /// <summary>Field used for sorting.</summary>
    public string? SortBy { get; set; }
    /// <summary>Sort order.</summary>
    public SortOrder? SortOrder { get; set; }
}
