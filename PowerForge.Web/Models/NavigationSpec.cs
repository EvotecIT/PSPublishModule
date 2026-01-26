namespace PowerForge.Web;

public sealed class NavigationSpec
{
    public MenuSpec[] Menus { get; set; } = Array.Empty<MenuSpec>();
}

public sealed class MenuSpec
{
    public string Name { get; set; } = string.Empty;
    public string? Label { get; set; }
    public MenuItemSpec[] Items { get; set; } = Array.Empty<MenuItemSpec>();
}

public sealed class MenuItemSpec
{
    public string Title { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string? Icon { get; set; }
    public string? Badge { get; set; }
    public string? Description { get; set; }
    public string? Target { get; set; }
    public string? Rel { get; set; }
    public bool? External { get; set; }
    public int? Weight { get; set; }
    public string? Match { get; set; }
    public MenuItemSpec[] Items { get; set; } = Array.Empty<MenuItemSpec>();
}
