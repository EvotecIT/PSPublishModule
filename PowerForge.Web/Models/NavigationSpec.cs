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

    /// <summary>Optional named regions (for example header.left/header.right/mobile.drawer).</summary>
    public NavigationRegionSpec[] Regions { get; set; } = Array.Empty<NavigationRegionSpec>();

    /// <summary>Optional footer configuration with column groups and links.</summary>
    public NavigationFooterSpec? Footer { get; set; }

    /// <summary>Optional profile overrides selected by route/collection/layout/project.</summary>
    public NavigationProfileSpec[] Profiles { get; set; } = Array.Empty<NavigationProfileSpec>();

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

    /// <summary>Optional visibility rules for the entire menu.</summary>
    public NavigationVisibilitySpec? Visibility { get; set; }
}

/// <summary>Defines a navigation item.</summary>
public sealed class MenuItemSpec
{
    /// <summary>Optional stable identifier.</summary>
    public string? Id { get; set; }

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

    /// <summary>Optional visibility rules for this item.</summary>
    public NavigationVisibilitySpec? Visibility { get; set; }

    /// <summary>Optional mega-menu sections for advanced dropdown layouts.</summary>
    public MenuSectionSpec[] Sections { get; set; } = Array.Empty<MenuSectionSpec>();

    /// <summary>Optional custom metadata exposed to templates.</summary>
    public Dictionary<string, object?>? Meta { get; set; }

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

/// <summary>Named navigation region that can aggregate menus and ad-hoc items.</summary>
public sealed class NavigationRegionSpec
{
    /// <summary>Region identifier (for example header.left, header.right, mobile.drawer).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional region title for templates.</summary>
    public string? Title { get; set; }

    /// <summary>Optional menu names to project into this region.</summary>
    public string[] Menus { get; set; } = Array.Empty<string>();

    /// <summary>Optional direct items rendered inside the region.</summary>
    public MenuItemSpec[] Items { get; set; } = Array.Empty<MenuItemSpec>();

    /// <summary>Include global actions in this region.</summary>
    public bool IncludeActions { get; set; }

    /// <summary>Optional CSS class for region container.</summary>
    public string? CssClass { get; set; }
}

/// <summary>Navigation footer model with reusable column groups.</summary>
public sealed class NavigationFooterSpec
{
    /// <summary>Optional footer profile label.</summary>
    public string? Label { get; set; }

    /// <summary>Footer columns.</summary>
    public NavigationFooterColumnSpec[] Columns { get; set; } = Array.Empty<NavigationFooterColumnSpec>();

    /// <summary>Optional menu names to convert into footer columns.</summary>
    public string[] Menus { get; set; } = Array.Empty<string>();

    /// <summary>Optional footer legal/support links.</summary>
    public MenuItemSpec[] Legal { get; set; } = Array.Empty<MenuItemSpec>();
}

/// <summary>Single footer column.</summary>
public sealed class NavigationFooterColumnSpec
{
    /// <summary>Column identifier.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Display title.</summary>
    public string? Title { get; set; }

    /// <summary>Column links/items.</summary>
    public MenuItemSpec[] Items { get; set; } = Array.Empty<MenuItemSpec>();
}

/// <summary>Profile override for route/layout/collection specific navigation.</summary>
public sealed class NavigationProfileSpec
{
    /// <summary>Profile name (for diagnostics/templates).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional route match glob(s) (for example /docs/**).</summary>
    public string[] Paths { get; set; } = Array.Empty<string>();

    /// <summary>Optional collection filters (for example docs, blog).</summary>
    public string[] Collections { get; set; } = Array.Empty<string>();

    /// <summary>Optional layout filters.</summary>
    public string[] Layouts { get; set; } = Array.Empty<string>();

    /// <summary>Optional project slug filters.</summary>
    public string[] Projects { get; set; } = Array.Empty<string>();

    /// <summary>Optional precedence for profile selection (higher wins).</summary>
    public int? Priority { get; set; }

    /// <summary>When false, only profile menus are used.</summary>
    public bool InheritMenus { get; set; } = true;

    /// <summary>When false, only profile actions are used.</summary>
    public bool InheritActions { get; set; } = true;

    /// <summary>When false, only profile regions are used.</summary>
    public bool InheritRegions { get; set; } = true;

    /// <summary>When false, only profile footer is used.</summary>
    public bool InheritFooter { get; set; } = true;

    /// <summary>Menu overrides for this profile.</summary>
    public MenuSpec[] Menus { get; set; } = Array.Empty<MenuSpec>();

    /// <summary>Action overrides for this profile.</summary>
    public MenuItemSpec[] Actions { get; set; } = Array.Empty<MenuItemSpec>();

    /// <summary>Region overrides for this profile.</summary>
    public NavigationRegionSpec[] Regions { get; set; } = Array.Empty<NavigationRegionSpec>();

    /// <summary>Footer override for this profile.</summary>
    public NavigationFooterSpec? Footer { get; set; }
}

/// <summary>Visibility rules for menu/menu-item rendering.</summary>
public sealed class NavigationVisibilitySpec
{
    /// <summary>Optional include path globs.</summary>
    public string[] Paths { get; set; } = Array.Empty<string>();

    /// <summary>Optional exclude path globs.</summary>
    public string[] ExcludePaths { get; set; } = Array.Empty<string>();

    /// <summary>Optional include collections.</summary>
    public string[] Collections { get; set; } = Array.Empty<string>();

    /// <summary>Optional include layouts.</summary>
    public string[] Layouts { get; set; } = Array.Empty<string>();

    /// <summary>Optional include project slugs.</summary>
    public string[] Projects { get; set; } = Array.Empty<string>();
}

/// <summary>Mega menu section definition.</summary>
public sealed class MenuSectionSpec
{
    /// <summary>Section identifier.</summary>
    public string? Name { get; set; }

    /// <summary>Section title.</summary>
    public string? Title { get; set; }

    /// <summary>Optional description.</summary>
    public string? Description { get; set; }

    /// <summary>Optional CSS class.</summary>
    public string? CssClass { get; set; }

    /// <summary>Section links/items.</summary>
    public MenuItemSpec[] Items { get; set; } = Array.Empty<MenuItemSpec>();

    /// <summary>Optional nested columns.</summary>
    public MenuColumnSpec[] Columns { get; set; } = Array.Empty<MenuColumnSpec>();
}

/// <summary>Column container for mega menu layouts.</summary>
public sealed class MenuColumnSpec
{
    /// <summary>Column identifier.</summary>
    public string? Name { get; set; }

    /// <summary>Column title.</summary>
    public string? Title { get; set; }

    /// <summary>Column links/items.</summary>
    public MenuItemSpec[] Items { get; set; } = Array.Empty<MenuItemSpec>();
}
