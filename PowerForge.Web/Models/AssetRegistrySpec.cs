namespace PowerForge.Web;

public sealed class AssetRegistrySpec
{
    public AssetBundleSpec[] Bundles { get; set; } = Array.Empty<AssetBundleSpec>();
    public RouteBundleSpec[] RouteBundles { get; set; } = Array.Empty<RouteBundleSpec>();
    public PreloadSpec[] Preloads { get; set; } = Array.Empty<PreloadSpec>();
    public CriticalCssSpec[] CriticalCss { get; set; } = Array.Empty<CriticalCssSpec>();
    public string? CssStrategy { get; set; }
}

public sealed class AssetBundleSpec
{
    public string Name { get; set; } = string.Empty;
    public string[] Css { get; set; } = Array.Empty<string>();
    public string[] Js { get; set; } = Array.Empty<string>();
}

public sealed class RouteBundleSpec
{
    public string Match { get; set; } = string.Empty;
    public string[] Bundles { get; set; } = Array.Empty<string>();
}

public sealed class PreloadSpec
{
    public string Href { get; set; } = string.Empty;
    public string As { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? Crossorigin { get; set; }
}

public sealed class CriticalCssSpec
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}
