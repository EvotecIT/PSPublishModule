namespace PowerForge.Web;

/// <summary>RSS/Atom feed generation settings.</summary>
public sealed class FeedSpec
{
    /// <summary>When false, disable implicit RSS outputs for blog/taxonomy pages.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Optional maximum number of items in generated feeds (0 or null = unlimited).</summary>
    public int? MaxItems { get; set; }

    /// <summary>When true, include full HTML content in RSS entries (content:encoded).</summary>
    public bool IncludeContent { get; set; }

    /// <summary>When true, emit tags/categories as RSS category elements.</summary>
    public bool IncludeCategories { get; set; } = true;

    /// <summary>When true, include Atom feeds in implicit blog/taxonomy outputs.</summary>
    public bool IncludeAtom { get; set; }

    /// <summary>When true, include JSON Feed outputs in implicit blog/taxonomy outputs.</summary>
    public bool IncludeJsonFeed { get; set; }
}
