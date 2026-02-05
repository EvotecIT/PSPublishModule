namespace PowerForge.Web;

/// <summary>Site navigation configuration.</summary>
public sealed class NavigationSpec
{
    /// <summary>Whether to generate default auto-navigation when no menus are defined.</summary>
    public bool AutoDefaults { get; set; } = true;
    /// <summary>Named menus to expose in templates.</summary>
    public MenuSpec[] Menus { get; set; } = Array.Empty<MenuSpec>();

    /// <summary>Optional navigation action items (buttons/links) for headers.</summary>
    public MenuItemSpec[] Actions { get; set; } = Array.Empty<MenuItemSpec>();

    /// <summary>Auto-generated menus derived from content structure.</summary>
    public NavigationAutoSpec[] Auto { get; set; } = Array.Empty<NavigationAutoSpec>();
}

/// <summary>Defines a named menu and its items.</summary>
public sealed class MenuSpec
{
    /// <summary>Menu identifier used by templates.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional label for UI.</summary>
    public string? Label { get; set; }

    /// <summary>Top-level menu items.</summary>
    public MenuItemSpec[] Items { get; set; } = Array.Empty<MenuItemSpec>();
}

/// <summary>Defines a navigation item.</summary>
public sealed class MenuItemSpec
{
    /// <summary>Display title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Optional display text (defaults to Title when used by a theme).</summary>
    public string? Text { get; set; }

    /// <summary>Destination URL.</summary>
    public string? Url { get; set; }

    /// <summary>Optional icon identifier.</summary>
    public string? Icon { get; set; }

    /// <summary>Optional icon HTML.</summary>
    public string? IconHtml { get; set; }

    /// <summary>Optional item kind (e.g. link, button).</summary>
    public string? Kind { get; set; }

    /// <summary>Optional CSS class.</summary>
    public string? CssClass { get; set; }

    /// <summary>Optional aria-label override.</summary>
    public string? AriaLabel { get; set; }

    /// <summary>Optional badge label.</summary>
    public string? Badge { get; set; }

    /// <summary>Optional description.</summary>
    public string? Description { get; set; }

    /// <summary>Optional link target override.</summary>
    public string? Target { get; set; }

    /// <summary>Optional rel attribute override.</summary>
    public string? Rel { get; set; }

    /// <summary>Marks the link as external.</summary>
    public bool? External { get; set; }

    /// <summary>Optional ordering weight.</summary>
    public int? Weight { get; set; }

    /// <summary>Optional route match override for active state.</summary>
    public string? Match { get; set; }

    /// <summary>Child menu items.</summary>
    public MenuItemSpec[] Items { get; set; } = Array.Empty<MenuItemSpec>();
}

/// <summary>Configuration for auto-generated navigation menus.</summary>
public sealed class NavigationAutoSpec
{
    /// <summary>Collection name used as the source.</summary>
    public string Collection { get; set; } = string.Empty;

    /// <summary>Menu name to populate.</summary>
    public string Menu { get; set; } = string.Empty;

    /// <summary>Optional route root for the menu (defaults to collection output).</summary>
    public string? Root { get; set; }

    /// <summary>Maximum depth to include.</summary>
    public int? MaxDepth { get; set; }

    /// <summary>Include section index pages in the menu.</summary>
    public bool IncludeIndex { get; set; } = true;

    /// <summary>Include draft content in the menu.</summary>
    public bool IncludeDrafts { get; set; }

    /// <summary>Optional sort expression (order,title).</summary>
    public string? Sort { get; set; }
}
