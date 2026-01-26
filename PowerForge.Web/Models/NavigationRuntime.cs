namespace PowerForge.Web;

public sealed class NavigationRuntime
{
    public NavigationMenu[] Menus { get; set; } = Array.Empty<NavigationMenu>();
}

public sealed class NavigationMenu
{
    public string Name { get; set; } = string.Empty;
    public string? Label { get; set; }
    public NavigationItem[] Items { get; set; } = Array.Empty<NavigationItem>();
}

public sealed class NavigationItem
{
    public string Title { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string? Icon { get; set; }
    public string? Badge { get; set; }
    public string? Description { get; set; }
    public string? Target { get; set; }
    public string? Rel { get; set; }
    public bool External { get; set; }
    public bool IsActive { get; set; }
    public bool IsAncestor { get; set; }
    public int? Weight { get; set; }
    public string? Match { get; set; }
    public NavigationItem[] Items { get; set; } = Array.Empty<NavigationItem>();
}

public sealed class BreadcrumbItem
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool IsCurrent { get; set; }
}
