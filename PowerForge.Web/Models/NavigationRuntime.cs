namespace PowerForge.Web;

/// <summary>Runtime navigation model resolved for a page.</summary>
public sealed class NavigationRuntime
{
    /// <summary>Resolved menus with active state.</summary>
    public NavigationMenu[] Menus { get; set; } = Array.Empty<NavigationMenu>();
}

/// <summary>Resolved menu with navigation items.</summary>
public sealed class NavigationMenu
{
    /// <summary>Menu identifier.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional UI label.</summary>
    public string? Label { get; set; }

    /// <summary>Resolved menu items.</summary>
    public NavigationItem[] Items { get; set; } = Array.Empty<NavigationItem>();
}

/// <summary>Resolved navigation item with active state.</summary>
public sealed class NavigationItem
{
    /// <summary>Display title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Destination URL.</summary>
    public string? Url { get; set; }

    /// <summary>Optional icon identifier.</summary>
    public string? Icon { get; set; }

    /// <summary>Optional badge label.</summary>
    public string? Badge { get; set; }

    /// <summary>Optional description.</summary>
    public string? Description { get; set; }

    /// <summary>Optional link target.</summary>
    public string? Target { get; set; }

    /// <summary>Optional rel attribute.</summary>
    public string? Rel { get; set; }

    /// <summary>True for external URLs.</summary>
    public bool External { get; set; }

    /// <summary>True when the current route matches this item.</summary>
    public bool IsActive { get; set; }

    /// <summary>True when the current route is within this item's subtree.</summary>
    public bool IsAncestor { get; set; }

    /// <summary>Ordering weight.</summary>
    public int? Weight { get; set; }

    /// <summary>Optional match override.</summary>
    public string? Match { get; set; }

    /// <summary>Child menu items.</summary>
    public NavigationItem[] Items { get; set; } = Array.Empty<NavigationItem>();
}

/// <summary>Breadcrumb item with resolved title and URL.</summary>
public sealed class BreadcrumbItem
{
    /// <summary>Display title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Breadcrumb URL.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>True for the current page.</summary>
    public bool IsCurrent { get; set; }
}
