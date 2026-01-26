namespace PowerForge.Web;

/// <summary>Defines CSS/JS bundles, preloads, and critical CSS behavior.</summary>
public sealed class AssetRegistrySpec
{
    /// <summary>Named asset bundles.</summary>
    public AssetBundleSpec[] Bundles { get; set; } = Array.Empty<AssetBundleSpec>();
    /// <summary>Route-to-bundle mappings.</summary>
    public RouteBundleSpec[] RouteBundles { get; set; } = Array.Empty<RouteBundleSpec>();
    /// <summary>Preload hints for critical assets.</summary>
    public PreloadSpec[] Preloads { get; set; } = Array.Empty<PreloadSpec>();
    /// <summary>Critical CSS files to inline.</summary>
    public CriticalCssSpec[] CriticalCss { get; set; } = Array.Empty<CriticalCssSpec>();
    /// <summary>CSS loading strategy (e.g., async).</summary>
    public string? CssStrategy { get; set; }
}

/// <summary>Defines a named bundle of CSS/JS assets.</summary>
public sealed class AssetBundleSpec
{
    /// <summary>Bundle identifier.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>CSS paths.</summary>
    public string[] Css { get; set; } = Array.Empty<string>();
    /// <summary>JS paths.</summary>
    public string[] Js { get; set; } = Array.Empty<string>();
}

/// <summary>Maps a route glob to bundle names.</summary>
public sealed class RouteBundleSpec
{
    /// <summary>Route pattern.</summary>
    public string Match { get; set; } = string.Empty;
    /// <summary>Bundles applied to matching routes.</summary>
    public string[] Bundles { get; set; } = Array.Empty<string>();
}

/// <summary>Preload link definition.</summary>
public sealed class PreloadSpec
{
    /// <summary>Asset href.</summary>
    public string Href { get; set; } = string.Empty;
    /// <summary>Preload as attribute.</summary>
    public string As { get; set; } = string.Empty;
    /// <summary>Optional asset MIME type.</summary>
    public string? Type { get; set; }
    /// <summary>Optional crossorigin value.</summary>
    public string? Crossorigin { get; set; }
}

/// <summary>Critical CSS file definition.</summary>
public sealed class CriticalCssSpec
{
    /// <summary>Friendly name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Path to the CSS file.</summary>
    public string Path { get; set; } = string.Empty;
}
