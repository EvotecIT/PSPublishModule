namespace PowerForge.Web;

/// <summary>Runtime navigation model resolved for a page.</summary>
public sealed class NavigationRuntime
{
    /// <summary>Resolved menus with active state.</summary>
    public NavigationMenu[] Menus { get; set; } = Array.Empty<NavigationMenu>();

    /// <summary>Resolved navigation action items (buttons/links).</summary>
    public NavigationItem[] Actions { get; set; } = Array.Empty<NavigationItem>();

    /// <summary>Resolved named regions for advanced header/footer/mobile layouts.</summary>
    public NavigationRegion[] Regions { get; set; } = Array.Empty<NavigationRegion>();

    /// <summary>Resolved footer model.</summary>
    public NavigationFooter? Footer { get; set; }

    /// <summary>Selected navigation profile name (if any).</summary>
    public string? ActiveProfile { get; set; }
}

/// <summary>Resolved menu with navigation items.</summary>
public sealed class NavigationMenu
{
    /// <summary>Menu identifier.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional UI label.</summary>
    public string? Label { get; set; }

    /// <summary>Optional template key for custom menu rendering.</summary>
    public string? Template { get; set; }

    /// <summary>Optional CSS class for menu container.</summary>
    public string? CssClass { get; set; }

    /// <summary>Optional custom metadata exposed to templates.</summary>
    public Dictionary<string, object?>? Meta { get; set; }

    /// <summary>Resolved menu items.</summary>
    public NavigationItem[] Items { get; set; } = Array.Empty<NavigationItem>();
}

/// <summary>Resolved navigation item with active state.</summary>
public sealed class NavigationItem
{
    /// <summary>Optional stable identifier.</summary>
    public string? Id { get; set; }

    /// <summary>Display title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Optional display text.</summary>
    public string? Text { get; set; }

    /// <summary>Destination URL.</summary>
    public string? Url { get; set; }

    /// <summary>Optional icon identifier.</summary>
    public string? Icon { get; set; }

    /// <summary>Optional icon HTML.</summary>
    public string? IconHtml { get; set; }

    /// <summary>Optional item kind.</summary>
    public string? Kind { get; set; }

    /// <summary>Optional slot name (for example nav/start/nav/end).</summary>
    public string? Slot { get; set; }

    /// <summary>Optional template key for custom component rendering.</summary>
    public string? Template { get; set; }

    /// <summary>Optional CSS class.</summary>
    public string? CssClass { get; set; }

    /// <summary>Optional aria-label override.</summary>
    public string? AriaLabel { get; set; }

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

    /// <summary>Optional mega-menu sections.</summary>
    public NavigationSection[] Sections { get; set; } = Array.Empty<NavigationSection>();

    /// <summary>Optional custom metadata exposed to templates.</summary>
    public Dictionary<string, object?>? Meta { get; set; }

    /// <summary>Child menu items.</summary>
    public NavigationItem[] Items { get; set; } = Array.Empty<NavigationItem>();
}

/// <summary>Resolved named region.</summary>
public sealed class NavigationRegion
{
    /// <summary>Region identifier.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional title.</summary>
    public string? Title { get; set; }

    /// <summary>Optional template key for region rendering.</summary>
    public string? Template { get; set; }

    /// <summary>Optional CSS class.</summary>
    public string? CssClass { get; set; }

    /// <summary>Optional custom metadata exposed to templates.</summary>
    public Dictionary<string, object?>? Meta { get; set; }

    /// <summary>Resolved items for the region.</summary>
    public NavigationItem[] Items { get; set; } = Array.Empty<NavigationItem>();
}

/// <summary>Resolved footer with columns and legal links.</summary>
public sealed class NavigationFooter
{
    /// <summary>Optional footer label.</summary>
    public string? Label { get; set; }

    /// <summary>Optional template key for footer rendering.</summary>
    public string? Template { get; set; }

    /// <summary>Optional CSS class for footer container.</summary>
    public string? CssClass { get; set; }

    /// <summary>Optional custom metadata exposed to templates.</summary>
    public Dictionary<string, object?>? Meta { get; set; }

    /// <summary>Footer columns.</summary>
    public NavigationFooterColumn[] Columns { get; set; } = Array.Empty<NavigationFooterColumn>();

    /// <summary>Footer legal/support links.</summary>
    public NavigationItem[] Legal { get; set; } = Array.Empty<NavigationItem>();
}

/// <summary>Resolved footer column.</summary>
public sealed class NavigationFooterColumn
{
    /// <summary>Column identifier.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Column title.</summary>
    public string? Title { get; set; }

    /// <summary>Optional template key for custom column rendering.</summary>
    public string? Template { get; set; }

    /// <summary>Optional CSS class for column container.</summary>
    public string? CssClass { get; set; }

    /// <summary>Optional custom metadata exposed to templates.</summary>
    public Dictionary<string, object?>? Meta { get; set; }

    /// <summary>Column links/items.</summary>
    public NavigationItem[] Items { get; set; } = Array.Empty<NavigationItem>();
}

/// <summary>Mega menu section runtime model.</summary>
public sealed class NavigationSection
{
    /// <summary>Section identifier.</summary>
    public string? Name { get; set; }

    /// <summary>Section title.</summary>
    public string? Title { get; set; }

    /// <summary>Section description.</summary>
    public string? Description { get; set; }

    /// <summary>Optional CSS class.</summary>
    public string? CssClass { get; set; }

    /// <summary>Section links/items.</summary>
    public NavigationItem[] Items { get; set; } = Array.Empty<NavigationItem>();

    /// <summary>Nested section columns.</summary>
    public NavigationColumn[] Columns { get; set; } = Array.Empty<NavigationColumn>();
}

/// <summary>Navigation column runtime model.</summary>
public sealed class NavigationColumn
{
    /// <summary>Column identifier.</summary>
    public string? Name { get; set; }

    /// <summary>Column title.</summary>
    public string? Title { get; set; }

    /// <summary>Column links/items.</summary>
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
