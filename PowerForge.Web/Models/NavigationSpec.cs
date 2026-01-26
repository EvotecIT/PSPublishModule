namespace PowerForge.Web;

/// <summary>Site navigation configuration.</summary>
public sealed class NavigationSpec
{
    /// <summary>Named menus to expose in templates.</summary>
    public MenuSpec[] Menus { get; set; } = Array.Empty<MenuSpec>();
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

    /// <summary>Destination URL.</summary>
    public string? Url { get; set; }

    /// <summary>Optional icon identifier.</summary>
    public string? Icon { get; set; }

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
