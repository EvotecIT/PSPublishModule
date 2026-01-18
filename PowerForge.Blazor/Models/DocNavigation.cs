namespace PowerForge.Blazor;

/// <summary>
/// Navigation structure for a documentation source.
/// </summary>
public class DocNavigation
{
    /// <summary>
    /// Source identifier this navigation belongs to.
    /// </summary>
    public string SourceId { get; set; } = string.Empty;

    /// <summary>
    /// Root navigation items.
    /// </summary>
    public List<DocNavItem> Items { get; set; } = new();
}

/// <summary>
/// A single navigation item (can be a page or a section with children).
/// </summary>
public class DocNavItem
{
    /// <summary>
    /// Display title for navigation.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Page slug (null for section headers without content).
    /// </summary>
    public string? Slug { get; set; }

    /// <summary>
    /// Optional icon name or SVG.
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// Sort order.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Whether this section is expanded by default.
    /// </summary>
    public bool IsExpanded { get; set; } = true;

    /// <summary>
    /// Child navigation items.
    /// </summary>
    public List<DocNavItem> Children { get; set; } = new();

    /// <summary>
    /// External URL (for links to external resources).
    /// </summary>
    public string? ExternalUrl { get; set; }

    /// <summary>
    /// Badge text (e.g., "New", "Beta", "Deprecated").
    /// </summary>
    public string? Badge { get; set; }

    /// <summary>
    /// Badge CSS class for styling.
    /// </summary>
    public string? BadgeClass { get; set; }
}
